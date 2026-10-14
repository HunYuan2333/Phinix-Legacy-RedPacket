"""Build one immutable API3 dual-runtime plugin against a fixed Host and private references."""
import argparse,hashlib,json,re,subprocess,zipfile,shutil,tempfile
from pathlib import Path

def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def validate_config(config):
    if config.get('clientApi')!='3.0.0' or config.get('games')!=['1.6','1.7']:
        raise ValueError('Configure API3 and both game profiles before main publication.')
    if not re.fullmatch('[0-9a-f]{40}',config.get('hostCommit','')):raise ValueError('Use a real fixed Host commit.')
    for key in ['references','references17']:
        value=config.get(key)
        if not isinstance(value,dict) or any(not value.get(field) for field in ['repository','tag','asset','sha256']):
            raise ValueError('Configure '+key+' from an existing private reference asset before main publication.')
        if not re.fullmatch('[0-9a-f]{64}',value['sha256']):raise ValueError('Invalid reference digest.')
        if not re.fullmatch('[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+',value['repository']) or not re.fullmatch('[A-Za-z0-9_.-]+',value['tag']) or not re.fullmatch(r'[A-Za-z0-9_.-]+\.zip',value['asset']):raise ValueError('Invalid reference origin.')
def provision(archive,host,expected,game='1.6'):
    if sha(archive)!=expected:raise ValueError('Private reference digest mismatch.')
    prefix='GameDlls/' if game=='1.6' else 'GameDlls17/'
    with zipfile.ZipFile(archive) as z:
        names=set()
        if sum(i.file_size for i in z.infolist())>512*1024*1024:raise ValueError('Reference size limit.')
        for i in z.infolist():
            target=(host/i.filename).resolve()
            if '\\' in i.filename or '..' in Path(i.filename).parts or i.filename in names or not target.is_relative_to(host.resolve()) or not i.filename.startswith((prefix,'.nuget/' if game=='1.6' else prefix)) or not i.filename.endswith('.dll') or ((i.external_attr>>16)&0o170000)==0o120000:raise ValueError('Unexpected reference archive entry.')
            names.add(i.filename)
        z.extractall(host)
def verify(output,version):
    with zipfile.ZipFile(output) as z:
        if z.testzip() is not None:raise ValueError('ZIP CRC failure.')
        if len(z.namelist())!=len(set(z.namelist())):raise ValueError('Duplicate ZIP entries.')
        envelope=json.loads(z.read('manifest.json'))
        if set(envelope)!= {'schemaVersion','variants'} or envelope['schemaVersion']!=2 or len(envelope['variants'])!=2:raise ValueError('Expected one schema2 dual-runtime package.')
        seen=set();own_files=set();first=None
        forbidden=re.compile(r'^(?:Assembly-CSharp|UnityEngine|UnityStubs|Waylith|Compat|mscorlib|System\.|0Harmony|Utils|ClientExtensionAbstractions|ClientPresentation.Contracts|PhinixClient)(?:\.|$)',re.I)
        for manifest in envelope['variants']:
            if type(manifest.get('schemaVersion')) is not int or manifest['schemaVersion']!=1:raise ValueError('Invalid variant schema.')
            games=manifest['compatibility']['rimWorldVersions']
            if len(games)!=1 or games[0] not in ['1.6','1.7'] or games[0] in seen:raise ValueError('Invalid game variants.')
            game=games[0];seen.add(game)
            if manifest['version']!=version or manifest['targetFramework']!= {'1.6':'net472','1.7':'net10.0'}[game] or manifest['compatibility']['abstractionsRange']!='>=3.0.0 <4.0.0':raise ValueError('Package profile mismatch.')
            shape={key:manifest[key] for key in ['packageId','name','version','modules','dependencies','externalMods']}
            if first is not None and shape!=first:raise ValueError('Variant public declarations differ.')
            first=shape
            for record in manifest['assemblies']+manifest['resources']:
                path=record['path'];relative=Path(path)
                if relative.is_absolute() or '..' in relative.parts or '\\' in path:raise ValueError('Unsafe package path.')
                full=game+'/'+path
                if full in own_files:raise ValueError('Duplicate package entry.')
                own_files.add(full)
                if len(z.read(full))!=record['length']:raise ValueError('Entry length mismatch.')
                if sha_bytes(z.read(full))!=record['sha256']:raise ValueError('Entry digest mismatch.')
            for assembly in manifest['assemblies']:
                if Path(assembly['path']).name.startswith(('System.','UnityEngine.')) or forbidden.match(Path(assembly['path']).name):raise ValueError('Host/game dependency was bundled.')
        if set(z.namelist())!={'manifest.json'}|own_files:raise ValueError('Unexpected package content.')
    return envelope

