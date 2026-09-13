using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DataGuard.Core.Security.SecretStores;

/// <summary>Linux Secret Service bridge using libsecret's <c>secret-tool</c>; values are passed on standard input.</summary>
internal static class LinuxSecretServiceSecretStore
{
    private const string SecretToolPath = "/usr/bin/secret-tool";
    private const int MaximumSecretOutputBytes = 1 * 1024 * 1024;
    private const int MaximumErrorOutputBytes = 16 * 1024;
    private const int HelperTimeoutMilliseconds = 10_000;

    public static void Store(string service, string account, string value)
    {
        EnsureAvailable();
        using var process = Start("store", "--label", "DataGuard credential", "service", service, "account", account);
        var standardOutput = ReadBoundedAsync(process.StandardOutput, MaximumSecretOutputBytes);
        var standardError = ReadBoundedAsync(process.StandardError, MaximumErrorOutputBytes);
        process.StandardInput.Write(value);
        process.StandardInput.Close();
        WaitForExitOrThrow(process);
        _ = standardOutput.GetAwaiter().GetResult();
        _ = standardError.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Linux Secret Service rejected the credential write.");
        }
    }

    public static string Read(string service, string account)
    {
        EnsureAvailable();
        using var process = Start("lookup", "service", service, "account", account);
        var standardOutput = ReadBoundedAsync(process.StandardOutput, MaximumSecretOutputBytes);
        var standardError = ReadBoundedAsync(process.StandardError, MaximumErrorOutputBytes);
        WaitForExitOrThrow(process);
        var value = standardOutput.GetAwaiter().GetResult();
        _ = standardError.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException("Linux Secret Service does not contain the requested credential.");
        }

        return value.TrimEnd('\r', '\n');
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumBytes)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder();
        var totalBytes = 0;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
            if (read == 0)
            {
                return builder.ToString();
            }

            totalBytes = checked(totalBytes + Encoding.UTF8.GetByteCount(buffer, 0, read));
            if (totalBytes > maximumBytes)
            {
                throw new InvalidOperationException("Linux Secret Service returned output exceeding the safety limit.");
            }

            builder.Append(buffer, 0, read);
        }
    }

    private static void WaitForExitOrThrow(Process process)
    {
        if (process.WaitForExit(HelperTimeoutMilliseconds))
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The helper may have exited between the timeout and Kill call.
        }

        process.WaitForExit();
        throw new TimeoutException("Linux Secret Service helper exceeded the operation timeout.");
    }

    private static Process Start(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(SecretToolPath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start Linux Secret Service client.");
    }

    private static void EnsureAvailable()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists(SecretToolPath))
        {
            throw new PlatformNotSupportedException("Encrypted credential storage requires the Linux Secret Service backend.");
        }
    }
}
