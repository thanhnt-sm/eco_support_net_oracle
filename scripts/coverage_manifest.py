#!/usr/bin/env python3
"""Validate a suite's fresh Cobertura reports and emit an explicit upload manifest.

Source paths must resolve to existing production files in this checkout. Reports
are normalized for upload; overlapping suite lines are unioned only for the local
line gate. Branch totals remain per report because Cobertura branch IDs are not
stable across assemblies, so summing them would misrepresent aggregate coverage.
"""
import argparse
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


def refresh_totals(element):
    """Keep Cobertura summary attributes consistent with retained class line records."""
    lines = element.findall('.//class/lines/line')
    covered = sum(int(line.get('hits', '0')) > 0 for line in lines)
    branches = [re.fullmatch(r'[\d.]+% \((\d+)/(\d+)\)', line.get('condition-coverage', '')) for line in lines if line.get('branch') == 'true']
    branch_hit = sum(int(match[1]) for match in branches if match)
    branch_total = sum(int(match[2]) for match in branches if match)
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
                    if line.get('branch') == 'true':
                        match = re.fullmatch(r'[\d.]+% \((\d+)/(\d+)\)', line.get('condition-coverage', ''))
                        if not match or int(match[1]) > int(match[2]):
                            raise ValueError(f'Invalid branch counts: {path}')
                        branches[0] += int(match[1])
                        branches[1] += int(match[2])
            if not report_hits:
                raise ValueError(f'Empty coverage report: {path}')
            for package in tree.findall('./packages/package'):
                refresh_totals(package)
            refresh_totals(tree)
            source_element = tree.find('sources')
            if source_element is not None:
                tree.remove(source_element)
            ET.SubElement(ET.SubElement(tree, 'sources'), 'source').text = '.'
            reports.append({'path': str(path), 'sha256': digest, 'generated_sources_omitted': sorted(set(generated)), 'lines': {'covered': sum(report_hits.values()), 'total': len(report_hits)}, 'branches': {'covered': branches[0], 'total': branches[1]}, 'xml': ET.tostring(tree, encoding='unicode')})
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
