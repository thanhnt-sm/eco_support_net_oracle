#!/usr/bin/env python3
"""Fail-closed contracts for the manual Marketplace workflow. No publishing or credential setup."""
from datetime import date
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

REPOSITORY = 'thanhnt-sm/eco_support_net_oracle'
WORKFLOW = '.github/workflows/marketplace-release.yml'
VS_ENTRIES = ('cli/dataguard.exe', 'DataGuard.Analyzers.dll', 'DataGuard.CodeFixes.dll',
              'DataGuard.Contracts.dll', 'DataGuard.SqlClassification.dll', 'DataGuard.VisualStudio.dll',
              'DataGuard.VisualStudio.pkgdef', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'ADDITIONAL-PERMISSIONS.md')
SHA = re.compile(r'[a-f0-9]{40}')
HASH = re.compile(r'[a-f0-9]{64}')


def require(condition, message):
    if not condition:
        raise ValueError(message)


def targets(config):
    return ('vscode', 'visualstudio') if config['target'] == 'both' else (config['target'],)


def validate_inputs(config, *, repository, event, ref, today=None):
    require(repository == REPOSITORY and event == 'workflow_dispatch' and ref == 'refs/heads/main',
            'Only a manual dispatch from this repository main is allowed')
    require(isinstance(config, dict), 'Release inputs must be an object')
    config = dict(config)
    # GitHub omits blank optional dispatch inputs. All entrypoints consume the
    # same canonical mapping; malformed explicit values are never coerced.
    for field in ('release_tag', 'prepared_run_id', 'vscode_sha256',
                  'visualstudio_sha256', 'readiness_url', 'confirm'):
        config.setdefault(field, '')
        require(isinstance(config[field], str), 'Optional release inputs must be strings: ' + field)
    require(type(config.get('publish')) is bool, 'publish must be a boolean')
    tag = config.get('release_tag', '')
    require(not config['publish'] or bool(tag), 'Publication requires an existing release tag')
    version = None
    if tag:
        require(re.fullmatch(r'v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', tag), 'Use a stable vMAJOR.MINOR.PATCH tag')
        version = tag[1:]
        parts = tuple(map(int, version.split('.')))
        require(max(parts) <= 65535, 'VS numeric version bounds exceeded')
        require(not config['publish'] or parts >= (0, 4, 0), 'Resolve the GPL transition before publication')
    require(SHA.fullmatch(config.get('source_sha', '')), 'A full lowercase source SHA is required')
    require(config.get('target') in ('vscode', 'visualstudio', 'both'), 'Invalid target')
    result = dict(config, version=version)
    if config['publish']:
        require((today or date.today()) < date(2026, 12, 1), 'PAT bridge expired; review supported authentication before publication')
        require(re.fullmatch(r'[1-9][0-9]*', config.get('prepared_run_id', '')), 'A successful preparation run ID is required')
        require(config.get('confirm') == f"PUBLISH {tag} {config['target']}", 'Exact publication confirmation is required')
        require(re.fullmatch(r'https://github\.com/thanhnt-sm/eco_support_net_oracle/(?:issues/[1-9][0-9]*|blob/[a-f0-9]{40}/[A-Za-z0-9_./-]+)', config.get('readiness_url', '')),
                'Record owner-approved package/editor/publisher/license readiness in this repository')
        for target in targets(result):
            require(HASH.fullmatch(config.get(target + '_sha256', '')), 'Selected package requires the human-accepted SHA-256')
    return result


def validate_run(run, run_id, workflow_sha):
    expected = dict(id=int(run_id), run_attempt=1, event='workflow_dispatch', head_branch='main',
                    head_sha=workflow_sha, path=WORKFLOW, status='completed', conclusion='success')
    require(all(run.get(k) == v for k, v in expected.items()) and run.get('repository', {}).get('full_name') == REPOSITORY,
            'Preparation must be a successful first-attempt run of this exact main workflow revision')


def validate_environment(environment):
    # Reuse the repository's release environment. GitHub enforces its configured rules;
    # a solo maintainer is not required to invent a second reviewer or alter settings.
    require(environment.get('name') == 'release',
            'Existing release environment is unavailable; no environment or settings are created')


def select_artifact(response, target, run_id, workflow_sha):
    items = response.get('artifacts', [])
    require(response.get('total_count') == len(items) and len(items) <= 100, 'Incomplete artifact inventory')
    selected = [a for a in items if a.get('name') == 'marketplace-release-' + target]
    require(len(selected) == 1, 'Expected exactly one target artifact')
    item = selected[0]; run = item.get('workflow_run') or {}
    require(item.get('expired') is False and isinstance(item.get('id'), int) and item['id'] > 0
            and re.fullmatch(r'sha256:[a-f0-9]{64}', item.get('digest', ''))
            and run.get('id') == int(run_id) and run.get('head_sha') == workflow_sha,
            'Artifact is expired, unbound or missing its service digest')
    return item['id']


