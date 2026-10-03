using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FieldService.Api;
using FieldService.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
var builder=WebApplication.CreateBuilder(args);
var key=builder.Configuration["Jwt:Key"]??throw new InvalidOperationException("Set Jwt__Key to a random secret of at least 32 characters.");
if(key.Length<32) throw new InvalidOperationException("Jwt__Key must contain at least 32 characters.");
builder.Services.AddDbContext<AppDb>(o=>o.UseNpgsql(builder.Configuration.GetConnectionString("Database")??throw new InvalidOperationException("ConnectionStrings__Database required.")));
builder.Services.AddScoped<JobService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>o.TokenValidationParameters=new(){ValidateIssuer=true,ValidIssuer="field-service",ValidateAudience=true,ValidAudience="field-service-clients",ValidateLifetime=true,ValidateIssuerSigningKey=true,IssuerSigningKey=new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),ClockSkew=TimeSpan.FromSeconds(30)});
builder.Services.AddAuthorization(o=>o.AddPolicy("Supervisor",p=>p.RequireRole("Supervisor")));
builder.Services.AddCors(o=>o.AddDefaultPolicy(p=>p.WithOrigins((builder.Configuration["Cors:Origins"]??"http://localhost:4200").Split(',')).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o=>o.AddPolicy("login",context=>System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new(){PermitLimit=10,Window=TimeSpan.FromMinutes(1),QueueLimit=0}))); 
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=6*1024*1024);
var app=builder.Build();
app.Use(async(ctx,next)=>
{
 try { await next(); }
 catch(RuleException e) { ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{message=e.Message}); }
 catch(DbUpdateConcurrencyException) { ctx.Response.StatusCode=409;await ctx.Response.WriteAsJsonAsync(new{message="Concurrent update; refresh and retry."}); }
 catch(Exception e) when(e is PostgresException {SqlState:"40001" or "40P01"}||e is DbUpdateException {InnerException:PostgresException {SqlState:"40001" or "40P01"}}) { ctx.Response.StatusCode=503;ctx.Response.Headers.RetryAfter="2";await ctx.Response.WriteAsJsonAsync(new{message="Concurrent transaction; retry the same operation."}); }
 catch(DbUpdateException e) when(e.InnerException is PostgresException {SqlState:"23505" or "23503"}) { ctx.Response.StatusCode=409;await ctx.Response.WriteAsJsonAsync(new{message="Identifier already exists, operation is in flight, or record is still referenced. Refresh and retry."}); }
});
app.UseCors();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();
using(var scope=app.Services.CreateScope()) { var db=scope.ServiceProvider.GetRequiredService<AppDb>();await db.Database.MigrateAsync(); if(app.Configuration.GetValue<bool>("Seed:Enabled")) await Seed.Run(db,app.Configuration); }
app.MapGet("/health",async(AppDb db)=>await db.Database.CanConnectAsync()?Results.Ok(new{status="healthy"}):Results.StatusCode(503));
app.MapPost("/api/auth/login",async(LoginRequest r,AppDb db)=>
{
 if(string.IsNullOrWhiteSpace(r.Email)||r.Email.Length>254||string.IsNullOrEmpty(r.Password)||r.Password.Length>500)return Results.BadRequest(new{message="Email and password required."});
 var user=await db.Users.SingleOrDefaultAsync(x=>x.Email==r.Email.Trim().ToLower());
 return user!=null&&Auth.Verify(user,r.Password)?Results.Ok(Auth.Issue(user,key)):Results.Json(new{message="Invalid email or password."},statusCode:401);
}).RequireRateLimiting("login");
var api=app.MapGroup("/api").RequireAuthorization();
api.AddEndpointFilter<InputValidationFilter>();
api.MapGet("/me",(ClaimsPrincipal u)=>new{id=u.UserId(),name=u.Identity!.Name,role=u.Supervisor()?"Supervisor":"Technician"});
api.MapGet("/parts",async(AppDb db)=>await db.Parts.OrderBy(x=>x.Name).Select(x=>new PartDto(x.Id,x.Name,x.Sku,x.Stock)).ToListAsync());
api.MapGet("/work-orders",async(AppDb db,JobService svc,ClaimsPrincipal u)=>
{
 var query=db.WorkOrders.AsQueryable();if(!u.Supervisor())query=query.Where(x=>x.TechnicianId==u.UserId());var result=new List<WorkOrderDto>();foreach(var w in await query.OrderBy(x=>x.DueAt).ToListAsync())result.Add(await svc.Dto(w));return Results.Ok(result);
});
api.MapGet("/work-orders/{id:guid}",async(Guid id,AppDb db,JobService svc,ClaimsPrincipal u)=> { var w=await db.WorkOrders.FindAsync(id);return w==null?Results.NotFound():!u.Supervisor()&&w.TechnicianId!=u.UserId()?Results.Forbid():Results.Ok(await svc.Dto(w)); });
api.MapGet("/sync/download",async(AppDb db,JobService svc,ClaimsPrincipal u)=>
{
 var result=new List<WorkOrderDto>();foreach(var w in await db.WorkOrders.Where(x=>x.TechnicianId==u.UserId()).OrderBy(x=>x.DueAt).ToListAsync())result.Add(await svc.Dto(w));return new DownloadBundle(result,await db.Parts.Select(x=>new PartDto(x.Id,x.Name,x.Sku,x.Stock)).ToListAsync(),DateTimeOffset.UtcNow);
}).RequireAuthorization(p=>p.RequireRole("Technician"));
api.MapPost("/sync/mutations",async(SyncMutation r,JobService svc,ClaimsPrincipal u)=>{var result=await svc.Mutate(u.UserId(),r);return Results.Json(result.Body,statusCode:result.Status);}).RequireAuthorization(p=>p.RequireRole("Technician"));
api.MapPut("/work-orders/{jobId:guid}/attachments/{id:guid}",async(Guid jobId,Guid id,HttpRequest request,AppDb db,ClaimsPrincipal u)=>
{
 var job=await db.WorkOrders.FindAsync(jobId);if(job==null)return Results.NotFound();if(job.TechnicianId!=u.UserId())return Results.Forbid();
 Rules.Require(id!=Guid.Empty,"Attachment ID required.");
 using var stream=new MemoryStream();var buffer=new byte[81920];int read;while((read=await request.Body.ReadAsync(buffer))>0){Rules.Require(stream.Length+read<=5*1024*1024,"Photo must be at most 5 MB.");await stream.WriteAsync(buffer.AsMemory(0,read));}
 var data=stream.ToArray();var type=request.ContentType?.Split(';')[0];
 var jpeg=data.Length>3&&data[0]==0xff&&data[1]==0xd8&&data[2]==0xff;var png=data.Length>=8&&data.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10});
 Rules.Require((type=="image/jpeg"&&jpeg)||(type=="image/png"&&png),"Only JPEG and PNG photos are accepted.");var hash=Convert.ToHexString(SHA256.HashData(data));
 var old=await db.Attachments.FindAsync(id);if(old!=null){Rules.Require(old.UserId==u.UserId()&&old.WorkOrderId==jobId&&old.Hash==hash,"Attachment ID collision.");return Results.Ok(new AttachmentAck(id,old.ContentType,old.Data.LongLength));}
 Rules.Require(job.Status is "Assigned" or "In Progress","Work order no longer accepts photos.");Rules.Require(await db.Attachments.CountAsync(x=>x.WorkOrderId==jobId)<20,"Photo limit reached.");
 db.Attachments.Add(new(){Id=id,WorkOrderId=jobId,UserId=u.UserId(),ContentType=type!,Hash=hash,Data=data});db.Log(u.UserId(),jobId,"Photo uploaded",id.ToString());await db.SaveChangesAsync();return Results.Ok(new AttachmentAck(id,type!,data.LongLength));
}).RequireAuthorization(p=>p.RequireRole("Technician"));
api.MapGet("/attachments/{id:guid}",async(Guid id,AppDb db,ClaimsPrincipal u,HttpResponse response)=>
{
 var a=await db.Attachments.FindAsync(id);if(a==null)return Results.NotFound();var job=await db.WorkOrders.FindAsync(a.WorkOrderId);if(!u.Supervisor()&&job?.TechnicianId!=u.UserId())return Results.Forbid();response.Headers["X-Content-Type-Options"]="nosniff";return Results.File(a.Data,a.ContentType);
});
var admin=api.MapGroup("").RequireAuthorization("Supervisor");
admin.MapGet("/sites",async(AppDb db)=>await db.Sites.OrderBy(x=>x.Name).ToListAsync());
admin.MapPost("/sites",async(SiteInput r,AppDb db,ClaimsPrincipal u)=>{Rules.Text(r.Name,"Name",200);Rules.Text(r.Address,"Address",500);var s=new Site{Id=Guid.NewGuid(),Name=r.Name,Address=r.Address};db.Sites.Add(s);db.Log(u.UserId(),s.Id,"Site created");await db.SaveChangesAsync();return Results.Ok(s);});
admin.MapPut("/sites/{id:guid}",async(Guid id,SiteInput r,AppDb db,ClaimsPrincipal u)=>{var s=await db.Sites.FindAsync(id);if(s==null)return Results.NotFound();Rules.Text(r.Name,"Name",200);Rules.Text(r.Address,"Address",500);s.Name=r.Name;s.Address=r.Address;db.Log(u.UserId(),id,"Site updated");await db.SaveChangesAsync();return Results.Ok(s);});
admin.MapDelete("/sites/{id:guid}",async(Guid id,AppDb db,ClaimsPrincipal u)=>{var s=await db.Sites.FindAsync(id);if(s==null)return Results.NotFound();Rules.Require(!await db.Assets.AnyAsync(x=>x.SiteId==id),"Remove or relocate assets first.");db.Sites.Remove(s);db.Log(u.UserId(),id,"Site deleted");await db.SaveChangesAsync();return Results.NoContent();});
admin.MapGet("/assets",async(AppDb db)=>await db.Assets.OrderBy(x=>x.Name).ToListAsync());
admin.MapPost("/assets",async(AssetInput r,AppDb db,ClaimsPrincipal u)=>{await ValidateAsset(r,db);var a=new Asset{Id=Guid.NewGuid()};SetAsset(a,r);db.Assets.Add(a);db.Log(u.UserId(),a.Id,"Asset created");await db.SaveChangesAsync();return Results.Ok(a);});
admin.MapPut("/assets/{id:guid}",async(Guid id,AssetInput r,AppDb db,ClaimsPrincipal u)=>{var a=await db.Assets.FindAsync(id);if(a==null)return Results.NotFound();await ValidateAsset(r,db);SetAsset(a,r);db.Log(u.UserId(),id,"Asset updated");await db.SaveChangesAsync();return Results.Ok(a);});
admin.MapDelete("/assets/{id:guid}",async(Guid id,AppDb db,ClaimsPrincipal u)=>{var a=await db.Assets.FindAsync(id);if(a==null)return Results.NotFound();Rules.Require(!await db.WorkOrders.AnyAsync(x=>x.AssetId==id)&&!await db.Schedules.AnyAsync(x=>x.AssetId==id),"Asset has maintenance records. Set it to Retired to retain history.");db.Assets.Remove(a);db.Log(u.UserId(),id,"Asset deleted");await db.SaveChangesAsync();return Results.NoContent();});
admin.MapGet("/technicians",async(AppDb db)=>await db.Users.Where(x=>x.Role=="Technician").Select(x=>new TechnicianDto(x.Id,x.Name,x.Email)).ToListAsync());
admin.MapPost("/technicians",async(TechnicianInput r,AppDb db,ClaimsPrincipal u)=>{Rules.Text(r.Name,"Name",200);Rules.Require(System.Net.Mail.MailAddress.TryCreate(r.Email,out _)&&r.Email.Length<=254,"Valid email required.");Rules.Require(r.Password.Length>=12&&r.Password.Length<=200,"Password must contain 12–200 characters.");var t=new User{Id=Guid.NewGuid(),Name=r.Name,Email=r.Email.Trim().ToLower(),Role="Technician"};t.PasswordHash=Auth.Hash(t,r.Password);db.Users.Add(t);db.Log(u.UserId(),t.Id,"Technician created");await db.SaveChangesAsync();return Results.Ok(new TechnicianDto(t.Id,t.Name,t.Email));});
admin.MapGet("/schedules",async(AppDb db)=>await db.Schedules.OrderBy(x=>x.NextDueAt).ToListAsync());
admin.MapPost("/schedules",async(ScheduleInput r,AppDb db,ClaimsPrincipal u)=>{await ValidateJob(r.AssetId,r.TechnicianId,r.Name,r.Priority,r.Checklist,db);Rules.Require(r.IntervalDays is >0 and <=3650,"Interval must be 1–3650 days.");var s=new MaintenanceSchedule{Id=Guid.NewGuid(),AssetId=r.AssetId,Name=r.Name,IntervalDays=r.IntervalDays,NextDueAt=r.NextDueAt.ToUniversalTime(),Priority=r.Priority,TechnicianId=r.TechnicianId,ChecklistJson=Json.Write(r.Checklist)};db.Schedules.Add(s);db.Log(u.UserId(),s.Id,"Schedule created");await db.SaveChangesAsync();return Results.Ok(s);});
admin.MapPost("/schedules/{id:guid}/toggle",async(Guid id,AppDb db,ClaimsPrincipal u)=>{var s=await db.Schedules.FindAsync(id);if(s==null)return Results.NotFound();s.Active=!s.Active;s.Version++;db.Log(u.UserId(),id,s.Active?"Schedule enabled":"Schedule paused");await db.SaveChangesAsync();return Results.Ok(s);});
admin.MapPost("/schedules/generate",async(AppDb db,ClaimsPrincipal u)=>
{
 await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);var count=0;var horizon=DateTimeOffset.UtcNow.AddDays(30);
 foreach(var s in await db.Schedules.Where(x=>x.Active&&x.NextDueAt<=horizon).OrderBy(x=>x.Id).ToListAsync())
 { var iterations=0;while(s.NextDueAt<=horizon&&iterations++<100){if(!await db.WorkOrders.AnyAsync(x=>x.ScheduleId==s.Id&&x.DueAt==s.NextDueAt)){var w=new WorkOrder{Id=Guid.NewGuid(),AssetId=s.AssetId,ScheduleId=s.Id,Title=s.Name,DueAt=s.NextDueAt,Priority=s.Priority,TechnicianId=s.TechnicianId,Status=s.TechnicianId==null?"Scheduled":"Assigned",ChecklistJson=s.ChecklistJson};db.WorkOrders.Add(w);db.Log(u.UserId(),w.Id,"Preventive work generated");count++;}s.NextDueAt=s.NextDueAt.AddDays(s.IntervalDays);}s.Version++; }
 await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{generated=count});
});
admin.MapPost("/work-orders",async(WorkOrderInput r,AppDb db,JobService svc,ClaimsPrincipal u)=>{await ValidateJob(r.AssetId,r.TechnicianId,r.Title,r.Priority,r.Checklist,db);if(r.FollowUpFaultId!=null)Rules.Require(await db.Faults.AnyAsync(x=>x.Id==r.FollowUpFaultId&&x.AssetId==r.AssetId),"Follow-up fault must belong to this asset.");var w=new WorkOrder{Id=Guid.NewGuid(),AssetId=r.AssetId,Title=r.Title,DueAt=r.DueAt.ToUniversalTime(),Priority=r.Priority,TechnicianId=r.TechnicianId,Status=r.TechnicianId==null?"Scheduled":"Assigned",ChecklistJson=Json.Write(r.Checklist),FollowUpFaultId=r.FollowUpFaultId};db.WorkOrders.Add(w);db.Log(u.UserId(),w.Id,"Work order created");await db.SaveChangesAsync();return Results.Ok(await svc.Dto(w));});
admin.MapPut("/work-orders/{id:guid}/assignment",async(Guid id,AssignmentInput r,AppDb db,JobService svc,ClaimsPrincipal u)=>{var w=await db.WorkOrders.FindAsync(id);if(w==null)return Results.NotFound();if(w.Version!=r.Version)return Results.Conflict(new ConflictResponse("Refresh before assigning.",await svc.Dto(w)));Rules.Require(w.Status is "Scheduled" or "Assigned" or "In Progress","Submitted work cannot be reassigned.");await Technician(r.TechnicianId,db);w.TechnicianId=r.TechnicianId;if(w.Status!="In Progress")w.Status=r.TechnicianId==null?"Scheduled":"Assigned";w.Version++;db.Log(u.UserId(),id,"Assignment updated",r.TechnicianId?.ToString()??"Unassigned");await db.SaveChangesAsync();return Results.Ok(await svc.Dto(w));});
admin.MapPost("/work-orders/{id:guid}/review",async(Guid id,ReviewInput r,AppDb db,JobService svc,ClaimsPrincipal u)=>{var w=await db.WorkOrders.FindAsync(id);if(w==null)return Results.NotFound();if(w.Version!=r.Version)return Results.Conflict(new ConflictResponse("Refresh before reviewing.",await svc.Dto(w)));Rules.Require(w.Status=="Submitted","Only submitted work can be completed.");Rules.Text(r.Notes,"Review notes",5000);w.Status="Completed";w.CompletedAt=DateTimeOffset.UtcNow;w.ReviewNotes=r.Notes;w.Version++;db.Log(u.UserId(),id,"Reviewed and completed",r.Notes);await db.SaveChangesAsync();return Results.Ok(await svc.Dto(w));});
admin.MapGet("/faults",async(AppDb db)=>await db.Faults.OrderByDescending(x=>x.ReportedAt).ToListAsync());
admin.MapGet("/assets/{id:guid}/history",async(Guid id,AppDb db,JobService svc)=>{var history=new List<WorkOrderDto>();foreach(var w in await db.WorkOrders.Where(x=>x.AssetId==id).OrderByDescending(x=>x.DueAt).ToListAsync())history.Add(await svc.Dto(w));return Results.Ok(new{workOrders=history,faults=await db.Faults.Where(x=>x.AssetId==id).ToListAsync()});});
admin.MapGet("/audit/{id:guid}",async(Guid id,AppDb db)=>await db.Audits.Where(x=>x.EntityId==id).OrderBy(x=>x.At).ToListAsync());
admin.MapGet("/reports",async(AppDb db)=>new { assets=await db.Assets.CountAsync(),sites=await db.Sites.CountAsync(),open=await db.WorkOrders.CountAsync(x=>x.Status!="Completed"),overdue=await db.WorkOrders.CountAsync(x=>x.Status!="Completed"&&x.DueAt<DateTimeOffset.UtcNow),submitted=await db.WorkOrders.CountAsync(x=>x.Status=="Submitted"),completed=await db.WorkOrders.CountAsync(x=>x.Status=="Completed"),faults=await db.Faults.CountAsync(),assignments=await db.WorkOrders.GroupBy(x=>new{x.TechnicianId,x.Status}).Select(g=>new{g.Key.TechnicianId,g.Key.Status,count=g.Count()}).ToListAsync(),partsUsed=await db.Consumptions.Join(db.Parts,c=>c.PartId,p=>p.Id,(c,p)=>new{p.Name,c.Quantity,c.WorkOrderId}).ToListAsync() });
app.Run();
static async Task Technician(Guid? id,AppDb db) { if(id!=null)Rules.Require(await db.Users.AnyAsync(x=>x.Id==id&&x.Role=="Technician"),"Technician not found."); }
static async Task ValidateJob(Guid asset,Guid? technician,string title,string priority,List<ChecklistDefinition> checklist,AppDb db){Rules.Text(title,"Title",300);Rules.Priority(priority);Rules.Checklist(checklist);Rules.Require(await db.Assets.AnyAsync(x=>x.Id==asset&&x.Status!="Retired"),"Select an active asset.");await Technician(technician,db);}
static async Task ValidateAsset(AssetInput r,AppDb db){Rules.Text(r.Name,"Name",200);Rules.Text(r.Identifier,"Identifier",100);Rules.Text(r.Category,"Category",100);Rules.Text(r.Location,"Location",300);Rules.Require(new[]{"Active","Out of Service","Retired"}.Contains(r.Status),"Invalid asset status.");Rules.Require(r.ServiceIntervalDays is >0 and <=3650,"Service interval must be 1–3650 days.");Rules.Require(await db.Sites.AnyAsync(x=>x.Id==r.SiteId),"Site not found.");}
static void SetAsset(Asset a,AssetInput r){a.SiteId=r.SiteId;a.Identifier=r.Identifier;a.Name=r.Name;a.Category=r.Category;a.Location=r.Location;a.Status=r.Status;a.ServiceIntervalDays=r.ServiceIntervalDays;}
public record TechnicianInput(string Name,string Email,string Password);
public partial class Program {}
