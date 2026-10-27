"""Owned installer receipt contracts; no installer or IDE is executed."""
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from scripts.vsix_installer_receipt import Receipt, ReceiptBlocked, MAX_BYTES


class ReceiptTests(unittest.TestCase):
    name = 'DGSmokeInstaller-' + 'a' * 32 + '.log'

    def test_basename_only_temp_placement_and_sanitized_metadata(self):
        with tempfile.TemporaryDirectory() as temp:
            receipt = Receipt(Path(temp), self.name)
            self.assertEqual(receipt.argument, '/logFile:' + self.name)
            payload = b'Error PRIVATE SQL SELECT secret FROM C:/private/account\n'
            with open(Path(temp) / self.name, 'ab') as stream:
                stream.write(payload)
            result = receipt.metadata()
            self.assertEqual(result['bytes'], len(payload))
            self.assertEqual(result['lines'], 1)
            self.assertEqual(set(result), {'bytes', 'lines', 'encoding', 'sha256'})
            self.assertNotIn('PRIVATE', str(result))
            receipt.cleanup()
            self.assertFalse((Path(temp) / self.name).exists())

    def test_missing_empty_or_replaced_receipt_blocks_and_preserves_replacement(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            receipt = Receipt(root, self.name)
            with self.assertRaises(ReceiptBlocked):
                receipt.metadata()
            path = root / self.name
            # Original descriptor remains open, so inode reuse cannot mask replacement.
            path.rename(root / 'moved-owned.log')
            with self.assertRaises(ReceiptBlocked):
                receipt.metadata()
            path.write_bytes(b'UNRELATED')
            with self.assertRaises(ReceiptBlocked):
                receipt.metadata()
            with self.assertRaises(ReceiptBlocked):
                receipt.cleanup()
            self.assertEqual(path.read_bytes(), b'UNRELATED')

    def test_collision_never_reads_or_deletes_existing_file(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / self.name
            path.write_bytes(b'UNRELATED')
            with self.assertRaises(ReceiptBlocked):
                Receipt(Path(temp), self.name)
            self.assertEqual(path.read_bytes(), b'UNRELATED')

    def test_symlink_reparse_and_hardlink_are_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = root / 'unrelated.log'
            target.write_bytes(b'UNRELATED')
            path = root / self.name
            # Creation must fail before reading an existing reparse/ordinary file.
            with patch.object(Path, 'lstat', side_effect=OSError('PRIVATE')):
                with self.assertRaises(ReceiptBlocked):
                    Receipt(root, self.name)
            receipt = Receipt(root, self.name)
            os.link(path, root / 'hardlink')
            with self.assertRaises(ReceiptBlocked):
                receipt.metadata()
            with self.assertRaises(ReceiptBlocked):
                receipt.cleanup()
            self.assertTrue(path.exists())
            self.assertEqual(target.read_bytes(), b'UNRELATED')

    def test_leaf_link_replacement_is_not_followed_or_deleted(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = root / 'unrelated'
            target.mkdir()
            (target / 'keep').write_bytes(b'KEEP')
            receipt = Receipt(root, self.name)
            path = root / self.name
            path.rename(root / 'moved-owned.log')
            if os.name == 'nt':
                import subprocess
                self.assertEqual(subprocess.run(['cmd', '/c', 'mklink', '/J', str(path), str(target)],
                                                capture_output=True).returncode, 0)
            else:
                path.symlink_to(target, target_is_directory=True)
            with self.assertRaises(ReceiptBlocked):
                receipt.metadata()
            with self.assertRaises(ReceiptBlocked):
                receipt.cleanup()
            self.assertTrue(path.exists())
            self.assertEqual((target / 'keep').read_bytes(), b'KEEP')

    def test_concurrent_growth_blocks_with_bounded_read(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            receipt = Receipt(root, self.name)
            path = root / self.name
            path.write_bytes(b'initial\n')
            real_read = os.read
            sizes = []
            def grow(fd, count):
                sizes.append(count)
                if len(sizes) == 1:
                    with path.open('ab') as stream:
                        stream.write(b'changed\n')
                return real_read(fd, count)
            with patch('scripts.vsix_installer_receipt.os.read', side_effect=grow):
                with self.assertRaises(ReceiptBlocked):
                    receipt.metadata()
            self.assertLessEqual(max(sizes), MAX_BYTES + 1)
            receipt.cleanup()

    def test_windows_lane_includes_receipt_contracts_and_path_filters(self):
        workflow = (Path(__file__).resolve().parents[2] / '.github/workflows/vsix-harness-checks.yml').read_text()
        for path in ['scripts/vsix_installer_receipt.py', 'scripts/tests/test_vsix_installer_receipt.py']:
            self.assertIn("'" + path + "'", workflow)
        self.assertIn('scripts.tests.test_vsix_installer_receipt', workflow)

    def test_real_symlink_or_junction_parent_is_not_followed(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            target = root / 'target'
            target.mkdir()
            link = root / 'link'
            if os.name == 'nt':
                import subprocess
                made = subprocess.run(['cmd', '/c', 'mklink', '/J', str(link), str(target)],
                                      capture_output=True).returncode == 0
                self.assertTrue(made, 'Windows fixture junction creation must succeed')
            else:
                link.symlink_to(target, target_is_directory=True)
            with self.assertRaises(ReceiptBlocked):
                Receipt(link, self.name)
            self.assertFalse((target / self.name).exists())

    def test_unrelated_files_are_retained(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            other = root / 'unrelated.log'
            other.write_bytes(b'KEEP')
            receipt = Receipt(root, self.name)
            receipt.cleanup()
            self.assertEqual(other.read_bytes(), b'KEEP')

    def test_malformed_oversized_and_unsafe_names_block(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for name in ['../x', 'C:/x.log', self.name + ':stream', 'ordinary.log']:
                with self.subTest(name=name), self.assertRaises(ReceiptBlocked):
                    Receipt(root, name)
            for data in [b'', b'\xff', b'a\x00b', b'log\x7f', 'log\u0085'.encode(),
                         'log\u009f'.encode(), b'x' * (MAX_BYTES + 1)]:
                receipt = Receipt(root, self.name)
                (root / self.name).write_bytes(data)
                with self.subTest(size=len(data)), self.assertRaises(ReceiptBlocked):
                    receipt.metadata()
                receipt.cleanup()
            for data in [b'normal log\n', 'normal log\n'.encode('utf-16')]:
                receipt = Receipt(root, self.name)
                (root / self.name).write_bytes(data)
                self.assertEqual(receipt.metadata()['lines'], 1)
                receipt.cleanup()


if __name__ == '__main__':
    unittest.main()
