#!/usr/bin/env python3
"""Validate a suite's fresh Cobertura reports and emit an explicit upload manifest.

Source paths must resolve to existing production files in this checkout. Reports
are normalized for upload; overlapping suite lines are unioned only for the local
line gate. Branch totals remain per report because Cobertura branch IDs are not
stable across assemblies, so summing them would misrepresent aggregate coverage.
"""
import argparse
from decimal import Decimal
import hashlib
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET


def source_path(filename, sources, root):
    filename = filename.replace('\\', '/')
    candidates = [Path(filename)] if Path(filename).is_absolute() else [root / filename] + [Path(s) / filename for s in sources]
    matches = set()
    for candidate in candidates:
        resolved = candidate.resolve()
        if resolved.is_file() and resolved.is_relative_to(root / 'src'):
            relative = resolved.relative_to(root).as_posix()
            if '/obj/' not in relative and '/bin/' not in relative and '/test/' not in relative and not relative.endswith(('.test.ts', '.test.js')):
                matches.add(relative)
    if len(matches) != 1:
        raise ValueError(f'Source must resolve uniquely inside production src/: {filename}')
    return matches.pop()


def branch_counts(line):
    """Validate collector booleans and preserve represented branch denominators."""
    flag = line.get('branch')
    condition = line.get('condition-coverage')
    if flag is None:
        is_branch = False
    elif flag.lower() in ('true', '1'):
        is_branch = True
    elif flag.lower() in ('false', '0'):
        is_branch = False
    else:
        raise ValueError(f'Invalid branch flag: {flag!r}')
    if flag is not None:
        line.set('branch', str(is_branch).lower())
    if not is_branch:
        if condition is not None:
            raise ValueError('Condition coverage on a non-branch line')
        return 0, 0
    match = re.fullmatch(r'(\d+(?:\.\d+)?)% \((\d+)/(\d+)\)', condition or '')
    if not match:
        raise ValueError(f'Invalid branch counts: {condition!r}')
    percentage, covered, total = Decimal(match[1]), int(match[2]), int(match[3])
    if total <= 0 or covered > total or percentage > 100:
        raise ValueError(f'Invalid branch counts: {condition!r}')
    # Coverlet truncates the displayed percentage to two decimal places.
    if abs(percentage - Decimal(100) * covered / total) > Decimal('0.01'):
        raise ValueError(f'Inconsistent branch percentage: {condition!r}')
    return covered, total


def refresh_totals(element):
    """Count class records once; method copies describe only their own summaries."""
    lines = element.findall('./lines/line' if element.tag in ('class', 'method') else './/class/lines/line')
    covered = sum(int(line.get('hits', '0')) > 0 for line in lines)
    branches = [branch_counts(line) for line in lines]
    branch_hit = sum(count[0] for count in branches)
    branch_total = sum(count[1] for count in branches)
    element.set('line-rate', str(covered / len(lines) if lines else 0))
    element.set('branch-rate', str(branch_hit / branch_total if branch_total else 0))
    if element.tag == 'coverage':
        for key, value in [('lines-covered', covered), ('lines-valid', len(lines)), ('branches-covered', branch_hit), ('branches-valid', branch_total)]:
            element.set(key, str(value))


