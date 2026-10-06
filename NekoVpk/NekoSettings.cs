using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NekoVpk
{
    internal sealed class NekoSettings : INotifyPropertyChanged
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NekoVpk");
        public static readonly string FilePath = Path.Combine(Dir, "settings.json");
        private static readonly string BackupPath = FilePath + ".bak";

        public static NekoSettings Default { get; } = new NekoSettings();

        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly object _lock = new();
        private JObject _data = new();
        private Timer? _saveTimer;
        private bool _backedUp;

        private NekoSettings()
        {
            bool needSave = false;
            JObject? loaded = null;

            try
            {
                if (File.Exists(FilePath))
                {
                    loaded = TryRead(FilePath);
                    if (loaded == null)
                    {
                        Quarantine(FilePath);
                        loaded = TryRead(BackupPath);
                        needSave = true;
                    }
                }
                else
                {
                    needSave = true;
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }

            _data = loaded ?? new JObject();

            var before = (JObject)_data.DeepClone();
            Normalize();
            if (needSave || !JToken.DeepEquals(before, _data))
                Save();
        }

        public string GameDir
        {
            get => Get("");
            set => Set(value ?? "");
        }

        public short CompressionLevel
        {
            get => Clamp(Get((short)3), (short)1, (short)5);
            set => Set(Clamp(value, (short)1, (short)5));
        }

        public bool IgnoreVpkErrors { get => Get(false); set => Set(value); }
        public bool SkipVariantBackup { get => Get(false); set => Set(value); }

        public string BackgroundImagePath
        {
            get => Get("");
            set => Set(value ?? "");
        }

        public double BackgroundBrightness
        {
            get => Clamp(Get(50.0), 0.0, 100.0);
            set => Set(Clamp(value, 0.0, 100.0));
        }

        public string ThemeColor
        {
            get => Get("");
            set => Set(value ?? "");
        }

        public string BackgroundStretch
        {
            get => OneOf(Get("UniformToFill"), "UniformToFill", "UniformToFill", "Uniform", "Fill", "None");
            set => Set(OneOf(value, "UniformToFill", "UniformToFill", "Uniform", "Fill", "None"));
        }

        [Secret]
        public string SteamApiKey
        {
            get => GetSecret();
            set => SetSecret(value);
        }

        public bool ClearSearchAfterDownload { get => Get(false); set => Set(value); }

        public string UserFont
        {
            get
            {
                var f = Get("Microsoft YaHei UI");
                return string.IsNullOrWhiteSpace(f) ? "Microsoft YaHei UI" : f;
            }
            set => Set(string.IsNullOrWhiteSpace(value) ? "Microsoft YaHei UI" : value);
        }

        public double UserFontSize
        {
            get => Clamp(Get(14.0), 5.0, 50.0);
            set => Set(Clamp(value, 5.0, 50.0));
        }

        public bool SaveColumnWidths { get => Get(true); set => Set(value); }

        public string DataGridColumnWidths
        {
            get => Get("");
            set => Set(value ?? "");
        }

        public string Language
        {
            get => OneOf(Get("Auto"), "Auto", "Auto", "zh-CN", "en-US", "ja-JP");
            set => Set(OneOf(value, "Auto", "Auto", "zh-CN", "en-US", "ja-JP"));
        }

        public bool EnableConflictDetection { get => Get(true); set => Set(value); }
        public bool AutoSizeColumnsOnSearch { get => Get(false); set => Set(value); }
        public bool ShowColumnTag { get => Get(true); set => Set(value); }
        public bool ShowColumnType { get => Get(true); set => Set(value); }
        public bool ShowColumnAddedTime { get => Get(true); set => Set(value); }
        public bool ShowColumnSize { get => Get(true); set => Set(value); }

        public void Save()
        {
            lock (_lock)
            {
                _saveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                try
                {
                    Directory.CreateDirectory(Dir);
                    string json = _data.ToString(Formatting.Indented);
                    string tmp = FilePath + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(false));

                    if (!_backedUp && File.Exists(FilePath))
                    {
                        File.Copy(FilePath, BackupPath, true);
                        _backedUp = true;
                    }

                    File.Move(tmp, FilePath, true);
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        public void Reset()
        {
            lock (_lock) { _data = new JObject(); }
            Normalize();
            Save();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        private T Get<T>(T def, [CallerMemberName] string name = "")
        {
            lock (_lock)
            {
                if (_data.TryGetValue(name, out var tok) && tok != null && tok.Type != JTokenType.Null)
                {
                    try
                    {
                        var v = tok.ToObject<T>();
                        if (v is double d && !double.IsFinite(d)) return def;
                        if (v != null) return v;
                    }
                    catch
                    {
                        // 类型不对/溢出:退回默认值
                    }
                }
                return def;
            }
        }

        private void Set<T>(T value, [CallerMemberName] string name = "")
        {
            bool changed;
            lock (_lock)
            {
                JToken tok = value == null ? JValue.CreateNull() : JToken.FromObject(value);
                changed = !_data.TryGetValue(name, out var old) || !JToken.DeepEquals(old, tok);
                if (changed) _data[name] = tok;
            }

            if (!changed) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            ScheduleSave();
        }

        private const string SecretPrefix = "dpapi:";

        [AttributeUsage(AttributeTargets.Property)]
        private sealed class SecretAttribute : Attribute { }

        private string GetSecret([CallerMemberName] string name = "")
        {
            string raw;
            lock (_lock)
            {
                raw = _data.TryGetValue(name, out var tok) && tok.Type == JTokenType.String
                    ? (string)tok! : "";
            }
            return Unprotect(raw);
        }

        private void SetSecret(string? value, [CallerMemberName] string name = "")
        {
            value = (value ?? "").Trim();
            if (GetSecret(name) == value) return;

            lock (_lock) { _data[name] = Protect(value); }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            ScheduleSave();
        }

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            if (!OperatingSystem.IsWindows()) return plain;
            try
            {
                byte[] cipher = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
                return SecretPrefix + Convert.ToBase64String(cipher);
            }
            catch (Exception ex)
            {
                Log(ex);
                return plain;
            }
        }

        private static string Unprotect(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            if (!raw.StartsWith(SecretPrefix, StringComparison.Ordinal)) return raw;
            if (!OperatingSystem.IsWindows()) return "";
            try
            {
                byte[] plain = ProtectedData.Unprotect(
                    Convert.FromBase64String(raw.Substring(SecretPrefix.Length)),
                    null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch
            {
                return "";
            }
        }

        private void ScheduleSave()
        {
            lock (_lock)
            {
                _saveTimer ??= new Timer(_ => Save(), null, Timeout.Infinite, Timeout.Infinite);
                _saveTimer.Change(800, Timeout.Infinite);
            }
        }

        private void Normalize()
        {
            var props = typeof(NekoSettings).GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var p in props)
            {
                if (!p.CanRead || !p.CanWrite) continue;
                try
                {
                    if (p.GetCustomAttribute<SecretAttribute>() != null)
                    {
                        lock (_lock)
                        {
                            string raw = _data.TryGetValue(p.Name, out var t) && t.Type == JTokenType.String
                                ? (string)t! : "";
                            if (!raw.StartsWith(SecretPrefix, StringComparison.Ordinal))
                                _data[p.Name] = Protect(raw.Trim());
                        }
                        continue;
                    }

                    var v = p.GetValue(this);
                    lock (_lock)
                    {
                        _data[p.Name] = v == null ? JValue.CreateNull() : JToken.FromObject(v);
                    }
                }
                catch (Exception ex)
                {
                    Log(ex);
                }
            }
        }

        private static JObject? TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return null;
                return JObject.Parse(text);
            }
            catch
            {
                return null;
            }
        }

        private static void Quarantine(string path)
        {
            try
            {
                string dst = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Move(path, dst, true);
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }

        private static T Clamp<T>(T v, T min, T max) where T : IComparable<T>
            => v.CompareTo(min) < 0 ? min : (v.CompareTo(max) > 0 ? max : v);

        private static string OneOf(string? v, string fallback, params string[] allowed)
            => v != null && allowed.Contains(v) ? v : fallback;

        private static void Log(Exception ex)
        {
            try { App.Logger.Warn(ex, "NekoSettings"); } catch { /* 日志本身出错就算了 */ }
        }
    }
}
