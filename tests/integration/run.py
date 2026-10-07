"""Disposable loopback Jellyfin acceptance harness. Never uses household credentials/media."""
import os,json,time,subprocess,tempfile,shutil,urllib.request,urllib.error,urllib.parse,secrets,sys,signal,datetime
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
DOTNET=os.environ['DOTNET_EXE'];SERVER=os.environ['JELLYFIN_SERVER_DLL'];FFMPEG=os.environ['JELLYFIN_FFMPEG']
BASE='http://127.0.0.1:18097'
state=Path(tempfile.mkdtemp(prefix='family-policy-integration-'));state.chmod(0o700)
report=[];server=None;guard=None

def api(path,data=None,token=None,method=None):
 headers={'Content-Type':'application/json'}
 if token:headers['Authorization']='MediaBrowser Token="'+token+'"'
 else:headers['Authorization']='MediaBrowser Client="Family Policy Test", Device="Synthetic", DeviceId="integration", Version="1"'
 req=urllib.request.Request(BASE+path,data=json.dumps(data).encode() if data is not None else None,headers=headers,method=method)
 try:
  with urllib.request.urlopen(req,timeout=20) as r:
   raw=r.read()
   try:value=json.loads(raw) if raw else None
   except json.JSONDecodeError:value=raw.decode()
   return r.status,value
 except urllib.error.HTTPError as e:return e.code,None

def expect(name,code,expected):
 assert code==expected,(name,code,expected)
 report.append({'check':name,'http':code})

def start():
 global server
 logpath=state/'server.log'
 offset=logpath.stat().st_size if logpath.exists() else 0
 log=logpath.open('ab')
 server=subprocess.Popen([DOTNET,SERVER,'--datadir',str(state/'data'),'--configdir',str(state/'config'),'--cachedir',str(state/'cache'),'--logdir',str(state/'log'),'--ffmpeg',FFMPEG,'--nowebclient','--nonetchange'],stdout=log,stderr=subprocess.STDOUT)
 for _ in range(60):
  if server.poll() is not None:raise RuntimeError('Server exited; inspect private test log '+str(state/'server.log'))
  try:
   if 'Main: Startup complete' in logpath.read_text(errors='replace')[offset:] and api('/health')==(200,'Healthy'):return
  except (OSError,urllib.error.URLError):pass
  time.sleep(1)
 raise RuntimeError('Test server timeout')

def stop():
 global server
 if server and server.poll() is None:
  server.terminate()
  try:server.wait(timeout=10)
  except subprocess.TimeoutExpired:server.kill();server.wait(timeout=5)

