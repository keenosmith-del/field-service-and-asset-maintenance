namespace FieldService.Mobile;
public partial class MainPage:ContentPage
{
 readonly TechnicianViewModel vm;
 public MainPage(TechnicianViewModel vm){InitializeComponent();BindingContext=this.vm=vm;}
 protected override async void OnAppearing(){base.OnAppearing();await vm.Restore();}
}
