"""One predeclared installer TEMP file. Never exports text or scans TEMP."""
import hashlib
import os
from pathlib import Path
import re
import stat
import unicodedata

MAX_BYTES = 1024 * 1024
NAME = re.compile(r'DGSmokeInstaller-[0-9a-f]{32}\.log')


class ReceiptBlocked(RuntimeError):
    pass


def log_argument(name):
    if not NAME.fullmatch(name):
        raise ReceiptBlocked('installer-receipt-name-unsafe')
    return '/logFile:' + name


def safe_parent(parent):
    if not parent.is_absolute() or '..' in parent.parts:
        raise ReceiptBlocked('installer-temp-unverified')
    for path in [parent, *parent.parents]:
        info = path.lstat()
        if not stat.S_ISDIR(info.st_mode) or getattr(info, 'st_file_attributes', 0) & 0x400:
            raise ReceiptBlocked('installer-temp-reparse-or-not-directory')


def open_leaf(path, create=False, delete=False):
    if os.name != 'nt':
        if not hasattr(os, 'O_NOFOLLOW'):
            raise ReceiptBlocked('installer-receipt-nofollow-unavailable')
        return os.open(path, os.O_RDONLY | os.O_NOFOLLOW | (os.O_CREAT | os.O_EXCL if create else 0), 0o600)
    # Keep a read handle with sharing enabled while VSIXInstaller appends. Do not
    # request DELETE access until cleanup: installer log sharing may reject it.
    import ctypes
    from ctypes import wintypes
    import msvcrt
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                                  wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    kernel.CreateFileW.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel.CreateFileW(str(path), 0x80000000 | (0x10000 if delete else 0),
                                7, None, 1 if create else 3, 0x00200000, None)
    if handle == wintypes.HANDLE(-1).value:
        raise OSError(ctypes.get_last_error(), 'receipt-open')
    try:
        fd = msvcrt.open_osfhandle(handle, os.O_RDONLY | os.O_BINARY)
    except Exception:
        kernel.CloseHandle(handle)
        raise
    return fd


def file_identity(fd):
    info = os.fstat(fd)
    if (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1 or not info.st_ino
            or getattr(info, 'st_file_attributes', 0) & 0x400):
        raise ReceiptBlocked('installer-receipt-type-or-links-unsafe')
    if os.name == 'nt':
        import ctypes
        from ctypes import wintypes
        import msvcrt
        class AttributeTag(ctypes.Structure):
            _fields_ = [('attributes', wintypes.DWORD), ('tag', wintypes.DWORD)]
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.GetFileInformationByHandleEx.argtypes = [wintypes.HANDLE, ctypes.c_int,
                                                      wintypes.LPVOID, wintypes.DWORD]
        data = AttributeTag()
        if (not kernel.GetFileInformationByHandleEx(msvcrt.get_osfhandle(fd), 9,
                                                   ctypes.byref(data), ctypes.sizeof(data))
                or data.attributes & 0x400):
            raise ReceiptBlocked('installer-receipt-reparse-or-unverified')
    return info


def same_file(first, second):
    return (first.st_dev, first.st_ino) == (second.st_dev, second.st_ino)


def delete_leaf(fd, path):
    if os.name != 'nt':
        path.unlink()  # POSIX fixture only; production host guard requires Windows.
        return
    import ctypes
    from ctypes import wintypes
    import msvcrt
    class Disposition(ctypes.Structure):
        _fields_ = [('delete', wintypes.BYTE)]
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.SetFileInformationByHandle.argtypes = [wintypes.HANDLE, ctypes.c_int,
                                                wintypes.LPVOID, wintypes.DWORD]
    data = Disposition(True)
    if not kernel.SetFileInformationByHandle(msvcrt.get_osfhandle(fd), 4,
                                             ctypes.byref(data), ctypes.sizeof(data)):
        raise ReceiptBlocked('installer-receipt-delete-unverified')


class Receipt:
    def __init__(self, parent, name):
        self.argument = log_argument(name)
        self.parent, self.path, self.fd = Path(parent), Path(parent) / name, None
        try:
            safe_parent(self.parent)
            self.fd = open_leaf(self.path, create=True)
            self.identity = file_identity(self.fd)
            self.check_owned()
        except (OSError, ReceiptBlocked):
            if self.fd is not None:
                os.close(self.fd)
                self.fd = None
            # Never remove a collision or a path with uncertain ownership.
            raise ReceiptBlocked('installer-receipt-reservation-unverified') from None

    def check_owned(self):
        safe_parent(self.parent)
        info = self.path.lstat()
        if (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1
                or getattr(info, 'st_file_attributes', 0) & 0x400
                or not same_file(info, self.identity)
                or not same_file(file_identity(self.fd), self.identity)):
            raise ReceiptBlocked('installer-receipt-ownership-unverified')

    def metadata(self):
        try:
            self.check_owned()
            before = file_identity(self.fd)
            if not 0 < before.st_size <= MAX_BYTES:
                raise ReceiptBlocked('installer-receipt-empty-or-oversized')
            os.lseek(self.fd, 0, os.SEEK_SET)
            data = bytearray()
            while len(data) <= MAX_BYTES:
                chunk = os.read(self.fd, min(65536, MAX_BYTES + 1 - len(data)))
                if not chunk:
                    break
                data.extend(chunk)
            after = file_identity(self.fd)
            self.check_owned()
            if (len(data) != before.st_size or before.st_size != after.st_size
                    or before.st_mtime_ns != after.st_mtime_ns or len(data) > MAX_BYTES):
                raise ReceiptBlocked('installer-receipt-changing-or-oversized')
            encoding = 'utf-16' if data[:2] in (b'\xff\xfe', b'\xfe\xff') else 'utf-8-sig'
            text = data.decode(encoding, errors='strict')
            if not text.strip() or any(unicodedata.category(char) == 'Cc' and char not in '\r\n\t' for char in text):
                raise ReceiptBlocked('installer-receipt-malformed')
            return dict(bytes=len(data), lines=len(text.splitlines()), encoding=encoding,
                        sha256=hashlib.sha256(data).hexdigest())
        except (OSError, UnicodeError):
            raise ReceiptBlocked('installer-receipt-missing-or-malformed') from None

    def cleanup(self):
        delete_fd = None
        try:
            self.check_owned()
            delete_fd = open_leaf(self.path, delete=True)
            if not same_file(file_identity(delete_fd), self.identity):
                raise ReceiptBlocked('installer-receipt-cleanup-ownership-unverified')
            self.check_owned()
            delete_leaf(delete_fd, self.path)
        except OSError:
            raise ReceiptBlocked('installer-receipt-cleanup-locked-or-unverified') from None
        finally:
            if delete_fd is not None:
                os.close(delete_fd)
            if self.fd is not None:
                os.close(self.fd)
                self.fd = None
