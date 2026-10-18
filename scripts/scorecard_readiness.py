#!/usr/bin/env python3
"""Bounded, read-only GitHub readiness for Scorecard's latest-30-commit SAST sample.

Reports are observations, not Scorecard results. Only completed/success from the
trusted code-scanning apps is ready; neutral is intentionally not successful.
"""
import argparse
import http.client
import json
import math
import os
from pathlib import Path
import re
import socket
import time
from urllib.parse import urlencode, quote


class ProbeError(Exception):
    """A controlled failure state; never expose remote exception text or bodies."""

    def __init__(self, state):
        super().__init__(state)
        self.state = state


def valid_sha(value):
    return isinstance(value, str) and re.fullmatch(r'[0-9a-f]{40}', value) is not None


def valid_repo(value):
    return isinstance(value, str) and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_.-]{0,99}/[A-Za-z0-9][A-Za-z0-9_.-]{0,99}', value) is not None


def valid_branch(value):
    return isinstance(value, str) and re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_./-]{0,199}', value) is not None and '..' not in value and '//' not in value and not value.endswith('/')


class GitHub:
    """Fixed TLS origin, GET only, no redirects, bounded response and request time."""

    def __init__(self, token, clock=time.monotonic, connection_factory=http.client.HTTPSConnection):
        self.token = token
        self.clock = clock
        self.connection_factory = connection_factory

    def get(self, path, params, timeout):
        if not re.fullmatch(r'/repos/[A-Za-z0-9_./%~-]+', path) or '..' in path or '//' in path:
            raise ProbeError('invalid_request')
        deadline = self.clock() + min(timeout, 15)
        connection = self.connection_factory('api.github.com', timeout=min(timeout, 15))
        try:
            headers = {'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28',
                       'User-Agent': 'DataGuard-Scorecard-Readiness'}
            if self.token:
                headers['Authorization'] = 'Bearer ' + self.token
            target = path + ('?' + urlencode(params) if params else '')
            connection.request('GET', target, headers=headers)
            sock = connection.sock
            self._remaining(deadline, sock)
            response = connection.getresponse()
            if 300 <= response.status < 400:
                raise ProbeError('redirect_rejected')
            if response.status == 429 or (response.status == 403 and
                    (response.getheader('X-RateLimit-Remaining') == '0' or response.getheader('Retry-After') is not None)):
                raise ProbeError('rate_limited')
            if response.status != 200:
                raise ProbeError('api_error')
            chunks = []
            size = 0
            while True:
                self._remaining(deadline, sock)
                chunk = response.read1(min(65536, 2 * 1024 * 1024 + 1 - size))
                if not chunk:
                    break
                chunks.append(chunk)
                size += len(chunk)
                if size > 2 * 1024 * 1024:
                    raise ProbeError('response_limit')
            self._remaining(deadline, sock)
            return json.loads(b''.join(chunks))
        except (socket.timeout, TimeoutError):
            raise ProbeError('request_timeout') from None
        except (OSError, http.client.HTTPException, ValueError, UnicodeError):
            raise ProbeError('api_error') from None
        finally:
            connection.close()

    def _remaining(self, deadline, sock):
        remaining = deadline - self.clock()
        if remaining <= 0:
            raise ProbeError('request_timeout')
        if sock is not None:
            sock.settimeout(remaining)


