#!/usr/bin/env python3
"""Run the plugin's CI gates locally — read from the workflow files, not re-typed.

WHY THIS EXISTS
===============
Twice in one day a PR went red on a gate the local check did not run: first
check_param_contract.py (a new reader of MAT_SPECIFICATIONS), then
check_roadmap_ids.py (a ROADMAP id used on two rows). Both were in CI; neither
was in the hand-kept list of "the gates" that had been run before pushing. A
hand-kept list of CI steps is a second copy of CI, and a second copy drifts.

So this script has no list of steps. It reads .github/workflows/*.yml and runs
every `run:` step of every job in the workflows that cover the plugin, in
order, the way the runner would. What it keeps by hand is only the
CLASSIFICATION of workflows — run, skipped with a reason, or out of scope — and
a workflow that is in none of the three is an ERROR, so a new CI workflow can
not be silently left out of the local run.

Steps it does not run, each reported with its reason: package installs
(pip / npm ci — install once yourself), artefact packaging (it writes into
CompiledPlugin, which a live Revit .addin may point at), and steps using a
GitHub expression it cannot evaluate.

USAGE
=====
    python tools/run_ci_gates.py            # everything in scope, builds and tests included
    python tools/run_ci_gates.py --quick    # skip the .NET build and unit-test jobs
    python tools/run_ci_gates.py --list     # show what would run, run nothing

Run it on a COMMITTED tree: several gates regenerate a file and fail if
`git diff` then shows a change, and an uncommitted edit to one of those files
reads as drift.

Needs PyYAML (`python -m pip install pyyaml`), Git Bash on Windows, and the
.NET 8 SDK for the build / test jobs.
"""
import argparse
import glob
import os
import re
import shutil
import subprocess
import sys
import tempfile

try:
    import yaml
except ImportError:
    sys.exit('run_ci_gates: PyYAML is required - python -m pip install pyyaml')

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
WF_DIR = os.path.join(REPO, '.github', 'workflows')

# workflow file -> jobs to run (None = all). The plugin's gates.
RUN = {
    'stingtools-plugin.yml': ['validate-data', 'build'],
    'stingtools-unit-tests.yml': None,
    'binding-spec-drift.yml': None,
    'param-csv-drift.yml': None,
    'param-name-targets.yml': None,
    'docs-index.yml': None,
    'roadmap-ids.yml': None,
    'smoke-test-gate.yml': None,
    'kut-workflow-tags.yml': None,
    'kut-document-gate.yml': None,
    'aps-scopes.yml': None,
}
# In scope for the plugin, deliberately not run here — with the reason.
SKIP = {
    'plugin-build.yml': 'duplicate of stingtools-plugin.yml "build" against the Revit NuGet refs, plus a '
                        'deploy-bundle stage; the local build uses the installed Revit',
    'plugin-release.yml': 'release packaging, runs on tags only',
    'ci-gate.yml': 'waits on the other checks; nothing of its own to run',
    'pr-labeler.yml': 'labels PRs; no gate',
}
# Jobs of a RUN workflow that are not plugin gates.
SKIP_JOBS = {
    ('stingtools-plugin.yml', 'viewer-check'): 'the web viewer bundle (npm) - not plugin code',
}
# Not the plugin: server, mobile, web, marketing, IFC substrate, multi-host core.
OUT_OF_SCOPE = {
    'contract-drift.yml', 'coordination-viewer-drift.yml', 'ifc-substrate.yml',
    'marketing-site-deploy.yml', 'marketing-site.yml', 'meetings-core-parity.yml',
    'multi-host-core.yml', 'planscape-mobile.yml', 'planscape-server.yml',
    'planscape-web.yml', 'viewer-served-artifact.yml',
}
HEAVY_JOBS = {('stingtools-plugin.yml', 'build'), ('stingtools-unit-tests.yml', 'unit-tests')}

INSTALL = re.compile(r'^\s*(python3? -m pip install|pip3? install|npm (ci|install)|apt(-get)? )', re.M)
PACKAGE = re.compile(r'(?i)^(package|stage)\b')


def bash_exe():
    for cand in (r'C:\Program Files\Git\bin\bash.exe', r'C:\Program Files (x86)\Git\bin\bash.exe'):
        if os.path.exists(cand):
            return cand
    return shutil.which('bash')


def pwsh_exe():
    return shutil.which('pwsh') or shutil.which('powershell')


def classify():
    files = sorted(os.path.basename(f) for f in glob.glob(os.path.join(WF_DIR, '*.yml')))
    known = set(RUN) | set(SKIP) | OUT_OF_SCOPE
    unknown = [f for f in files if f not in known]
    missing = [f for f in known if f not in files]
    return files, unknown, missing


def expand(cmd, env):
    """${{ env.X }} -> value; any other expression makes the step unrunnable here."""
    cmd = re.sub(r'\$\{\{\s*env\.(\w+)\s*\}\}', lambda m: env.get(m.group(1), ''), cmd)
    return None if '${{' in cmd else cmd


def run_step(cmd, shell, cwd, env):
    if shell in ('pwsh', 'powershell'):
        exe = pwsh_exe()
        if not exe:
            return None, 'no PowerShell on PATH'
        fd, path = tempfile.mkstemp(suffix='.ps1')
        with os.fdopen(fd, 'w', encoding='utf-8-sig') as f:
            f.write('$ErrorActionPreference = "Stop"\n' + cmd)
        argv = [exe, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path]
    else:
        exe = bash_exe()
        if not exe:
            return None, 'no bash on PATH'
        fd, path = tempfile.mkstemp(suffix='.sh')
        with os.fdopen(fd, 'w', encoding='utf-8', newline='\n') as f:
            f.write('set -e\n' + cmd.replace('python3 ', 'python '))
        argv = [exe, path]
    p = subprocess.run(argv, cwd=cwd, env=env, capture_output=True, text=True,
                       encoding='utf-8', errors='replace')
    os.remove(path)
    return p.returncode == 0, (p.stdout + p.stderr)


