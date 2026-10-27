"""Synthetic readiness probes: no network or credential access."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location('scorecard_readiness', Path(__file__).parents[1] / 'scorecard_readiness.py')
readiness = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(readiness)

REPO = 'example/project'
TIP = 'a' * 40
HEAD = 'b' * 40
OTHER = 'c' * 40


def pr(number=1, head=HEAD):
    return {'number': number, 'merged_at': '2026-10-16T00:00:00Z',
            'base': {'ref': 'main', 'repo': {'full_name': REPO}},
            'head': {'sha': head, 'repo': {'full_name': REPO}}}


def suite(suite_id=1, status='completed', conclusion='success', head=HEAD, app='github-code-scanning'):
    return {'id': suite_id, 'head_sha': head, 'status': status, 'conclusion': conclusion,
            'app': {'slug': app}}


class Clock:
    def __init__(self):
        self.now = 0

    def __call__(self):
        return self.now

    def sleep(self, seconds):
        self.now += seconds


class FakeApi:
    def __init__(self, attempts=None, commits=None, associations=None):
        self.attempts = attempts or [[suite()]]
        self.commits = commits or [TIP]
        self.associations = associations if associations is not None else {sha: [pr()] for sha in self.commits}
        self.calls = []
        self.polls = {}

    def get(self, path, params, timeout):
        self.calls.append((path, params, timeout))
        if path.endswith('/branches/main'):
            return {'commit': {'sha': TIP}}
        if path.endswith('/commits'):
            return [{'sha': sha} for sha in self.commits]
        if path.endswith('/pulls'):
            sha = path.split('/')[-2]
            page = params['page']
            return self.associations[sha][(page - 1) * 100:page * 100]
        if path.endswith('/check-suites'):
            sha = path.split('/')[-2]
            count = self.polls.get(sha, 0)
            if params['page'] == 1:
                self.polls[sha] = count + 1
            values = self.attempts[min(count, len(self.attempts) - 1)]
            values = [value for value in values if value.get('head_sha') == sha] if sha != HEAD else values
            return {'total_count': len(values), 'check_suites': values[(params['page'] - 1) * 100:params['page'] * 100]}
        raise AssertionError(path)


class ReadinessTests(unittest.TestCase):
    def run_probe(self, api=None, **kwargs):
        self.clock = Clock()
        self.api = api or FakeApi()
        return readiness.Readiness(REPO, 'main', self.api, clock=self.clock,
                                   sleep=self.clock.sleep, timeout=30, grace=10,
                                   poll_interval=5, **kwargs).run()

    def test_success_anchors_sample_to_observed_tip(self):
        report = self.run_probe()
        self.assertEqual(report['state'], 'ready')
        self.assertEqual(report['observed_sha'], TIP)
        self.assertEqual(report['eligible_commit_count'], 1)
        self.assertEqual(self.api.calls[1][1]['sha'], TIP)
        self.assertEqual(self.api.calls[1][1]['per_page'], 30)

    def test_pending_then_success(self):
        report = self.run_probe(FakeApi([[suite(status='in_progress', conclusion=None)], [suite()]]))
        self.assertEqual(report['state'], 'ready')
        self.assertEqual(self.clock.now, 5)

    def test_pr66_recurrence_26_of_28_heads_waits_for_both_exact_heads(self):
        # Synthetic replay of the verified recurrence: PR66 merged 11:23:51,
        # Scorecard sampled at 11:24:06 (26/28, SAST 9), and analysis was still
        # running at 11:25:24. t=0 below is the sample; t=78 is that observation.
        # Completion at t=90/108 is synthetic, not claimed hosted evidence.
        for finish_last_head in (True, False):
            with self.subTest(finish_last_head=finish_last_head):
                clock = Clock()
                commits = [TIP] + [f'{number:040x}' for number in range(1, 30)]
                heads = [f'{number:040x}' for number in range(100, 128)]
                associations = {commit: [pr(index + 1, heads[index])] if index < 28 else []
                                for index, commit in enumerate(commits)}
                api = FakeApi(commits=commits, associations=associations)
                original_get = api.get

                def get(path, params, timeout):
                    if not path.endswith('/check-suites'):
                        return original_get(path, params, timeout)
                    head = path.split('/')[-2]
                    index = heads.index(head)
                    ready = index < 26 or (index == 26 and clock.now >= 90) or (
                        index == 27 and finish_last_head and clock.now >= 108)
                    values = [suite(index + 1, 'completed' if ready else 'in_progress',
                                    'success' if ready else None, head=head),
                              suite(1001, head=TIP, app='github-actions'),
                              suite(1002, head=TIP),
                              suite(1003, head=head, app='github-actions')]
                    return {'total_count': len(values), 'check_suites': values}

                api.get = get
                observations = []
                probe = readiness.Readiness(REPO, 'main', api, clock=clock, timeout=150,
                                            grace=10, poll_interval=6)

                def observe_and_sleep(seconds):
                    states = [head['state'] for head in probe.report['heads']]
                    observations.append((clock.now, states.count('ready'), states.count('pending')))
                    clock.sleep(seconds)

                probe.sleep = observe_and_sleep
                report = probe.run()
                self.assertEqual(report['eligible_commit_count'], 28)
                self.assertEqual(len(report['sample_commits']), 30)
                self.assertEqual(len(report['heads']), 28)
                self.assertIn((0, 26, 2), observations)
                self.assertIn((78, 26, 2), observations)
                self.assertIn((90, 27, 1), observations)
                self.assertTrue(all(len(head['suites']) == 1 for head in report['heads']))
                if finish_last_head:
                    self.assertEqual(report['state'], 'ready')
                    self.assertEqual(clock.now, 108)
                    self.assertTrue(all(head['state'] == 'ready' for head in report['heads']))
                else:
                    self.assertEqual(report['state'], 'timeout')
                    self.assertEqual(clock.now, 150)
                    self.assertEqual(report['heads'][-1]['state'], 'pending')

    def test_pending_times_out(self):
        report = self.run_probe(FakeApi([[suite(status='queued', conclusion=None)]]))
        self.assertEqual(report['state'], 'timeout')
        self.assertEqual(report['heads'][0]['state'], 'pending')
        self.assertEqual(self.clock.now, 30)

    def test_missing_waits_grace_then_fails(self):
        report = self.run_probe(FakeApi([[]]))
        self.assertEqual(report['state'], 'missing')
        self.assertEqual(self.clock.now, 10)

    def test_default_grace_allows_suite_registration_after_60_seconds(self):
        clock = Clock()
        api = FakeApi()
        original = api.get

        def get(path, params, timeout):
            if not path.endswith('/check-suites'):
                return original(path, params, timeout)
            values = [] if clock.now < 90 else [suite(
                status='in_progress' if clock.now < 120 else 'completed',
                conclusion=None if clock.now < 120 else 'success')]
            return {'total_count': len(values), 'check_suites': values}

        api.get = get
        probe = readiness.Readiness(REPO, 'main', api, clock=clock)
        observations = []

        def observe_and_sleep(seconds):
            observations.append((clock.now, probe.report['heads'][0]['state']))
            clock.sleep(seconds)

        probe.sleep = observe_and_sleep
        report = probe.run()
        self.assertEqual(report['state'], 'ready')
        self.assertIn((60, 'waiting_missing'), observations)
        self.assertIn((90, 'pending'), observations)
        self.assertEqual(clock.now, 120)

    def test_default_budget_bounds_30_never_registered_heads_without_request_cap(self):
        clock = Clock()
        commits = [TIP] + [f'{number:040x}' for number in range(1, 30)]
        heads = [f'{number:040x}' for number in range(100, 130)]
        associations = {commit: [pr(index + 1, heads[index])]
                        for index, commit in enumerate(commits)}
        api = FakeApi([[]], commits=commits, associations=associations)
        report = readiness.Readiness(REPO, 'main', api, clock=clock, sleep=clock.sleep).run()
        self.assertEqual(report['state'], 'timeout')
        self.assertEqual(report['eligible_commit_count'], 30)
        self.assertEqual(clock.now, 900)
        self.assertEqual(len(api.calls), 932)  # 32 sample requests + 30 heads * 30 rounds.
        self.assertLess(len(api.calls), 1000)
        self.assertTrue(all(head['state'] == 'waiting_missing' for head in report['heads']))

    def test_previous_head_success_cannot_satisfy_new_head(self):
        report = self.run_probe(FakeApi([[suite(head=OTHER)]]))
        self.assertEqual(report['state'], 'missing')

    def test_main_ci_green_is_ignored(self):
        report = self.run_probe(FakeApi([[suite(app='github-actions')]]))
        self.assertEqual(report['state'], 'missing')

    def test_higher_id_pending_blocks_lower_id_success(self):
        report = self.run_probe(FakeApi([[suite(1), suite(2, 'in_progress', None)]]))
        self.assertEqual(report['state'], 'timeout')

    def test_higher_id_failure_blocks_lower_id_success(self):
        report = self.run_probe(FakeApi([[suite(1), suite(2, conclusion='failure')]]))
        self.assertEqual(report['state'], 'failed')

    def test_lower_suite_id_rerequest_pending_blocks_higher_id_success(self):
        report = self.run_probe(FakeApi([[suite(1, 'in_progress', None), suite(2)]]))
        self.assertEqual(report['state'], 'timeout')
        self.assertEqual([s['id'] for s in report['heads'][0]['suites']], [1, 2])

    def test_lower_suite_id_failure_blocks_higher_id_success(self):
        report = self.run_probe(FakeApi([[suite(1, conclusion='failure'), suite(2)]]))
        self.assertEqual(report['state'], 'failed')
        self.assertEqual([s['id'] for s in report['heads'][0]['suites']], [1, 2])

    def test_every_non_success_conclusion_fails(self):
        for conclusion in ['failure', 'cancelled', 'skipped', 'neutral', 'timed_out', 'action_required', 'stale']:
            with self.subTest(conclusion=conclusion):
                report = self.run_probe(FakeApi([[suite(conclusion=conclusion)]]))
                self.assertEqual(report['state'], 'failed')

    def test_duplicate_pr_association_is_not_ambiguous(self):
        report = self.run_probe(FakeApi(associations={TIP: [pr(), pr()]}))
        self.assertEqual(report['state'], 'ready')

    def test_multiple_merged_pr_associations_fail_closed(self):
        report = self.run_probe(FakeApi(associations={TIP: [pr(), pr(2)]}))
        self.assertEqual(report['state'], 'ambiguous')

    def test_direct_commit_is_explicitly_not_applicable(self):
        report = self.run_probe(FakeApi(associations={TIP: []}))
        self.assertEqual(report['state'], 'not_applicable')
        self.assertEqual(report['sample_commits'][0]['state'], 'direct_commit')

    def test_deduplicates_heads_preserves_commit_multiplicity(self):
        report = self.run_probe(FakeApi(commits=[TIP, OTHER]))
        self.assertEqual(report['state'], 'ready')
        self.assertEqual(report['eligible_commit_count'], 2)
        self.assertEqual(len(report['sample_commits']), 2)
        self.assertEqual(len(report['heads']), 1)
        self.assertEqual(self.api.polls[HEAD], 1)

    def test_foreign_base_or_unmerged_pr_is_not_eligible(self):
        for changed in [dict(pr(), merged_at=None), dict(pr(), base={'ref': 'main', 'repo': {'full_name': 'foreign/project'}})]:
            report = self.run_probe(FakeApi(associations={TIP: [changed]}))
            self.assertEqual(report['state'], 'not_applicable')

    def test_merged_fork_pr_head_is_included_using_target_repository_endpoint(self):
        merged = pr()
        merged['head']['repo']['full_name'] = 'fork/project'
        report = self.run_probe(FakeApi(associations={TIP: [merged]}))
        self.assertEqual(report['state'], 'ready')
        self.assertEqual(report['eligible_commit_count'], 1)
        self.assertTrue(all(call[0].startswith('/repos/' + REPO + '/') for call in self.api.calls))

    def test_merged_other_base_branch_association_is_included(self):
        merged = pr()
        merged['base']['ref'] = 'release'
        report = self.run_probe(FakeApi(associations={TIP: [merged]}))
        self.assertEqual(report['state'], 'ready')
        self.assertEqual(report['eligible_commit_count'], 1)

    def test_pagination_finds_later_association(self):
        unmerged = dict(pr(), merged_at=None)
        report = self.run_probe(FakeApi(associations={TIP: [unmerged] * 100 + [pr()]}))
        self.assertEqual(report['state'], 'ready')
        self.assertEqual([c[1]['page'] for c in self.api.calls if c[0].endswith('/pulls')], [1, 2])

    def test_page_cap_fails_explicitly(self):
        report = self.run_probe(FakeApi(associations={TIP: [pr()] * 500}))
        self.assertEqual(report['state'], 'page_limit')

    def test_api_and_rate_limit_errors_are_controlled(self):
        for state in ['api_error', 'rate_limited']:
            api = FakeApi()
            api.get = lambda *args: (_ for _ in ()).throw(readiness.ProbeError(state))
            report = self.run_probe(api)
            self.assertEqual(report['state'], state)

    def test_invalid_suite_id_rejected(self):
        for value in [True, '123', -1]:
            report = self.run_probe(FakeApi([[suite(value)]]))
            self.assertEqual(report['state'], 'invalid_response')

    def test_instances_do_not_share_state(self):
        self.assertEqual(self.run_probe()['state'], 'ready')
        self.assertEqual(self.run_probe(FakeApi([[]]))['state'], 'missing')

    def test_pending_head_is_waited_despite_other_failed_head(self):
        api = FakeApi(commits=[TIP, OTHER], associations={TIP: [pr()], OTHER: [pr(2, OTHER)]})
        original = api.get
        def get(path, params, timeout):
            if path.endswith(OTHER + '/check-suites'):
                return {'total_count': 1, 'check_suites': [suite(head=OTHER, status='queued', conclusion=None)]}
            if path.endswith(HEAD + '/check-suites'):
                return {'total_count': 1, 'check_suites': [suite(conclusion='failure')]}
            return original(path, params, timeout)
        api.get = get
        report = self.run_probe(api)
        self.assertEqual(report['state'], 'timeout')
        self.assertEqual({h['state'] for h in report['heads']}, {'failed', 'pending'})
        self.assertEqual(self.clock.now, 30)

    def test_cli_writes_failure_report_and_valid_sha_without_remote_text(self):
        with tempfile.TemporaryDirectory() as directory:
            report_path = Path(directory) / 'report.json'
            github_output = Path(directory) / 'output'
            with patch.object(readiness, 'GitHub', return_value=FakeApi([[]])), patch.dict('os.environ', {'GH_TOKEN': 'synthetic-secret', 'GITHUB_OUTPUT': str(github_output)}):
                code = readiness.main(['--repo', REPO, '--branch', 'main', '--timeout', '1', '--grace', '0', '--output', str(report_path)])
            self.assertNotEqual(code, 0)
            self.assertEqual(json.loads(report_path.read_text())['state'], 'missing')
            self.assertEqual(github_output.read_text(), 'sample_sha=' + TIP + '\n')
            self.assertNotIn('synthetic-secret', report_path.read_text())

    def test_invalid_branch_tip_has_no_output_sha(self):
        api = FakeApi()
        api.get = lambda *args: {'commit': {'sha': 'secret\ninjection'}}
        report = self.run_probe(api)
        self.assertEqual(report['state'], 'invalid_response')
        self.assertIsNone(report['observed_sha'])
        self.assertNotIn('injection', json.dumps(report))

    def test_request_budget_is_bounded(self):
        report = self.run_probe(max_requests=2)
        self.assertEqual(report['state'], 'request_limit')
        self.assertEqual(len(self.api.calls), 2)

    def test_total_deadline_applies_during_initial_sample(self):
        clock = Clock()
        api = FakeApi()
        original = api.get
        def delayed(path, params, timeout):
            clock.sleep(31)
            return original(path, params, timeout)
        api.get = delayed
        report = readiness.Readiness(REPO, 'main', api, clock=clock, sleep=clock.sleep, timeout=30, grace=10).run()
        self.assertEqual(report['state'], 'timeout')
        self.assertEqual(len(api.calls), 1)
        self.assertLessEqual(api.calls[0][2], 15)

    def test_arbitrary_transport_exception_is_redacted(self):
        api = FakeApi()
        api.get = lambda *args: (_ for _ in ()).throw(ValueError('synthetic-token https://evil.example/body'))
        report = self.run_probe(api)
        self.assertEqual(report['state'], 'api_error')
        self.assertNotIn('synthetic-token', json.dumps(report))
        self.assertNotIn('evil.example', json.dumps(report))

    def test_suite_pagination_uses_local_pages_and_preserves_pending(self):
        values = [suite(i) for i in range(1, 101)] + [suite(101, conclusion='failure')]
        report = self.run_probe(FakeApi([values]))
        self.assertEqual(report['state'], 'failed')
        self.assertEqual(report['heads'][0]['suites'][-1]['id'], 101)
        self.assertEqual([call[1]['page'] for call in self.api.calls if call[0].endswith('/check-suites')], [1, 2])

    def test_suite_page_cap_is_not_silently_truncated(self):
        report = self.run_probe(FakeApi([[suite(i) for i in range(1, 502)]]))
        self.assertEqual(report['state'], 'page_limit')

    def test_duplicate_suite_page_is_rejected(self):
        api = FakeApi([[suite(i) for i in range(1, 101)]])
        original = api.get
        def repeated(path, params, timeout):
            result = original(path, params, timeout)
            if path.endswith('/check-suites'):
                return {'total_count': 200, 'check_suites': [suite(i) for i in range(1, 101)]}
            return result
        api.get = repeated
        self.assertEqual(self.run_probe(api)['state'], 'invalid_response')

    def test_inconsistent_suite_total_count_fails_closed(self):
        api = FakeApi()
        original = api.get
        def inconsistent(path, params, timeout):
            result = original(path, params, timeout)
            if path.endswith('/check-suites'):
                result['total_count'] = 0
            return result
        api.get = inconsistent
        self.assertEqual(self.run_probe(api)['state'], 'invalid_response')

    def test_malformed_association_is_explicit_failure(self):
        bad = pr()
        bad['base']['repo']['full_name'] = ['secret-value']
        report = self.run_probe(FakeApi(associations={TIP: [bad]}))
        self.assertEqual(report['state'], 'invalid_response')
        self.assertNotIn('secret-value', json.dumps(report))

    def test_both_trusted_apps_must_be_successful(self):
        report = self.run_probe(FakeApi([[suite(), suite(2, app='github-advanced-security', conclusion='neutral')]]))
        self.assertEqual(report['state'], 'failed')

    def test_direct_commits_do_not_hide_eligible_commits(self):
        report = self.run_probe(FakeApi(commits=[TIP, OTHER], associations={TIP: [], OTHER: [pr()]}))
        self.assertEqual(report['state'], 'ready')
        self.assertEqual(report['eligible_commit_count'], 1)
        self.assertEqual(report['sample_commits'][0]['state'], 'direct_commit')

    def test_conflicting_same_pr_heads_are_ambiguous(self):
        report = self.run_probe(FakeApi(associations={TIP: [pr(), pr(head=OTHER)]}))
        self.assertEqual(report['state'], 'ambiguous')


class TransportTests(unittest.TestCase):
    class Response:
        def __init__(self, status=200, body=b'{}', headers=None):
            self.status = status
            self.body = body
            self.headers = headers or {}

        def getheader(self, key):
            return self.headers.get(key)

        def read1(self, limit):
            result, self.body = self.body[:limit], self.body[limit:]
            return result

    class Connection:
        def __init__(self, response):
            self.response = response
            self.calls = []
            self.closed = False
            self.sock = None

        def request(self, *args, **kwargs):
            self.calls.append((args, kwargs))

        def getresponse(self):
            return self.response

        def close(self):
            self.closed = True

    def client(self, response):
        self.connection = self.Connection(response)
        self.destinations = []
        def factory(host, timeout):
            self.destinations.append((host, timeout))
            return self.connection
        return readiness.GitHub('synthetic-token', connection_factory=factory)

    def test_only_fixed_origin_get_with_clipped_timeout(self):
        client = self.client(self.Response())
        self.assertEqual(client.get('/repos/example/project/commits', {'page': 2}, 60), {})
        self.assertEqual(self.destinations, [('api.github.com', 15)])
        self.assertEqual(self.connection.calls[0][0], ('GET', '/repos/example/project/commits?page=2'))
        self.assertTrue(self.connection.closed)

    def test_redirect_is_rejected_and_not_followed(self):
        client = self.client(self.Response(302, headers={'Location': 'https://evil.example/secret'}))
        with self.assertRaises(readiness.ProbeError) as raised:
            client.get('/repos/example/project/commits', {}, 10)
        self.assertEqual(raised.exception.state, 'redirect_rejected')
        self.assertEqual(len(self.destinations), 1)
        self.assertTrue(self.connection.closed)

    def test_response_link_is_never_followed(self):
        client = self.client(self.Response(headers={'Link': '<https://evil.example>; rel="next"'}))
        client.get('/repos/example/project/commits', {}, 10)
        self.assertEqual(len(self.destinations), 1)

    def test_api_statuses_have_controlled_states_and_no_body(self):
        for status, headers, expected in [(403, {}, 'api_error'), (401, {}, 'api_error'),
                (429, {}, 'rate_limited'), (403, {'Retry-After': '60'}, 'rate_limited'),
                (403, {'X-RateLimit-Remaining': '0'}, 'rate_limited'), (500, {}, 'api_error')]:
            with self.subTest(status=status, headers=headers):
                client = self.client(self.Response(status, b'synthetic-token', headers))
                with self.assertRaises(readiness.ProbeError) as raised:
                    client.get('/repos/example/project/commits', {}, 10)
                self.assertEqual(str(raised.exception), expected)

    def test_body_size_is_bounded(self):
        client = self.client(self.Response(body=b'x' * (2 * 1024 * 1024 + 1)))
        with self.assertRaises(readiness.ProbeError) as raised:
            client.get('/repos/example/project/commits', {}, 10)
        self.assertEqual(raised.exception.state, 'response_limit')

    def test_arbitrary_urls_and_path_breakouts_rejected_before_connection(self):
        for path in ['https://evil.example', '//evil.example', '/repos/example/../secret']:
            client = self.client(self.Response())
            with self.assertRaises(readiness.ProbeError):
                client.get(path, {}, 10)
            self.assertEqual(self.destinations, [])

    def test_slow_body_hits_request_deadline(self):
        clock = Clock()
        response = self.Response(body=b'{}')
        original = response.read1
        def slow(limit):
            clock.sleep(11)
            return original(limit)
        response.read1 = slow
        client = self.client(response)
        client.clock = clock
        with self.assertRaises(readiness.ProbeError) as raised:
            client.get('/repos/example/project/commits', {}, 10)
        self.assertEqual(raised.exception.state, 'request_timeout')


if __name__ == '__main__':
    unittest.main()
