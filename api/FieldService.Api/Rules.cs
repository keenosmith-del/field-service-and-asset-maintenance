using FieldService.Contracts;
namespace FieldService.Api;
public class RuleException(string message):Exception(message);
public static class Rules
{
 public static void Require(bool condition,string message) { if(!condition) throw new RuleException(message); }
 public static void Text(string? value,string field,int max=1000)=>Require(!string.IsNullOrWhiteSpace(value)&&value.Length<=max,$"{field} is required (maximum {max} characters).");
 public static void Priority(string value)=>Require(new[]{"Low","Normal","High","Critical"}.Contains(value),"Invalid priority.");
 public static void Checklist(List<ChecklistDefinition> items) { Require(items.Count is >0 and <=100,"Provide 1–100 checklist items."); Require(items.Select(x=>x.Id).Distinct().Count()==items.Count&&items.All(x=>x.Id!=Guid.Empty),"Checklist IDs must be unique."); foreach(var i in items) Text(i.Label,"Checklist label",300); }
 public static void Apply(WorkOrder job,SyncMutation mutation)
 {
  Require(mutation.OperationId!=Guid.Empty,"Operation ID required."); Require(job.Status is "Assigned" or "In Progress","This work order cannot accept technician edits.");
  var i=mutation.Inspection; Require(i.Notes.Length<=10000,"Notes too long."); Require(i.StartedAt!=null&&i.StartedAt<=DateTimeOffset.UtcNow.AddMinutes(5),"A valid start time is required.");
  var definitions=Json.Read<List<ChecklistDefinition>>(job.ChecklistJson);
  Require(i.Answers.Count<=100&&i.Answers.Select(x=>x.ItemId).Distinct().Count()==i.Answers.Count,"Duplicate checklist answers.");
  Require(i.Answers.All(x=>definitions.Any(d=>d.Id==x.ItemId)&&x.Notes.Length<=2000),"Unknown checklist item or notes too long.");
  Require(i.Parts.Count<=100&&i.Parts.Select(x=>x.PartId).Distinct().Count()==i.Parts.Count&&i.Parts.All(x=>x.Quantity>0&&x.Quantity<=10000&&decimal.Round(x.Quantity,3)==x.Quantity),"Parts must be unique with positive quantities.");
  Require(i.Faults.Count<=100&&i.Faults.Select(x=>x.Id).Distinct().Count()==i.Faults.Count,"Duplicate faults."); foreach(var f in i.Faults){ Require(f.Id!=Guid.Empty,"Fault ID required."); Text(f.Description,"Fault description",2000); Require(new[]{"Low","Medium","High","Critical"}.Contains(f.Severity),"Invalid fault severity."); }
  Require(i.AttachmentIds.Count<=20&&i.AttachmentIds.Distinct().Count()==i.AttachmentIds.Count,"At most 20 unique photos.");
  Require(mutation.Action is "Save" or "Submit","Invalid mutation action.");
  if(mutation.Action=="Submit") { Require(definitions.Where(x=>x.Required).All(d=>i.Answers.Any(a=>a.ItemId==d.Id&&a.Done)),"Complete every required checklist item."); Require(i.SubmittedAt!=null&&i.SubmittedAt>=i.StartedAt&&i.SubmittedAt<=DateTimeOffset.UtcNow.AddMinutes(5),"A valid submission time is required."); job.Status="Submitted"; }
  else { Require(i.SubmittedAt==null,"Save must not include a submission time."); job.Status="In Progress"; }
  job.InspectionJson=Json.Write(i); job.Version++;
 }
}