def inspect_package(path, target, version):
    require(target in ('vscode', 'visualstudio'), 'Invalid package target')
    with zipfile.ZipFile(path) as archive:
        entries = archive.infolist()
        names = [e.filename.replace('\\', '/') for e in entries]
        require(len(entries) <= 30000 and sum(e.file_size for e in entries) <= 2 * 1024**3, 'Package exceeds inspection bounds')
        require(len(names) == len({name.casefold() for name in names}), 'Duplicate or case-alias archive entries')
        require(all(all(part not in ('', '.') for part in name.rstrip('/').split('/')) for name in names), 'Noncanonical archive path')
        require(all(not n.startswith('/') and ':' not in n and '..' not in PurePosixPath(n).parts for n in names), 'Unsafe archive path')
        require(all((e.external_attr >> 16) & 0o170000 != 0o120000 for e in entries), 'Symlink archive entry')
        by_name = dict(zip(names, entries))
        def read(name):
            require(name in by_name and by_name[name].file_size > 0, 'Required package payload missing: ' + name)
            return archive.read(by_name[name])
        if target == 'vscode':
            metadata = json.loads(read('extension/package.json'))
            require((metadata.get('publisher'), metadata.get('name'), metadata.get('version')) == ('thanhnt-sm', 'dataguard-vscode', version), 'VS Code identity/version mismatch')
            for name in ('out/extension.js', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'ADDITIONAL-PERMISSIONS.md',
                         'server/DataGuard.LanguageServer.deps.json', 'server/DataGuard.LanguageServer.runtimeconfig.json'):
                read('extension/' + name)
            dll = read('extension/server/DataGuard.LanguageServer.dll')
            manifest = json.loads(read('extension/server/manifest.json'))
            require(manifest == {'schemaVersion': 1, 'artifact': 'DataGuard.LanguageServer.dll', 'sha256': hashlib.sha256(dll).hexdigest()}, 'LSP integrity mismatch')
        else:
            raw = read('extension.vsixmanifest')
            require(b'<!DOCTYPE' not in raw.upper() and b'<!ENTITY' not in raw.upper(), 'XML entities are not allowed')
            identities = ET.fromstring(raw).findall('.//{http://schemas.microsoft.com/developer/vsx-schema/2011}Identity')
            require(len(identities) == 1, 'Missing or ambiguous VS identity')
            identity = identities[0]
            require((identity.get('Publisher'), identity.get('Id'), identity.get('Version')) == ('thanhnt-sm', 'DataGuard.VisualStudio', version), 'Visual Studio identity/version mismatch')
            for name in VS_ENTRIES: read(name)
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def record_package(path, target, config, workflow_sha, run_id):
    result = {'schema_version': 1, 'repository': REPOSITORY, 'workflow': WORKFLOW, 'workflow_sha': workflow_sha,
            'source_sha': config['source_sha'], 'release_tag': config['release_tag'], 'version': config['version'],
            'target': target, 'prepared_run_id': str(run_id), 'file': 'package.vsix',
            'sha256': inspect_package(path, target, config['version'])}
    if target == 'visualstudio':
        folder = Path(path).parent
        fields = {}
        for name in ('vs-publish.json', 'overview.md'):
            item = folder / name
            require(item.is_file() and not item.is_symlink() and 0 < item.stat().st_size <= 5 * 1024**2,
                    'Missing or unsafe VS listing metadata')
            fields[name] = hashlib.sha256(item.read_bytes()).hexdigest()
        publish = json.loads((folder / 'vs-publish.json').read_text(encoding='utf-8'))
        require(publish.get('publisher') == 'thanhnt-sm'
                and publish.get('identity', {}).get('internalName') == 'DataGuard.VisualStudio'
                and publish.get('private') is False and publish.get('overview') == 'overview.md',
                'VS listing publisher/identity/visibility/overview mismatch')
        result['listing_sha256'] = fields
    return result


def verify_package(path, manifest, target, config, workflow_sha, run_id, accepted_hash):
    expected = record_package(path, target, config, workflow_sha, run_id)
    require(manifest == expected, 'Package manifest/source/run mismatch')
    require(HASH.fullmatch(accepted_hash) and expected['sha256'] == accepted_hash, 'Package differs from the human-accepted bytes')


def api(endpoint):
    # Fixed repository and caller-validated numeric IDs only. Do not print response/error bodies.
    result = subprocess.run(['gh', 'api', 'repos/' + REPOSITORY + '/' + endpoint], capture_output=True, text=True)
    require(result.returncode == 0, 'Required GitHub read failed; verify existing access, do not bypass')
    return json.loads(result.stdout)


def git(root, *args):
    result = subprocess.run(['git', '-C', str(root), *args], capture_output=True, text=True)
    require(result.returncode == 0, 'Source/tag/main ancestry verification failed')
    return result.stdout.strip()


