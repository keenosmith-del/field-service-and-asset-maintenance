using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FieldService.Contracts;
using FieldService.Mobile.Core;

var url=Environment.GetEnvironmentVariable("API_URL")??"http://localhost:8080/";
var password=Environment.GetEnvironmentVariable("DEMO_PASSWORD")??throw new InvalidOperationException("DEMO_PASSWORD required.");
var options=OfflineStore.JsonOptions;
using var admin=new HttpClient{BaseAddress=new Uri(url)};
using var technician=new HttpClient{BaseAddress=new Uri(url)};
var supervisor=await Post<Session>(admin,"api/auth/login",new LoginRequest("supervisor@fieldservice.local",password));
var session=await Post<Session>(technician,"api/auth/login",new LoginRequest("technician@fieldservice.local",password));
admin.DefaultRequestHeaders.Authorization=new("Bearer",supervisor.Token);technician.DefaultRequestHeaders.Authorization=new("Bearer",session.Token);
var name="JOURNEY-"+Guid.NewGuid().ToString("N")[..8];
var site=await Post<JsonElement>(admin,"api/sites",new SiteInput(name,"Live SQLite/API verification"));
var asset=await Post<JsonElement>(admin,"api/assets",new AssetInput(site.GetProperty("id").GetGuid(),name,name,"Pump","Test workshop","Active",30));
var assetId=asset.GetProperty("id").GetGuid();var item=new ChecklistDefinition(Guid.NewGuid(),"Inspect seals offline");
var schedule=await Post<JsonElement>(admin,"api/schedules",new ScheduleInput(assetId,name,30,DateTimeOffset.UtcNow.AddMinutes(10),"High",session.UserId,[item]));
await Post<JsonElement>(admin,"api/schedules/generate",new{});
var work=(await Get<List<WorkOrderDto>>(admin,"api/work-orders")).Single(j=>j.AssetId==assetId&&j.Title==name);
var part=(await Get<List<PartDto>>(technician,"api/parts")).First(p=>p.Stock>=1);
var handler=new NetworkFaultHandler();using var mobileClient=new HttpClient(handler){BaseAddress=new Uri(url)};mobileClient.DefaultRequestHeaders.Authorization=new("Bearer",session.Token);
var path=Path.Combine(Path.GetTempPath(),name+".db");var store=new OfflineStore(path,mobileClient,session.UserId);
try
{
 await store.Initialize();await store.Download();handler.Offline=true;
 var inspection=new Inspection{StartedAt=DateTimeOffset.UtcNow,Notes="Offline progress",Answers=[new(item.Id,true,"Checked without connectivity")],Faults=[new(Guid.NewGuid(),"Seal wear","Medium")],Parts=[new(part.Id,1)]};
 await store.SaveDraft(work.Id,inspection,false);
 var photo=Guid.NewGuid();var bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZlSAAAAAASUVORK5CYII=");
 await store.AddPhoto(work.Id,photo,"image/png",bytes);
 inspection=OfflineStore.Read<Inspection>((await store.Jobs()).Single(j=>j.Id==work.Id.ToString()).DraftJson);await store.SaveDraft(work.Id,inspection,true);await store.Close();
 store=new(path,mobileClient,session.UserId);await store.Initialize();
 Check((await store.Pending()).Count==1,"Restart must retain one coalesced submission.");Check((await store.Photos(work.Id.ToString())).Single().Data.SequenceEqual(bytes),"Restart must retain exact photo bytes.");
 try{await store.Sync(true);throw new Exception("Offline synchronization unexpectedly succeeded.");}catch(HttpRequestException){}
 Check((await store.Pending()).Count==1,"Offline request must retain outbox.");
 handler.Offline=false;handler.LoseAcknowledgment=true;await store.Sync(true);
 Check((await store.Pending()).Count==1,"Lost acknowledgment must retain immutable operation.");var frozen=(await store.Pending()).Single();Check(frozen.Payload!="","Payload must be frozen before network I/O.");
 var submitted=await Get<WorkOrderDto>(admin,$"api/work-orders/{work.Id}");Check(submitted.Status=="Submitted","Server must persist submission despite response loss.");
 var remaining=(await Get<List<PartDto>>(technician,"api/parts")).Single(p=>p.Id==part.Id).Stock;Check(remaining==part.Stock-1,"Parts must be consumed once.");
 var completed=await Post<WorkOrderDto>(admin,$"api/work-orders/{work.Id}/review",new ReviewInput(submitted.Version,"Verified SQLite restart, lost acknowledgment, photo and parts"));Check(completed.Status=="Completed","Supervisor must complete submitted work.");
 await store.Close();store=new(path,mobileClient,session.UserId);await store.Initialize();await store.Sync(true);
 Check((await store.Pending()).Count==0,"Receipt replay must acknowledge persisted work.");var local=(await store.Jobs()).Single(j=>j.Id==work.Id.ToString());Check(local.State=="Synced","Local delivery state must be acknowledged.");Check(OfflineStore.Read<WorkOrderDto>(local.ServerJson).Status=="Completed","Download must reconcile review performed before acknowledgment recovery.");
 Check(handler.Mutations.Count==2&&handler.Mutations[0]==handler.Mutations[1],"Retries must preserve identical operation bytes.");
 Check((await Get<List<PartDto>>(technician,"api/parts")).Single(p=>p.Id==part.Id).Stock==remaining,"Receipt replay must not consume stock twice.");
 using var evidence=await admin.GetAsync($"api/attachments/{photo}");evidence.EnsureSuccessStatusCode();Check((await evidence.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes),"Supervisor must receive exact persisted photo.");
 var history=await Get<JsonElement>(admin,$"api/assets/{assetId}/history");Check(history.GetProperty("workOrders")[0].GetProperty("status").GetString()=="Completed","Asset history must include reviewed inspection.");Check(history.GetProperty("faults").GetArrayLength()==1,"Fault must be persisted exactly once.");
 var report=await Get<JsonElement>(admin,"api/reports");Check(report.GetProperty("partsUsed").EnumerateArray().Count(p=>p.GetProperty("workOrderId").GetGuid()==work.Id)==1,"Report must contain one consumption row.");
 var audits=await Get<JsonElement>(admin,$"api/audit/{work.Id}");Check(audits.GetArrayLength()>=4,"Lifecycle must be audited.");
 var fault=history.GetProperty("faults")[0].GetProperty("id").GetGuid();var followUp=await Post<WorkOrderDto>(admin,"api/work-orders",new WorkOrderInput(assetId,name+" corrective",DateTimeOffset.UtcNow,"High",session.UserId,[item],fault));Check(followUp.FollowUpFaultId==fault,"Corrective work must expose its persisted fault relationship.");
 // A new installation receives existing server evidence for offline viewing.
 var cachePath=path+"-cache";var cache=new OfflineStore(cachePath,technician,session.UserId);
 try{await cache.Initialize();await cache.Download();Check((await cache.Photos(work.Id.ToString())).Single().Data.SequenceEqual(bytes),"Downloaded server photo must persist in a fresh SQLite cache.");}finally{await cache.Close();File.Delete(cachePath);}
 // A definite stock rejection must preserve the draft and allow an explicit correction.
 await store.Download();var rejected=new Inspection{StartedAt=DateTimeOffset.UtcNow,Notes="Correct stock after server rejection",Answers=[new(item.Id,true,"Checked")],Parts=[new(part.Id,remaining+1)]};
 await store.SaveDraft(followUp.Id,rejected,true);await store.Sync(true);var held=(await store.Jobs()).Single(j=>j.Id==followUp.Id.ToString());Check(held.State=="Conflict"&&held.WantsSubmit,"Rejected submission must be retained for explicit correction.");
 var unchanged=await Get<WorkOrderDto>(admin,$"api/work-orders/{followUp.Id}");Check(unchanged.Status=="Assigned"&&unchanged.Version==followUp.Version,"Rejected transaction must not advance server lifecycle.");Check((await Get<List<PartDto>>(technician,"api/parts")).Single(p=>p.Id==part.Id).Stock==remaining,"Rejected transaction must not deduct stock.");
 await store.Resolve(followUp.Id,unchanged,true);held=(await store.Jobs()).Single(j=>j.Id==followUp.Id.ToString());var correction=OfflineStore.Read<Inspection>(held.DraftJson);correction.Parts.Clear();await store.SaveDraft(followUp.Id,correction,true);await store.Sync(true);
 Check((await store.Pending()).Count==0,"Corrected submission must clear its outbox after acknowledgment.");Check((await Get<WorkOrderDto>(admin,$"api/work-orders/{followUp.Id}")).Status=="Submitted","Corrected draft must reach submitted state.");Check((await store.Archives(followUp.Id.ToString())).Count==1,"Rejected draft must remain archived.");
 Console.WriteLine("PASS: real SQLite + HTTP + PostgreSQL: scheduled assignment, offline checklist/notes/fault/photo/parts/submission, process restart, lost acknowledgment, immutable receipt replay, review before recovery, exactly-once stock/faults, completed history, evidence, reports, audit, corrective follow-up, fresh photo download, stock rollback and explicit correction.");
}
finally{await store.Close();File.Delete(path);}
async Task<T> Post<T>(HttpClient client,string uri,object value){using var r=await client.PostAsJsonAsync(uri,value,options);if(!r.IsSuccessStatusCode)throw new Exception($"POST {uri}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");return (await r.Content.ReadFromJsonAsync<T>(options))!;}
async Task<T> Get<T>(HttpClient client,string uri)=> (await client.GetFromJsonAsync<T>(uri,options))!;
static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
sealed class NetworkFaultHandler:DelegatingHandler
{
 public bool Offline;public bool LoseAcknowledgment;public List<string> Mutations=[];
 public NetworkFaultHandler():base(new HttpClientHandler()){}
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
 {
  if(Offline)throw new HttpRequestException("Deliberately disconnected before HTTP transport.");
  var mutation=request.Method==HttpMethod.Post&&request.RequestUri!.AbsolutePath.EndsWith("sync/mutations");if(mutation)Mutations.Add(await request.Content!.ReadAsStringAsync(ct));
  var response=await base.SendAsync(request,ct);
  if(mutation&&LoseAcknowledgment&&response.IsSuccessStatusCode){LoseAcknowledgment=false;await response.Content.LoadIntoBufferAsync();response.Dispose();throw new HttpRequestException("Lost response after real server commit.");}
  return response;
 }
}
