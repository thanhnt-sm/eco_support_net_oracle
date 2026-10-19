#!/usr/bin/env python3
"""Read-only UTF-8/prose audit. Review findings need a human, never auto-rewriting.

Usage: python3 scripts/check_document_encoding.py --files-from paths.txt --json report.json
Byte/control checks cover the whole file. NFC/mojibake/language heuristics omit
fenced/inline code and URL targets; natural-language code comments need manual review.
Exit 1 means byte/control errors. Exit 0 does not certify spelling or rendering.
"""
import argparse
import json
from pathlib import Path
import re
import unicodedata

VI_MARKS = re.compile(r'[ăđơưĂĐƠƯạảấầẩẫậắằẳẵặẹẻẽếềểễệỉịọỏốồổỗộớờởỡợụủứừửữựỳỵỷỹ]', re.I)
MOJIBAKE = re.compile(r'Ã[\u0080-\u00bf]|Â[\u0080-\u00bf]|á[º»]|â[€™œ“”–—]')
UNACCENTED = re.compile(r'\b(?:huong dan|tai lieu|cau hinh|tieng viet|nguoi dung|kiem tra|phat hanh|ma nguon|co so du lieu)\b', re.I)
INVISIBLE = set(range(0x202A, 0x202F)) | set(range(0x2066, 0x206A)) | {0x200B, 0xFEFF}


def prose_lines(text):
    fence = None
    for number, line in enumerate(text.splitlines(), 1):
        marker = re.match(r'^\s{0,3}(`{3,}|~{3,})', line)
        if fence:
            if marker and marker[1][0] == fence[0] and len(marker[1]) >= len(fence):
                fence = None
            continue
        if marker:
            fence = marker[1]
            continue
        if line.startswith(('    ', '\t')):
            continue
        line = re.sub(r'(`+).*?\1', '', line)
        line = re.sub(r'\]\([^)]*\)', ']', line)
        line = re.sub(r'https?://\S+|mailto:\S+', '', line)
        yield number, line


def inspect_bytes(data):
    findings = []
    try:
        text = data.decode('utf-8', errors='strict')
    except UnicodeDecodeError as error:
        return [{'line': data[:error.start].count(b'\n') + 1, 'kind': 'invalid_utf8',
                 'severity': 'error', 'detail': f'byte offset {error.start}'}]
    for number, line in enumerate(text.split('\n'), 1):
        if '\ufffd' in line:
            findings.append({'line': number, 'kind': 'replacement_character', 'severity': 'error', 'detail': 'U+FFFD'})
        for char in sorted(set(line)):
            point = ord(char)
            if unicodedata.category(char) == 'Cc' and char not in '\t\r':
                findings.append({'line': number, 'kind': 'invalid_control', 'severity': 'error', 'detail': f'U+{point:04X}'})
            elif point in INVISIBLE:
                findings.append({'line': number, 'kind': 'invisible_control', 'severity': 'review', 'detail': f'U+{point:04X}'})
    for number, line in prose_lines(text):
        for condition, kind, detail in [
            (unicodedata.normalize('NFC', line) != line, 'non_nfc_prose', 'Review combining characters; do not normalize code blindly.'),
            (MOJIBAKE.search(line), 'possible_mojibake', 'Possible UTF-8 decoded through a legacy code page.'),
            (not VI_MARKS.search(line) and len(UNACCENTED.findall(line)) >= 2,
             'possible_unaccented_vietnamese', 'Two or more Vietnamese phrase candidates without Vietnamese marks; human review required.'),
        ]:
            if condition:
                findings.append({'line': number, 'kind': kind, 'severity': 'review', 'detail': detail})
    return findings


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', nargs='*')
    parser.add_argument('--files-from', type=Path, help='UTF-8 newline-separated file paths, relative to current directory.')
    parser.add_argument('--json', type=Path, help='Optional UTF-8 JSON report path.')
    args = parser.parse_args(argv)
    paths = list(args.paths)
    if args.files_from:
        paths += args.files_from.read_text(encoding='utf-8').splitlines()
    paths = sorted(set(p for p in paths if p))
    if not paths:
        parser.error('provide paths or --files-from')
    results = []
    for name in paths:
        try:
            findings = inspect_bytes(Path(name).read_bytes())
        except OSError as error:
            findings = [{'line': 0, 'kind': 'unreadable_file', 'severity': 'error', 'detail': type(error).__name__}]
        results.append({'path': name, 'findings': findings})
    errors = sum(f['severity'] == 'error' for r in results for f in r['findings'])
    reviews = sum(f['severity'] == 'review' for r in results for f in r['findings'])
    report = {'files_checked': len(paths), 'errors': errors, 'review_findings': reviews, 'results': results,
              'limitations': 'Not a spellchecker or font/host rendering test. Code comments require manual prose review.'}
    if args.json:
        args.json.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'{len(paths)} files checked; {errors} errors; {reviews} findings requiring review.')
    for result in results:
        for finding in result['findings']:
            print(f"{result['path']}:{finding['line']}: {finding['kind']} ({finding['severity']})")
    return 1 if errors else 0


if __name__ == '__main__':
    raise SystemExit(main())
