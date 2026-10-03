using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FieldService.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
namespace FieldService.Api;
public static class Auth
{
 public static Guid UserId(this ClaimsPrincipal user)=>Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
 public static bool Supervisor(this ClaimsPrincipal user)=>user.IsInRole("Supervisor");
 public static Session Issue(User user,string key)
 {
  var expires=DateTimeOffset.UtcNow.AddHours(12);
  var token=new JwtSecurityToken("field-service","field-service-clients",new[]{new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Role,user.Role),new Claim(ClaimTypes.Name,user.Name)},expires:expires.UtcDateTime,signingCredentials:new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),SecurityAlgorithms.HmacSha256));
  return new(new JwtSecurityTokenHandler().WriteToken(token),user.Id,user.Name,user.Role,expires);
 }
 public static string Hash(User user,string password)=>new PasswordHasher<User>().HashPassword(user,password);
 public static bool Verify(User user,string password)=>new PasswordHasher<User>().VerifyHashedPassword(user,user.PasswordHash,password)!=PasswordVerificationResult.Failed;
}
