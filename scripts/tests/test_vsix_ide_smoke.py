from pathlib import Path
import tempfile
import unittest

from scripts import vsix_ide_smoke as smoke


class SmokeContractTests(unittest.TestCase):
    def test_installer_requires_exact_target_and_profile_switches(self):
        for help_text in ['', '/quiet /instanceIds:<ids>', '/quiet /rootSuffix:<suffix>', '/quiet /instanceIds:<ids> /rootSuffix:<suffix>', '/quiet /instanceIds:<ids> rootSuffix mentioned in prose']:
            with self.subTest(help_text=help_text), self.assertRaises(smoke.Blocked):
                smoke.install_arguments(help_text, 'abc123', 'DGSmoke' + 'a' * 32, Path('exact.vsix'), 'DGSmokeInstaller-' + 'a' * 32 + '.log')
        args = smoke.install_arguments('/quiet /instanceIds:<ids> /rootSuffix:<suffix> /logFile:<name>', 'abc123', 'DGSmoke' + 'a' * 32, Path('exact.vsix'), 'DGSmokeInstaller-' + 'a' * 32 + '.log')
        self.assertEqual(args, ['/quiet', '/instanceIds:abc123', '/rootSuffix:DGSmoke' + 'a' * 32, '/logFile:DGSmokeInstaller-' + 'a' * 32 + '.log', 'exact.vsix'])

    def test_default_or_injected_profiles_rejected(self):
        for suffix in ['', 'Exp', 'DGSmoke../x', 'DGSmoke' + 'a' * 31, 'DGSmoke' + 'a' * 32 + '/admin']:
            with self.subTest(suffix=suffix), self.assertRaises(smoke.Blocked):
                smoke.install_arguments('/quiet /instanceIds:<ids> /rootSuffix:<suffix> /logFile:<name>', 'abc123', suffix, Path('x'), 'DGSmokeInstaller-' + 'a' * 32 + '.log')

    def test_host_selection_is_unambiguous_and_complete(self):
        valid = dict(instanceId='abc123', installationPath='C:/VS', installationVersion='17.14.1', isComplete=True, isLaunchable=True)
        self.assertEqual(smoke.select_host([valid])['instanceId'], 'abc123')
        for hosts in [[], [valid, valid], [dict(valid, isLaunchable=False)], [dict(valid, instanceId='../x')]]:
            with self.subTest(hosts=hosts), self.assertRaises(smoke.Blocked):
                smoke.select_host(hosts)

    def test_cleanup_refuses_preexisting_symlink_and_unowned_paths(self):
        with tempfile.TemporaryDirectory() as temp:
            parent = Path(temp)
            owned = parent / ('17.0_abcDGSmoke' + 'a' * 32)
            owned.mkdir()
            with self.assertRaises(smoke.Blocked):
                smoke.clean_profile(owned, parent, owned.name, preexisting=True)
            elsewhere = parent / 'other'
            elsewhere.mkdir()
            with self.assertRaises(smoke.Blocked):
                smoke.clean_profile(elsewhere, parent, owned.name)
            link = parent / 'link'
            from unittest.mock import patch
            with patch.object(Path, 'is_symlink', return_value=True):
                with self.assertRaises(smoke.Blocked):
                    smoke.clean_profile(link, parent, 'link')
            smoke.clean_profile(owned, parent, owned.name)
            self.assertFalse(owned.exists())
            self.assertTrue(elsewhere.exists())

    def test_installed_payload_requires_exact_bytes(self):
        import zipfile
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            vsix = root / 'x.vsix'
            with zipfile.ZipFile(vsix, 'w') as z:
                z.writestr('DataGuard.VisualStudio.dll', b'expected')
            profile = root / 'profile'
            payload = profile / 'Extensions' / 'random'
            payload.mkdir(parents=True)
            dll = payload / 'DataGuard.VisualStudio.dll'
            dll.write_bytes(b'wrong')
            with self.assertRaises(smoke.Blocked):
                smoke.verify_payload(vsix, profile)
            dll.write_bytes(b'expected')
            self.assertEqual(len(smoke.verify_payload(vsix, profile)), 64)

    def test_probe_cannot_pass_with_version_or_property_missing(self):
        for result in [{}, {'optionsRead': True}, {'optionsRead': True, 'hostVersion': '18.0'}, {'optionsRead': False, 'hostVersion': '17.0'}]:
            with self.subTest(result=result), self.assertRaises(smoke.Blocked):
                smoke.verify_probe(result, '17.14')
        smoke.verify_probe({'optionsRead': True, 'hostVersion': '17.0', 'propertyType': 'Boolean'}, '17.14')


