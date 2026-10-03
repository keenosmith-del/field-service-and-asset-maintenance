using FieldService.Api;
using FieldService.Contracts;
using Xunit;
namespace FieldService.Tests;
public class RulesTests
{
 static WorkOrder Job()=>new(){Id=Guid.NewGuid(),Status="Assigned",ChecklistJson=Json.Write(new List<ChecklistDefinition>{new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),"Inspect")})};
 static Inspection Inspection(bool complete)=>new(){StartedAt=DateTimeOffset.UtcNow.AddMinutes(-1),SubmittedAt=DateTimeOffset.UtcNow,Answers=[new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),complete,"")]};
 [Fact] public void SubmissionRequiresChecklist(){var w=Job();Assert.Throws<RuleException>(()=>Rules.Apply(w,new(Guid.NewGuid(),w.Id,1,"Submit",Inspection(false))));Assert.Equal("Assigned",w.Status);}
 [Fact] public void TechnicianCannotComplete(){var w=Job();Assert.Throws<RuleException>(()=>Rules.Apply(w,new(Guid.NewGuid(),w.Id,1,"Complete",Inspection(true))));}
 [Fact] public void ClosedWorkRejectsChanges(){var w=Job();w.Status="Completed";Assert.Throws<RuleException>(()=>Rules.Apply(w,new(Guid.NewGuid(),w.Id,1,"Submit",Inspection(true))));}
 [Fact] public void ValidSubmissionIsOnlySubmitted(){var w=Job();Rules.Apply(w,new(Guid.NewGuid(),w.Id,1,"Submit",Inspection(true)));Assert.Equal("Submitted",w.Status);Assert.Equal(2,w.Version);Assert.Null(w.CompletedAt);}
 [Fact] public void DuplicatePartsRejected(){var w=Job();var i=Inspection(true);var id=Guid.NewGuid();i.Parts=[new(id,1),new(id,2)];Assert.Throws<RuleException>(()=>Rules.Apply(w,new(Guid.NewGuid(),w.Id,1,"Submit",i)));}
 [Fact] public void PasswordIsHashedAndVerified(){var u=new User();u.PasswordHash=Auth.Hash(u,"test-password-long");Assert.NotEqual("test-password-long",u.PasswordHash);Assert.True(Auth.Verify(u,"test-password-long"));Assert.False(Auth.Verify(u,"wrong"));}
}
