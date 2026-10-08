"""Reserve one immutable release version per main commit; retries reuse the same draft."""
import json, os, re, subprocess, time
from pathlib import Path

def api(path, payload=None):
    command=['gh','api',path]
    if payload is not None: command+=['--method','POST','--input','-']
    result=subprocess.run(command,input=json.dumps(payload) if payload is not None else None,text=True,capture_output=True)
    if result.returncode: raise RuntimeError(result.stderr.strip())
    return json.loads(result.stdout)

def choose_version(base, releases):
    major,minor,patch=map(int,base.split('.'))
    versions=[int(m[3]) for r in releases if (m:=re.fullmatch(r'v(\d+)\.(\d+)\.(\d+)',r['tag_name'])) and (int(m[1]),int(m[2]))==(major,minor)]
    return f'{major}.{minor}.{max(patch, max(versions,default=-1)+1)}'

def reserve(config, sha):
    repo=config['repository'];base=config['baseVersion']
    for attempt in range(8):
        releases=[];page=1
        while True:
            batch=api(f'repos/{repo}/releases?per_page=100&page={page}');releases+=batch
            if len(batch)<100:break
            page+=1
            if page>100:raise RuntimeError('Release history exceeds the bounded scan limit.')
        previous=[r for r in releases if r['target_commitish']==sha and re.fullmatch(r'v\d+\.\d+\.\d+',r['tag_name'])]
        if len(previous)>1:raise RuntimeError('Multiple versions already reserved for this commit.')
        if previous:return previous[0]
        version=choose_version(base,releases)
        try:
            return api(f'repos/{repo}/releases',dict(tag_name='v'+version,target_commitish=sha,name=config['name']+' '+version,
                body='Automated main-branch build. Source commit: '+sha+'\nHost commit: '+config['hostCommit'],draft=True,prerelease=False))
        except RuntimeError as error:
            if '422' not in str(error):raise
            time.sleep(min(attempt+1,5))
    raise RuntimeError('Could not reserve a unique version after concurrent release updates.')

if __name__=='__main__':
    config=json.loads(Path('ci/config.json').read_text())
    if os.environ.get('GITHUB_REF')!='refs/heads/main' or os.environ.get('GITHUB_EVENT_NAME')!='push' or os.environ.get('GITHUB_REPOSITORY')!=config['repository']:
        raise SystemExit('Official releases require a push to this repository main branch.')
    sha=os.environ['GITHUB_SHA'];release=reserve(config,sha)
    Path('.ci-release.json').write_text(json.dumps(release))
    with open(os.environ['GITHUB_OUTPUT'],'a') as output:
        output.write('version='+release['tag_name'][1:]+'\n')
        output.write('published='+str(not release['draft']).lower()+'\n')
