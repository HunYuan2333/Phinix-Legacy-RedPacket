import contextlib
import importlib.util
import io
import os
from pathlib import Path
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('notify',Path(__file__).parents[1]/'notify_index.py')
notify=importlib.util.module_from_spec(spec);spec.loader.exec_module(notify)

class NotifyTests(unittest.TestCase):
    def run_notice(self,conclusions):
        posts=[];gets=[]
        def api(path,data=None):
            if data is not None:
                posts.append((path,data));return {'workflow_run_id':len(posts)}
            gets.append(path)
            return dict(event='workflow_dispatch',head_branch='main',path='.github/workflows/'+notify.WORKFLOW,
                        status='completed',conclusion=conclusions[len(posts)-1])
        with contextlib.redirect_stdout(io.StringIO()):
            result=notify.notify(api,lambda:0,lambda seconds:None)
        return result,posts,gets
    def test_success_dispatches_all_policy_scan_on_main(self):
        result,posts,gets=self.run_notice(['success'])
        self.assertTrue(result);self.assertEqual(len(posts),1)
        self.assertEqual(posts[0][1],{'ref':'main','inputs':{'check_only':False}})
        self.assertEqual(gets,['/actions/runs/1'])
    def test_stale_failed_run_gets_new_dispatch_not_rerun(self):
        result,posts,gets=self.run_notice(['failure','success'])
        self.assertTrue(result);self.assertEqual(len(posts),2)
        self.assertTrue(all(p[0].endswith('/dispatches') for p in posts))
    def test_retry_is_bounded(self):
        with self.assertRaisesRegex(RuntimeError,'retry limit'):
            self.run_notice(['cancelled','failure','failure'])
    def test_missing_run_id_is_rejected(self):
        with self.assertRaisesRegex(RuntimeError,'run identity'):
            notify.notify(lambda p,d=None:{},lambda:0,lambda t:None)
    def test_wrong_run_context_is_rejected(self):
        def api(p,d=None):
            return {'workflow_run_id':1} if d else dict(event='workflow_dispatch',head_branch='dev')
        with contextlib.redirect_stdout(io.StringIO()),self.assertRaisesRegex(RuntimeError,'context mismatch'):
            notify.notify(api,lambda:0,lambda t:None)
    def test_wait_is_bounded(self):
        clock=[0]
        def api(p,d=None):
            return {'workflow_run_id':1} if d else dict(event='workflow_dispatch',head_branch='main',
                path='.github/workflows/'+notify.WORKFLOW,status='queued')
        with contextlib.redirect_stdout(io.StringIO()),self.assertRaisesRegex(RuntimeError,'timed out'):
            notify.notify(api,lambda:clock[0],lambda t:clock.__setitem__(0,clock[0]+t))
        self.assertEqual(clock[0],1200)
    def test_missing_token_does_not_call_api(self):
        with patch.dict(os.environ,{'GITHUB_EVENT_NAME':'push','GITHUB_REF':'refs/heads/main'},clear=True),patch.object(notify,'notify') as call,contextlib.redirect_stdout(io.StringIO()):
            notify.main();call.assert_not_called()
    def test_dev_never_calls_api(self):
        with patch.dict(os.environ,{'GITHUB_EVENT_NAME':'push','GITHUB_REF':'refs/heads/dev','GH_TOKEN':'test'},clear=True),patch.object(notify,'notify') as call,contextlib.redirect_stdout(io.StringIO()):
            notify.main();call.assert_not_called()
    def test_api_failure_is_warning_without_release_failure(self):
        with patch.dict(os.environ,{'GITHUB_EVENT_NAME':'push','GITHUB_REF':'refs/heads/main','GH_TOKEN':'test'},clear=True),patch.object(notify,'notify',side_effect=RuntimeError('secret detail')),contextlib.redirect_stdout(io.StringIO()) as out:
            notify.main();self.assertIn('::warning::',out.getvalue());self.assertNotIn('secret detail',out.getvalue())

if __name__=='__main__':unittest.main()
