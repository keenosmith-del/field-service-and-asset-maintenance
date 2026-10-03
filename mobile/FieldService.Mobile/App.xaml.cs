namespace FieldService.Mobile;
public partial class App:Application
{
 readonly TechnicianViewModel vm=new();
 public App(){InitializeComponent();}
 protected override Window CreateWindow(IActivationState? activationState)=>new(new NavigationPage(new MainPage(vm)));
}
