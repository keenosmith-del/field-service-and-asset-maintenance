using System.Net;
using System.Net.Http.Json;
using FieldService.Contracts;
using FieldService.Mobile.Core;
using Xunit;
namespace FieldService.Tests;
public class OfflineTests
{
 sealed class FakeServer:HttpMessageHandler
 {
  public Guid UserId=Guid.NewGuid();public WorkOrderDto Job;public int Uploads;public int Mutations;public bool Offline;public bool LoseAck;public bool Reject;public bool HideJob;public readonly Dictionary<Guid,SyncAck> Receipts=[];public readonly List<SyncMutation> Requests=[];
  public FakeServer(){var checklist=new List<ChecklistDefinition>{new(Guid.NewGuid(),"Inspect")};Job=new(Guid.NewGuid(),Guid.NewGuid(),"Pump","P-1","Site","Inspection",DateTimeOffset.UtcNow,"Normal",UserId,"Sam","Assigned",1,checklist,new(),"",null);}
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   if(Offline)throw new HttpRequestException("Offline");
   if(request.Method==HttpMethod.Get&&request.RequestUri!.AbsolutePath.EndsWith("download"))return new(HttpStatusCode.OK){Content=JsonContent.Create(new DownloadBundle(HideJob?[]:[Job],[],DateTimeOffset.UtcNow))};
   if(request.Method==HttpMethod.Get&&request.RequestUri!.AbsolutePath.Contains("attachments/"))return new(HttpStatusCode.OK){Content=new ByteArrayContent([137,80,78,71,13,10,26,10])};
   if(request.Method==HttpMethod.Get)return new(HttpStatusCode.OK){Content=JsonContent.Create(Job)};
   if(request.Method==HttpMethod.Put){Uploads++;return new(HttpStatusCode.OK){Content=JsonContent.Create(new AttachmentAck(Guid.Parse(request.RequestUri!.Segments.Last()),"image/png",8))};}
   var mutation=(await request.Content!.ReadFromJsonAsync<SyncMutation>())!;Requests.Add(mutation);
   if(Receipts.TryGetValue(mutation.OperationId,out var old))return new(HttpStatusCode.OK){Content=JsonContent.Create(old)};
   if(mutation.BaseVersion!=Job.Version)return new(HttpStatusCode.Conflict){Content=JsonContent.Create(new ConflictResponse("Changed",Job))};
   if(Reject)return new(HttpStatusCode.BadRequest){Content=JsonContent.Create(new{message="Insufficient stock"})};
   Mutations++;Job=Job with{Version=Job.Version+1,Status=mutation.Action=="Submit"?"Submitted":"In Progress",Inspection=mutation.Inspection};var ack=new SyncAck(mutation.OperationId,Job);Receipts[mutation.OperationId]=ack;
   if(LoseAck){LoseAck=false;throw new HttpRequestException("Acknowledgment lost");}return new(HttpStatusCode.OK){Content=JsonContent.Create(ack)};
  }
 }
 static HttpClient Client(FakeServer h)=>new(h){BaseAddress=new Uri("http://test/")};
 static Inspection Draft(FakeServer s)=>new(){StartedAt=DateTimeOffset.UtcNow.AddMinutes(-1),Answers=[new(s.Job.Checklist[0].Id,true,"Checked")],Notes="Offline notes"};
 [Fact] public async Task RestartRetainsOutboxAndPhotosUntilAcknowledgment()
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var h=new FakeServer();var store=new OfflineStore(path,Client(h),h.UserId);await store.Initialize();await store.Download();await store.SaveDraft(h.Job.Id,Draft(h),false);var photo=Guid.NewGuid();await store.AddPhoto(h.Job.Id,photo,"image/png",[137,80,78,71,13,10,26,10]);var draft=OfflineStore.Read<Inspection>((await store.Jobs())[0].DraftJson);await store.SaveDraft(h.Job.Id,draft,true);await store.Close();
  store=new(path,Client(h),h.UserId);await store.Initialize();Assert.Single(await store.Pending());Assert.Single(await store.Photos(h.Job.Id.ToString()));Assert.Equal("Pending",(await store.Jobs())[0].State);
  h.Offline=true;await Assert.ThrowsAsync<HttpRequestException>(()=>store.Sync(true));Assert.Single(await store.Pending());h.Offline=false;await store.Sync(true);Assert.Empty(await store.Pending());Assert.Equal("Synced",(await store.Jobs())[0].State);Assert.Equal("Submitted",h.Job.Status);Assert.Equal(1,h.Uploads);await store.Close();File.Delete(path);
 }
 [Fact] public async Task LostAcknowledgmentReplaysIdenticalOperationExactlyOnce()
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var h=new FakeServer{LoseAck=true};var store=new OfflineStore(path,Client(h),h.UserId);await store.Initialize();await store.Download();await store.SaveDraft(h.Job.Id,Draft(h),true);await store.Sync(true);Assert.Single(await store.Pending());await store.Close();
  store=new(path,Client(h),h.UserId);await store.Initialize();await store.Sync(true);Assert.Equal(1,h.Mutations);Assert.Equal(OfflineStore.Write(h.Requests[0]),OfflineStore.Write(h.Requests[1]));Assert.Empty(await store.Pending());await store.Close();File.Delete(path);
 }
 [Fact] public async Task ConflictsRetainDraftAndRequireExplicitResolution()
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var h=new FakeServer();var store=new OfflineStore(path,Client(h),h.UserId);await store.Initialize();await store.Download();await store.SaveDraft(h.Job.Id,Draft(h),false);h.Job=h.Job with{Version=2};await store.Sync(true);Assert.Equal("Conflict",(await store.Jobs())[0].State);Assert.Contains("Offline notes",(await store.Jobs())[0].DraftJson);await store.Resolve(h.Job.Id,h.Job,true);Assert.Single(await store.Archives(h.Job.Id.ToString()));await store.Sync(true);Assert.Equal("Synced",(await store.Jobs())[0].State);await store.Close();File.Delete(path);
 }
 [Fact] public async Task PhotoSaveAtomicallyQueuesItsInspectionReference()
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var h=new FakeServer();var store=new OfflineStore(path,Client(h),h.UserId);await store.Initialize();await store.Download();await store.SaveDraft(h.Job.Id,Draft(h),false);var photo=Guid.NewGuid();await store.AddPhoto(h.Job.Id,photo,"image/png",[137,80,78,71,13,10,26,10]);await store.Close();
  store=new(path,Client(h),h.UserId);await store.Initialize();var queued=Assert.Single(await store.Pending());Assert.Contains(photo,OfflineStore.Read<Inspection>(queued.InspectionJson).AttachmentIds);await store.Sync(true);Assert.Contains(photo,h.Job.Inspection.AttachmentIds);Assert.Equal(1,h.Uploads);await store.Close();File.Delete(path);
 }

 [Fact] public async Task RejectedSubmissionCanBeCorrectedWithoutLosingArchivedEvidence()
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var h=new FakeServer{Reject=true};var store=new OfflineStore(path,Client(h),h.UserId);await store.Initialize();await store.Download();await store.SaveDraft(h.Job.Id,Draft(h),true);await store.Sync(true);
  var local=Assert.Single(await store.Jobs());Assert.Equal("Conflict",local.State);Assert.True(local.WantsSubmit);Assert.Single(await store.Pending());
  await store.Resolve(h.Job.Id,h.Job,true);local=Assert.Single(await store.Jobs());Assert.False(local.WantsSubmit);Assert.Null(OfflineStore.Read<Inspection>(local.DraftJson).SubmittedAt);Assert.Contains("submittedAt",Assert.Single(await store.Archives(local.Id)).Json);
  h.Reject=false;var corrected=OfflineStore.Read<Inspection>(local.DraftJson);corrected.Notes="Corrected inspection";await store.SaveDraft(h.Job.Id,corrected,true);await store.Sync(true);Assert.Empty(await store.Pending());Assert.Equal("Corrected inspection",h.Job.Inspection.Notes);Assert.Equal(1,h.Mutations);await store.Close();File.Delete(path);
 }
 [Fact] public async Task ServerPhotographsAreCachedForOfflineViewingAfterRestart()
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var h=new FakeServer();var photo=Guid.NewGuid();h.Job.Inspection.AttachmentIds=[photo];var store=new OfflineStore(path,Client(h),h.UserId);await store.Initialize();await store.Download();await store.Close();
  h.Offline=true;store=new(path,Client(h),h.UserId);await store.Initialize();var cached=Assert.Single(await store.Photos(h.Job.Id.ToString()));Assert.True(cached.Acknowledged);Assert.Equal(photo.ToString(),cached.Id);Assert.Equal(8,cached.Data.Length);await store.Close();File.Delete(path);
 }
 [Fact] public async Task AssignmentRestorationPersistsAccessWithoutOverwritingDirtyDraft()
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".db");var h=new FakeServer();var store=new OfflineStore(path,Client(h),h.UserId);await store.Initialize();await store.Download();await store.SaveDraft(h.Job.Id,Draft(h),false);
  h.HideJob=true;await store.Download();Assert.False(Assert.Single(await store.Jobs()).Accessible);h.HideJob=false;await store.Download();var local=Assert.Single(await store.Jobs());Assert.True(local.Accessible);Assert.Equal("Conflict",local.State);Assert.Contains("Offline notes",local.DraftJson);await store.Close();File.Delete(path);
 }

}