try:
 (state/'config').mkdir()
 (state/'config/network.xml').write_text('<NetworkConfiguration><InternalHttpPort>18097</InternalHttpPort><PublicHttpPort>18097</PublicHttpPort><EnableIPv4>true</EnableIPv4><EnableIPv6>false</EnableIPv6><AutoDiscovery>false</AutoDiscovery><EnableUPnP>false</EnableUPnP><EnableRemoteAccess>false</EnableRemoteAccess><LocalNetworkAddresses><string>127.0.0.1</string></LocalNetworkAddresses></NetworkConfiguration>')
 plugin=state/'data/plugins/FamilyPolicy';plugin.mkdir(parents=True)
 for name in ['FamilyPolicy.Plugin.dll','FamilyPolicy.Core.dll']:shutil.copy(ROOT/'src/FamilyPolicy.Plugin/bin/Release/net10.0'/name,plugin)
 start()
 api('/Startup/User');password=secrets.token_urlsafe(24)
 expect('create synthetic parent',api('/Startup/User',{'Name':'TestParent','Password':password})[0],204)
 expect('finish setup',api('/Startup/Complete',{})[0],204)
 code,auth=api('/Users/AuthenticateByName',{'Username':'TestParent','Pw':password});expect('parent login',code,200);admin=auth['AccessToken'];parent_id=auth['User']['Id']
 children=[]
 for name in ['ChildA','ChildB']:
  pwd=secrets.token_urlsafe(24);code,user=api('/Users/New',{'Name':name,'Password':pwd},admin);expect('create '+name,code,200)
  policy=user['Policy'];policy.update(MaxParentalRating=10,BlockUnratedItems=['Movie'],EnableLiveTvAccess=False)
  expect('baseline policy '+name,api('/Users/'+user['Id']+'/Policy',policy,admin)[0],204)
  code,login=api('/Users/AuthenticateByName',{'Username':name,'Pw':pwd});expect('login '+name,code,200);children.append((user['Id'],login['AccessToken']))
  expect('child cannot read parent state',api('/FamilyPolicy/State',token=login['AccessToken'])[0],403)
  expect('child cannot forge heartbeat',api('/FamilyPolicy/Heartbeat',{'UserIds':[user['Id']]},login['AccessToken'])[0],403)
 expect('anonymous parent endpoint',api('/FamilyPolicy/State')[0],401)
 expect('enrollment requires guard',api('/FamilyPolicy/Accounts/'+children[0][0],{'ExpectedRevision':0,'Policy':{'Rules':[]}},admin,'PUT')[0],503)
 env=os.environ.copy();env.update(FAMILY_POLICY_SERVER=BASE,FAMILY_POLICY_TOKEN=admin,FAMILY_POLICY_USERS=','.join(x[0] for x in children))
 guard=subprocess.Popen([DOTNET,str(ROOT/'src/FamilyPolicy.Guard/bin/Release/net10.0/FamilyPolicy.Guard.dll')],env=env,stdout=(state/'guard.log').open('wb'),stderr=subprocess.STDOUT)
 time.sleep(2)
 fixtures=state/'media';fixtures.mkdir();seed=state/'seed.mp4'
 subprocess.run([FFMPEG,'-hide_banner','-loglevel','error','-f','lavfi','-i','color=c=blue:s=160x90:d=2','-c:v','libx264','-pix_fmt','yuv420p','-movflags','+faststart',str(seed)],check=True)
 for name,rating,tags in [('Safe','PG',[]),('Mature','R',[]),('Soccer','', ['family:subject:Soccer']),('Workout','PG',['family:category:Workout']),('Franchise','R',['family:franchise:Star Wars'])]:
  folder=fixtures/name;folder.mkdir();shutil.copy(seed,folder/(name+'.mp4'))
  (folder/'movie.nfo').write_text('<movie><title>'+name+'</title><mpaa>'+rating+'</mpaa>'+''.join('<tag>'+t+'</tag>' for t in tags)+'</movie>')
 options={'LibraryOptions':{'PathInfos':[{'Path':str(fixtures)}],'EnableRealtimeMonitor':False,'EnableInternetProviders':False,'MetadataSavers':[],'SaveLocalMetadata':False,'TypeOptions':[{'Type':'Movie','MetadataFetchers':['Nfo'],'MetadataFetcherOrder':['Nfo'],'ImageFetchers':[]}]}}
 expect('create fixture library',api('/Library/VirtualFolders?name=Fixtures&collectionType=movies&refreshLibrary=true',options,admin)[0],204)
 movies=[]
 for _ in range(60):
  _,items=api('/Items?Recursive=true&IncludeItemTypes=Movie&Fields=OfficialRating,Tags,MediaSources',token=admin)
  movies=items['Items']
  if len(movies)==5 and all('MediaSources' in m and m['MediaSources'] for m in movies):break
  time.sleep(1)
 assert len(movies)==5,[(m['Name'],m.get('OfficialRating'),m.get('Tags')) for m in movies]
 print('Synthetic fixture metadata:',[(m['Name'],m.get('OfficialRating'),m.get('Tags')) for m in movies],file=sys.stderr)
 titles={m['Name']:m for m in movies};assert set(titles)=={'Safe','Mature','Soccer','Workout','Franchise'},set(titles)
 subject_preview={'Rules':[{'Id':'unrated-soccer','Effect':'Allow','When':{'All':[{'Field':'Unrated'},{'Field':'Subject','Values':['Soccer']}]}}]}
 code,decision=api('/FamilyPolicy/Preview/'+titles['Soccer']['Id'],subject_preview,admin)
 assert code==200 and decision['ContentAllowed'] is False
 report.append({'check':'imported subject tags cannot grant access','blocked_before_parent_confirmation':True})
 for name,labels in [('Soccer',{'Subjects':['Soccer']}),('Workout',{'Categories':['Workout']}),('Franchise',{'Franchises':['Star Wars']})]:
  _,s=api('/FamilyPolicy/State',token=admin)
  expect('parent-owned labels',api('/FamilyPolicy/Labels/'+titles[name]['Id'],{'ExpectedRevision':s['Revision'],'Labels':labels},admin,'PUT')[0],200)
  expect('child cannot change labels',api('/FamilyPolicy/Labels/'+titles[name]['Id'],{'ExpectedRevision':s['Revision'],'Labels':labels},children[0][1],'PUT')[0],403)
 pg={'Rules':[{'Id':'pg','Dimension':'Content','Effect':'Allow','When':{'Field':'RatingAtMost','Number':10}}]}
 policy={'TimeZone':'UTC','DefaultContentAllowed':False,'DefaultTimeAllowed':True,'Rules':pg['Rules']+[
  {'Id':'soccer','Priority':10,'Effect':'Allow','When':{'All':[{'Field':'Unrated'},{'Field':'Subject','Values':['Soccer']}]}}
 ]}
 for uid,token in children:
  _,s=api('/FamilyPolicy/State',token=admin)
  expect('parent enrolls child',api('/FamilyPolicy/Accounts/'+uid,{'ExpectedRevision':s['Revision'],'Policy':policy},admin,'PUT')[0],200)
  expect('child cannot change own policy',api('/FamilyPolicy/Accounts/'+uid,{'ExpectedRevision':s['Revision'],'Policy':pg},token,'PUT')[0],403)
  expect('child cannot restore restrictions',api('/FamilyPolicy/Accounts/'+uid+'/Restore',{'ExpectedRevision':0},token)[0],403)
  for name,expected in [('Safe',200),('Soccer',200),('Mature',403),('Franchise',403)]:
   expect('direct item '+name,api('/Users/'+uid+'/Items/'+titles[name]['Id'],token=token)[0],expected)
  _,views=api('/UserViews?UserId='+uid,token=token);assert len(views['Items'])>0,views
  _,catalog=api('/Items?Recursive=true&IncludeItemTypes=Movie',token=token);visible={m['Name'] for m in catalog['Items']}
  assert visible=={'Safe','Soccer','Workout'},visible
  report.append({'check':'catalog exact allowlist','titles':sorted(visible)})
 if os.environ.get('FAMILY_POLICY_BROWSER_TESTS')=='1':
  from browser import check_ui
  report.append(check_ui(BASE,admin,parent_id,ROOT))
 # Per-child title override, independent of industry rating.
 uid,token=children[0];_,s=api('/FamilyPolicy/State',token=admin)
 exception={**policy,'Rules':policy['Rules']+[{'Id':'specific-exception','Priority':100,'Effect':'Allow','When':{'Field':'Title','Values':[titles['Mature']['Id']]}}]}
 expect('parent grants individual R exception',api('/FamilyPolicy/Accounts/'+uid,{'ExpectedRevision':s['Revision'],'Policy':exception},admin,'PUT')[0],200)
 expect('approved child direct item',api('/Users/'+uid+'/Items/'+titles['Mature']['Id'],token=token)[0],200)
 expect('other child remains blocked',api('/Users/'+children[1][0]+'/Items/'+titles['Mature']['Id'],token=children[1][1])[0],403)
 # Actual native file routes: successful bytes for approved title, denial for other child.
 for t,expected in [(token,200),(children[1][1],403)]:
  q=urllib.request.Request(BASE+'/Videos/'+titles['Mature']['Id']+'/stream?static=true',headers={'Authorization':'MediaBrowser Token="'+t+'"'})
  try:
   with urllib.request.urlopen(q,timeout=10) as r:code=r.status;assert r.read(16)
  except urllib.error.HTTPError as e:code=e.code
  expect('native media file delivery',code,expected)
 # Series grants must inherit to episodes while a narrower episode block wins.
 shows=state/'shows';shows.mkdir();show=shows/'SeriesFixture';(show/'Season 1').mkdir(parents=True)
 (show/'tvshow.nfo').write_text('<tvshow><title>SeriesFixture</title><mpaa>TV-MA</mpaa></tvshow>')
 for index in [1,2]:
  stem='SeriesFixture S01E0'+str(index);shutil.copy(seed,show/'Season 1'/(stem+'.mp4'))
  (show/'Season 1'/(stem+'.nfo')).write_text('<episodedetails><title>Episode '+str(index)+'</title><season>1</season><episode>'+str(index)+'</episode><mpaa>TV-MA</mpaa></episodedetails>')
 tvoptions={'LibraryOptions':{'PathInfos':[{'Path':str(shows)}],'EnableRealtimeMonitor':False,'TypeOptions':[{'Type':kind,'MetadataFetchers':['Nfo'],'MetadataFetcherOrder':['Nfo'],'ImageFetchers':[]} for kind in ['Series','Episode']]}}
 expect('create generated TV fixtures',api('/Library/VirtualFolders?name=TVFixtures&collectionType=tvshows&refreshLibrary=true',tvoptions,admin)[0],204)
 tv=[]
 for _ in range(30):
  _,result=api('/Items?Recursive=true&IncludeItemTypes=Series,Episode',token=admin);tv=result['Items']
  if len(tv)==3:break
  time.sleep(1)
 assert len(tv)==3,[(i['Name'],i['Type']) for i in tv]
 series=next(i for i in tv if i['Type']=='Series');episodes=sorted((i for i in tv if i['Type']=='Episode'),key=lambda i:i['IndexNumber'])
 episode_policy={**exception,'Rules':exception['Rules']+[{'Id':'series-grant','Priority':100,'Effect':'Allow','When':{'Field':'Title','Values':[series['Id']]}},{'Id':'episode-deny','Priority':200,'Effect':'Deny','When':{'Field':'Title','Values':[episodes[1]['Id']]}}]}
 _,s=api('/FamilyPolicy/State',token=admin)
 expect('apply series and episode decisions',api('/FamilyPolicy/Accounts/'+uid,{'ExpectedRevision':s['Revision'],'Policy':episode_policy},admin,'PUT')[0],200)
 expect('approved series visible',api('/Users/'+uid+'/Items/'+series['Id'],token=token)[0],200)
 expect('episode inherits series grant',api('/Users/'+uid+'/Items/'+episodes[0]['Id'],token=token)[0],200)
 expect('episode denial wins',api('/Users/'+uid+'/Items/'+episodes[1]['Id'],token=token)[0],403)
 expect('other child series denied',api('/Users/'+children[1][0]+'/Items/'+series['Id'],token=children[1][1])[0],403)
 # Real dynamic HLS manifests and media segment delivery through native Jellyfin controllers.
 def raw(path):
  req=urllib.request.Request(urllib.parse.urljoin(BASE,path),headers={'Authorization':'MediaBrowser Token="'+token+'"'})
  try:
   with urllib.request.urlopen(req,timeout=20) as r:assert r.status==200;return r.read()
  except urllib.error.HTTPError as e:
   e.read();raise RuntimeError('Native HLS HTTP '+str(e.code)) from None
 master='/Videos/'+titles['Mature']['Id']+'/master.m3u8?VideoCodec=h264&AudioCodec=aac&VideoBitRate=500000&AudioBitRate=128000&SegmentContainer=ts&MinSegments=1&MediaSourceId='+titles['Mature']['MediaSources'][0]['Id']
 playlist=raw(master).decode()
 def entry(text):return next(line.strip() for line in text.splitlines() if line.strip() and not line.startswith('#'))
 variant=urllib.parse.urljoin(BASE+master,entry(playlist))
 body=raw(variant).decode();segment=urllib.parse.urljoin(variant,entry(body))
 assert len(raw(segment))>0
 report.append({'check':'native HLS manifest and segment delivery','approved_child':True})
 expect('other child HLS denied',api(master,token=children[1][1])[0],403)
 # Native timed cutoff on a large physical file: stop delivery without relying on a client Stop command.
 with (fixtures/'Workout'/'Workout.mp4').open('ab') as out:out.truncate(512*1024*1024)
 now=datetime.datetime.now(datetime.timezone.utc)
 start_time=(now-datetime.timedelta(seconds=1)).strftime('%H:%M:%S')
 end_time=(now+datetime.timedelta(seconds=5)).strftime('%H:%M:%S')
 timed={**exception,'DefaultTimeAllowed':False,'Rules':exception['Rules']+[{'Id':'test-deadline','Dimension':'Time','Effect':'Allow','When':{'Field':'Window','Window':{'Start':start_time,'End':end_time}}}]}
 _,s=api('/FamilyPolicy/State',token=admin)
 expect('configure timed native test',api('/FamilyPolicy/Accounts/'+uid,{'ExpectedRevision':s['Revision'],'Policy':timed},admin,'PUT')[0],200)
 config='url = "'+BASE+'/Videos/'+titles['Workout']['Id']+'/stream?static=true"\nheader = "Authorization: MediaBrowser Token=\\"'+token+'\\""\n'
 begun=time.monotonic()
 transfer=subprocess.run(['curl','--config','-','--silent','--show-error','--output','/dev/null','--limit-rate','1M','--max-time','15','--write-out','%{http_code} %{size_download}'],input=config,capture_output=True,text=True)
 elapsed=round(time.monotonic()-begun,2)
 assert transfer.returncode not in [0,28] and transfer.stdout.startswith('200 ') and elapsed<14,(transfer.returncode,transfer.stdout,elapsed)
 report.append({'check':'native PhysicalFile schedule cutoff','curl_exit':transfer.returncode,'http_and_bytes':transfer.stdout,'elapsed_seconds':elapsed})
 expect('native restart denied after deadline',api('/Items/'+titles['Workout']['Id']+'/PlaybackInfo',token=token)[0],403)
 expect('cross-user query spoof',api('/Items?UserId='+children[1][0],token=token)[0],403)
 expect('legacy segment route denied',api('/Videos/'+titles['Safe']['Id']+'/hls/p/0.ts',token=token)[0],403)
 _,s=api('/FamilyPolicy/State',token=admin)
 expect('original policy restore',api('/FamilyPolicy/Accounts/'+uid+'/Restore',{'ExpectedRevision':s['Revision']},admin)[0],200)
 _,u=api('/Users/'+uid,token=admin);assert u['Policy']['MaxParentalRating']==10 and u['Policy']['BlockUnratedItems']==['Movie']
 _,s=api('/FamilyPolicy/State',token=admin)
 expect('reenroll for failure test',api('/FamilyPolicy/Accounts/'+uid,{'ExpectedRevision':s['Revision'],'Policy':policy},admin,'PUT')[0],200)
 # Plugin absent: independent process must disable managed users through native APIs.
 stop();shutil.move(str(plugin),str(state/'disabled-plugin'));start()
 for _ in range(20):
  statuses=[api('/Users/'+uid,token=admin)[1]['Policy']['IsDisabled'] for uid,_ in children]
  if all(statuses):break
  time.sleep(1)
 assert all(statuses),statuses
 report.append({'check':'plugin-absence guard','all_managed_accounts_disabled':True})
 print(json.dumps({'passed':True,'checks':report},indent=2))
finally:
 if guard and guard.poll() is None:guard.terminate();guard.wait(timeout=10)
 stop()
 # Private synthetic logs/config remain available if the harness failed. No production data was used.
 if sys.exc_info()[0] is None:shutil.rmtree(state)
 else:print('Private diagnostic directory:',state,file=sys.stderr)
