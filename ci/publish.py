"""Publish a reserved draft only after its verified ZIP and build summary are attached."""
import json,os,subprocess
from pathlib import Path
config=json.loads(Path('ci/config.json').read_text());release=json.loads(Path('.ci-release.json').read_text());summary=json.loads(Path('dist/build-summary.json').read_text())
if os.environ.get('GITHUB_REF')!='refs/heads/main' or os.environ.get('GITHUB_EVENT_NAME')!='push' or os.environ.get('GITHUB_REPOSITORY')!=config['repository'] or summary['sourceCommit']!=os.environ['GITHUB_SHA'] or summary['version']!=release['tag_name'][1:]:
    raise SystemExit('Release provenance does not match this main push.')
if release['target_commitish'] != summary['sourceCommit'] or summary['repository'] != config['repository'] or summary['hostCommit'] != config['hostCommit']:
    raise SystemExit('Reserved release or fixed host provenance mismatch.')
import hashlib
asset_path=Path('dist')/summary['asset']
if asset_path.name != summary['asset'] or hashlib.sha256(asset_path.read_bytes()).hexdigest() != summary['sha256'] or asset_path.stat().st_size != summary['sizeBytes']:
    raise SystemExit('Verified plugin asset changed before publication.')
# Reuse identical partially uploaded assets; never replace bytes in a reserved version.
import hashlib,tempfile
assets=json.loads(subprocess.check_output(['gh','api',f"repos/{config['repository']}/releases/{release['id']}/assets"],text=True))
missing=[]
for name in [summary['asset'],'build-summary.json','SHA256SUMS']:
    existing=next((a for a in assets if a['name']==name),None)
    if existing:
        with tempfile.TemporaryDirectory() as folder:
            subprocess.run(['gh','release','download',release['tag_name'],'--pattern',name,'--dir',folder,'--repo',config['repository']],check=True)
            if hashlib.sha256((Path(folder)/name).read_bytes()).digest()!=hashlib.sha256((Path('dist')/name).read_bytes()).digest():
                raise SystemExit('Reserved release asset differs; preserve it and investigate: '+name)
    else:missing.append('dist/'+name)
if missing:subprocess.run(['gh','release','upload',release['tag_name'],*missing,'--repo',config['repository']],check=True)
result=subprocess.run(['gh','api',f"repos/{config['repository']}/releases/{release['id']}",'--method','PATCH','--input','-'],input=json.dumps({'draft':False,'prerelease':False}),text=True,check=True,capture_output=True)
print('Published immutable release '+release['tag_name'])
