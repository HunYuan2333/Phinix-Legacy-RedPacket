"""Build a plugin against fixed host source and privately provisioned compile-only references."""
import argparse, hashlib, json, re, shutil, subprocess, zipfile
from pathlib import Path

def run(args):subprocess.run([str(a) for a in args],check=True)
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def provision(archive, host, expected):
    if sha(archive)!=expected:raise ValueError('Private reference digest mismatch.')
    with zipfile.ZipFile(archive) as z:
        if sum(i.file_size for i in z.infolist())>256*1024*1024:raise ValueError('Reference size limit.')
        for i in z.infolist():
            target=(host/i.filename).resolve()
            if not target.is_relative_to(host.resolve()) or not i.filename.startswith(('GameDlls/','.nuget/')) or not i.filename.endswith('.dll') or ((i.external_attr>>16)&0o170000)==0o120000:
                raise ValueError('Unexpected reference archive entry.')
        z.extractall(host)
def verify(output,version):
    with zipfile.ZipFile(output) as z:
        manifest=json.loads(z.read('manifest.json'))
        if manifest['version']!=version:raise ValueError('Package version mismatch.')
        own={a['file']['path'] if 'file' in a else a['path'] for a in manifest['assemblies']}
        resources={r['path'] for r in manifest['resources']}
        if set(z.namelist())!={'manifest.json'}|own|resources:raise ValueError('Unexpected package content.')
        forbidden=re.compile(r'^(?:Assembly-CSharp|UnityEngine|mscorlib|0Harmony|Utils|ClientExtensionAbstractions|PhinixClient)(?:\.|$)',re.I)
        for path in own:
            if forbidden.match(Path(path).name):raise ValueError('Host/game dependency was bundled.')
        for record in list(manifest['assemblies'])+list(manifest['resources']):
            if hashlib.sha256(z.read(record['path'])).hexdigest()!=record['sha256']:raise ValueError('Entry digest mismatch.')
    return manifest

def main():
    p=argparse.ArgumentParser();p.add_argument('--host',type=Path,required=True);p.add_argument('--references',type=Path,required=True);p.add_argument('--version',required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    config=json.loads(Path('ci/config.json').read_text());host=a.host.resolve()
    if subprocess.check_output(['git','-C',str(host),'rev-parse','HEAD'],text=True).strip()!=config['hostCommit']:raise ValueError('Host source does not match the fixed commit.')
    provision(a.references,host,config['references']['sha256'])
    run(['dotnet','build',host/'Phinix.sln','--configuration','Release 1.6','-p:BuildInParallel=false','-p:NuGetAudit=false','-m:1'])
    tool=host/'Extensions/PluginStore/Tools/ManagedPackageTool'
    run(['dotnet','build',tool/'ManagedPackageTool.csproj','-c','Release','-p:BuildInParallel=false','-p:NuGetAudit=false','-m:1'])
    a.output.mkdir(parents=True,exist_ok=True);output=a.output/(config['assetPrefix']+'-'+a.version+'.zip')
    args=['python3','pack.py','--game-references',host/'GameDlls','--version',a.version,'--output',output]
    if config['kind']=='example':args+=['--phinix-root',host]
    else:
        args+=['--phinix-package',host/'Output/phinix-rework','--packager',tool/'bin/Release/net10.0/ManagedPackageTool.dll']
        if config['kind']=='talent':args+=['--harmony-references',host/'.nuget/Lib.Harmony.2.3.6/lib/net472']
    run(args);manifest=verify(output,a.version)
    summary=dict(schemaVersion=1,repository=config['repository'],sourceCommit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),
        hostCommit=config['hostCommit'],commonCommit=subprocess.check_output(['git','-C',str(host/'Dependencies/Phinix.Common'),'rev-parse','HEAD'],text=True).strip(),
        packageId=manifest['packageId'],version=a.version,asset=output.name,sizeBytes=output.stat().st_size,sha256=sha(output))
    (a.output/'build-summary.json').write_text(json.dumps(summary,indent=2)+'\n')
    (a.output/'SHA256SUMS').write_text(summary['sha256']+'  '+output.name+'\n')
    print(json.dumps(summary))
if __name__=='__main__':main()
