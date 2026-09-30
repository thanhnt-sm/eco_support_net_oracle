// <copyright file="ExtensionVersion.cs" company="Than Nguyen">
// Copyright (c) 2026 Than Nguyen. All rights reserved.
// </copyright>

namespace DataGuard.VisualStudio;

using System;
using System.IO;
using System.Xml;

/// <summary>
/// Single source of the extension version shown in logs and Help → About. The installed
/// <c>extension.vsixmanifest</c> (rewritten by release.yml) wins; the constant is the fallback and
/// is asserted against source.extension.vsixmanifest by CI.
/// </summary>
internal static class ExtensionVersion
{
    /// <summary>Fallback version; must match source.extension.vsixmanifest Identity/@Version.</summary>
    internal const string Fallback = "0.3.0";

    private static string? current;

    internal static string Current
    {
        get
        {
            if (current == null)
            {
                current = ReadManifestVersion() ?? Fallback;
            }

            return current;
        }
    }

    internal static string? ReadManifestVersion()
    {
        try
        {
            var location = typeof(ExtensionVersion).Assembly.Location;
            if (string.IsNullOrEmpty(location))
            {
                return null;
            }

            var directory = Path.GetDirectoryName(location);
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            return ReadManifestVersion(Path.Combine(directory, "extension.vsixmanifest"));
        }
        catch
        {
            return null;
        }
    }

    internal static string? ReadManifestVersion(string manifestPath)
    {
        try
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024,
            };
            using (var reader = XmlReader.Create(manifestPath, settings))
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Identity")
                    {
                        var version = reader.GetAttribute("Version");
                        return string.IsNullOrWhiteSpace(version) ? null : version!.Trim();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is XmlException || ex is IOException || ex is UnauthorizedAccessException)
        {
            // Fall back to the compiled constant.
        }

        return null;
    }
}
