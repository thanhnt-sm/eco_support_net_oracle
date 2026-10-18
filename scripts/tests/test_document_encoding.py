"""Offline encoding regression cases; technical strings are not language errors."""
import importlib.util
from pathlib import Path
import unittest

SPEC = importlib.util.spec_from_file_location('document_encoding', Path(__file__).resolve().parents[1] / 'check_document_encoding.py')
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class DocumentEncodingTests(unittest.TestCase):
    def kinds(self, text):
        return {x['kind'] for x in MODULE.inspect_bytes(text.encode('utf-8'))}

    def test_invalid_utf8(self):
        self.assertEqual(MODULE.inspect_bytes(b'bad\xff')[0]['kind'], 'invalid_utf8')

    def test_replacement_character(self):
        self.assertIn('replacement_character', self.kinds('Ti\ufffdng Việt'))

    def test_invalid_control(self):
        self.assertIn('invalid_control', self.kinds('Tài\x00 liệu'))

    def test_invisible_bidi_control(self):
        self.assertIn('invisible_control', self.kinds('Tài\u202e liệu'))

    def test_nfd_prose_is_reviewed(self):
        self.assertIn('non_nfc_prose', self.kinds('Tie\u0302\u0301ng Việt'))

    def test_mojibake_prose(self):
        self.assertIn('possible_mojibake', self.kinds('Tiếng Việt'.encode('utf-8').decode('cp1252')))

    def test_unaccented_prose_is_warning(self):
        findings = MODULE.inspect_bytes(b'Huong dan tai lieu cho nguoi dung.')
        self.assertTrue(any(x['kind'] == 'possible_unaccented_vietnamese' and x['severity'] == 'review' for x in findings))

    def test_technical_identifiers_urls_and_code_not_language_errors(self):
        text = '`DATAGUARD_CONNECTION_STRING` `huong dan tai lieu` https://example.test/huong-dan\n```cs\nvar x = "Tie\u0302\u0301ng"; // huong dan tai lieu\n```\n'
        self.assertEqual(MODULE.inspect_bytes(text.encode('utf-8')), [])

    def test_valid_vietnamese_and_technical_terms(self):
        self.assertEqual(self.kinds('Hướng dẫn kiểm tra CLI, .NET, React và cơ sở dữ liệu.\n\tĐược phê duyệt.\r\n'), set())

    def test_tilde_fence_and_multibacktick_inline(self):
        self.assertEqual(self.kinds('~~~text\nhuong dan tai lieu\n~~~\n``huong dan ` tai lieu``'), set())

    def test_code_invalid_bytes_not_hidden(self):
        self.assertEqual(MODULE.inspect_bytes(b'```\n\xff\n```')[0]['kind'], 'invalid_utf8')

    def test_plain_english_not_vietnamese_error(self):
        self.assertEqual(self.kinds('The API uses string IDs and JSON. Read the runtime guide.'), set())
