import json,secrets,urllib.request,urllib.error
B='http://127.0.0.1:18096'
def api(path,data=None,headers=None):
 q=urllib.request.Request(B+path,data=json.dumps(data).encode() if data is not None else None,headers={'Content-Type':'application/json',**(headers or {})})
 try:
  with urllib.request.urlopen(q,timeout=10) as r:
   raw=r.read();return r.status,json.loads(raw) if raw else None,r.headers.get('X-Family-Policy-Spike')
 except urllib.error.HTTPError as e:return e.code,None,e.headers.get('X-Family-Policy-Spike')
code,_,_=api('/Startup/User');assert code==200
pwd=secrets.token_urlsafe(24)
code,_,_=api('/Startup/User',{'Name':'SyntheticParent','Password':pwd});assert code==204,code
code,_,_=api('/Startup/Complete',{});assert code==204,code
code,r,_=api('/Users/AuthenticateByName',{'Username':'SyntheticParent','Pw':pwd},headers={'Authorization':'MediaBrowser Client="Policy Spike", Device="Test", DeviceId="synthetic", Version="1"'});assert code==200,code
h={'Authorization':'MediaBrowser Token="'+r['AccessToken']+'"','X-Spike-Deny':'true'}
for path in ['/Videos/11111111111111111111111111111111/stream','/Videos/11111111111111111111111111111111/hls1/p/0.ts','/Items/11111111111111111111111111111111/PlaybackInfo']:
 code,_,header=api(path,headers=h);assert code==403 and header=='loaded',(path,code,header)
 print('Native Jellyfin controller intercepted:',path,'HTTP',code)