def sha_bytes(raw):return hashlib.sha256(raw).hexdigest()
def verify_host_api(host,config):
    required=config.get('requiredPresentationMembers',[]);contract=host/'Client/PresentationContracts/UI/IClientPresentationService.cs'
    if required and not contract.is_file():raise ValueError('Fixed Host lacks the public presentation contract; update hostCommit to the tested final commit.')
    source=contract.read_text() if required else ''
    for member in required:
        if not re.search(r'\b'+re.escape(member)+r'\s*\(',source):raise ValueError('Fixed Host lacks '+member+'. Update hostCommit before releasing.')
    if not (host/'tools/compatibility/build-managed-dual.py').is_file():raise ValueError('Fixed Host lacks dual-runtime build tooling; select its tested final commit.')

def main():
    p=argparse.ArgumentParser();p.add_argument('--host',type=Path);p.add_argument('--references',type=Path);p.add_argument('--references17',type=Path);p.add_argument('--version');p.add_argument('--output',type=Path);p.add_argument('--check-config',action='store_true');p.add_argument('--check-host',action='store_true');a=p.parse_args()
    config=json.loads(Path('ci/config.json').read_text());validate_config(config)
    if a.check_config:return
    if not a.host:p.error('--host required.')
    host=a.host.resolve()
    if subprocess.check_output(['git','-C',str(host),'rev-parse','HEAD'],text=True).strip()!=config['hostCommit']:raise ValueError('Host source does not match the fixed commit.')
    verify_host_api(host,config)
    if a.check_host:return
    if not all([a.references,a.references17,a.version,a.output]):p.error('Both private references, --version and --output are required.')
    provision(a.references,host,config['references']['sha256']);provision(a.references17,host,config['references17']['sha256'],'1.7')
    a.output.mkdir(parents=True,exist_ok=True);output=a.output/(config['assetPrefix']+'-'+a.version+'.zip')
    if output.exists():raise ValueError('Preserve existing release output.')
    # The packager deliberately rejects output inside a source checkout.
    with tempfile.TemporaryDirectory(prefix='phinix-ci-package-') as folder:
        isolated_output=Path(folder)/output.name
        subprocess.run(['python3',str(host/'tools/compatibility/build-managed-dual.py'),'--online-restore','--host',str(host),'--plugin',str(Path.cwd()),'--game16',str(host/'GameDlls'),'--game17',str(host/'GameDlls17'),'--harmony17',str(host/'GameDlls17/0Harmony.dll'),'--version',a.version,'--output',str(isolated_output)],check=True)
        verify(isolated_output,a.version)
        shutil.copyfile(isolated_output,output)
    verify(output,a.version)
    summary=dict(schemaVersion=2,repository=config['repository'],sourceCommit=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip(),hostCommit=config['hostCommit'],commonCommit=subprocess.check_output(['git','-C',str(host/'Dependencies/Phinix.Common'),'rev-parse','HEAD'],text=True).strip(),version=a.version,asset=output.name,sizeBytes=output.stat().st_size,sha256=sha(output),games=config['games'],clientApi=config['clientApi'],referenceDigests={key:config[key]['sha256'] for key in ['references','references17']})
    # Product identity comes from both checked variants, never from a workflow override.
    summary['packageId']=json.loads(zipfile.ZipFile(output).read('manifest.json'))['variants'][0]['packageId']
    (a.output/'build-summary.json').write_text(json.dumps(summary,indent=2)+'\n');(a.output/'SHA256SUMS').write_text(summary['sha256']+'  '+output.name+'\n');print(json.dumps(summary))
if __name__=='__main__':main()