def verify_source(root, config, workflow_sha):
    sha = config['source_sha']; version = config['version']
    require(git(root, 'rev-parse', 'HEAD') == sha, 'Checkout does not match the immutable source')
    if config.get('release_tag'):
        require(git(root, 'rev-parse', 'refs/tags/' + config['release_tag'] + '^{commit}') == sha, 'Release tag moved or mismatches source')
    git(root, 'merge-base', '--is-ancestor', sha, workflow_sha)
    package = json.loads((root / 'src/DataGuard.VSCode/package.json').read_text())
    lock = json.loads((root / 'src/DataGuard.VSCode/package-lock.json').read_text())
    version = version or package['version']
    require(re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', version), 'Source version must be stable numeric')
    require(max(map(int, version.split('.'))) <= 65535, 'VS numeric version bounds exceeded')
    config['version'] = version
    identity = ET.parse(root / 'src/DataGuard.VisualStudio/source.extension.vsixmanifest').find('.//{http://schemas.microsoft.com/developer/vsx-schema/2011}Identity')
    require(package['version'] == lock['version'] == lock['packages']['']['version'] == identity.get('Version') == version, 'Source package versions must match release tag; no automatic bump')
    require(package['publisher'] == identity.get('Publisher') == 'thanhnt-sm' and package['name'] == 'dataguard-vscode' and identity.get('Id') == 'DataGuard.VisualStudio', 'Unexpected publisher or product')
    publish = json.loads((root / 'src/DataGuard.VisualStudio/vs-publish.json').read_text())
    require(publish.get('publisher') == 'thanhnt-sm' and publish.get('identity', {}).get('internalName') == 'DataGuard.VisualStudio' and publish.get('private') is False, 'VS publishing manifest mismatch')
    fallback = (root / 'src/DataGuard.VisualStudio/ExtensionVersion.cs').read_text()
    require(f'Fallback = "{version}"' in fallback, 'VS fallback version differs')
    def floor(value):
        require(re.fullmatch(r'\^\d+\.\d+\.\d+', value), 'Review unsupported engine constraint before release')
        return tuple(map(int, value[1:].split('.')))
    blockers = []
    if tuple(map(int, version.split('.'))) < (0, 4, 0):
        blockers.append('Version/license transition unresolved: build-only artifact, not publication-ready')
    try:
        require(floor(package['engines']['vscode']) >= floor(lock['packages']['node_modules/vscode-languageclient']['engines']['vscode']), 'VS Code engine is below dependency requirement')
    except ValueError as error:
        blockers.append(str(error))
    require(not config['publish'] or not blockers, '; '.join(blockers))
    return blockers


def main():
    mode = sys.argv[1]
    config = validate_inputs(json.loads(os.environ['RELEASE_INPUTS']), repository=os.environ['GITHUB_REPOSITORY'],
                             event=os.environ['GITHUB_EVENT_NAME'], ref=os.environ['GITHUB_REF'])
    workflow_sha = os.environ['GITHUB_SHA']
    require(SHA.fullmatch(workflow_sha), 'Invalid workflow SHA')
    if mode == 'source':
        blockers = verify_source(Path(sys.argv[2]), config, workflow_sha)
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as summary:
            summary.write('Build-only preparation does not authorize publication.\n')
            for blocker in blockers:
                summary.write('- ' + blocker + '\n')
        outputs = {'version': config['version']}
        if config['publish']:
            env = api('environments/release')
            validate_environment(env)
            run_id = config['prepared_run_id']
            validate_run(api('actions/runs/' + run_id), run_id, workflow_sha)
            artifacts = api('actions/runs/' + run_id + '/artifacts?per_page=100')
            for target in targets(config):
                outputs[target + '_artifact_id'] = str(select_artifact(artifacts, target, run_id, workflow_sha))
        with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as out:
            for key, value in outputs.items(): out.write(f'{key}={value}\n')
    elif mode == 'record':
        target, folder = sys.argv[2], Path(sys.argv[3])
        config['version'] = os.environ['PACKAGE_VERSION']
        manifest = record_package(folder / 'package.vsix', target, config, workflow_sha, os.environ['GITHUB_RUN_ID'])
        (folder / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        (folder / 'package.vsix.sha256').write_text(manifest['sha256'] + '  package.vsix\n')
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a', encoding='utf-8') as out:
            out.write(f"\n{target}: `{manifest['sha256']}`; source `{config['source_sha']}`; version `{config['version']}`. Install and approve these exact bytes before publication.\n")
    elif mode == 'verify':
        target, folder = sys.argv[2], Path(sys.argv[3])
        require(config['publish'] and target in targets(config), 'Publication target is not selected')
        verify_package(folder / 'package.vsix', json.loads((folder / 'manifest.json').read_text()), target,
                       config, workflow_sha, config['prepared_run_id'], config[target + '_sha256'])
    else:
        raise ValueError('Unknown release verification mode')


if __name__ == '__main__':
    try:
        main()
    except (ValueError, OSError, KeyError, ET.ParseError, zipfile.BadZipFile) as error:
        # Validation errors describe contracts, not API payloads or credential values.
        print('marketplace-release: ' + str(error), file=sys.stderr)
        sys.exit(1)
