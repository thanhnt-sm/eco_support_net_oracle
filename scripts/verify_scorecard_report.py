#!/usr/bin/env python3
"""Validate local Scorecard metadata without executing or rewriting either report.

Artifact/run identity and trusted-main checkout are enforced by the calling workflow.
This verifier checks the bounded report structure and repository provenance, not the
complete SARIF schema or the security conclusions produced by Scorecard.

JSON metadata contract: ossf/scorecard v5.6.0, pkg/scorecard/json.go (JSONScorecardResultV2
and resultsToJSON2). repo.commit identifies the analyzed repository; scorecard.commit
identifies the tool build and must never be substituted for the analyzed revision.
"""
import argparse
import html
import json
import math
import os
from pathlib import Path
import re
import stat
import sys

MAX_BYTES = 8 * 1024 * 1024
MAX_DEPTH = 64
MAX_NODES = 200_000
SHA_PATTERN = re.compile(r'[0-9a-fA-F]{40}')
REPO_PATTERN = re.compile(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+')
TRUSTED_REF = 'refs/heads/main'


def object_pairs(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON field.')
        result[key] = value
    return result


def reject_constant(_value):
    raise ValueError('Non-finite JSON number.')


def validate_bounds(value):
    pending = [(value, 0)]
    count = 0
    while pending:
        item, depth = pending.pop()
        count += 1
        if count > MAX_NODES or depth > MAX_DEPTH:
            raise ValueError('Report structure exceeds bounds.')
        if isinstance(item, float) and not math.isfinite(item):
            raise ValueError('Non-finite JSON number.')
        if isinstance(item, str) and len(item) > 65_536:
            raise ValueError('Report string exceeds bounds.')
        if isinstance(item, dict):
            pending.extend((key, depth + 1) for key in item)
            pending.extend((child, depth + 1) for child in item.values())
        elif isinstance(item, list):
            pending.extend((child, depth + 1) for child in item)


def read_report(path):
    # Fixed filenames only. Reject links/nonregular files before opening; O_NOFOLLOW
    # also prevents final-component replacement with a symlink on Linux runners.
    if not stat.S_ISREG(path.lstat().st_mode):
        raise ValueError('Report must be a regular file.')
    descriptor = os.open(path, os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0))
    with os.fdopen(descriptor, 'rb') as stream:
        metadata = os.fstat(stream.fileno())
        if not stat.S_ISREG(metadata.st_mode) or metadata.st_size > MAX_BYTES:
            raise ValueError('Report size or file type is invalid.')
        data = stream.read(MAX_BYTES + 1)
    if len(data) > MAX_BYTES:
        raise ValueError('Report exceeds byte limit.')
    result = json.loads(data.decode('utf-8'), object_pairs_hook=object_pairs,
                        parse_constant=reject_constant)
    validate_bounds(result)
    if not isinstance(result, dict):
        raise ValueError('Report root must be an object.')
    return result


def validate(directory, repository):
    if not REPO_PATTERN.fullmatch(repository):
        raise ValueError('Expected repository must be owner/name.')
    report = read_report(directory / 'results.json')
    sarif = read_report(directory / 'results.sarif')
    repo = report.get('repo')
    if not isinstance(repo, dict) or repo.get('name') not in (repository, 'github.com/' + repository):
        raise ValueError('Scorecard repository identity mismatch.')
    revision = repo.get('commit')
    if not isinstance(revision, str) or not SHA_PATTERN.fullmatch(revision):
        raise ValueError('Scorecard repository revision must be a full commit SHA.')
    runtime = report.get('scorecard')
    if not isinstance(runtime, dict) or not isinstance(runtime.get('version'), str) or not runtime['version']:
        raise ValueError('Scorecard runtime version metadata is missing or invalid.')
    if 'commit' in runtime and not isinstance(runtime['commit'], str):
        raise ValueError('Scorecard tool commit metadata must be a string.')
    if 'checks' in report and not isinstance(report['checks'], list):
        raise ValueError('Scorecard checks must be an array.')
    if 'score' in report and (type(report['score']) not in (int, float)):
        raise ValueError('Scorecard score must be numeric.')
    runs = sarif.get('runs')
    if sarif.get('version') != '2.1.0' or not isinstance(runs, list) or not runs:
        raise ValueError('SARIF must have version 2.1.0 and nonempty runs.')
    for run in runs:
        if not isinstance(run, dict):
            raise ValueError('SARIF run must be an object.')
        tool = run.get('tool')
        driver = tool.get('driver') if isinstance(tool, dict) else None
        if not isinstance(driver, dict) or not isinstance(driver.get('name'), str) or not driver['name']:
            raise ValueError('SARIF tool driver metadata is invalid.')
        if 'results' in run and (not isinstance(run['results'], list)
                                 or any(not isinstance(item, dict) for item in run['results'])):
            raise ValueError('SARIF results must be an array of objects.')
        if 'versionControlProvenance' in run:
            provenance = run['versionControlProvenance']
            if not isinstance(provenance, list):
                raise ValueError('SARIF provenance must be an array.')
            for entry in provenance:
                if not isinstance(entry, dict):
                    raise ValueError('SARIF provenance entry must be an object.')
                if 'revisionId' in entry and entry['revisionId'] != revision:
                    raise ValueError('SARIF provenance revision mismatch.')
                if 'branch' in entry and entry['branch'] not in ('main', TRUSTED_REF):
                    raise ValueError('SARIF provenance must identify the trusted main branch.')
                if 'repositoryUri' in entry and entry['repositoryUri'] != 'https://github.com/' + repository:
                    raise ValueError('SARIF provenance repository mismatch.')
    return revision, {key: runtime[key] for key in ('version', 'commit') if key in runtime}


def emit_output(contents):
    destination = os.environ.get('GITHUB_OUTPUT')
    if destination:
        with Path(destination).open('a', encoding='utf-8') as stream:
            stream.write(contents)


def emit_summary(runtime):
    destination = os.environ.get('GITHUB_STEP_SUMMARY')
    if destination:
        # JSON quoting makes controls visible; HTML escaping prevents Markdown/HTML
        # report data from becoming active summary content. This is tool metadata,
        # never the analyzed repository commit.
        encoded = html.escape(json.dumps(runtime, ensure_ascii=True))
        with Path(destination).open('a', encoding='utf-8') as stream:
            stream.write('Scorecard runtime metadata (tool version/commit):\n<pre>' + encoded + '</pre>\n'
                         'The analysis container is selected by tag, not a digest; '
                         'runtime metadata does not establish image digest pinning.\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', required=True)
    parser.add_argument('--expected-sha', required=True)
    parser.add_argument('--scan-outcome', required=True)
    parser.add_argument('--directory', required=True, type=Path)
    args = parser.parse_args()
    if args.scan_outcome != 'success':
        emit_output('valid=false\n')
        print('Scorecard scan/publication did not succeed; no SHA is authorized.', file=sys.stderr)
        return 1
    try:
        revision, runtime = validate(args.directory, args.repo)
    except (ValueError, OSError, UnicodeError, RecursionError):
        emit_output('valid=false\n')
        print('Scorecard report metadata validation failed; no SHA is authorized.', file=sys.stderr)
        return 1
    # Do not emit any successful output until BOTH reports are fully validated.
    emit_summary(runtime)
    emit_output(f'valid=true\nsha={revision}\nref={TRUSTED_REF}\n')
    if not SHA_PATTERN.fullmatch(args.expected_sha) or args.expected_sha != revision:
        print('Scorecard readiness SHA alignment failure; valid reports retain their actual analyzed SHA.', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
