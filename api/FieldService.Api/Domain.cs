using System.Text.Json;
using FieldService.Contracts;
using Microsoft.EntityFrameworkCore;
namespace FieldService.Api;
public class User { public Guid Id {get;set;} public string Email {get;set;}=""; public string Name {get;set;}=""; public string PasswordHash {get;set;}=""; public string Role {get;set;}="Technician"; }
public class Site { public Guid Id {get;set;} public string Name {get;set;}=""; public string Address {get;set;}=""; }
public class Asset { public Guid Id {get;set;} public Guid SiteId {get;set;} public string Identifier {get;set;}=""; public string Name {get;set;}=""; public string Category {get;set;}=""; public string Location {get;set;}=""; public string Status {get;set;}="Active"; public int ServiceIntervalDays {get;set;}=30; }
public class MaintenanceSchedule { public Guid Id {get;set;} public Guid AssetId {get;set;} public string Name {get;set;}=""; public int IntervalDays {get;set;} public DateTimeOffset NextDueAt {get;set;} public string Priority {get;set;}="Normal"; public Guid? TechnicianId {get;set;} public string ChecklistJson {get;set;}="[]"; public bool Active {get;set;}=true; public long Version {get;set;}=1; }
public class WorkOrder { public Guid Id {get;set;} public Guid AssetId {get;set;} public Guid? ScheduleId {get;set;} public Guid? FollowUpFaultId {get;set;} public string Title {get;set;}=""; public DateTimeOffset DueAt {get;set;} public string Priority {get;set;}="Normal"; public Guid? TechnicianId {get;set;} public string Status {get;set;}="Scheduled"; public long Version {get;set;}=1; public string ChecklistJson {get;set;}="[]"; public string InspectionJson {get;set;}="{}"; public string ReviewNotes {get;set;}=""; public DateTimeOffset? CompletedAt {get;set;} }
public class Part { public Guid Id {get;set;} public string Name {get;set;}=""; public string Sku {get;set;}=""; public decimal Stock {get;set;} public long Version {get;set;}=1; }
public class Fault { public Guid Id {get;set;} public Guid AssetId {get;set;} public Guid WorkOrderId {get;set;} public string Description {get;set;}=""; public string Severity {get;set;}=""; public DateTimeOffset ReportedAt {get;set;} }
public class Attachment { public Guid Id {get;set;} public Guid WorkOrderId {get;set;} public Guid UserId {get;set;} public string ContentType {get;set;}=""; public string Hash {get;set;}=""; public byte[] Data {get;set;}=[]; }
public class Receipt { public Guid Id {get;set;} public Guid UserId {get;set;} public string RequestHash {get;set;}=""; public string ResponseJson {get;set;}=""; public DateTimeOffset CreatedAt {get;set;} }
public class Audit { public Guid Id {get;set;} public Guid UserId {get;set;} public Guid EntityId {get;set;} public string Action {get;set;}=""; public string Detail {get;set;}=""; public DateTimeOffset At {get;set;} }
public class Consumption { public Guid Id {get;set;} public Guid WorkOrderId {get;set;} public Guid PartId {get;set;} public decimal Quantity {get;set;} }
public class AppDb(DbContextOptions<AppDb> options):DbContext(options)
{
 public DbSet<User> Users=>Set<User>(); public DbSet<Site> Sites=>Set<Site>(); public DbSet<Asset> Assets=>Set<Asset>(); public DbSet<MaintenanceSchedule> Schedules=>Set<MaintenanceSchedule>(); public DbSet<WorkOrder> WorkOrders=>Set<WorkOrder>(); public DbSet<Part> Parts=>Set<Part>(); public DbSet<Fault> Faults=>Set<Fault>(); public DbSet<Attachment> Attachments=>Set<Attachment>(); public DbSet<Receipt> Receipts=>Set<Receipt>(); public DbSet<Audit> Audits=>Set<Audit>(); public DbSet<Consumption> Consumptions=>Set<Consumption>();
 protected override void OnModelCreating(ModelBuilder b)
 {
  b.Entity<User>().HasIndex(x=>x.Email).IsUnique(); b.Entity<User>().Property(x=>x.Email).HasMaxLength(254);
  b.Entity<Asset>().HasIndex(x=>x.Identifier).IsUnique(); b.Entity<Asset>().HasOne<Site>().WithMany().HasForeignKey(x=>x.SiteId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<WorkOrder>().Property(x=>x.Version).IsConcurrencyToken(); b.Entity<MaintenanceSchedule>().Property(x=>x.Version).IsConcurrencyToken(); b.Entity<Part>().Property(x=>x.Version).IsConcurrencyToken();
  b.Entity<WorkOrder>().HasIndex(x=>new{x.TechnicianId,x.Status,x.DueAt}); b.Entity<WorkOrder>().HasIndex(x=>new{x.ScheduleId,x.DueAt}).IsUnique();
  b.Entity<WorkOrder>().HasOne<Asset>().WithMany().HasForeignKey(x=>x.AssetId).OnDelete(DeleteBehavior.Restrict); b.Entity<WorkOrder>().HasOne<User>().WithMany().HasForeignKey(x=>x.TechnicianId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<WorkOrder>().HasOne<MaintenanceSchedule>().WithMany().HasForeignKey(x=>x.ScheduleId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<MaintenanceSchedule>().HasOne<Asset>().WithMany().HasForeignKey(x=>x.AssetId).OnDelete(DeleteBehavior.Restrict); b.Entity<MaintenanceSchedule>().HasOne<User>().WithMany().HasForeignKey(x=>x.TechnicianId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Part>().HasIndex(x=>x.Sku).IsUnique(); b.Entity<Part>().Property(x=>x.Stock).HasPrecision(18,3);
  b.Entity<Consumption>().HasIndex(x=>new{x.WorkOrderId,x.PartId}).IsUnique(); b.Entity<Consumption>().Property(x=>x.Quantity).HasPrecision(18,3); b.Entity<Consumption>().HasOne<WorkOrder>().WithMany().HasForeignKey(x=>x.WorkOrderId).OnDelete(DeleteBehavior.Restrict); b.Entity<Consumption>().HasOne<Part>().WithMany().HasForeignKey(x=>x.PartId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Fault>().HasOne<WorkOrder>().WithMany().HasForeignKey(x=>x.WorkOrderId).OnDelete(DeleteBehavior.Restrict); b.Entity<Fault>().HasOne<Asset>().WithMany().HasForeignKey(x=>x.AssetId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Attachment>().HasOne<WorkOrder>().WithMany().HasForeignKey(x=>x.WorkOrderId).OnDelete(DeleteBehavior.Restrict); b.Entity<Attachment>().HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict);
  b.Entity<Receipt>().HasOne<User>().WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Restrict); b.Entity<Audit>().HasIndex(x=>new{x.EntityId,x.At});
 }
 public void Log(Guid user,Guid entity,string action,string detail="")=>Audits.Add(new(){Id=Guid.NewGuid(),UserId=user,EntityId=entity,Action=action,Detail=detail,At=DateTimeOffset.UtcNow});
}
public static class Json { public static readonly JsonSerializerOptions Options=new(JsonSerializerDefaults.Web); public static string Write<T>(T value)=>JsonSerializer.Serialize(value,Options); public static T Read<T>(string value)=>JsonSerializer.Deserialize<T>(value,Options)!; }
