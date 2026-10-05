using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NekoVpk.Core;

public static class AddonFolders
{
    private static readonly string[] SourceModKeywords = ["sourcemod", "metamod"];

    private static readonly string[] SourceModExactNames = ["stripper", "l4dtoolz"];

    private static readonly HashSet<string> StorageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vpk",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp",
        ".nekobak",
    };

    public static bool IsSourceModRelated(string folderName)
    {
        foreach (var keyword in SourceModKeywords)
        {
            if (folderName.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return SourceModExactNames.Any(n => n.Equals(folderName, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsReservedName(string folderName)
        => folderName.Equals("workshop", StringComparison.OrdinalIgnoreCase)
           || IsSourceModRelated(folderName);

    public static List<DirectoryInfo> GetVisibleFolders(DirectoryInfo addonDir)
    {
        try
        {
            return addonDir.EnumerateDirectories()
                .Where(d => !IsReservedName(d.Name))
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static readonly HashSet<string> IgnoredStorageEntries = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini", "thumbs.db", ".ds_store",
    };

    public static bool IsStorageFolder(DirectoryInfo dir)
    {
        try
        {
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (IgnoredStorageEntries.Contains(entry.Name)) continue;
                if (entry is DirectoryInfo) return false;
                if (!StorageExtensions.Contains(entry.Extension)) return false;
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "FolderNameEmpty";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.')) return "FolderNameInvalid";
        if (IsReservedName(name)) return "FolderNameReserved";
        return null;
    }

    private static readonly HashSet<string> IconImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ico", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp",
    };

    public static string? FindCustomIconPath(string folderPath)
    {
        try
        {
            string ini = Path.Combine(folderPath, "desktop.ini");
            if (!File.Exists(ini)) return null;

            string? iconResource = null;
            string? iconFile = null;
            bool inSection = false;

            foreach (var rawLine in ReadIniText(ini).Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == ';') continue;

                if (line[0] == '[')
                {
                    inSection = line.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();

                if (key.Equals("IconResource", StringComparison.OrdinalIgnoreCase)) iconResource = value;
                else if (key.Equals("IconFile", StringComparison.OrdinalIgnoreCase)) iconFile = value;
            }

            foreach (var candidate in new[] { iconResource, iconFile })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;

                string path = candidate.Trim().Trim('"');

                int comma = path.LastIndexOf(',');
                if (comma > 0 && int.TryParse(path[(comma + 1)..].Trim(), out _))
                {
                    path = path[..comma].Trim().Trim('"');
                }

                path = Environment.ExpandEnvironmentVariables(path);
                if (!Path.IsPathRooted(path)) path = Path.Combine(folderPath, path);

                if (IconImageExtensions.Contains(Path.GetExtension(path)) && File.Exists(path)) return path;
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    private static string ReadIniText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (Exception)
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
            }
            catch (Exception)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }
    }

    public static long GetVpkSize(string folderPath)
    {
        long total = 0;
        try
        {
            foreach (var vpk in new DirectoryInfo(folderPath).GetFiles("*.vpk"))
            {
                total += LengthOrZero(vpk.FullName);
                total += LengthOrZero(Path.ChangeExtension(vpk.FullName, ".jpg"));
                total += LengthOrZero(vpk.FullName + ".nekobak");
            }
        }
        catch (Exception)
        {
        }
        return total;
    }

    private static long LengthOrZero(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public static void ClearReadOnlyRecursive(string folderPath)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
        try
        {
            foreach (var entry in new DirectoryInfo(folderPath).EnumerateFileSystemInfos("*", options))
            {
                ClearReadOnly(entry);
            }
        }
        catch (Exception)
        {
        }
        ClearReadOnly(new DirectoryInfo(folderPath));
    }

    private static void ClearReadOnly(FileSystemInfo entry)
    {
        try
        {
            if ((entry.Attributes & FileAttributes.ReadOnly) != 0)
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }
        }
        catch (Exception)
        {
        }
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < units.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {units[order]}";
    }

    public static List<int> PickFolderIndices(IReadOnlyList<bool> isFolder, Func<int, bool> isSelected, Func<bool, bool> keep)
    {
        List<int> result = [];
        for (int i = 0; i < isFolder.Count; i++)
        {
            if (isFolder[i] && keep(isSelected(i))) result.Add(i);
        }
        return result;
    }
}
