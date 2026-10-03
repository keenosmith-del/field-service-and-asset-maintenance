namespace FieldService.Mobile;
public partial class JobPage:ContentPage
{
 readonly JobViewModel vm;
 public JobPage(JobViewModel vm){InitializeComponent();BindingContext=this.vm=vm;}
 protected override async void OnAppearing(){base.OnAppearing();try{await vm.Load();}catch(Exception ex){vm.Error=ex.Message;}}
 protected override async void OnDisappearing(){await vm.AutoSave();base.OnDisappearing();}
}
