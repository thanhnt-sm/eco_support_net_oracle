namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Text;

/// <summary>Best-effort local log storage with one bounded backup. The caller serializes writes.</summary>
internal static class BoundedLogFile
{
    internal const int MaximumFileBytes = 1024 * 1024;

    internal static void Append(string path, string sanitizedText)
    {
        var bytes = new UTF8Encoding(false).GetBytes(sanitizedText);
        if (bytes.Length > MaximumFileBytes)
        {
            return;
        }

        var backup = path + ".1";
        RejectReparsePoints(path);
        RejectReparsePoints(backup);
        if (File.Exists(backup) && new FileInfo(backup).Length > MaximumFileBytes)
        {
            File.Delete(backup);
        }

        using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
        {
            if (stream.Length + bytes.Length > MaximumFileBytes)
            {
                if (stream.Length <= MaximumFileBytes)
                {
                    using (var rotated = new FileStream(backup, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        stream.CopyTo(rotated);
                    }
                }

                // Do not preserve an older unbounded file as an oversized backup.
                stream.SetLength(0);
            }

            stream.Seek(0, SeekOrigin.End);
            stream.Write(bytes, 0, bytes.Length);
        }
    }

    private static void RejectReparsePoints(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("Diagnostic log path is a reparse point.");
        }

        for (var parent = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); parent != null; parent = parent.Parent)
        {
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("Diagnostic log directory is a reparse point.");
            }
        }
    }
}
