#!/usr/bin/env python3
"""Real API/PostgreSQL integration checks. Creates uniquely named test data; retains it for audit."""
import os, json, uuid, urllib.request, urllib.error, datetime, base64
from pathlib import Path
BASE=os.environ.get('API_URL','http://localhost:8080')
settings={}
if Path('.env').exists(): settings=dict(line.split('=',1) for line in Path('.env').read_text().splitlines() if '=' in line and not line.startswith('#'))
password=os.environ.get('DEMO_PASSWORD',settings.get('DEMO_PASSWORD'))
assert password,'Set DEMO_PASSWORD or run scripts/setup.sh'
def call(path,method='GET',body=None,token=None,expected=200,content_type='application/json'):
 data=body if isinstance(body,bytes) else json.dumps(body).encode() if body is not None else None
 headers={'Content-Type':content_type}
 if token:headers['Authorization']='Bearer '+token
 try:
  with urllib.request.urlopen(urllib.request.Request(BASE+path,data=data,headers=headers,method=method),timeout=30) as r:status=r.status;raw=r.read()
 except urllib.error.HTTPError as e:status=e.code;raw=e.read()
 assert status==expected,f'{method} {path}: expected {expected}, got {status}: {raw.decode(errors="replace")}'
 return json.loads(raw) if raw and content_type=='application/json' else raw
supervisor=call('/api/auth/login','POST',{'email':'supervisor@fieldservice.local','password':password})
tech=call('/api/auth/login','POST',{'email':'technician@fieldservice.local','password':password})
a=supervisor['token'];t=tech['token']
call('/api/assets',expected=401)
call('/api/sites',token=t,expected=403)
call('/api/auth/login','POST',{'email':'technician@fieldservice.local','password':'incorrect'},expected=401)
prefix='CHECK-'+str(uuid.uuid4())[:8]
site=call('/api/sites','POST',{'name':prefix,'address':'Integration verification site'},a)
asset=call('/api/assets','POST',{'siteId':site['id'],'name':prefix+' Pump','identifier':prefix,'category':'Pump','location':'Plant room','status':'Active','serviceIntervalDays':30},a)
checklist=[{'id':str(uuid.uuid4()),'label':'Inspect seals','required':True}]
now=lambda:datetime.datetime.now(datetime.timezone.utc).isoformat()
job=call('/api/work-orders','POST',{'assetId':asset['id'],'title':prefix+' Inspection','dueAt':now(),'priority':'High','technicianId':tech['userId'],'checklist':checklist},a)
parts=call('/api/parts',token=t);part=parts[0];before=part['stock']
photo_id=str(uuid.uuid4());png=base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9ZlSAAAAAASUVORK5CYII=')
for _ in range(2):call(f'/api/work-orders/{job["id"]}/attachments/{photo_id}','PUT',png,t,content_type='image/png')
inspection={'notes':'Offline inspection recovered and synchronized','startedAt':now(),'submittedAt':now(),'answers':[{'itemId':checklist[0]['id'],'done':True,'notes':'Seals inspected'}],'faults':[{'id':str(uuid.uuid4()),'description':'Seal wear','severity':'Medium'}],'parts':[{'partId':part['id'],'quantity':1}],'attachmentIds':[photo_id]}
mutation={'operationId':str(uuid.uuid4()),'workOrderId':job['id'],'baseVersion':job['version'],'action':'Submit','inspection':inspection}
bad=json.loads(json.dumps(mutation));bad['operationId']=str(uuid.uuid4());bad['inspection']['answers'][0]['done']=False
call('/api/sync/mutations','POST',bad,t,expected=400)
malformed=json.loads(json.dumps(mutation));malformed['inspection']['answers']=None
call('/api/sync/mutations','POST',malformed,t,expected=400)
call(f'/api/work-orders/{job["id"]}/attachments/{uuid.uuid4()}','PUT',b'not an image',t,expected=400,content_type='image/png')
# Two in-flight retries must either return the receipt or a retryable concurrency response.
from concurrent.futures import ThreadPoolExecutor
import time
def submit_retry():
 for attempt in range(6):
  try:return call('/api/sync/mutations','POST',mutation,t)
  except AssertionError as e:
   if not any(code in str(e) for code in ['got 503','got 409']):raise
   time.sleep(0.15*(attempt+1))
 raise AssertionError('Concurrent submission did not settle')
with ThreadPoolExecutor(max_workers=2) as pool:
 ack,again=list(pool.map(lambda _:submit_retry(),range(2)))
