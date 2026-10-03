import { Component, signal, provideZonelessChangeDetection } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
interface Session { token:string; userId:string; name:string; role:string; expiresAt:string }
interface Site { id:string; name:string; address:string }
interface Asset { id:string; siteId:string; identifier:string; name:string; category:string; location:string; status:string; serviceIntervalDays:number }
interface Technician { id:string; name:string; email:string }
interface Definition { id:string; label:string; required:boolean }
interface Inspection { notes:string; startedAt:string|null; submittedAt:string|null; answers:{itemId:string;done:boolean;notes:string}[]; faults:{id:string;description:string;severity:string}[]; parts:{partId:string;quantity:number}[]; attachmentIds:string[] }
interface Job { assetLocation:string; siteAddress:string; id:string; assetId:string; assetName:string; assetIdentifier:string; siteName:string; title:string; dueAt:string; priority:string; technicianId:string|null; technicianName:string|null; status:string; version:number; checklist:Definition[]; inspection:Inspection; reviewNotes:string; completedAt:string|null }
interface Schedule {id:string;assetId:string;name:string;intervalDays:number;nextDueAt:string;priority:string;technicianId:string|null;active:boolean;checklistJson:string}
interface Fault {id:string;assetId:string;workOrderId:string;description:string;severity:string;reportedAt:string}
interface Part {id:string;name:string;sku:string;stock:number}
interface Audit {id:string;userId:string;action:string;detail:string;at:string}
interface Report {assets:number;sites:number;open:number;overdue:number;submitted:number;completed:number;faults:number;assignments:{technicianId:string|null;status:string;count:number}[];partsUsed:{name:string;quantity:number;workOrderId:string}[]}
@Component({selector:'app-root',standalone:true,imports:[CommonModule,FormsModule],templateUrl:'./app.html'})
class App {
 session=signal<Session|null>(null);busy=signal(false);error=signal('');notice=signal('');tab=signal('Overview');
 tabs=['Overview','Work orders','Assets','Sites','Schedules','Technicians','Faults','Reports'];
 email='supervisor@fieldservice.local';password='';sites=signal<Site[]>([]);assets=signal<Asset[]>([]);technicians=signal<Technician[]>([]);jobs=signal<Job[]>([]);schedules=signal<Schedule[]>([]);faults=signal<Fault[]>([]);parts=signal<Part[]>([]);report=signal<Report|null>(null);
 selected=signal<Job|null>(null);audits=signal<Audit[]>([]);photos=signal<{id:string;url:string}[]>([]);history=signal<Job[]>([]);historyAsset=signal('');filter='';assignment='';reviewNotes='';
 siteForm={id:'',name:'',address:''};assetForm={id:'',siteId:'',identifier:'',name:'',category:'',location:'',status:'Active',serviceIntervalDays:30};
 jobForm={assetId:'',title:'',dueAt:this.localDate(),priority:'Normal',technicianId:'',checklist:'Inspect equipment condition\nVerify safe operation',followUpFaultId:''};
 scheduleForm={assetId:'',name:'',intervalDays:30,nextDueAt:this.localDate(),priority:'Normal',technicianId:'',checklist:'Inspect equipment condition\nVerify safe operation'};
 technicianForm={name:'',email:'',password:''};
 constructor(){const raw=sessionStorage.getItem('field-session');if(raw){try{const s=JSON.parse(raw) as Session;if(new Date(s.expiresAt)>new Date()&&s.role==='Supervisor'){this.session.set(s);void this.refresh();}else sessionStorage.removeItem('field-session');}catch{sessionStorage.removeItem('field-session');}}}
 localDate(){const d=new Date();d.setMinutes(d.getMinutes()-d.getTimezoneOffset());return d.toISOString().slice(0,16);}
 async api<T>(path:string,method='GET',body?:unknown):Promise<T>{const r=await fetch('/api'+path,{method,headers:{'Content-Type':'application/json',...(this.session()?{'Authorization':'Bearer '+this.session()!.token}:{})},body:body===undefined?undefined:JSON.stringify(body)});if(!r.ok){let message='Request failed ('+r.status+')';try{const e=await r.json();message=e.message||e.title||message;}catch{}if(r.status===401)this.logout();throw new Error(message);}return r.status===204?undefined as T:await r.json() as T;}
 async run(action:()=>Promise<void>){if(this.busy())return;this.busy.set(true);this.error.set('');this.notice.set('');try{await action();}catch(e){this.error.set(e instanceof Error?e.message:String(e));}finally{this.busy.set(false);}}
 async login(){await this.run(async()=>{const s=await this.api<Session>('/auth/login','POST',{email:this.email,password:this.password});if(s.role!=='Supervisor')throw new Error('Use the MAUI application for technician accounts.');sessionStorage.setItem('field-session',JSON.stringify(s));this.session.set(s);this.password='';await this.load();});}
 logout(){this.session.set(null);sessionStorage.removeItem('field-session');this.closeDetail();}
 async load(){const [sites,assets,technicians,jobs,schedules,faults,report,parts]=await Promise.all([this.api<Site[]>('/sites'),this.api<Asset[]>('/assets'),this.api<Technician[]>('/technicians'),this.api<Job[]>('/work-orders'),this.api<Schedule[]>('/schedules'),this.api<Fault[]>('/faults'),this.api<Report>('/reports'),this.api<Part[]>('/parts')]);this.sites.set(sites);this.assets.set(assets);this.technicians.set(technicians);this.jobs.set(jobs);this.schedules.set(schedules);this.faults.set(faults);this.report.set(report);this.parts.set(parts);}
 async refresh(){await this.run(async()=>{await this.load();this.notice.set('Loaded current server records.');});}
 siteName(id:string){return this.sites().find(x=>x.id===id)?.name||id;}
 assetName(id:string){return this.assets().find(x=>x.id===id)?.name||id;}
 technicianName(id:string|null){return this.technicians().find(x=>x.id===id)?.name||'Unassigned';}
 partName(id:string){return this.parts().find(x=>x.id===id)?.name||id;}
 filteredJobs(){const f=this.filter.toLowerCase();return this.jobs().filter(x=>[x.title,x.assetIdentifier,x.technicianName||'',x.status].join(' ').toLowerCase().includes(f));}
 overdue(j:Job){return j.status!=='Completed'&&new Date(j.dueAt)<new Date();}
 definitions(text:string){return text.split('\n').map(x=>x.trim()).filter(Boolean).map(label=>({id:crypto.randomUUID(),label,required:true}));}
 async saveSite(){await this.run(async()=>{const {id,...body}=this.siteForm;await this.api('/sites'+(id?'/'+id:''),id?'PUT':'POST',body);this.siteForm={id:'',name:'',address:''};await this.load();this.notice.set('Site saved.');});}
 editSite(site:Site){this.siteForm={...site};}
 async deleteSite(id:string){if(!confirm('Delete this site? Sites containing assets cannot be deleted.'))return;await this.run(async()=>{await this.api('/sites/'+id,'DELETE');await this.load();});}
 async saveAsset(){await this.run(async()=>{const {id,...body}=this.assetForm;await this.api('/assets'+(id?'/'+id:''),id?'PUT':'POST',body);this.assetForm={id:'',siteId:'',identifier:'',name:'',category:'',location:'',status:'Active',serviceIntervalDays:30};await this.load();this.notice.set('Asset saved.');});}
 editAsset(asset:Asset){this.assetForm={...asset};this.tab.set('Assets');}
 async deleteAsset(id:string){if(!confirm('Delete asset? Assets with maintenance history must be retired instead.'))return;await this.run(async()=>{await this.api('/assets/'+id,'DELETE');await this.load();});}
 async assetHistory(asset:Asset){await this.run(async()=>{const r=await this.api<{workOrders:Job[]}>('/assets/'+asset.id+'/history');this.history.set(r.workOrders);this.historyAsset.set(asset.name);});}
 async createJob(){await this.run(async()=>{const f=this.jobForm;await this.api('/work-orders','POST',{...f,dueAt:new Date(f.dueAt).toISOString(),technicianId:f.technicianId||null,checklist:this.definitions(f.checklist),followUpFaultId:f.followUpFaultId||null});f.title='';f.followUpFaultId='';await this.load();this.notice.set('Work order created.');});}
 async createSchedule(){await this.run(async()=>{const f=this.scheduleForm;await this.api('/schedules','POST',{...f,nextDueAt:new Date(f.nextDueAt).toISOString(),technicianId:f.technicianId||null,checklist:this.definitions(f.checklist)});f.name='';await this.load();this.notice.set('Schedule saved. Generate work to create assignments for the next 30 days.');});}
 async generate(){await this.run(async()=>{const r=await this.api<{generated:number}>('/schedules/generate','POST',{});await this.load();this.notice.set(r.generated+' preventive work orders generated.');});}
 async toggleSchedule(s:Schedule){await this.run(async()=>{await this.api('/schedules/'+s.id+'/toggle','POST',{});await this.load();});}
 async createTechnician(){await this.run(async()=>{await this.api('/technicians','POST',this.technicianForm);this.technicianForm={name:'',email:'',password:''};await this.load();this.notice.set('Technician account created.');});}
 followUp(f:Fault){this.jobForm={...this.jobForm,assetId:f.assetId,title:'Corrective: '+f.description,priority:f.severity==='Critical'?'Critical':'High',followUpFaultId:f.id};this.tab.set('Work orders');window.scrollTo({top:0,behavior:'smooth'});}
 async open(j:Job){await this.run(async()=>{this.closeDetail();const fresh=await this.api<Job>('/work-orders/'+j.id);this.selected.set(fresh);this.assignment=fresh.technicianId||'';this.reviewNotes=fresh.reviewNotes;this.audits.set(await this.api<Audit[]>('/audit/'+j.id));const urls:{id:string;url:string}[]=[];for(const id of fresh.inspection.attachmentIds){const r=await fetch('/api/attachments/'+id,{headers:{Authorization:'Bearer '+this.session()!.token}});if(!r.ok)throw new Error('Photo load failed ('+r.status+')');urls.push({id,url:URL.createObjectURL(await r.blob())});this.photos.set([...urls]);}});}
 closeDetail(){for(const p of this.photos())URL.revokeObjectURL(p.url);this.photos.set([]);this.selected.set(null);}
 answerLabel(j:Job,id:string){return j.checklist.find(x=>x.id===id)?.label||id;}
 async assign(){await this.run(async()=>{const j=this.selected()!;this.selected.set(await this.api<Job>('/work-orders/'+j.id+'/assignment','PUT',{technicianId:this.assignment||null,version:j.version}));await this.load();this.audits.set(await this.api<Audit[]>('/audit/'+j.id));this.notice.set('Assignment updated. Offline edits on an older version will require resolution.');});}
 async review(){await this.run(async()=>{const j=this.selected()!;this.selected.set(await this.api<Job>('/work-orders/'+j.id+'/review','POST',{version:j.version,notes:this.reviewNotes}));await this.load();this.audits.set(await this.api<Audit[]>('/audit/'+j.id));this.notice.set('Inspection reviewed and completed. Asset history updated.');});}
 exportReport(){const rows=[['Work order','Asset','Site','Technician','Status','Due','Completed'],...this.jobs().map(x=>[x.title,x.assetIdentifier,x.siteName,x.technicianName||'',x.status,x.dueAt,x.completedAt||''])];const csv=rows.map(r=>r.map(x=>'"'+( /^[=+@-]/.test(x)?"'"+x:x).replaceAll('"','""')+'"').join(',')).join('\r\n');const url=URL.createObjectURL(new Blob([csv],{type:'text/csv'}));const a=document.createElement('a');a.href=url;a.download='maintenance-report.csv';a.click();URL.revokeObjectURL(url);}
}
bootstrapApplication(App,{providers:[provideZonelessChangeDetection()]}).catch(console.error);
