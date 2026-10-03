using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FieldService.Contracts;
using FieldService.Mobile.Core;
namespace FieldService.Mobile;
public partial class TechnicianViewModel:ObservableObject
{
 [ObservableProperty] string apiUrl=DeviceInfo.Platform==DevicePlatform.Android?"http://10.0.2.2:8080/":"http://localhost:8080/";
 [ObservableProperty] string email="technician@fieldservice.local";
 [ObservableProperty] string password="";
 [ObservableProperty] string message="";
 [ObservableProperty] string connectionStatus="";
 [ObservableProperty] string userName="";
 [ObservableProperty] bool isSignedIn;
 [ObservableProperty] bool isBusy;
 public bool ShowLogin=>!IsSignedIn;
 partial void OnIsSignedInChanged(bool value)=>OnPropertyChanged(nameof(ShowLogin));
 public ObservableCollection<JobRow> Jobs {get;}=[];
 public OfflineStore? Store {get;private set;}
 HttpClient? client;bool restored;readonly SemaphoreSlim commands=new(1,1);
 public TechnicianViewModel(){Connectivity.ConnectivityChanged+=OnConnectivity;ConnectionStatus=Connectivity.NetworkAccess==NetworkAccess.Internet?"Online":"Offline · local work available";_ = RetryLoop();}
 async Task RetryLoop(){using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30));while(await timer.WaitForNextTickAsync()){if(IsSignedIn&&Connectivity.NetworkAccess==NetworkAccess.Internet)await Run(()=>Store!.Sync(),"Synchronization checked");}}
 async void OnConnectivity(object? sender,ConnectivityChangedEventArgs e){ConnectionStatus=e.NetworkAccess==NetworkAccess.Internet?"Online":"Offline · local work available";if(e.NetworkAccess==NetworkAccess.Internet&&IsSignedIn)await Run(()=>Store!.Sync(),"Connectivity restored; synchronization checked");}
 public async Task Restore(){if(restored){await Refresh();return;}restored=true;try{var json=await SecureStorage.Default.GetAsync("session");var url=await SecureStorage.Default.GetAsync("api-url");if(json!=null&&url!=null){ApiUrl=url;await Connect(OfflineStore.Read<Session>(json));await Refresh();Message="Cached assignments restored. Sign in online again if the session has expired.";}}catch(Exception ex){Message=ex.Message;}}
 async Task Connect(Session session)
 {
  if(Store!=null)await Store.Close();client?.Dispose();client=new HttpClient{BaseAddress=new Uri(ApiUrl.EndsWith('/')?ApiUrl:ApiUrl+"/"),Timeout=TimeSpan.FromSeconds(30)};client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",session.Token);
  Store=new OfflineStore(Path.Combine(FileSystem.AppDataDirectory,$"technician-{session.UserId}.db"),client,session.UserId);await Store.Initialize();UserName=session.Name;IsSignedIn=true;
 }
 [RelayCommand] async Task Login()=>await Run(async()=>{using var login=new HttpClient{BaseAddress=new Uri(ApiUrl.EndsWith('/')?ApiUrl:ApiUrl+"/"),Timeout=TimeSpan.FromSeconds(30)};var response=await login.PostAsJsonAsync("api/auth/login",new LoginRequest(Email,Password));if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Sign-in failed. Check credentials and API URL.");var session=(await response.Content.ReadFromJsonAsync<Session>(OfflineStore.JsonOptions))!;if(session.Role!="Technician")throw new InvalidOperationException("Use a technician account in this app.");await SecureStorage.Default.SetAsync("session",OfflineStore.Write(session));await SecureStorage.Default.SetAsync("api-url",ApiUrl);await Connect(session);Password="";await Store!.Download();},"Signed in and downloaded jobs");
 [RelayCommand] async Task Download()=>await Run(()=>Store!.Download(),"Assignments downloaded; pending edits retained");
 [RelayCommand] async Task Sync()=>await Run(()=>Store!.Sync(true),"Sync checked. Inspect job states for pending work or conflicts.");
 [RelayCommand] async Task Logout()=>await Run(async()=>{SecureStorage.Default.Remove("session");if(Store!=null)await Store.Close();Store=null;client?.Dispose();IsSignedIn=false;Jobs.Clear();},"Signed out; SQLite work retained per account");
 [RelayCommand] async Task Open(JobRow row){if(Store==null)return;await Application.Current!.Windows[0].Page!.Navigation.PushAsync(new JobPage(new JobViewModel(Store,row.Id)));}
 async Task Run(Func<Task> action,string success)
 {
  if(!await commands.WaitAsync(0))return;IsBusy=true;Message="Working… Local edits remain in SQLite until acknowledged.";try{await action();Message=success;}catch(Exception ex){Message=ex.Message;}finally{if(Store!=null)await Refresh();IsBusy=false;commands.Release();}
 }
 public async Task Refresh(){if(Store==null)return;var jobs=await Store.Jobs();await MainThread.InvokeOnMainThreadAsync(()=>{Jobs.Clear();foreach(var local in jobs.OrderBy(x=>OfflineStore.Read<WorkOrderDto>(x.ServerJson).DueAt)){var j=OfflineStore.Read<WorkOrderDto>(local.ServerJson);Jobs.Add(new(j.Id,j.Title,$"{j.AssetIdentifier} · {j.SiteName} · due {j.DueAt.LocalDateTime:g}",$"{j.Status} · {local.State}"));}});}
}
public record JobRow(Guid Id,string Title,string Summary,string State);
