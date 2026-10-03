using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FieldService.Contracts;
using FieldService.Mobile.Core;
namespace FieldService.Mobile;
public partial class JobViewModel(OfflineStore store,Guid id):ObservableObject
{
 [ObservableProperty] bool isEditable;
 [ObservableProperty] string title="";
 [ObservableProperty] string details="";
 [ObservableProperty] string state="";
 [ObservableProperty] string error="";
 [ObservableProperty] string notes="";
 [ObservableProperty] string faultDescription="";
 [ObservableProperty] string faultSeverity="Medium";
 [ObservableProperty] string quantity="1";
 [ObservableProperty] string photoCount="";
 [ObservableProperty] string comparison="";
 [ObservableProperty] bool hasComparison;
 WorkOrderDto? reviewedServer;
 [ObservableProperty] PartDto? selectedPart;
 public List<string> Severities {get;}=["Low","Medium","High","Critical"];
 public ObservableCollection<AnswerRow> Answers {get;}=[];
 public ObservableCollection<FaultEntry> Faults {get;}=[];
 public ObservableCollection<UsedPartRow> UsedParts {get;}=[];
 public ObservableCollection<PartDto> AvailableParts {get;}=[];
 public ObservableCollection<ImageSource> PhotoPreviews {get;}=[];
 Inspection inspection=new();WorkOrderDto? server;LocalJob? local;bool loading;readonly SemaphoreSlim editing=new(1,1);
 public async Task Load()
 {
  loading=true;try{local=(await store.Jobs()).Single(x=>x.Id==id.ToString());server=OfflineStore.Read<WorkOrderDto>(local.ServerJson);inspection=OfflineStore.Read<Inspection>(local.DraftJson);Title=server.Title;Details=$"{server.AssetName} · {server.AssetIdentifier}\n{server.SiteName} · {server.AssetLocation}\n{server.SiteAddress} · {server.Priority} · {server.DueAt.LocalDateTime:g}";State=$"{(local.WantsSubmit?"Submitted locally":inspection.StartedAt!=null&&server.Status=="Assigned"?"In Progress locally":server.Status)} · {local.State}";Error=local.Error;IsEditable=local.Accessible&&!local.WantsSubmit&&local.State!="Conflict"&&(server.Status is "Assigned" or "In Progress");Notes=inspection.Notes;
  Answers.Clear();foreach(var d in server.Checklist){var a=inspection.Answers.FirstOrDefault(x=>x.ItemId==d.Id);var row=new AnswerRow{Id=d.Id,Label=d.Label+(d.Required?" *":""),Done=a?.Done??false,Notes=a?.Notes??""};row.PropertyChanged+=AnswerChanged;Answers.Add(row);}Faults.Clear();foreach(var f in inspection.Faults)Faults.Add(f);AvailableParts.Clear();foreach(var p in await store.Parts())AvailableParts.Add(p);RenderParts();PhotoPreviews.Clear();foreach(var p in (await store.Photos(id.ToString())).Where(p=>inspection.AttachmentIds.Contains(Guid.Parse(p.Id)))){var bytes=p.Data;PhotoPreviews.Add(ImageSource.FromStream(()=>new MemoryStream(bytes)));}PhotoCount=$"{PhotoPreviews.Count} photos cached · {inspection.AttachmentIds.Count} referenced";
  }finally{loading=false;}
 }
 void RenderParts(){UsedParts.Clear();foreach(var p in inspection.Parts)UsedParts.Add(new(p.PartId,$"{AvailableParts.FirstOrDefault(x=>x.Id==p.PartId)?.Name??p.PartId.ToString()} × {p.Quantity}"));}
 partial void OnNotesChanged(string value){if(!loading)_=AutoSave();}
 async void AnswerChanged(object? sender,PropertyChangedEventArgs e){if(!loading)await AutoSave();}
 void Collect(){inspection.Notes=Notes;inspection.Answers=Answers.Select(x=>new ChecklistAnswer(x.Id,x.Done,x.Notes)).ToList();}
 async Task Run(Func<Task> action){await editing.WaitAsync();try{await action();}catch(Exception ex){Error=ex.Message;}finally{editing.Release();}}
 public async Task AutoSave(){if(loading||!IsEditable||inspection.StartedAt==null||local?.WantsSubmit==true||local?.State=="Conflict"||server?.Status is "Submitted" or "Completed")return;await Run(async()=>{Collect();await store.SaveDraft(id,inspection,false);State="In Progress · Pending";});}
 [RelayCommand] async Task Start()=>await Run(async()=>{inspection.StartedAt??=DateTimeOffset.UtcNow;Collect();await store.SaveDraft(id,inspection,false);await Load();});
 [RelayCommand] async Task Save()=>await AutoSave();
 [RelayCommand] async Task Submit()=>await Run(async()=>{Collect();await store.SaveDraft(id,inspection,true);await Load();});
 [RelayCommand] async Task AddFault()=>await Run(async()=>{if(string.IsNullOrWhiteSpace(FaultDescription)||FaultDescription.Length>2000)throw new InvalidOperationException("Enter a fault description of at most 2,000 characters.");inspection.Faults.Add(new(Guid.NewGuid(),FaultDescription,FaultSeverity));Collect();await store.SaveDraft(id,inspection,false);FaultDescription="";await Load();});
 [RelayCommand] async Task AddPart()=>await Run(async()=>{if(SelectedPart==null||!decimal.TryParse(Quantity,NumberStyles.Number,CultureInfo.CurrentCulture,out var qty)||qty<=0||qty>10000||decimal.Round(qty,3)!=qty)throw new InvalidOperationException("Select a part and enter a quantity up to 10,000 with at most three decimal places.");var old=inspection.Parts.FirstOrDefault(x=>x.PartId==SelectedPart.Id);if((old?.Quantity??0)+qty>10000)throw new InvalidOperationException("Total quantity must be at most 10,000.");inspection.Parts.RemoveAll(x=>x.PartId==SelectedPart.Id);inspection.Parts.Add(new(SelectedPart.Id,(old?.Quantity??0)+qty));Collect();await store.SaveDraft(id,inspection,false);await Load();});
 [RelayCommand] async Task RemoveFault(FaultEntry fault)=>await Run(async()=>{inspection.Faults.RemoveAll(x=>x.Id==fault.Id);Collect();await store.SaveDraft(id,inspection,false);await Load();});
 [RelayCommand] async Task RemovePart(UsedPartRow part)=>await Run(async()=>{inspection.Parts.RemoveAll(x=>x.PartId==part.Id);Collect();await store.SaveDraft(id,inspection,false);await Load();});
 [RelayCommand] async Task Capture()=>await Photo(true);
 [RelayCommand] async Task Pick()=>await Photo(false);
 async Task Photo(bool capture)=>await Run(async()=>
 {
  Collect();await store.SaveDraft(id,inspection,false);
  if(capture&&!MediaPicker.Default.IsCaptureSupported)throw new InvalidOperationException("Camera is unavailable; select a photo instead.");
  var file=capture?await MediaPicker.Default.CapturePhotoAsync():(await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions{SelectionLimit=1})).FirstOrDefault();if(file==null)return;
  await using var stream=await file.OpenReadAsync();using var memory=new MemoryStream();var buffer=new byte[81920];int read;while((read=await stream.ReadAsync(buffer))>0){if(memory.Length+read>5*1024*1024)throw new InvalidOperationException("Select a JPEG or PNG under 5 MB.");await memory.WriteAsync(buffer.AsMemory(0,read));}
  var bytes=memory.ToArray();var type=bytes.Length>3&&bytes[0]==0xff&&bytes[1]==0xd8?"image/jpeg":bytes.Length>=8&&bytes[0]==137&&bytes[1]==80?"image/png":throw new InvalidOperationException("Select a JPEG or PNG photo.");await store.AddPhoto(id,Guid.NewGuid(),type,bytes);await Load();Collect();await store.SaveDraft(id,inspection,false);await Load();
 });
 [RelayCommand] async Task Compare()=>await Run(async()=>
 {
  reviewedServer=await store.Current(id);Collect();Comparison=$"LOCAL DRAFT\n{OfflineStore.Write(inspection)}\nSERVER v{reviewedServer.Version} ({reviewedServer.Status})\n{OfflineStore.Write(reviewedServer.Inspection)}";HasComparison=true;
 });
 [RelayCommand] async Task Resolve()=>await Run(async()=>
 {
  if(reviewedServer==null)throw new InvalidOperationException("Compare the current server version first.");
  var page=Application.Current!.Windows[0].Page!;var choice=await page.DisplayActionSheetAsync("Resolve the compared versions. Rebase replaces the server inspection with the retained local snapshot.","Cancel",null,"Adopt server (archive local draft)","Keep local for editing (explicit rebase)");if(choice=="Cancel"||choice==null)return;
  if(!await page.DisplayAlertAsync("Resolve conflict",choice+"? The local draft will be retained in an archive. A kept draft must be submitted again after correction.","Resolve","Cancel"))return;await store.Resolve(id,reviewedServer,choice.StartsWith("Keep"));HasComparison=false;Comparison="";reviewedServer=null;await Load();
 });
 [RelayCommand] async Task Export()=>await Run(async()=>{var current=(await store.Jobs()).Single(x=>x.Id==id.ToString());var path=Path.Combine(FileSystem.CacheDirectory,$"inspection-{id}.json");await File.WriteAllTextAsync(path,OfflineStore.Write(new{current,archives=await store.Archives(id.ToString()),photos=await store.Photos(id.ToString())}));await Share.Default.RequestAsync(new ShareFileRequest("Retained inspection",new ShareFile(path)));});
}
public partial class AnswerRow:ObservableObject {public Guid Id {get;set;} public string Label {get;set;}="";[ObservableProperty] bool done;[ObservableProperty] string notes="";}
public record UsedPartRow(Guid Id,string Display);
