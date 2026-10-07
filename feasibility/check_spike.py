"""Local feasibility checks only. Never targets the production server."""
import json,subprocess,time,urllib.request,urllib.error,concurrent.futures
BASE='http://127.0.0.1:18096'
def get(path):
    try:
        with urllib.request.urlopen(BASE+path,timeout=10) as r:return r.status,r.headers.get('X-Family-Policy-Spike')
    except urllib.error.HTTPError as e:return e.code,e.headers.get('X-Family-Policy-Spike')
results=[]
for path,expected in [('/System/Info/Public',200),('/PolicySpike/Decision?kind=workout',200),('/PolicySpike/Decision?kind=movie',403),('/PolicySpike/Decision',403)]:
    code,header=get(path);assert code==expected and header=='loaded',(path,code,header)
    results.append({'test':path,'http':code,'global_filter':'loaded'})
def transfer(endpoint):
    started=time.monotonic()
    p=subprocess.run(['curl','--silent','--show-error','--max-time','15','--limit-rate','1M','--output','/dev/null','--write-out','%{http_code} %{size_download}',BASE+'/PolicySpike/'+endpoint+'?kind=workout'],capture_output=True,text=True)
    duration=round(time.monotonic()-started,2)
    # EOF/truncated-content or transfer failure must precede the client timeout.
    assert p.returncode not in [0,28] and duration<14,(endpoint,p.returncode,duration,p.stdout)
    return {'test':endpoint,'curl_exit':p.returncode,'http_bytes':p.stdout,'elapsed_seconds':duration,'cutoff_seconds':3}
with concurrent.futures.ThreadPoolExecutor() as pool:results.extend(pool.map(transfer,['Stream','PhysicalFile']))
print(json.dumps(results,indent=2))
