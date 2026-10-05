using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace NekoVpk.Core;

public static class TempCache
{
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "NekoVpk_cache");
    private static readonly object _lock = new();
    private static readonly HashSet<string> _usedFiles = new(StringComparer.OrdinalIgnoreCase);

    public static string GetGifPath(string url)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        string name = Convert.ToHexString(hash, 0, 16) + ".gif";

        Directory.CreateDirectory(CacheDir);
        string path = Path.Combine(CacheDir, name);
        lock (_lock) { _usedFiles.Add(path); }
        return path;
    }

    public static void WriteFile(string path, byte[] bytes)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            try
            {
                File.Move(tmp, path, true);
            }
            catch (Exception) when (File.Exists(path))
            {
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    public static void SweepOld()
    {
        try
        {
            var threshold = DateTime.UtcNow - TimeSpan.FromDays(1);
            if (Directory.Exists(CacheDir))
            {
                foreach (var f in Directory.EnumerateFiles(CacheDir))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(f) < threshold) File.Delete(f);
                    }
                    catch { }
                }
            }

            string temp = Path.GetTempPath();
            foreach (var pattern in new[] { "nekovpk_cache_*.gif", "nekovpk_bbcode_*.gif" })
            {
                foreach (var f in Directory.EnumerateFiles(temp, pattern))
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch { }
    }

    public static void CleanupSession()
    {
        string[] files;
        lock (_lock) { files = new List<string>(_usedFiles).ToArray(); _usedFiles.Clear(); }

        foreach (var f in files)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }

        try
        {
            if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, false);
        }
        catch { }
    }
}