class OrchestrationTests(unittest.TestCase):
    def scenario(self, failure=None):
        import json
        import os
        from unittest.mock import patch, MagicMock
        import zipfile
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            install = root / 'VS'
            (install / 'Common7/IDE').mkdir(parents=True)
            for tool in ['VSIXInstaller.exe', 'devenv.exe']:
                (install / 'Common7/IDE' / tool).write_bytes(b'tool')
            vsix = root / 'exact.vsix'
            with zipfile.ZipFile(vsix, 'w') as archive:
                archive.writestr('DataGuard.VisualStudio.dll', b'expected')
            evidence = root / 'evidence.json'
            installer_temp = root / 'actual-installer-temp'
            installer_temp.mkdir()
            unrelated = installer_temp / 'unrelated.log'
            unrelated.write_bytes(b'KEEP')
            env = {'GITHUB_ACTIONS': 'true', 'RUNNER_ENVIRONMENT': 'github-hosted',
                   'GITHUB_REPOSITORY': 'thanhnt-sm/eco_support_net_oracle',
                   'ProgramFiles(x86)': str(root), 'SystemRoot': str(root),
                   'LOCALAPPDATA': str(root / 'local'), 'APPDATA': str(root / 'roaming'),
                   'DG_SMOKE_INSTANCE': 'abc123'}
            calls = []
            process = MagicMock(pid=1234, returncode=1 if failure == 'crash' else 0)
            process.poll.return_value = None if failure == 'cleanup' else 0
            if failure == 'cleanup':
                process.terminate.side_effect = OSError('PRIVATE account/path')
            def fake_run(args, timeout=60, env=None):
                calls.append([str(x) for x in args])
                name = str(args[0])
                if name == 'git':
                    return 'a' * 40
                if name.endswith('vswhere.exe'):
                    return json.dumps([dict(instanceId='different' if failure == 'host' else 'abc123',
                        installationPath=str(install), installationVersion='17.14.1', isComplete=True, isLaunchable=True)])
                if name.endswith('VSIXInstaller.exe') and args[1] == '/?':
                    return '/quiet /instanceIds:<id>' if failure == 'capability' else '/quiet /instanceIds:<id> /rootSuffix:<suffix> /logFile:<name>'
                if name.endswith('VSIXInstaller.exe'):
                    self.assertEqual(env, installer_environment[0])
                    log_name = str(args[4]).split(':', 1)[1]
                    self.assertEqual(Path(log_name).name, log_name)
                    if failure != 'receipt-missing':
                        (installer_temp / log_name).write_bytes(b'\xff' if failure == 'receipt-malformed' else b'PRIVATE SQL C:/account token=secret\n')
                    if failure == 'install':
                        raise smoke.Blocked('process-nonzero-exit-1')
                    suffix = args[3].split(':')[1]
                    profile = root / 'local/Microsoft/VisualStudio' / ('17.0_abc123' + suffix)
                    profile = profile / 'Extensions/owned'
                    profile.mkdir(parents=True)
                    (profile / 'DataGuard.VisualStudio.dll').write_bytes(b'wrong' if failure == 'payload' else b'expected')
                    return ''
                if name.endswith('powershell.exe'):
                    if '-Command' in args:
                        installer_environment.append(env)
                        return str(installer_temp) + '\n'
                    if failure == 'probe':
                        raise smoke.Blocked('process-timeout-possible-modal-consent-or-license')
                    if failure == 'unexpected':
                        raise OSError('PRIVATE account/path')
                    activity = Path(popen.call_args.args[0][-1])
                    activity.write_text('<activity><entry><type>Information</type><guid>{04a7c09c-4f79-439f-8298-952900cdb5ae}</guid><description>End package load [DataGuardPackage]</description></entry></activity>')
                    return json.dumps(dict(optionsRead=True, propertyType='Boolean', hostVersion='17.0'))
                self.fail('unexpected process')
            installer_environment = []
            with patch.dict(os.environ, env, clear=True), patch.object(smoke.sys, 'platform', 'win32'), \
                 patch.object(smoke, 'run', side_effect=fake_run), \
                 patch.object(smoke.subprocess, 'Popen', return_value=process) as popen:
                code = smoke.execute(vsix, evidence, 30)
                record = json.loads(evidence.read_text())
                self.assertNotIn('PRIVATE', evidence.read_text())
                self.assertNotIn('token=secret', evidence.read_text())
                self.assertEqual(unrelated.read_bytes(), b'KEEP')
                self.assertEqual(list(installer_temp.iterdir()), [unrelated])
                return code, record, calls, popen.call_count

    def test_orchestrated_pass_requires_all_phases(self):
        code, record, calls, launches = self.scenario()
        self.assertEqual(code, 0)
        self.assertEqual(record['status'], 'PASS')
        self.assertEqual(launches, 1)
        self.assertIn('package-load-completed-in-activity-log', record['phases'])

    def test_failures_block_before_dependent_mutations(self):
        for failure in ['host', 'capability', 'install', 'payload', 'probe', 'cleanup', 'crash', 'receipt-missing', 'receipt-malformed']:
            with self.subTest(failure=failure):
                code, record, calls, launches = self.scenario(failure)
                self.assertEqual(code, 2)
                self.assertEqual(record['status'], 'BLOCKED')
                self.assertEqual(launches, 1 if failure in ['probe', 'cleanup', 'crash'] else 0)
                if failure in ['host', 'capability']:
                    self.assertFalse(any(c[0].endswith('VSIXInstaller.exe') and c[1] != '/?' for c in calls))

    def test_receipt_metadata_does_not_upgrade_installer_failure(self):
        code, record, _, launches = self.scenario('install')
        self.assertEqual(code, 2)
        self.assertEqual(record['reason'], 'process-nonzero-exit-1')
        self.assertIn('sha256', record['installerReceipt'])
        self.assertEqual(launches, 0)

    def test_exception_text_is_never_exported(self):
        code, record, _, _ = self.scenario('unexpected')
        self.assertEqual(code, 1)
        self.assertEqual(record['reason'], 'unexpected-OSError')

    def test_activity_requires_exact_package_success(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'activity.xml'
            for content in ['', '<activity>', '<activity><entry><type>Information</type><guid>{04a7c09c-4f79-439f-8298-952900cdb5ae}</guid><description>End package load</description><hr>80004005</hr></entry></activity>', '<activity><entry><type>Error</type><guid>{04a7c09c-4f79-439f-8298-952900cdb5ae}</guid><description>End package load</description></entry></activity>', '<activity><entry><type>Information</type><guid>other</guid><description>End package load</description></entry></activity>']:
                path.write_text(content)
                with self.subTest(content=content), self.assertRaises(smoke.Blocked):
                    smoke.verify_activity(path)


if __name__ == '__main__':
    unittest.main()
