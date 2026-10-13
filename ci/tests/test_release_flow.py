import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

def module(name):
    spec=importlib.util.spec_from_file_location(name,Path(__file__).parents[1]/(name+'.py'))
    value=importlib.util.module_from_spec(spec);spec.loader.exec_module(value);return value
release=module('release');build=module('build')

class ReleaseTests(unittest.TestCase):
    def test_new_minor_excludes_old_line(self):
        self.assertEqual(release.choose_version('1.2.0',[{'tag_name':'v1.0.4'},{'tag_name':'v1.0.3','draft':True}]),'1.2.0')
    def test_drafts_reserve_patch(self):
        self.assertEqual(release.choose_version('1.2.0',[{'tag_name':'v1.2.0'},{'tag_name':'v1.2.1','draft':True}]),'1.2.2')
    def test_same_commit_reuses_reservation(self):
        reserved={'tag_name':'v1.2.0','target_commitish':'source','draft':True}
        def api(path,payload=None):
            self.assertIsNone(payload);return [reserved]
        with patch.object(release,'api',api):self.assertIs(release.reserve({'repository':'test/repo','baseVersion':'1.2.0'},'source'),reserved)
    def test_new_commit_reserves_next_patch(self):
        posts=[]
        def api(path,payload=None):
            if payload is None:return [{'tag_name':'v1.2.0','target_commitish':'previous','draft':False}]
            posts.append(payload);return payload
        with patch.object(release,'api',api):
            result=release.reserve({'repository':'test/repo','baseVersion':'1.2.0','name':'Plugin','hostCommit':'tested-host'},'next-source')
        self.assertEqual(result['tag_name'],'v1.2.1');self.assertTrue(result['draft']);self.assertEqual(len(posts),1)
    def test_missing_host_contract_blocks_build(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ValueError):build.verify_host_api(Path(folder),{'requiredPresentationMembers':['WithIconSource']})
    def test_old_host_member_blocks_build(self):
        with tempfile.TemporaryDirectory() as folder:
            host=Path(folder);file=host/'Client/ClientExtensionAbstractions/UI/IClientPresentationService.cs';file.parent.mkdir(parents=True);file.write_text('public class ClientPresentationRow {}')
            with self.assertRaises(ValueError):build.verify_host_api(host,{'requiredPresentationMembers':['WithIconSource']})
    def test_current_member_passes_precheck(self):
        with tempfile.TemporaryDirectory() as folder:
            host=Path(folder);file=host/'Client/ClientExtensionAbstractions/UI/IClientPresentationService.cs';file.parent.mkdir(parents=True);file.write_text('public ClientPresentationRow WithIconSource(Func<Texture> source) => null;')
            build.verify_host_api(host,{'requiredPresentationMembers':['WithIconSource']})

if __name__=='__main__':unittest.main()

class PackagingEntryTests(unittest.TestCase):
    def test_pack_help_has_unique_arguments(self):
        import subprocess, sys
        result = subprocess.run([sys.executable, str(Path(__file__).resolve().parents[2] / 'pack.py'), '--help'], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.count('--version VERSION'), 2)

    def test_separate_compile_baseline_is_pinned(self):
        import json
        c=json.loads((Path(__file__).resolve().parents[2] / 'ci/config.json').read_text())
        self.assertRegex(c['compileHostCommit'], r'^[0-9a-f]{40}$')
        self.assertNotEqual(c['hostCommit'], c['compileHostCommit'])
