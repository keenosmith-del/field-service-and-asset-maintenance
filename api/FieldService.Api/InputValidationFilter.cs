using System.Collections;
using System.Reflection;
namespace FieldService.Api;
// Minimal API JSON binding does not enforce C# nullable annotations. Reject null required fields
// before business rules run so malformed clients receive a useful 400 rather than a server error.
public class InputValidationFilter:IEndpointFilter
{
 public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context,EndpointFilterDelegate next)
 {
  foreach(var value in context.Arguments) if(value!=null&&IsInput(value.GetType())) Check(value,0);
  return await next(context);
 }
 static bool IsInput(Type type)=>type.Namespace=="FieldService.Contracts"||type==typeof(TechnicianInput);
 static void Check(object value,int depth)
 {
  Rules.Require(depth<8,"Input nesting too deep.");var nullability=new NullabilityInfoContext();
  foreach(var p in value.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance))
  {
   var v=p.GetValue(value);if(v==null){Rules.Require(nullability.Create(p).ReadState!=NullabilityState.NotNull,$"{p.Name} is required.");continue;}
   if(v is string text){Rules.Require(text.Length<=20000,$"{p.Name} is too long.");continue;}
   if(v is IEnumerable items){var count=0;foreach(var item in items){Rules.Require(++count<=200,"Too many entries.");Rules.Require(item!=null,$"{p.Name} contains a null entry.");if(IsInput(item!.GetType()))Check(item,depth+1);}}
   else if(IsInput(v.GetType()))Check(v,depth+1);
  }
 }
}
