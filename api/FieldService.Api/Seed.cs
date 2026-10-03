using Microsoft.EntityFrameworkCore;
using FieldService.Contracts;
namespace FieldService.Api;
public static class Seed
{
 public static async Task Run(AppDb db,IConfiguration config)
 {
  if(await db.Users.AnyAsync()) return;
  var password=config["Seed:Password"]??throw new InvalidOperationException("Seed__Password is required for the demo seed."); Rules.Require(password.Length>=12,"Demo seed password must contain at least 12 characters.");
  var supervisor=new User{Id=Guid.NewGuid(),Name="Alex Supervisor",Email="supervisor@fieldservice.local",Role="Supervisor"};supervisor.PasswordHash=Auth.Hash(supervisor,password);
  var technician=new User{Id=Guid.NewGuid(),Name="Sam Technician",Email="technician@fieldservice.local",Role="Technician"};technician.PasswordHash=Auth.Hash(technician,password);
  var site=new Site{Id=Guid.NewGuid(),Name="Johannesburg Operations",Address="10 Service Road, Johannesburg"};
  var asset=new Asset{Id=Guid.NewGuid(),SiteId=site.Id,Identifier="PUMP-001",Name="Cooling water pump",Category="Pump",Location="Plant room A",ServiceIntervalDays=30};
  var checklist=new List<ChecklistDefinition>{new(Guid.NewGuid(),"Inspect seals for leaks"),new(Guid.NewGuid(),"Verify operating pressure"),new(Guid.NewGuid(),"Check bearings and lubrication")};
  db.Users.AddRange(supervisor,technician); db.Sites.Add(site);db.Assets.Add(asset);
  db.Parts.AddRange(new Part{Id=Guid.NewGuid(),Name="Pump seal kit",Sku="SEAL-01",Stock=20},new Part{Id=Guid.NewGuid(),Name="Bearing",Sku="BRG-01",Stock=40});
  db.WorkOrders.Add(new(){Id=Guid.NewGuid(),AssetId=asset.Id,Title="Monthly pump inspection",DueAt=DateTimeOffset.UtcNow.AddDays(1),Priority="Normal",TechnicianId=technician.Id,Status="Assigned",ChecklistJson=Json.Write(checklist)});
  db.Log(supervisor.Id,asset.Id,"Demo seeded");await db.SaveChangesAsync();
 }
}
