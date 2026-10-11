"""Wake the existing Index scanner after a successful main release.

Notifications never approve a package. A maintainer-owned Actions:write token
on the Index repository is optional; hourly scanning remains the fallback.
"""
import json
import os
import subprocess
import time

INDEX = 'HunYuan2333/Phinix-Plugin-Index'
WORKFLOW = 'plugin-source-updates.yml'
PREFIX = 'repos/' + INDEX


def api(path, data=None):
    command = ['gh', 'api', '-H', 'X-GitHub-Api-Version: 2026-03-10', PREFIX + path]
    if data is not None:
        command += ['--method', 'POST', '--input', '-']
    result = subprocess.run(command, input=None if data is None else json.dumps(data),
                            text=True, capture_output=True, timeout=45)
    if result.returncode:
        # Do not echo response bodies, headers, command environment or credentials.
        raise RuntimeError('Index API request failed')
    return json.loads(result.stdout)


def notify(request=api, now=time.monotonic, sleep=time.sleep):
    deadline = now() + 1200
    for attempt in range(3):
        # Ref main is resolved by GitHub for this new run. Never rerun an old SHA.
        result = request('/actions/workflows/' + WORKFLOW + '/dispatches',
                         {'ref': 'main', 'inputs': {'check_only': False}})
        run_id = result.get('workflow_run_id')
        if type(run_id) is not int or run_id < 1:
            raise RuntimeError('Index dispatch returned no run identity')
        print('Index scan: https://github.com/' + INDEX + '/actions/runs/' + str(run_id))
        while now() < deadline:
            run = request('/actions/runs/' + str(run_id))
            if (run.get('event') != 'workflow_dispatch' or run.get('head_branch') != 'main'
                    or run.get('path') != '.github/workflows/' + WORKFLOW):
                raise RuntimeError('Index run context mismatch')
            if run.get('status') == 'completed':
                if run.get('conclusion') == 'success':
                    print('Index scan completed; admission and catalog publication remain independently validated.')
                    return True
                break
            sleep(min(15, max(0, deadline - now())))
        if now() >= deadline:
            raise RuntimeError('Index scan wait timed out; scheduled scanning remains enabled')
        if attempt < 2:
            print('Index scan did not complete successfully; dispatching a fresh main snapshot.')
            sleep(15)
    raise RuntimeError('Index scan retry limit reached; scheduled scanning remains enabled')


def main():
    if os.environ.get('GITHUB_EVENT_NAME') != 'push' or os.environ.get('GITHUB_REF') != 'refs/heads/main':
        print('::warning::Index notification skipped outside a main push.')
        return
    if not os.environ.get('GH_TOKEN'):
        print('::warning::Configure INDEX_UPDATE_TOKEN (Index Actions:write, maintainer-owned); using scheduled scanning meanwhile.')
        return
    try:
        notify()
    except (RuntimeError, OSError, ValueError, subprocess.TimeoutExpired):
        print('::warning::Index notification was not confirmed; the plugin Release remains published. Check Index runs or trigger its scanner manually.')


if __name__ == '__main__':
    main()
