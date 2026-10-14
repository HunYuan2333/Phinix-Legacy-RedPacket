import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec=importlib.util.spec_from_file_location('dual_build',Path(__file__).parents[1]/'build.py')
build=importlib.util.module_from_spec(spec);spec.loader.exec_module(build)

def envelope():
    variants=[]
    for game,tfm in [('1.6','net472'),('1.7','net10.0')]:
        variants.append(dict(schemaVersion=1,packageId='fixture.plugin',name='Fixture',version='1.0.0',modules=[],dependencies=[],externalMods=[],targetFramework=tfm,
            compatibility=dict(rimWorldVersions=[game],abstractionsRange='>=3.0.0 <4.0.0'),resources=[],
            assemblies=[dict(path='Fixture.dll',length=7,sha256=hashlib.sha256(b'fixture').hexdigest())]))
    return dict(schemaVersion=2,variants=variants)

def write(path,value,duplicate=False):
    with zipfile.ZipFile(path,'w') as z:
        z.writestr('manifest.json',json.dumps(value))
        for v in value['variants']:
            for a in v['assemblies']:
                z.writestr(v['compatibility']['rimWorldVersions'][0]+'/'+a['path'],b'fixture')
        if duplicate:z.writestr('manifest.json',json.dumps(value))

class DualBuildTests(unittest.TestCase):
    def test_missing_second_reference_blocks_before_publication(self):
        cfg=json.loads((Path(__file__).parents[1]/'config.json').read_text())
        cfg['references17']=None
        with self.assertRaisesRegex(ValueError,'references17'):build.validate_config(cfg)

    def test_valid_dual_envelope_and_reject_invalid_profiles_or_bytes(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'package.zip';valid=envelope();write(path,valid);build.verify(path,'1.0.0')
            changes=[lambda m:m['variants'][1].update(targetFramework='net472'),
                lambda m:m['variants'][1].update(packageId='different.plugin'),
                lambda m:m['variants'][1]['assemblies'][0].update(sha256='0'*64),
                lambda m:m['variants'][1]['assemblies'][0].update(length=100),
                lambda m:m['variants'][1]['assemblies'][0].update(path='../escape.dll'),
                lambda m:m['variants'][1]['assemblies'][0].update(path='System.Runtime.dll')]
            for change in changes:
                with self.subTest(change=change):
                    value=copy.deepcopy(valid);change(value);write(path,value)
                    with self.assertRaises(ValueError):build.verify(path,'1.0.0')

    def test_duplicate_entries_rejected(self):
        import warnings
        with tempfile.TemporaryDirectory() as folder, warnings.catch_warnings():
            warnings.simplefilter('ignore');path=Path(folder)/'package.zip';write(path,envelope(),True)
            with self.assertRaisesRegex(ValueError,'Duplicate ZIP'):build.verify(path,'1.0.0')

    def test_private_refs_only_expected_profile_and_safe_paths(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);host=root/'host';host.mkdir();archive=root/'refs.zip'
            for entry in ['../outside.dll','GameDlls17/../evil.dll','GameDlls17/evil\\name.dll','GameDlls/Assembly-CSharp.dll']:
                with self.subTest(entry=entry):
                    with zipfile.ZipFile(archive,'w') as z:z.writestr(entry,b'ref')
                    with self.assertRaises(ValueError):build.provision(archive,host,build.sha(archive),'1.7')
            with zipfile.ZipFile(archive,'w') as z:z.writestr('GameDlls17/Assembly-CSharp.dll',b'ref')
            with self.assertRaisesRegex(ValueError,'digest'):build.provision(archive,host,'0'*64,'1.7')
            build.provision(archive,host,build.sha(archive),'1.7')
            self.assertEqual((host/'GameDlls17/Assembly-CSharp.dll').read_bytes(),b'ref')