class Readiness:
    """One independent observation; mutable state is never shared between runs."""

    def __init__(self, repo, branch, api, *, clock=time.monotonic, sleep=time.sleep,
                 timeout=900, grace=900, poll_interval=30, max_pages=5, max_requests=1000):
        self.repo = repo
        self.branch = branch
        self.api = api
        self.clock = clock
        self.sleep = sleep
        self.timeout = timeout
        self.grace = grace
        self.poll_interval = poll_interval
        self.max_pages = max_pages
        self.max_requests = max_requests
        self.requests = 0
        self.deadline = 0
        self.missing_since = {}
        self.report = {'repo': repo if valid_repo(repo) else None, 'observed_sha': None,
                       'state': 'initializing', 'sample_commits': [], 'eligible_commit_count': 0,
                       'heads': []}

    def run(self):
        self.deadline = self.clock() + self.timeout
        try:
            if not valid_repo(self.repo) or not valid_branch(self.branch):
                raise ProbeError('invalid_request')
            numbers = (self.timeout, self.grace, self.poll_interval)
            if any(not isinstance(n, (float, int)) or not math.isfinite(n) for n in numbers) or not 0 < self.timeout <= 900 or not 0 <= self.grace <= self.timeout or self.poll_interval <= 0:
                raise ProbeError('invalid_request')
            self._sample()
            if self.report['eligible_commit_count'] == 0:
                self.report['state'] = 'not_applicable'
                return self.report
            self._poll()
        except ProbeError as error:
            self.report['state'] = error.state
        return self.report

    def _get(self, suffix, params=None):
        remaining = self.deadline - self.clock()
        if remaining <= 0:
            raise ProbeError('timeout')
        if self.requests >= self.max_requests:
            raise ProbeError('request_limit')
        self.requests += 1
        try:
            result = self.api.get('/repos/' + self.repo + suffix, params or {}, min(15, remaining))
        except ProbeError:
            raise
        except Exception:
            # Injected transports must not leak server bodies, tokens or URLs either.
            raise ProbeError('api_error') from None
        if self.clock() >= self.deadline:
            raise ProbeError('timeout')
        return result

    def _pages(self, suffix, key=None):
        items = []
        suite_ids = set()
        expected_count = None
        for page in range(1, self.max_pages + 1):
            result = self._get(suffix, {'per_page': 100, 'page': page})
            if key:
                if not isinstance(result, dict) or type(result.get('total_count')) is not int or result['total_count'] < 0:
                    raise ProbeError('invalid_response')
                if expected_count is not None and result['total_count'] != expected_count:
                    raise ProbeError('invalid_response')
                expected_count = result['total_count']
                values = result.get(key)
            else:
                values = result
            if not isinstance(values, list) or len(values) > 100 or any(not isinstance(v, dict) for v in values):
                raise ProbeError('invalid_response')
            if key:
                for value in values:
                    suite_id = value.get('id')
                    if type(suite_id) is not int or suite_id <= 0 or suite_id in suite_ids:
                        raise ProbeError('invalid_response')
                    suite_ids.add(suite_id)
            items.extend(values)
            if key and len(items) > expected_count:
                raise ProbeError('invalid_response')
            if key and len(items) == expected_count:
                return items
            if len(values) < 100:
                if key and len(items) < result['total_count']:
                    raise ProbeError('invalid_response')
                return items
        raise ProbeError('page_limit')

    def _sample(self):
        branch = self._get('/branches/' + quote(self.branch, safe=''))
        tip = branch.get('commit', {}).get('sha') if isinstance(branch, dict) and isinstance(branch.get('commit'), dict) else None
        if not valid_sha(tip):
            raise ProbeError('invalid_response')
        self.report['observed_sha'] = tip
        commits = self._get('/commits', {'sha': tip, 'per_page': 30, 'page': 1})
        if not isinstance(commits, list) or not 1 <= len(commits) <= 30:
            raise ProbeError('invalid_response')
        seen = set()
        for commit in commits:
            sha = commit.get('sha') if isinstance(commit, dict) else None
            if not valid_sha(sha) or sha in seen or (not seen and sha != tip):
                raise ProbeError('invalid_response')
            seen.add(sha)
            self.report['sample_commits'].append({'sha': sha, 'state': 'unresolved'})
        heads = set()
        for commit in self.report['sample_commits']:
            associations = self._pages('/commits/' + commit['sha'] + '/pulls')
            eligible = {}
            for association in associations:
                if not association.get('merged_at'):
                    continue
                base = association.get('base')
                head = association.get('head')
                if not isinstance(base, dict) or not isinstance(head, dict):
                    raise ProbeError('invalid_response')
                if not isinstance(base.get('repo'), dict):
                    raise ProbeError('invalid_response')
                base_name = base['repo'].get('full_name')
                if not valid_repo(base_name):
                    raise ProbeError('invalid_response')
                # Association repository is the PR base repository, as in Scorecard.
                # Fork heads and PRs merged into other branches remain eligible;
                # the sampled commits themselves are already anchored to the tip.
                if base_name.lower() != self.repo.lower():
                    continue
                number, sha = association.get('number'), head.get('sha')
                if type(number) is not int or number <= 0 or not valid_sha(sha):
                    raise ProbeError('invalid_response')
                if number in eligible and eligible[number] != sha:
                    raise ProbeError('ambiguous')
                eligible[number] = sha
            if len(eligible) > 1:
                commit['state'] = 'ambiguous'
                raise ProbeError('ambiguous')
            if not eligible:
                commit['state'] = 'direct_commit' if not associations else 'no_eligible_merged_pr'
                continue
            number, sha = next(iter(eligible.items()))
            commit.update(state='eligible', pr_number=number, head_sha=sha)
            self.report['eligible_commit_count'] += 1
            if sha not in heads:
                heads.add(sha)
                self.report['heads'].append({'sha': sha, 'state': 'unresolved', 'suites': []})

    def _head(self, head):
        suites = self._pages('/commits/' + head['sha'] + '/check-suites', 'check_suites')
        recognized = []
        for suite in suites:
            app = suite.get('app')
            slug = app.get('slug') if isinstance(app, dict) else None
            if slug not in ('github-code-scanning', 'github-advanced-security') or suite.get('head_sha') != head['sha']:
                continue
            suite_id = suite.get('id')
            if type(suite_id) is not int or suite_id <= 0:
                raise ProbeError('invalid_response')
            status, conclusion = suite.get('status'), suite.get('conclusion')
            if status not in ('queued', 'in_progress', 'pending', 'requested', 'waiting', 'completed') or conclusion not in (None, 'success', 'failure', 'cancelled', 'skipped', 'neutral', 'timed_out', 'action_required', 'stale', 'startup_failure'):
                raise ProbeError('invalid_response')
            record = {'app': slug, 'id': suite_id, 'status': status, 'conclusion': conclusion}
            recognized.append(record)
        # IDs identify suites, not attempt chronology: an older suite can be
        # rerequested. Conservatively require every recognized suite to succeed.
        head['suites'] = sorted(recognized, key=lambda record: (record['id'], record['app']))
        if not recognized:
            start = self.missing_since.setdefault(head['sha'], self.clock())
            head['state'] = 'missing' if self.clock() - start >= self.grace else 'waiting_missing'
        elif any(s['status'] != 'completed' for s in recognized):
            self.missing_since.pop(head['sha'], None)
            head['state'] = 'pending'
        else:
            self.missing_since.pop(head['sha'], None)
            head['state'] = 'ready' if all(s['conclusion'] == 'success' for s in recognized) else 'failed'

    def _poll(self):
        while True:
            for head in self.report['heads']:
                self._head(head)
            states = {head['state'] for head in self.report['heads']}
            if 'pending' not in states and 'waiting_missing' not in states:
                self.report['state'] = 'failed' if 'failed' in states else 'missing' if 'missing' in states else 'ready'
                return
            remaining = self.deadline - self.clock()
            if remaining <= 0:
                raise ProbeError('timeout')
            grace_remaining = [max(0, self.grace - (self.clock() - since)) for since in self.missing_since.values()]
            positive_grace = [value for value in grace_remaining if value > 0]
            delay = min(self.poll_interval, remaining, min(positive_grace) if positive_grace else remaining)
            self.sleep(delay)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', required=True)
    parser.add_argument('--branch', default='main')
    parser.add_argument('--timeout', type=int, default=900)
    parser.add_argument('--grace', type=int, default=900)
    parser.add_argument('--output', type=Path, default=Path('scorecard-readiness.json'))
    args = parser.parse_args(argv)
    report = Readiness(args.repo, args.branch, GitHub(os.environ.get('GH_TOKEN', '')),
                       timeout=args.timeout, grace=args.grace).run()
    try:
        args.output.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
        if valid_sha(report['observed_sha']) and os.environ.get('GITHUB_OUTPUT'):
            with Path(os.environ['GITHUB_OUTPUT']).open('a', encoding='utf-8') as stream:
                stream.write('sample_sha=' + report['observed_sha'] + '\n')
        if os.environ.get('GITHUB_STEP_SUMMARY'):
            with Path(os.environ['GITHUB_STEP_SUMMARY']).open('a', encoding='utf-8') as stream:
                stream.write('Scorecard readiness: **' + report['state'] + '**; sampled ' +
                             str(len(report['sample_commits'])) + '; eligible ' +
                             str(report['eligible_commit_count']) + '.\n')
    except OSError:
        print('Scorecard readiness: output_error')
        return 1
    print('Scorecard readiness: state=' + report['state'] +
          ' sampled=' + str(len(report['sample_commits'])) +
          ' eligible=' + str(report['eligible_commit_count']))
    return 0 if report['state'] == 'ready' else 1


if __name__ == '__main__':
    raise SystemExit(main())