assert ack==again,'Idempotent acknowledgment changed'
assert ack['workOrder']['status']=='Submitted'
after=next(p['stock'] for p in call('/api/parts',token=t) if p['id']==part['id']);assert after==before-1,'Duplicate part consumption'
changed=json.loads(json.dumps(mutation));changed['inspection']['notes']='tampered replay';call('/api/sync/mutations','POST',changed,t,expected=409)
stale=json.loads(json.dumps(mutation));stale['operationId']=str(uuid.uuid4());call('/api/sync/mutations','POST',stale,t,expected=409)
call(f'/api/work-orders/{job["id"]}/review','POST',{'version':ack['workOrder']['version'],'notes':'Verified'},t,expected=403)
reviewed=call(f'/api/work-orders/{job["id"]}/review','POST',{'version':ack['workOrder']['version'],'notes':'Verified checklist, photo and parts'},a);assert reviewed['status']=='Completed'
history=call(f'/api/assets/{asset["id"]}/history',token=a);assert history['workOrders'][0]['status']=='Completed';assert len(history['faults'])==1
report=call('/api/reports',token=a);assert report['completed']>=1
call(f'/api/work-orders/{job["id"]}/assignment','PUT',{'technicianId':None,'version':reviewed['version']},a,expected=400)
# Scheduling occurrences and repeated generation must not duplicate work.
schedule=call('/api/schedules','POST',{'assetId':asset['id'],'name':prefix+' Preventive','intervalDays':30,'nextDueAt':now(),'priority':'Normal','technicianId':tech['userId'],'checklist':checklist},a)
call('/api/schedules/generate','POST',{},a);second=call('/api/schedules/generate','POST',{},a);assert second['generated']==0
# Another technician must not edit or view this assignment.
other=call('/api/technicians','POST',{'name':prefix+' Other','email':prefix.lower()+'@test.local','password':password},a)
other_session=call('/api/auth/login','POST',{'email':other['email'],'password':password})
foreign=json.loads(json.dumps(mutation));foreign['operationId']=str(uuid.uuid4())
call('/api/sync/mutations','POST',foreign,other_session['token'],expected=403)
call(f'/api/work-orders/{job["id"]}',token=other_session['token'],expected=403)
handoff=call('/api/work-orders','POST',{'assetId':asset['id'],'title':prefix+' Handoff','dueAt':now(),'priority':'Normal','technicianId':tech['userId'],'checklist':checklist},a)
assert handoff['assetLocation']=='Plant room'
handoff_photo=str(uuid.uuid4());call(f'/api/work-orders/{handoff["id"]}/attachments/{handoff_photo}','PUT',png,t,content_type='image/png')
draft={'startedAt':now(),'notes':'Retain earlier technician evidence','answers':[{'itemId':checklist[0]['id'],'done':True,'notes':'Checked'}],'faults':[],'parts':[],'attachmentIds':[handoff_photo]}
progress=call('/api/sync/mutations','POST',{'operationId':str(uuid.uuid4()),'workOrderId':handoff['id'],'baseVersion':1,'action':'Save','inspection':draft},t)
reassigned=call(f'/api/work-orders/{handoff["id"]}/assignment','PUT',{'technicianId':other['id'],'version':progress['workOrder']['version']},a)
continued=call('/api/sync/mutations','POST',{'operationId':str(uuid.uuid4()),'workOrderId':handoff['id'],'baseVersion':reassigned['version'],'action':'Save','inspection':draft},other_session['token'])
assert continued['workOrder']['inspection']['attachmentIds']==[handoff_photo]
# Management edits are persisted and stale edits rejected; retired assets cannot generate future work.
profile=call(f'/api/technicians/{other["id"]}','PUT',{'name':prefix+' Updated','email':other['email']},a);assert profile['name'].endswith('Updated')
stock=next(p for p in call('/api/parts',token=a) if p['id']==part['id'])
call(f'/api/parts/{part["id"]}/stock','PUT',{'stock':stock['stock']+1,'version':stock['version']},t,expected=403)
replenished=call(f'/api/parts/{part["id"]}/stock','PUT',{'stock':stock['stock']+1,'version':stock['version']},a);assert replenished['stock']==before
call(f'/api/parts/{part["id"]}/stock','PUT',{'stock':stock['stock'],'version':stock['version']},a,expected=409)
current=next(s for s in call('/api/schedules',token=a) if s['id']==schedule['id'])
updated={'assetId':asset['id'],'name':prefix+' Updated schedule','intervalDays':60,'nextDueAt':now(),'priority':'High','technicianId':tech['userId'],'checklist':checklist}
edited=call(f'/api/schedules/{schedule["id"]}','PUT',{'schedule':updated,'version':current['version']},a);assert edited['intervalDays']==60
call(f'/api/schedules/{schedule["id"]}','PUT',{'schedule':updated,'version':current['version']},a,expected=409)
paused=call(f'/api/schedules/{schedule["id"]}/toggle','POST',{'version':edited['version']},a);assert not paused['active']
enabled=call(f'/api/schedules/{schedule["id"]}/toggle','POST',{'version':paused['version']},a);assert enabled['active']
call(f'/api/assets/{asset["id"]}','PUT',{'siteId':site['id'],'name':asset['name'],'identifier':asset['identifier'],'category':'Pump','location':'Plant room','status':'Retired','serviceIntervalDays':30},a)
count=len(call('/api/work-orders',token=a));call('/api/schedules/generate','POST',{},a);assert len(call('/api/work-orders',token=a))==count,'Retired asset generated work'
# Site and asset update/delete integrations, including history protection.
call(f'/api/assets/{asset["id"]}','DELETE',token=a,expected=400)
spare_site=call('/api/sites','POST',{'name':prefix+' Temporary','address':'Temporary'},a)
spare_asset=call('/api/assets','POST',{'siteId':spare_site['id'],'identifier':prefix+'-TEMP','name':'Temporary','category':'Test','location':'Store','status':'Active','serviceIntervalDays':30},a)
call(f'/api/sites/{spare_site["id"]}','DELETE',token=a,expected=400)
call(f'/api/sites/{spare_site["id"]}','PUT',{'name':prefix+' Edited','address':'Updated address'},a)
call(f'/api/assets/{spare_asset["id"]}','DELETE',token=a,expected=204)
call(f'/api/sites/{spare_site["id"]}','DELETE',token=a,expected=204)
print('PASS: management edits, stale inventory/schedule protection, retired assets, CRUD, authentication, RBAC, ownership, photo replay, required checklist, durable mutation replay, exactly-once parts, version conflicts, review, history, reports and schedule generation')
