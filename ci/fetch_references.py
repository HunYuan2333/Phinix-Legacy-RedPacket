"""Download only pinned assets from private repositories; never upload compile references."""
import argparse,json,subprocess
from pathlib import Path
from build import validate_config,sha
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);a=p.parse_args();config=json.loads(Path('ci/config.json').read_text());validate_config(config);a.output.mkdir(parents=True,exist_ok=True)
for key,name in [('references','references16.zip'),('references17','references17.zip')]:
 value=config[key];info=json.loads(subprocess.check_output(['gh','repo','view',value['repository'],'--json','isPrivate,nameWithOwner'],text=True))
 if not info['isPrivate'] or info['nameWithOwner']!=value['repository']:raise ValueError('Compile references must remain private.')
 folder=a.output/key;folder.mkdir(exist_ok=False)
 subprocess.run(['gh','release','download',value['tag'],'--repo',value['repository'],'--pattern',value['asset'],'--dir',str(folder)],check=True)
 asset=folder/value['asset']
 if sha(asset)!=value['sha256']:raise ValueError('Private reference digest mismatch.')
 asset.rename(a.output/name)
