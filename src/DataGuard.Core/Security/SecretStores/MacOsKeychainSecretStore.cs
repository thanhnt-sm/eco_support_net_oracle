using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DataGuard.Core.Security.SecretStores;

/// <summary>
/// Native bridge to the login Keychain. It avoids the <c>security</c> CLI,
/// whose password argument would otherwise be visible to process inspection.
/// </summary>
internal static class MacOsKeychainSecretStore
{
    private const int Success = 0;
    private const int ItemNotFound = -25300;
    private const int DuplicateItem = -25299;

    public static void Store(string service, string account, string value)
    {
        EnsureMacOs();
        var serviceBytes = Encoding.UTF8.GetBytes(service);
        var accountBytes = Encoding.UTF8.GetBytes(account);
        var valueBytes = Encoding.UTF8.GetBytes(value);
        IntPtr existingPassword = IntPtr.Zero;
        IntPtr item = IntPtr.Zero;
        try
        {
            var status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes, (uint)accountBytes.Length, accountBytes, out _, out existingPassword, out item);
            if (status == Success)
            {
                ThrowIfError(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)valueBytes.Length, valueBytes));
                return;
            }

            if (status != ItemNotFound)
            {
                ThrowIfError(status);
            }

            status = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes, (uint)accountBytes.Length, accountBytes, (uint)valueBytes.Length, valueBytes, out item);
            if (status == DuplicateItem)
            {
                ThrowIfError(SecKeychainFindGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes, (uint)accountBytes.Length, accountBytes, out _, out existingPassword, out item));
                ThrowIfError(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)valueBytes.Length, valueBytes));
                return;
            }

            ThrowIfError(status);
        }
        finally
        {
            if (existingPassword != IntPtr.Zero)
            {
                SecKeychainItemFreeContent(IntPtr.Zero, existingPassword);
            }

            if (item != IntPtr.Zero)
            {
                CFRelease(item);
            }
            Array.Clear(valueBytes, 0, valueBytes.Length);
        }
    }

    public static string Read(string service, string account)
    {
        EnsureMacOs();
        var serviceBytes = Encoding.UTF8.GetBytes(service);
        var accountBytes = Encoding.UTF8.GetBytes(account);
        IntPtr passwordData = IntPtr.Zero;
        IntPtr item = IntPtr.Zero;
        try
        {
            ThrowIfError(SecKeychainFindGenericPassword(IntPtr.Zero, (uint)serviceBytes.Length, serviceBytes, (uint)accountBytes.Length, accountBytes, out var length, out passwordData, out item));
            var bytes = new byte[checked((int)length)];
            Marshal.Copy(passwordData, bytes, 0, bytes.Length);
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        finally
        {
            if (passwordData != IntPtr.Zero)
            {
                SecKeychainItemFreeContent(IntPtr.Zero, passwordData);
            }

            if (item != IntPtr.Zero)
            {
                CFRelease(item);
            }
        }
    }

    private static void EnsureMacOs()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("The macOS Keychain backend is only available on macOS.");
        }
    }

    private static void ThrowIfError(int status)
    {
        if (status != Success)
        {
            throw new InvalidOperationException($"macOS Keychain operation failed with OSStatus {status}.");
        }
    }

    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, uint passwordLength, byte[] passwordData, out IntPtr itemRef);
    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainFindGenericPassword(IntPtr keychain, uint serviceNameLength, byte[] serviceName, uint accountNameLength, byte[] accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);
    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attrList, uint length, byte[] data);
    [DllImport("/System/Library/Frameworks/Security.framework/Security")]
    private static extern int SecKeychainItemFreeContent(IntPtr attrList, IntPtr data);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern int CFRelease(IntPtr cf);
}
