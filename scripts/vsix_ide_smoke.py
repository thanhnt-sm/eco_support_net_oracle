#!/usr/bin/env python3
"""Opt-in, disposable hosted Windows install/load smoke. Exit 2 means BLOCKED."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import uuid
import zipfile
import xml.etree.ElementTree as ET


class Blocked(RuntimeError):
    pass


def select_host(hosts):
    if len(hosts) != 1:
        raise Blocked('host-selection-ambiguous-or-missing')
    host = hosts[0]
    if not (host.get('isComplete') and host.get('isLaunchable')
            and re.fullmatch(r'[A-Za-z0-9]+', host.get('instanceId', ''))
            and re.fullmatch(r'\d+\.\d+(?:\.\d+)*', host.get('installationVersion', ''))):
        raise Blocked('host-incomplete')
    return host


def install_arguments(help_text, instance, suffix, vsix):
    if not re.fullmatch(r'DGSmoke[0-9a-f]{32}', suffix) or not re.fullmatch(r'[A-Za-z0-9]+', instance):
        raise Blocked('unsafe-target')
    # Demand actual switch syntax in this installation's help, never prose or
    # instanceIds alone (that would target the normal user profile).
    for switch in ('quiet', 'instanceIds', 'rootSuffix'):
        pattern = r'(?i)(?<!\w)/' + switch + (r'\b' if switch == 'quiet' else r'\s*:')
        if not re.search(pattern, help_text):
            raise Blocked('installer-isolated-profile-switch-unverified')
    return ['/quiet', '/instanceIds:' + instance, '/rootSuffix:' + suffix, str(vsix)]


def clean_profile(path, parent, name, preexisting=False):
    if preexisting or path.is_symlink() or path.parent != parent or path.name != name:
        raise Blocked('cleanup-ownership-unverified')
    if path.exists():
        # Refuse junctions/reparse points anywhere in a tree before recursive deletion.
        for entry in [path, *path.rglob('*')]:
            attrs = getattr(entry.lstat(), 'st_file_attributes', 0)
            if entry.is_symlink() or attrs & 0x400:
                raise Blocked('cleanup-reparse-point')
        shutil.rmtree(path)


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def verify_payload(vsix, profile):
    with zipfile.ZipFile(vsix) as archive:
        names = [n for n in archive.namelist() if n == 'DataGuard.VisualStudio.dll']
        if len(names) != 1:
            raise Blocked('vsix-package-dll-missing-or-ambiguous')
        expected = hashlib.sha256(archive.read(names[0])).hexdigest()
    dlls = list((profile / 'Extensions').rglob('DataGuard.VisualStudio.dll'))
    if len(dlls) != 1 or sha(dlls[0]) != expected:
        raise Blocked('installed-package-bytes-unverified')
    return expected


def verify_probe(result, version):
    if (result.get('optionsRead') is not True or result.get('propertyType') != 'Boolean'
            or result.get('hostVersion', '').split('.')[0] != version.split('.')[0]):
        raise Blocked('package-options-load-proof-missing')


def verify_activity(path):
    try:
        root = ET.parse(path).getroot()
        for entry in root.findall('entry'):
            guid = (entry.findtext('guid') or '').strip('{}').lower()
            description = entry.findtext('description') or ''
            if (guid == '04a7c09c-4f79-439f-8298-952900cdb5ae'
                    and description.startswith('End package load')
                    and entry.findtext('type') == 'Information'
                    and (entry.findtext('hr') or '0').strip().lower() in ('0', '0x0', '00000000', '0x00000000')):
                return
    except (OSError, ET.ParseError):
        pass
    raise Blocked('package-completed-load-activity-evidence-missing')


def run(args, timeout=60):
    try:
        result = subprocess.run([str(a) for a in args], capture_output=True, timeout=timeout)
    except subprocess.TimeoutExpired:
        raise Blocked('process-timeout-possible-modal-consent-or-license') from None
    if result.returncode != 0:
        raise Blocked('process-nonzero-exit-' + str(result.returncode))
    # Windows PowerShell and console tools can use the OEM code page.
    return result.stdout.decode('utf-8-sig', errors='replace')


def execute(vsix, evidence, timeout):
    record = {'schema': 1, 'status': 'BLOCKED', 'phase': 'preflight', 'phases': [],
              'scope': 'Stage1-install-load-only', 'cleanup': 'not-needed'}
    ide = None
    profiles = []
    temporary = None
    try:
        if not vsix.is_file():
            raise Blocked('exact-built-vsix-missing')
        record['vsixSha256'] = sha(vsix)
        if (sys.platform != 'win32' or os.environ.get('GITHUB_ACTIONS') != 'true'
                or os.environ.get('RUNNER_ENVIRONMENT') != 'github-hosted'
                or os.environ.get('GITHUB_REPOSITORY') != 'thanhnt-sm/eco_support_net_oracle'):
            raise Blocked('requires-repo-disposable-github-hosted-windows')
        record['commit'] = run(['git', 'rev-parse', 'HEAD']).strip()
        vswhere = Path(os.environ['ProgramFiles(x86)']) / 'Microsoft Visual Studio/Installer/vswhere.exe'
        record['phase'] = 'host-discovery'
        hosts = json.loads(run([vswhere, '-latest', '-products', '*', '-requires',
                                'Microsoft.VisualStudio.Component.CoreEditor', 'Microsoft.Component.MSBuild', '-format', 'json']))
        host = select_host(hosts)
        version = host['installationVersion']
        if host['instanceId'] != os.environ.get('DG_SMOKE_INSTANCE'):
            raise Blocked('build-and-smoke-installation-mismatch')
        record.update(hostVersion=version, instanceId=host['instanceId'],
                      imageVersion=os.environ.get('ImageVersion', 'unknown'))
        installation = Path(host['installationPath'])
        installer = installation / 'Common7/IDE/VSIXInstaller.exe'
        devenv = installation / 'Common7/IDE/devenv.exe'
        if not installer.is_file() or not devenv.is_file():
            raise Blocked('selected-installation-tools-missing')
        record['installerSha256'] = sha(installer)
        suffix = 'DGSmoke' + uuid.uuid4().hex
        record['rootSuffix'] = suffix
        profile_name = version.split('.')[0] + '.0_' + host['instanceId'] + suffix
        for variable in ('LOCALAPPDATA', 'APPDATA'):
            parent = Path(os.environ[variable]) / 'Microsoft/VisualStudio'
            profile = parent / profile_name
            if profile.exists() or profile.is_symlink():
                raise Blocked('unique-profile-already-exists')
            profiles.append((profile, parent, profile_name))
        record['phase'] = 'installer-capability'
        help_text = run([installer, '/?'], timeout=30)
        record['installerHelpSha256'] = hashlib.sha256(help_text.encode()).hexdigest()
        # Do not retain arbitrary installer/IDE logs: they may contain paths or account data.
        temporary = tempfile.TemporaryDirectory(prefix='DGSmoke-')
        temp = temporary.name
        staged = Path(temp) / 'candidate.vsix'
        shutil.copyfile(vsix, staged)
        if sha(staged) != record['vsixSha256']:
            raise Blocked('staging-sha-mismatch')
        arguments = install_arguments(help_text, host['instanceId'], suffix, staged)
        record['phases'].append('installer-capability-verified')
        record['phase'] = 'install'
        run([installer, *arguments], timeout=timeout)
        if sha(staged) != record['vsixSha256']:
            raise Blocked('candidate-mutated')
        record['packageDllSha256'] = verify_payload(staged, profiles[0][0])
        record['phases'].append('installed-package-bytes-verified')
        record['phase'] = 'launch-load'
        # No solution, no /ResetSettings, /NoSplash, /admin, trust seed or consent bypass.
        ide = subprocess.Popen([str(devenv), '/RootSuffix', suffix, '/Log', str(Path(temp) / 'activity.xml')],
                               stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        record['launchedPid'] = ide.pid
        powershell = Path(os.environ['SystemRoot']) / 'System32/WindowsPowerShell/v1.0/powershell.exe'
        probe = Path(__file__).with_name('vsix_ide_probe.ps1')
        result = json.loads(run([powershell, '-NoProfile', '-STA', '-File', probe,
                                 '-ProcessId', str(ide.pid), '-MajorVersion', version.split('.')[0]], timeout=timeout))
        verify_probe(result, version)
        try:
            ide.wait(timeout=15)
        except subprocess.TimeoutExpired:
            raise Blocked('owned-ide-did-not-exit-after-options-probe') from None
        if ide.returncode != 0:
            raise Blocked('owned-ide-nonzero-exit')
        verify_activity(Path(temp) / 'activity.xml')
        record['phases'].extend(['canonical-options-property-read', 'package-load-completed-in-activity-log'])
        record['status'] = 'PASS'
        record['phase'] = 'complete'
    except Blocked as exc:
        record['reason'] = str(exc)
    except Exception as exc:
        # Exception text can expose account names, file paths or other private data.
        record['status'] = 'FAIL'
        record['reason'] = 'unexpected-' + type(exc).__name__
    finally:
        if ide is not None and ide.poll() is None:
            # Popen retains the exact process handle on Windows, preventing PID-reuse kills.
            try:
                ide.terminate()
                ide.wait(timeout=15)
            except Exception:
                record['status'] = 'BLOCKED'
                record['cleanup'] = 'owned-ide-still-running-dispose-host'
        if profiles and (ide is None or ide.poll() is not None):
            try:
                for profile, parent, name in profiles:
                    clean_profile(profile, parent, name)
                record['cleanup'] = 'exact-owned-profile-directories-removed-dispose-host-for-registry-and-services'
            except Exception:
                record['status'] = 'BLOCKED'
                record['cleanup'] = 'profile-locked-or-ownership-unverified-dispose-host'
        if temporary is not None:
            if ide is None or ide.poll() is not None:
                try:
                    temporary.cleanup()
                except Exception:
                    record['status'] = 'BLOCKED'
                    record['cleanup'] = 'owned-temp-locked-dispose-host'
            else:
                record['status'] = 'BLOCKED'
        evidence.parent.mkdir(parents=True, exist_ok=True)
        evidence.write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
    return 0 if record['status'] == 'PASS' else 2 if record['status'] == 'BLOCKED' else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--vsix', required=True, type=Path)
    parser.add_argument('--evidence', required=True, type=Path)
    parser.add_argument('--timeout', type=int, default=120, choices=range(30, 301))
    args = parser.parse_args()
    sys.exit(execute(args.vsix, args.evidence, args.timeout))