def dirty_paths():
    """Tracked files git status reports as modified in the working tree. (git diff
    --name-only misses a file whose content is equal after EOL normalisation but whose
    bytes are not — exactly the case this is for.)"""
    out = subprocess.run(['git', 'status', '--porcelain'], cwd=REPO, capture_output=True, text=True).stdout
    return [l[3:].strip() for l in out.splitlines() if len(l) > 3 and l[1] == 'M']


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n\n')[0])
    ap.add_argument('--quick', action='store_true', help='skip the .NET build and unit-test jobs')
    ap.add_argument('--list', action='store_true', help='print what would run, run nothing')
    args = ap.parse_args()

    files, unknown, missing = classify()
    if unknown or missing:
        for f in unknown:
            print(f'UNCLASSIFIED workflow: {f} - add it to RUN, SKIP or OUT_OF_SCOPE in tools/run_ci_gates.py')
        for f in missing:
            print(f'STALE entry: {f} is classified but no longer exists - remove it')
        return 2

    dirty = subprocess.run(['git', 'status', '--porcelain'], cwd=REPO, capture_output=True, text=True).stdout.strip()
    dirty_before = set(dirty_paths())
    if dirty and not args.list:
        print('note: the working tree has uncommitted changes; gates that regenerate a file and '
              'git-diff it will report any uncommitted edit to that file as drift.\n')

    summary = os.path.join(tempfile.gettempdir(), 'run_ci_gates_summary.md')
    base_env = dict(os.environ, PYTHONUTF8='1', PYTHONIOENCODING='utf-8',
                    GITHUB_STEP_SUMMARY=summary, GITHUB_OUTPUT=summary + '.out', CI='true')
    results = []
    for wf_file in sorted(RUN):
        wf = yaml.safe_load(open(os.path.join(WF_DIR, wf_file), encoding='utf-8'))
        wf_env = dict(base_env, **{k: str(v) for k, v in (wf.get('env') or {}).items()})
        for job_name, job in (wf.get('jobs') or {}).items():
            if RUN[wf_file] is not None and job_name not in RUN[wf_file]:
                if (wf_file, job_name) in SKIP_JOBS:
                    results.append(('SKIP', f'{wf_file}:{job_name}', SKIP_JOBS[(wf_file, job_name)]))
                    continue
                results.append(('SKIP', f'{wf_file}:{job_name}', 'not a plugin gate'))
                continue
            if args.quick and (wf_file, job_name) in HEAVY_JOBS:
                results.append(('SKIP', f'{wf_file}:{job_name}', '--quick'))
                continue
            env = dict(wf_env, **{k: str(v) for k, v in (job.get('env') or {}).items()})
            defaults = ((job.get('defaults') or {}).get('run') or {})
            for st in job.get('steps') or []:
                if 'run' not in st:
                    continue
                name = f"{wf_file}:{job_name} :: {st.get('name') or st['run'].splitlines()[0]}"
                cmd = expand(st['run'], env)
                if cmd is None:
                    results.append(('SKIP', name, 'uses a GitHub expression')); continue
                if INSTALL.search(cmd):
                    results.append(('SKIP', name, 'package install - install once yourself')); continue
                if PACKAGE.match(st.get('name') or ''):
                    results.append(('SKIP', name, 'artefact packaging - writes into CompiledPlugin')); continue
                if args.list:
                    results.append(('WOULD RUN', name, '')); continue
                shell = st.get('shell') or defaults.get('shell') or 'bash'
                cwd = os.path.join(REPO, st.get('working-directory') or defaults.get('working-directory') or '.')
                ok, out = run_step(cmd, shell, cwd, dict(env, **{k: str(v) for k, v in (st.get('env') or {}).items()}))
                if ok is None:
                    results.append(('SKIP', name, out)); continue
                results.append(('PASS' if ok else 'FAIL', name, ''))
                if not ok:
                    print(f'--- FAIL: {name}\n{out[-3000:]}\n')

    # Regenerating steps rewrite files. Where the only difference left is line endings
    # (a generator writing LF on a CRLF checkout) there is nothing to review — put the
    # file back so a clean tree stays clean. Real content drift is what the gates report.
    if not args.list:
        for path in sorted(set(dirty_paths()) - dirty_before):
            same = subprocess.run(['git', 'diff', '--quiet', '--', path], cwd=REPO).returncode == 0
            if same:
                subprocess.run(['git', 'checkout', '--', path], cwd=REPO, capture_output=True)

    for f in sorted(SKIP):
        results.append(('SKIP', f, SKIP[f]))
    width = max(len(n) for _, n, _ in results)
    for status, name, why in results:
        print(f'{status:9} {name.ljust(width)}  {why}'.rstrip())
    fails = sum(1 for s, _, _ in results if s == 'FAIL')
    passes = sum(1 for s, _, _ in results if s == 'PASS')
    print(f'\n{passes} passed, {fails} failed, '
          f'{sum(1 for s, _, _ in results if s == "SKIP")} skipped '
          f'({len(OUT_OF_SCOPE)} workflows out of plugin scope)')
    return 1 if fails else 0


if __name__ == '__main__':
    sys.exit(main())
