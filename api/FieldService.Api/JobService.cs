using System.Data;
using System.Security.Cryptography;
using System.Text;
using FieldService.Contracts;
using Microsoft.EntityFrameworkCore;
namespace FieldService.Api;
public class JobService(AppDb db)
{
 public async Task<WorkOrderDto> Dto(WorkOrder w)
 {
  var a=await db.Assets.SingleAsync(x=>x.Id==w.AssetId);var s=await db.Sites.SingleAsync(x=>x.Id==a.SiteId);var t=w.TechnicianId==null?null:await db.Users.SingleAsync(x=>x.Id==w.TechnicianId);
  return new(w.Id,a.Id,a.Name,a.Identifier,s.Name,w.Title,w.DueAt,w.Priority,w.TechnicianId,t?.Name,w.Status,w.Version,Json.Read<List<ChecklistDefinition>>(w.ChecklistJson),Json.Read<Inspection>(w.InspectionJson),w.ReviewNotes,w.CompletedAt){AssetLocation=a.Location,SiteAddress=s.Address};
 }
 public async Task<(int Status,object Body)> Mutate(Guid userId,SyncMutation request)
 {
  var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json.Write(request))));
  await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
  // One durable receipt per operation, including the exact acknowledgment, in the business transaction.
  var receipt=await db.Receipts.FindAsync(request.OperationId);
  if(receipt!=null){ if(receipt.UserId!=userId||receipt.RequestHash!=hash) return(409,new{message="Operation ID already used with a different payload or identity."}); return(200,Json.Read<SyncAck>(receipt.ResponseJson)); }
  var job=await db.WorkOrders.SingleOrDefaultAsync(x=>x.Id==request.WorkOrderId);
  if(job==null) return(404,new{message="Work order not found."});
  if(job.TechnicianId!=userId) return(403,new{message="Work order is no longer assigned to you."});
  if(job.Version!=request.BaseVersion) return(409,new ConflictResponse("Work order changed on the server. Resolve explicitly.",await Dto(job)));
  Rules.Apply(job,request);
  foreach(var id in request.Inspection.AttachmentIds) Rules.Require(await db.Attachments.AnyAsync(x=>x.Id==id&&x.WorkOrderId==job.Id),"Upload and acknowledge every photo before submitting the inspection.");
  foreach(var p in request.Inspection.Parts) Rules.Require(await db.Parts.AnyAsync(x=>x.Id==p.PartId),"Unknown part.");
  if(request.Action=="Submit")
  {
   foreach(var use in request.Inspection.Parts.OrderBy(x=>x.PartId))
   {
    var part=await db.Parts.SingleAsync(x=>x.Id==use.PartId);Rules.Require(part.Stock>=use.Quantity,$"Insufficient stock: {part.Name}.");part.Stock-=use.Quantity;part.Version++;
    db.Consumptions.Add(new(){Id=Guid.NewGuid(),WorkOrderId=job.Id,PartId=part.Id,Quantity=use.Quantity});
   }
   foreach(var fault in request.Inspection.Faults) { Rules.Require(!await db.Faults.AnyAsync(x=>x.Id==fault.Id),"Fault ID already used."); db.Faults.Add(new(){Id=fault.Id,AssetId=job.AssetId,WorkOrderId=job.Id,Description=fault.Description,Severity=fault.Severity,ReportedAt=DateTimeOffset.UtcNow}); }
  }
  var ack=new SyncAck(request.OperationId,await Dto(job));
  db.Receipts.Add(new(){Id=request.OperationId,UserId=userId,RequestHash=hash,ResponseJson=Json.Write(ack),CreatedAt=DateTimeOffset.UtcNow});db.Log(userId,job.Id,request.Action,$"Operation {request.OperationId}; version {job.Version}");
  await db.SaveChangesAsync();await tx.CommitAsync();return(200,ack);
 }
}