def validate(paths, root):
    root = root.resolve()
    if not paths:
        raise ValueError('No coverage reports')
    seen_paths, seen_content, hits, sources_found, reports = set(), set(), {}, set(), []
    for path in paths:
        path = path.resolve()
        try:
            data = path.read_bytes()
            digest = hashlib.sha256(data).hexdigest()
            if path in seen_paths or digest in seen_content:
                raise ValueError(f'Duplicate coverage report: {path}')
            seen_paths.add(path)
            seen_content.add(digest)
            tree = ET.fromstring(data)
            if tree.tag != 'coverage':
                raise ValueError(f'Not Cobertura: {path}')
            input_branches = None
            if tree.get('branches-covered') is not None or tree.get('branches-valid') is not None:
                covered, total = int(tree.attrib['branches-covered']), int(tree.attrib['branches-valid'])
                if not 0 <= covered <= total:
                    raise ValueError('Invalid input branch totals')
                input_branches = {'covered': covered, 'total': total}
            source_roots = [s.text for s in tree.findall('./sources/source') if s.text]
            generated = []
            report_hits, branches = {}, [0, 0]
            for classes in tree.findall('./packages/package/classes'):
                for cls in list(classes):
                    if '/obj/' in ('/' + cls.get('filename', '').replace('\\', '/')):
                        generated.append(cls.get('filename'))
                        classes.remove(cls)  # Generated compiler output, matching the existing gate.
            for cls in tree.findall('./packages/package/classes/class'):
                filename = source_path(cls.attrib['filename'], source_roots, root)
                sources_found.add(filename)
                cls.set('filename', filename)
                for line in cls.findall('./lines/line'):
                    number, count = int(line.attrib['number']), int(line.attrib['hits'])
                    if number < 1 or count < 0:
                        raise ValueError(f'Invalid line/hits: {path}')
                    if number > len((root / filename).read_text(encoding='utf-8-sig').splitlines()):
                        raise ValueError(f'Line outside source: {filename}:{number}')
                    key = (filename, number)
                    report_hits[key] = report_hits.get(key, False) or count > 0
                    hits[key] = hits.get(key, False) or count > 0
                    branch_hit, branch_total = branch_counts(line)
                    branches[0] += branch_hit
                    branches[1] += branch_total
                for method in cls.findall('./methods/method'):
                    refresh_totals(method)
                refresh_totals(cls)
            if not report_hits:
                raise ValueError(f'Empty coverage report: {path}')
            for package in tree.findall('./packages/package'):
                refresh_totals(package)
            refresh_totals(tree)
            source_element = tree.find('sources')
            if source_element is not None:
                tree.remove(source_element)
            ET.SubElement(ET.SubElement(tree, 'sources'), 'source').text = '.'
            reports.append({'path': str(path), 'sha256': digest, 'generated_sources_omitted': sorted(set(generated)), 'lines': {'covered': sum(report_hits.values()), 'total': len(report_hits)}, 'input_branches': input_branches, 'branch_scope': 'represented retained class source-line records; per report only', 'branches': {'covered': branches[0], 'total': branches[1]}, 'xml': ET.tostring(tree, encoding='unicode')})
        except (OSError, ET.ParseError, KeyError, TypeError) as error:
            raise ValueError(f'Invalid coverage report {path}: {error}') from error
    return {'lines': {'covered': sum(hits.values()), 'total': len(hits)}, 'sources': sorted(sources_found), 'reports': reports}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path.cwd())
    parser.add_argument('--input', type=Path, required=True, help='Fresh suite-only results directory')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--expect', type=int, required=True, help='Required collector report count')
    parser.add_argument('--minimum', type=float, default=0)
    parser.add_argument('--github-output', type=Path)
    args = parser.parse_args()
    try:
        paths = sorted(args.input.glob('*/coverage.cobertura.xml'))
        if len(paths) != args.expect:
            raise ValueError(f'Expected {args.expect} collector reports, found {len(paths)} in {args.input}')
        result = validate(paths, args.root)
        rate = 100 * result['lines']['covered'] / result['lines']['total']
        if rate < args.minimum:
            raise ValueError(f'Coverage {rate:.2f}% below {args.minimum}%')
        if args.output.exists():
            raise ValueError(f'Output must be fresh: {args.output}')
        args.output.mkdir(parents=True)
        upload_paths = []
        for index, report in enumerate(result['reports']):
            target = args.output / f'{index + 1}.cobertura.xml'
            target.write_text(report.pop('xml'), encoding='utf-8')
            report['upload_path'] = target.as_posix()
            upload_paths.append(target.as_posix())
        result['scope'] = 'union of measured lines in this suite; not whole-product coverage'
        (args.output / 'manifest.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
        (args.output / 'files.txt').write_text('\n'.join(upload_paths) + '\n', encoding='utf-8')
        if args.github_output:
            with args.github_output.open('a', encoding='utf-8') as stream:
                stream.write('files=' + ','.join(upload_paths) + '\n')
        print(f"Measured suite line coverage: {rate:.2f}% ({result['lines']['covered']}/{result['lines']['total']})")
    except ValueError as error:
        parser.exit(1, f'Coverage validation failed: {error}\n')


if __name__ == '__main__':
    main()
