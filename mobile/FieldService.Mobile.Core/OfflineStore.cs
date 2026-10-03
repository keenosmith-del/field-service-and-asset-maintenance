using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FieldService.Contracts;
using SQLite;
namespace FieldService.Mobile.Core;
public class LocalJob { [PrimaryKey] public string Id {get;set;}=""; public string ServerJson {get;set;}=""; public string DraftJson {get;set;}=""; public string State {get;set;}="Synced"; public string Error {get;set;}=""; public bool WantsSubmit {get;set;} public bool Accessible {get;set;}=true; }
public class OutboxEntry { [PrimaryKey,AutoIncrement] public int Sequence {get;set;} public string OperationId {get;set;}=""; [Indexed] public string JobId {get;set;}=""; public string Action {get;set;}="Save"; public string InspectionJson {get;set;}=""; public string Payload {get;set;}=""; public int Attempts {get;set;} public long RetryAt {get;set;} public string State {get;set;}="Pending"; }
public class LocalPhoto { [PrimaryKey] public string Id {get;set;}=""; [Indexed] public string JobId {get;set;}=""; public string ContentType {get;set;}=""; public byte[] Data {get;set;}=[]; public bool Acknowledged {get;set;} public int Attempts {get;set;} public long RetryAt {get;set;} public string Error {get;set;}=""; }
public class ReferenceData { [PrimaryKey] public string Key {get;set;}=""; public string Json {get;set;}=""; }
public class DraftArchive { [PrimaryKey] public string Id {get;set;}=""; public string JobId {get;set;}=""; public string Json {get;set;}=""; public string Reason {get;set;}=""; public DateTime At {get;set;} }
public class OfflineStore
{
 static OfflineStore()=>SQLitePCL.Batteries_V2.Init();
 public static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web);
 readonly SQLiteAsyncConnection db;readonly SemaphoreSlim gate=new(1,1);readonly HttpClient client;readonly Guid userId;
 public OfflineStore(string path,HttpClient client,Guid userId){db=new(path);this.client=client;this.userId=userId;}
 public static string Write<T>(T value)=>JsonSerializer.Serialize(value,JsonOptions);public static T Read<T>(string value)=>JsonSerializer.Deserialize<T>(value,JsonOptions)!;
 public async Task Initialize()
 {
  await db.CreateTableAsync<LocalJob>();await db.CreateTableAsync<OutboxEntry>();await db.CreateTableAsync<LocalPhoto>();await db.CreateTableAsync<ReferenceData>();await db.CreateTableAsync<DraftArchive>();
  // A killed process has no active request. The frozen operation is retried with its original ID and payload.
  await db.ExecuteAsync("UPDATE OutboxEntry SET State = 'Pending' WHERE State = 'Syncing'");await db.ExecuteAsync("UPDATE LocalJob SET State = 'Pending' WHERE State = 'Syncing'");
 }
 public Task<List<LocalJob>> Jobs()=>db.Table<LocalJob>().ToListAsync();
 public async Task<List<PartDto>> Parts(){var r=await db.FindAsync<ReferenceData>("parts");return r==null?[]:Read<List<PartDto>>(r.Json);}
 public Task<List<LocalPhoto>> Photos(string job)=>db.Table<LocalPhoto>().Where(x=>x.JobId==job).ToListAsync();
 public Task<List<OutboxEntry>> Pending()=>db.Table<OutboxEntry>().OrderBy(x=>x.Sequence).ToListAsync();
 public Task<List<DraftArchive>> Archives(string job)=>db.Table<DraftArchive>().Where(x=>x.JobId==job).ToListAsync();
 public async Task SaveDraft(Guid jobId,Inspection inspection,bool submit)
 {
  // Snapshot mutable UI data before awaiting the synchronization lock.
  inspection=Read<Inspection>(Write(inspection));
  await gate.WaitAsync();try
  {
   var id=jobId.ToString();var local=await db.FindAsync<LocalJob>(id)??throw new InvalidOperationException("Download this job first.");var server=Read<WorkOrderDto>(local.ServerJson);
   if(!local.Accessible||server.TechnicianId!=userId||server.Status is "Submitted" or "Completed")throw new InvalidOperationException("This job cannot be edited.");
   if(local.State=="Conflict")throw new InvalidOperationException("Resolve the conflict before editing.");
   if(local.WantsSubmit)throw new InvalidOperationException("Submission is queued. Synchronize or resolve it before editing.");
   if(inspection.StartedAt==null)throw new InvalidOperationException("Start the job first.");
   if(submit&&server.Checklist.Where(x=>x.Required).Any(x=>!inspection.Answers.Any(a=>a.ItemId==x.Id&&a.Done)))throw new InvalidOperationException("Complete every required checklist item.");
   if(submit)inspection.SubmittedAt=DateTimeOffset.UtcNow;
   var json=Write(inspection);local.DraftJson=json;local.State="Pending";local.Error="";local.WantsSubmit=submit;
   var entry=new OutboxEntry{OperationId=Guid.NewGuid().ToString(),JobId=id,Action=submit?"Submit":"Save",InspectionJson=json};
   // Coalesce only unsent edits. Once frozen for network I/O the payload and ID never change.
   await db.RunInTransactionAsync(c=>{c.Execute("DELETE FROM OutboxEntry WHERE JobId = ? AND Payload = ''",id);c.Update(local);c.Insert(entry);});
  }finally{gate.Release();}
 }
 public async Task AddPhoto(Guid jobId,Guid photoId,string contentType,byte[] data)
 {
  if(data.Length>5*1024*1024)throw new InvalidOperationException("Photos must be at most 5 MB.");
  await gate.WaitAsync();try{var local=await db.FindAsync<LocalJob>(jobId.ToString())??throw new InvalidOperationException("Job not found.");if(!local.Accessible||local.WantsSubmit||local.State=="Conflict")throw new InvalidOperationException("Job is locked for submission or conflict resolution.");var draft=Read<Inspection>(local.DraftJson);if(draft.StartedAt==null)throw new InvalidOperationException("Start the job first.");if(draft.AttachmentIds.Count>=20)throw new InvalidOperationException("At most 20 photos.");draft.AttachmentIds.Add(photoId);local.DraftJson=Write(draft);local.State="Pending";
   await db.RunInTransactionAsync(c=>{c.Insert(new LocalPhoto{Id=photoId.ToString(),JobId=jobId.ToString(),ContentType=contentType,Data=data});c.Execute("DELETE FROM OutboxEntry WHERE JobId = ? AND Payload = ''",local.Id);c.Insert(new OutboxEntry{OperationId=Guid.NewGuid().ToString(),JobId=local.Id,Action="Save",InspectionJson=local.DraftJson});c.Update(local);});
  }finally{gate.Release();}
 }
 public async Task Download()
 {
  await gate.WaitAsync();try{await DownloadInternal();}finally{gate.Release();}
 }
 async Task DownloadInternal()
 {
  var response=await client.GetAsync("api/sync/download");response.EnsureSuccessStatusCode();var bundle=(await response.Content.ReadFromJsonAsync<DownloadBundle>(JsonOptions))!;
  var existing=await Jobs();
  await db.RunInTransactionAsync(c=>
  {
   c.InsertOrReplace(new ReferenceData{Key="parts",Json=Write(bundle.Parts)});
   foreach(var w in bundle.WorkOrders)
   {
    var local=c.Find<LocalJob>(w.Id.ToString());
    if(local==null)c.Insert(new LocalJob{Id=w.Id.ToString(),ServerJson=Write(w),DraftJson=Write(w.Inspection)});
    else if(local.State=="Synced"){local.ServerJson=Write(w);local.DraftJson=Write(w.Inspection);local.Accessible=true;c.Update(local);}
    else {var previous=Read<WorkOrderDto>(local.ServerJson);local.Accessible=true;if(w.Version!=previous.Version&&!c.Table<OutboxEntry>().Any(e=>e.JobId==local.Id&&e.Payload!="")){local.State="Conflict";local.Error="Server version changed. Compare and resolve before retrying.";}c.Update(local);}
   }
   foreach(var local in existing.Where(l=>bundle.WorkOrders.All(w=>w.Id.ToString()!=l.Id))){local.Accessible=false;local.State="Conflict";local.Error="Assignment is no longer accessible. Local work is retained; contact your supervisor.";c.Update(local);}
  });
  // Server evidence must remain viewable after reassignment, restart, and disconnection.
  foreach(var work in bundle.WorkOrders)
   foreach(var id in work.Inspection.AttachmentIds)
    if(await db.FindAsync<LocalPhoto>(id.ToString())==null)
    {
     using var photo=await client.GetAsync($"api/attachments/{id}");photo.EnsureSuccessStatusCode();
     var bytes=await photo.Content.ReadAsByteArrayAsync();if(bytes.Length>5*1024*1024)throw new InvalidOperationException("Server photo exceeds the local size limit.");
     await db.InsertAsync(new LocalPhoto{Id=id.ToString(),JobId=work.Id.ToString(),ContentType=photo.Content.Headers.ContentType?.MediaType??"image/jpeg",Data=bytes,Acknowledged=true});
    }
 }
 public async Task Sync(bool manual=false)
 {
  await gate.WaitAsync();try
  {
   foreach(var local in (await Jobs()).Where(x=>x.State is "Pending" or "Failed" or "Local").OrderBy(x=>x.Id))
   {
    if(!local.Accessible)continue;
    var entries=await db.Table<OutboxEntry>().Where(x=>x.JobId==local.Id).OrderBy(x=>x.Sequence).ToListAsync();if(entries.Count==0)continue;
    local.State="Syncing";local.Error="";await db.UpdateAsync(local);
    try
    {
     foreach(var entry in entries)
     {
      if(!manual&&entry.RetryAt>DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new RetryLaterException();
     foreach(var photo in (await Photos(local.Id)).Where(p=>Read<Inspection>(entry.InspectionJson).AttachmentIds.Contains(Guid.Parse(p.Id))))
     {
      if(photo.Acknowledged)continue;if(!manual&&photo.RetryAt>DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new RetryLaterException();
      try { using var body=new ByteArrayContent(photo.Data);body.Headers.ContentType=new MediaTypeHeaderValue(photo.ContentType);using var photoResponse=await client.PutAsync($"api/work-orders/{local.Id}/attachments/{photo.Id}",body);await CheckResponse(photoResponse,local);var photoAck=await photoResponse.Content.ReadFromJsonAsync<AttachmentAck>(JsonOptions);if(photoAck?.Id.ToString()!=photo.Id)throw new InvalidOperationException("Photo acknowledgment mismatch.");photo.Acknowledged=true;photo.Error="";await db.UpdateAsync(photo); }
      catch(Exception ex){photo.Attempts++;photo.RetryAt=NextRetry(photo.Attempts);photo.Error=ex.Message;await db.UpdateAsync(photo);throw;}
     }
      if(entry.Payload==""){var current=Read<WorkOrderDto>(local.ServerJson);entry.Payload=Write(new SyncMutation(Guid.Parse(entry.OperationId),Guid.Parse(entry.JobId),current.Version,entry.Action,Read<Inspection>(entry.InspectionJson)));}
      entry.Attempts++;entry.State="Syncing";await db.UpdateAsync(entry);
      using var res=await client.PostAsJsonAsync("api/sync/mutations",Read<SyncMutation>(entry.Payload),JsonOptions);await CheckResponse(res,local);
      var ack=await res.Content.ReadFromJsonAsync<SyncAck>(JsonOptions)??throw new InvalidOperationException("Missing acknowledgment.");if(ack.OperationId.ToString()!=entry.OperationId||ack.WorkOrder.Id.ToString()!=local.Id)throw new InvalidOperationException("Operation acknowledgment mismatch.");
      local.ServerJson=Write(ack.WorkOrder);
      await db.RunInTransactionAsync(c=>{c.Delete(entry);if(!c.Table<OutboxEntry>().Any(e=>e.JobId==local.Id)){local.State="Synced";local.WantsSubmit=false;local.DraftJson=Write(ack.WorkOrder.Inspection);}c.Update(local);});
     }
     local.State="Synced";local.DraftJson=Write(Read<WorkOrderDto>(local.ServerJson).Inspection);local.WantsSubmit=false;local.Error="";await db.UpdateAsync(local);
    }
    catch(RetryLaterException){local.State="Pending";await db.UpdateAsync(local);}
    catch(Exception ex)
    {
     if(local.State!="Conflict")local.State="Failed";local.Error=ex.Message;await db.UpdateAsync(local);
     foreach(var entry in await db.Table<OutboxEntry>().Where(x=>x.JobId==local.Id).ToListAsync()){entry.State=local.State;entry.RetryAt=NextRetry(entry.Attempts);await db.UpdateAsync(entry);}
     if(ex is AuthenticationException)throw;
    }
   }
   await DownloadInternal();
  }finally{gate.Release();}
 }
 static long NextRetry(int attempts)=>DateTimeOffset.UtcNow.AddSeconds(Math.Min(300,Math.Pow(2,Math.Min(attempts,8)))+Random.Shared.Next(0,4)).ToUnixTimeSeconds();
 static async Task CheckResponse(HttpResponseMessage res,LocalJob local)
 {
  if(res.IsSuccessStatusCode)return;var body=await res.Content.ReadAsStringAsync();
  if(res.StatusCode==HttpStatusCode.Unauthorized)throw new AuthenticationException("Session expired. Sign in online with the same account; local work is retained.");
  // A definitive rejection requires explicit correction, not infinite replay of an invalid frozen request.
  if(res.StatusCode==HttpStatusCode.BadRequest){local.State="Conflict";throw new InvalidOperationException("Inspection rejected. Compare and keep the local draft to edit it, then submit again. "+body);}
  if(res.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.Forbidden or HttpStatusCode.NotFound){local.State="Conflict";throw new InvalidOperationException("Server conflict or assignment change. Compare server and local work. "+body);}
  throw new InvalidOperationException($"Server {(int)res.StatusCode}: {body}");
 }
 public async Task<WorkOrderDto> Current(Guid id){var res=await client.GetAsync($"api/work-orders/{id}");res.EnsureSuccessStatusCode();return (await res.Content.ReadFromJsonAsync<WorkOrderDto>(JsonOptions))!;}
 public async Task Resolve(Guid id,WorkOrderDto reviewedServer,bool keepLocal)
 {
  await gate.WaitAsync();try
  {
   var local=await db.FindAsync<LocalJob>(id.ToString())??throw new InvalidOperationException("Job not found.");
   // Fetch again; do not overwrite changes made after the comparison screen was opened.
   var server=await Current(id);if(server.Version!=reviewedServer.Version)throw new InvalidOperationException("Server changed again. Compare the latest version.");
   if(keepLocal&&(server.TechnicianId!=userId||server.Status is "Submitted" or "Completed"))throw new InvalidOperationException("Local work cannot be replayed into a closed or reassigned job. Use server version or contact supervisor.");
   var draft=local.DraftJson;
   var editable=Read<Inspection>(draft);editable.SubmittedAt=null;var rebased=Write(editable);
   await db.RunInTransactionAsync(c=>
   {
    c.Insert(new DraftArchive{Id=Guid.NewGuid().ToString(),JobId=local.Id,Json=draft,Reason=keepLocal?"Explicit local rebase":"Explicit server adoption",At=DateTime.UtcNow});c.Execute("DELETE FROM OutboxEntry WHERE JobId = ?",local.Id);
    local.ServerJson=Write(server);local.Accessible=true;local.Error="";local.State=keepLocal?"Pending":"Synced";local.WantsSubmit=false;
    if(!keepLocal)local.DraftJson=Write(server.Inspection);
    else {local.DraftJson=rebased;c.Insert(new OutboxEntry{OperationId=Guid.NewGuid().ToString(),JobId=local.Id,Action="Save",InspectionJson=rebased});}
    c.Update(local);
   });
  }finally{gate.Release();}
 }
 public async Task Close(){await gate.WaitAsync();try{await db.CloseAsync();}finally{gate.Release();}}
}
public class RetryLaterException:Exception;
public class AuthenticationException(string message):Exception(message);
