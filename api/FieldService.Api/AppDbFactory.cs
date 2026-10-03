using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace FieldService.Api;
public class AppDbFactory:IDesignTimeDbContextFactory<AppDb>
{
 public AppDb CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<AppDb>().UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Database")??"Host=localhost;Port=55432;Database=fieldservice;Username=fieldservice").Options);
}
