"""Adversarial offline release contracts; no credentials, network or publication."""
import copy
import importlib.util
import json
import os
import subprocess
import sys
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
from datetime import date

import yaml

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('marketplace_release', ROOT / 'scripts/marketplace_release.py')
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
SHA = 'a' * 40
REPO = 'thanhnt-sm/eco_support_net_oracle'


class ReleaseContracts(unittest.TestCase):
    def config(self, publish=False):
        return {'release_tag': 'v0.4.1', 'source_sha': SHA, 'target': 'both', 'publish': publish,
                'prepared_run_id': '123', 'vscode_sha256': 'b' * 64, 'visualstudio_sha256': 'c' * 64,
                'readiness_url': f'https://github.com/{REPO}/issues/71', 'confirm': 'PUBLISH v0.4.1 both'}

    def validate(self, config, **kwargs):
        return release.validate_inputs(config, repository=REPO, event='workflow_dispatch',
                                       ref='refs/heads/main', today=date(2026, 10, 18), **kwargs)

    def test_valid_prepare_and_publish(self):
        self.assertEqual(self.validate(self.config())['version'], '0.4.1')
        self.assertTrue(self.validate(self.config(True))['publish'])

    def test_optional_fields_are_normalized_without_mutating_input(self):
        optional = ('release_tag', 'prepared_run_id', 'vscode_sha256', 'visualstudio_sha256', 'readiness_url', 'confirm')
        minimal = {'source_sha': SHA, 'target': 'both', 'publish': False}
        omitted = self.validate(minimal)
        explicit = self.validate(dict(minimal, **dict.fromkeys(optional, '')))
        self.assertEqual(omitted, explicit)
        self.assertEqual(minimal, {'source_sha': SHA, 'target': 'both', 'publish': False})
        for field in optional:
            for value in (None, False, 123, [], {}):
                with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                    self.validate(dict(minimal, **{field: value}))

    def test_publication_still_requires_every_selected_optional_field(self):
        for field in ('release_tag', 'prepared_run_id', 'vscode_sha256', 'visualstudio_sha256', 'readiness_url', 'confirm'):
            for omitted in (True, False):
                config = self.config(True)
                if omitted: del config[field]
                else: config[field] = ''
                with self.subTest(field=field, omitted=omitted), self.assertRaises(ValueError):
                    self.validate(config)
        config = self.config(True)
        config.update(target='vscode', confirm='PUBLISH v0.4.1 vscode')
        del config['visualstudio_sha256']
        self.assertEqual(self.validate(config)['visualstudio_sha256'], '')

    def test_record_entrypoint_accepts_omitted_and_empty_inputs_for_both_packages(self):
        optional = ('release_tag', 'prepared_run_id', 'vscode_sha256', 'visualstudio_sha256', 'readiness_url', 'confirm')
        for target in ('vscode', 'visualstudio'):
            recorded = []
            for explicit in (False, True):
                with self.subTest(target=target, explicit=explicit), tempfile.TemporaryDirectory() as directory:
                    folder = Path(directory)
                    self.package(directory, product=target, version='0.3.0')
                    if target == 'visualstudio':
                        (folder / 'vs-publish.json').write_text(json.dumps({
                            'publisher': 'thanhnt-sm', 'identity': {'internalName': 'DataGuard.VisualStudio'},
                            'private': False, 'overview': 'overview.md'}))
                        (folder / 'overview.md').write_text('Synthetic listing')
                    inputs = {'source_sha': SHA, 'target': 'both', 'publish': False}
                    if explicit: inputs.update(dict.fromkeys(optional, ''))
                    env = {'RELEASE_INPUTS': json.dumps(inputs), 'GITHUB_REPOSITORY': REPO,
                           'GITHUB_EVENT_NAME': 'workflow_dispatch', 'GITHUB_REF': 'refs/heads/main',
                           'GITHUB_SHA': SHA, 'GITHUB_RUN_ID': '123', 'PACKAGE_VERSION': '0.3.0',
                           'GITHUB_STEP_SUMMARY': str(folder / 'summary.md')}
                    result = subprocess.run([sys.executable, str(ROOT / 'scripts/marketplace_release.py'),
                                             'record', target, directory], env=env, capture_output=True, text=True)
                    self.assertEqual(result.returncode, 0, result.stderr)
                    manifest = json.loads((folder / 'manifest.json').read_text())
                    self.assertEqual(manifest['release_tag'], '')
                    self.assertEqual(manifest['version'], '0.3.0')
                    self.assertEqual(manifest['source_sha'], SHA)
                    self.assertTrue((folder / 'package.vsix.sha256').is_file())
                    # ZIP timestamps vary; compare the canonical dispatch metadata separately from package bytes.
                    recorded.append({key: value for key, value in manifest.items() if key != 'sha256'})
            self.assertEqual(recorded[0], recorded[1])

    def test_source_and_verify_entrypoints_share_optional_input_validation(self):
        minimal = {'source_sha': SHA, 'target': 'both', 'publish': False}
        with tempfile.TemporaryDirectory() as directory:
            env = {'RELEASE_INPUTS': json.dumps(minimal), 'GITHUB_REPOSITORY': REPO,
                   'GITHUB_EVENT_NAME': 'workflow_dispatch', 'GITHUB_REF': 'refs/heads/main',
                   'GITHUB_SHA': SHA, 'GITHUB_STEP_SUMMARY': str(Path(directory) / 'summary.md'),
                   'GITHUB_OUTPUT': str(Path(directory) / 'output')}
            def source(root, config, workflow_sha):
                self.assertEqual(config['release_tag'], '')
                config['version'] = '0.3.0'
                return ['Build-only preview']
            with patch.dict(os.environ, env, clear=True), patch.object(sys, 'argv', ['release', 'source', directory]), patch.object(release, 'verify_source', side_effect=source), patch.object(release, 'api') as api:
                release.main()
                api.assert_not_called()
            self.assertEqual((Path(directory) / 'output').read_text(), 'version=0.3.0\n')
            env['RELEASE_INPUTS'] = json.dumps(dict(minimal, publish=True))
            with patch.dict(os.environ, env, clear=True), patch.object(sys, 'argv', ['release', 'verify', 'vscode', directory]), patch.object(release, 'verify_package') as verify:
                with self.assertRaisesRegex(ValueError, 'Publication requires an existing release tag'):
                    release.main()
                verify.assert_not_called()

    def test_invalid_input_matrix(self):
        mutations = [('release_tag', 'nightly'), ('release_tag', 'v0.3.0'), ('release_tag', 'v01.4.0'),
                     ('release_tag', 'v0.4.0;echo injected'), ('release_tag', 'v0.4.0-rc.1'),
                     ('source_sha', 'main'), ('target', 'everything'), ('publish', 'false'),
                     ('prepared_run_id', '../123'), ('vscode_sha256', 'bad'),
                     ('readiness_url', 'https://evil.example/ok'), ('confirm', 'yes')]
        for key, value in mutations:
            with self.subTest(key=key, value=value):
                config = self.config(True); config[key] = value
                with self.assertRaises(ValueError): self.validate(config)

    def test_context_and_pat_expiry_fail_closed(self):
        for fields in ({'repository': 'other/repo'}, {'event': 'pull_request'}, {'ref': 'refs/heads/feature'},
                       {'today': date(2026, 12, 1)}):
            args = dict(repository=REPO, event='workflow_dispatch', ref='refs/heads/main', today=date(2026, 10, 18))
            args.update(fields)
            with self.assertRaises(ValueError): release.validate_inputs(self.config(True), **args)
        self.assertFalse(release.validate_inputs(self.config(), repository=REPO, event='workflow_dispatch',
                         ref='refs/heads/main', today=date(2027, 1, 1))['publish'])

    def test_unselected_target_does_not_require_its_hash(self):
        c = self.config(True); c.update(target='vscode', visualstudio_sha256='', confirm='PUBLISH v0.4.1 vscode')
        self.assertEqual(self.validate(c)['target'], 'vscode')

    def test_source_tag_ancestry_versions_identity_and_engine(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            vscode = root / 'src/DataGuard.VSCode'
            visualstudio = root / 'src/DataGuard.VisualStudio'
            vscode.mkdir(parents=True)
            visualstudio.mkdir(parents=True)
            package = {'version': '0.4.1', 'publisher': 'thanhnt-sm', 'name': 'dataguard-vscode',
                       'engines': {'vscode': '^1.91.0'}}
            lock = {'version': '0.4.1', 'packages': {'': {'version': '0.4.1'},
                    'node_modules/vscode-languageclient': {'engines': {'vscode': '^1.91.0'}}}}
            (vscode / 'package.json').write_text(json.dumps(package))
            (vscode / 'package-lock.json').write_text(json.dumps(lock))
            (visualstudio / 'source.extension.vsixmanifest').write_text(
                '<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">'
                '<Metadata><Identity Version="0.4.1" Publisher="thanhnt-sm" Id="DataGuard.VisualStudio" />'
                '</Metadata></PackageManifest>')
            (visualstudio / 'ExtensionVersion.cs').write_text('public const string Fallback = "0.4.1";')
            (visualstudio / 'vs-publish.json').write_text(json.dumps({
                'publisher': 'thanhnt-sm', 'identity': {'internalName': 'DataGuard.VisualStudio'}, 'private': False}))
            config = self.validate(self.config(True))
            with patch.object(release, 'git', side_effect=[SHA, SHA, '']) as git:
                release.verify_source(root, config, SHA)
                self.assertEqual(git.call_args.args, (root, 'merge-base', '--is-ancestor', SHA, SHA))
            preview = self.validate(dict(self.config(), release_tag=''))
            lower_engine = dict(package, engines={'vscode': '^1.85.0'})
            (vscode / 'package.json').write_text(json.dumps(lower_engine))
            with patch.object(release, 'git', side_effect=[SHA, '']):
                warnings = release.verify_source(root, preview, SHA)
            self.assertEqual(preview['version'], '0.4.1')
            self.assertEqual(warnings, ['VS Code engine is below dependency requirement'])
            (vscode / 'package.json').write_text(json.dumps(package))
            for responses in (['d' * 40], [SHA, 'd' * 40], [SHA, SHA, ValueError('not ancestor')]):
                with patch.object(release, 'git', side_effect=responses), self.assertRaises(ValueError):
                    release.verify_source(root, config, SHA)
            for field, value in [('version', '0.3.0'), ('publisher', 'impostor'),
                                 ('engines', {'vscode': '^1.85.0'}), ('engines', {'vscode': '*'})]:
                changed = dict(package); changed[field] = value
                (vscode / 'package.json').write_text(json.dumps(changed))
                with self.subTest(field=field, value=value), patch.object(release, 'git', side_effect=[SHA, SHA, '']), self.assertRaises(ValueError):
                    release.verify_source(root, config, SHA)

    def test_prepared_run_identity(self):
        run = {'id': 123, 'run_attempt': 1, 'event': 'workflow_dispatch', 'head_branch': 'main',
               'head_sha': SHA, 'path': release.WORKFLOW, 'status': 'completed', 'conclusion': 'success',
               'repository': {'full_name': REPO}}
        release.validate_run(run, '123', SHA)
        for key, value in [('id', 124), ('run_attempt', 2), ('event', 'pull_request'), ('head_branch', 'feature'),
                           ('head_sha', 'd'*40), ('path', '.github/workflows/other.yml'), ('conclusion', 'failure')]:
            changed = dict(run); changed[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError): release.validate_run(changed, '123', SHA)

    def test_existing_release_environment_accepts_solo_and_honors_native_rules(self):
        for rules in ([], [{'type': 'required_reviewers', 'prevent_self_review': True}]):
            environment = {'name': 'release', 'protection_rules': rules}
            release.validate_environment(environment)
            self.assertEqual(environment['protection_rules'], rules)
        with self.assertRaises(ValueError): release.validate_environment({'name': 'wrong'})

    def test_build_only_accepts_untagged_source_but_publication_does_not(self):
        config = self.config(); config['release_tag'] = ''
        self.assertIsNone(self.validate(config)['version'])
        config['publish'] = True
        with self.assertRaises(ValueError): self.validate(config)
        config = self.config(); config['release_tag'] = 'v0.3.0'
        self.assertEqual(self.validate(config)['version'], '0.3.0')
        config['publish'] = True
        with self.assertRaises(ValueError): self.validate(config)

    def test_artifact_requires_exact_run_id_and_digest(self):
        item = {'id': 456, 'name': 'marketplace-release-vscode', 'expired': False,
                'digest': 'sha256:'+'a'*64, 'workflow_run': {'id': 123, 'head_sha': SHA}}
        self.assertEqual(release.select_artifact({'total_count': 1, 'artifacts': [item]}, 'vscode', '123', SHA), 456)
        for key, value in [('expired', True), ('digest', ''), ('workflow_run', {'id': 124, 'head_sha': SHA})]:
            changed = dict(item); changed[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                release.select_artifact({'total_count': 1, 'artifacts': [changed]}, 'vscode', '123', SHA)
        with self.assertRaises(ValueError):
            release.select_artifact({'total_count': 2, 'artifacts': [item, item]}, 'vscode', '123', SHA)

    def package(self, directory, product='vscode', publisher='thanhnt-sm', version='0.4.1'):
        path = Path(directory) / 'package.vsix'
        with zipfile.ZipFile(path, 'w') as z:
            if product == 'vscode':
                z.writestr('extension/package.json', json.dumps({'name':'dataguard-vscode','publisher':publisher,'version':version}))
                for f in ('out/extension.js', 'LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'ADDITIONAL-PERMISSIONS.md',
                          'server/DataGuard.LanguageServer.dll', 'server/DataGuard.LanguageServer.deps.json',
                          'server/DataGuard.LanguageServer.runtimeconfig.json'):
                    z.writestr('extension/'+f, 'synthetic')
                import hashlib
                z.writestr('extension/server/manifest.json',json.dumps({'schemaVersion':1,'artifact':'DataGuard.LanguageServer.dll',
                              'sha256':hashlib.sha256(b'synthetic').hexdigest()}))
            else:
                z.writestr('extension.vsixmanifest', f'<PackageManifest xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011"><Metadata><Identity Id="DataGuard.VisualStudio" Publisher="{publisher}" Version="{version}" /></Metadata></PackageManifest>')
                for f in release.VS_ENTRIES: z.writestr(f, 'synthetic')
        return path

    def test_package_identity_and_required_payload(self):
        with tempfile.TemporaryDirectory() as d:
            for product in ('vscode', 'visualstudio'):
                path=self.package(d,product)
                release.inspect_package(path,product,'0.4.1')
                path=self.package(d,product,publisher='impostor')
                with self.assertRaises(ValueError): release.inspect_package(path,product,'0.4.1')
                path=self.package(d,product,version='0.3.0')
                with self.assertRaises(ValueError): release.inspect_package(path,product,'0.4.1')
            path=self.package(d)
            with zipfile.ZipFile(path,'a') as z: z.writestr('../injected', 'no')
            with self.assertRaises(ValueError): release.inspect_package(path,'vscode','0.4.1')

    def test_vsce_packaged_license_txt_is_required(self):
        with tempfile.TemporaryDirectory() as directory:
            path = self.package(directory)
            release.inspect_package(path, 'vscode', '0.4.1')
            with zipfile.ZipFile(path) as archive:
                entries = {name: archive.read(name) for name in archive.namelist()
                           if name != 'extension/LICENSE.txt'}
            with zipfile.ZipFile(path, 'w') as archive:
                for name, value in entries.items(): archive.writestr(name, value)
            with self.assertRaisesRegex(ValueError, 'extension/LICENSE.txt'):
                release.inspect_package(path, 'vscode', '0.4.1')

    def test_archive_aliases_and_duplicate_case_are_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            for alias in ('extension/./package.json', 'EXTENSION/PACKAGE.JSON', 'extension//package.json'):
                path = self.package(d)
                with zipfile.ZipFile(path, 'a') as z: z.writestr(alias, '{}')
                with self.subTest(alias=alias), self.assertRaises(ValueError):
                    release.inspect_package(path, 'vscode', '0.4.1')

    def test_visual_studio_listing_metadata_is_bound_to_manifest(self):
        with tempfile.TemporaryDirectory() as d:
            path = self.package(d, 'visualstudio')
            metadata = {'publisher':'thanhnt-sm','identity':{'internalName':'DataGuard.VisualStudio'},
                        'private':False,'overview':'overview.md'}
            (Path(d)/'vs-publish.json').write_text(json.dumps(metadata))
            (Path(d)/'overview.md').write_text('Approved listing')
            config=self.validate(self.config())
            manifest=release.record_package(path,'visualstudio',config,SHA,'123')
            release.verify_package(path,manifest,'visualstudio',config,SHA,'123',manifest['sha256'])
            (Path(d)/'overview.md').write_text('Substituted listing')
            with self.assertRaises(ValueError):
                release.verify_package(path,manifest,'visualstudio',config,SHA,'123',manifest['sha256'])
            metadata['publisher']='impostor'
            (Path(d)/'vs-publish.json').write_text(json.dumps(metadata))
            with self.assertRaises(ValueError): release.record_package(path,'visualstudio',config,SHA,'123')

    def test_untagged_preview_cannot_be_promoted_by_publication_inputs(self):
        with tempfile.TemporaryDirectory() as directory:
            config = self.validate(self.config())
            config['release_tag'] = ''
            path = self.package(directory)
            manifest = release.record_package(path, 'vscode', config, SHA, '123')
            published = self.validate(self.config(True))
            with self.assertRaises(ValueError):
                release.verify_package(path, manifest, 'vscode', published, SHA, '123', manifest['sha256'])

    def test_swapped_bytes_or_manifest_cannot_pass_human_hash(self):
        with tempfile.TemporaryDirectory() as d:
            c=self.validate(self.config()); path=self.package(d)
            manifest=release.record_package(path,'vscode',c,SHA,'123')
            release.verify_package(path,manifest,'vscode',c,SHA,'123',manifest['sha256'])
            with self.assertRaises(ValueError): release.verify_package(path,manifest,'vscode',c,SHA,'123','0'*64)
            for key,value in [('source_sha','d'*40),('workflow_sha','d'*40),('prepared_run_id','999')]:
                changed=dict(manifest);changed[key]=value
                with self.subTest(key=key),self.assertRaises(ValueError):
                    release.verify_package(path,changed,'vscode',c,SHA,'123',manifest['sha256'])


class WorkflowContract(unittest.TestCase):
    def test_sbom_describes_release_artifacts_and_actual_supplier(self):
        doc = yaml.safe_load((ROOT / release.WORKFLOW).read_text())
        for name in ('prepare-vscode', 'prepare-visualstudio'):
            steps = doc['jobs'][name]['steps']
            command = next(step['run'] for step in steps if 'sbom-tool' in step.get('run', '') and ' generate ' in step['run'])
            self.assertRegex(command, r'-ps [\'"]thanhnt-sm[\'"]')
            self.assertRegex(command, r'-b [\'"]\$(?:env:)?RUNNER_TEMP/release[\'"]')

    def test_manual_default_and_secret_isolation(self):
        doc=yaml.safe_load((ROOT/release.WORKFLOW).read_text())
        triggers=doc.get('on',doc.get(True))
        self.assertEqual({'workflow_dispatch'},set(triggers))
        self.assertIs(triggers['workflow_dispatch']['inputs']['publish']['default'],False)
        self.assertEqual({'contents':'read'},doc['permissions'])
        jobs=doc['jobs']
        for name in ('publish-vscode','publish-visualstudio'):
            job=jobs[name]
            self.assertIn('inputs.publish',job['if'])
            self.assertIn("refs/heads/main",job['if'])
            self.assertEqual('release',job['environment'])
            self.assertIn('test',job['needs'])
            self.assertIn('windows-test',job['needs'])
            self.assertNotIn('continue-on-error',job)
            secret_steps=[s for s in job['steps'] if 'secrets.' in json.dumps(s)]
            self.assertEqual(1,len(secret_steps))
            self.assertEqual(secret_steps[0],job['steps'][-1])
            download=next(s for s in job['steps'] if 'actions/download-artifact@' in s.get('uses',''))
            self.assertIn('artifact-ids',download['with'])
            self.assertIn('run-id',download['with'])
            self.assertIs(download['with']['merge-multiple'],True)
        for name,job in jobs.items():
            if not name.startswith('publish-'): self.assertNotIn('secrets.',json.dumps(job))
        text=(ROOT/release.WORKFLOW).read_text()
        self.assertIn('--ignore-scripts',text)
        self.assertIn('--signer-digest',text)
        self.assertNotIn('pull_request_target',text)
        for job in jobs.values():
            for step in job.get('steps',[]):
                if 'uses' in step: self.assertRegex(step['uses'],r'@[a-f0-9]{40}$')


if __name__=='__main__': unittest.main()
