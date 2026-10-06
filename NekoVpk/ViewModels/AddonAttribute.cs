using Avalonia;
using System.Collections.Generic;
using System.IO;
using SteamDatabase.ValvePak;
using ReactiveUI;
using NekoVpk.Core;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace NekoVpk.ViewModels
{
    public enum AddonSource
    {
        Unknown,
        WorkShop,
        Local
    }

    public class NekoVariant : ReactiveObject
    {
        public string Id { get; }
        public string Name { get; }
        public string DisplayName => string.IsNullOrEmpty(Name) ? $"{Id}.neko7z" : $"{Id}.neko7z ({Name})";

        public NekoVariant(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    public class AddonAttribute : ViewModelBase
    {
        public static HashSet<AddonAttribute> dirty = [];

        protected AddonInfo AddonInfo;

        protected bool? _Enabled;

        public bool? Enable
        {
            get => _Enabled;
            set
            {
                if (!string.IsNullOrEmpty(SubFolder)) return;

                if (_Enabled != value)
                {
                    _Enabled = value;
                    this.RaisePropertyChanged(nameof(Enable));
                    dirty.Add(this);
                }
            }
        }

        public HashSet<string> ModifiedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        private bool _hasConflict;
        public bool HasConflict
        {
            get => _hasConflict;
            set 
            {
                this.RaiseAndSetIfChanged(ref _hasConflict, value);
                this.RaisePropertyChanged(nameof(ConflictBackgroundBrush));
                this.RaisePropertyChanged(nameof(ConflictState));
            }
        }

        public string ConflictState => HasConflict ? "Conflict" : "Normal";

        public bool IsWorkshopSearchResult { get; set; }

        public string ConflictBackgroundBrush => HasConflict ? "#1AFF0000" : (IsInstalled && IsWorkshopSearchResult ? "#1A00FF00" : "Transparent");

        private bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                this.RaiseAndSetIfChanged(ref _isInstalled, value);
                this.RaisePropertyChanged(nameof(ConflictBackgroundBrush));
                this.RaisePropertyChanged(nameof(ShowDownloadButton));
            }
        }

        private bool _isDownloadingNow;
        public bool IsDownloadingNow
        {
            get => _isDownloadingNow;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDownloadingNow, value);
                this.RaisePropertyChanged(nameof(ShowDownloadButton));
            }
        }

        public bool ShowDownloadButton => !IsInstalled && !IsDownloadingNow;

        public string FileName { get; }
        public AddonSource Source { get; }

        public string Title { get => SanitizeSingleLine(AddonInfo.Title) ?? FileName; }

        private static string? SanitizeSingleLine(string? s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var parts = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts).Trim();
        }

        public string? Version { get => AddonInfo.Version; }
        
        public string? Author 
        { 
            get => AddonInfo.Author; 
        }

        public string? AddonInfo_Author => AddonInfo.Author;
        public string? AddonInfo_Link => AddonInfo.Link;
        public string? AddonInfo_Date => AddonInfo.Date;

        public bool IsWorkshopManaged => Source == AddonSource.WorkShop;

        public string? Description { get => AddonInfo.Description; }
        public string? Url
        {
            get
            {
                if (!string.IsNullOrEmpty(WorkShopID))
                {
                    return @"https://steamcommunity.com/sharedfiles/filedetails/?id=" + WorkShopID;
                }
                return AddonInfo.Url0;
            }
        }

        public string? PreviewUrl => AddonInfo.Url0;

        public string Stars { get; set; } = "";
        public bool HasStars => !string.IsNullOrEmpty(Stars);

        private Avalonia.Media.Imaging.Bitmap? _previewBitmap;
        public Avalonia.Media.Imaging.Bitmap? PreviewBitmap
        {
            get => _previewBitmap;
            set => this.RaiseAndSetIfChanged(ref _previewBitmap, value);
        }

        public void LoadImage(string? localVpkPath = null)
        {
            if (_previewBitmap != null) return;
            if (string.IsNullOrEmpty(PreviewUrl) && string.IsNullOrEmpty(localVpkPath)) return;

            Task.Run(async () =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(localVpkPath) && TryLoadFromLocalVpk(localVpkPath))
                    {
                        return;
                    }

                    if (string.IsNullOrEmpty(PreviewUrl)) return;

                    var bytes = await ImageHttp.GetBytesAsync(PreviewUrl);
                    using var ms = new MemoryStream(bytes);
                    var bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToHeight(ms, 220);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => PreviewBitmap = bitmap);
                }
                catch { }
            });
        }

        private bool TryLoadFromLocalVpk(string vpkPath)
        {
            try
            {
                string jpgPath = Path.ChangeExtension(vpkPath, ".jpg");
                if (File.Exists(jpgPath))
                {
                    using var stream = File.OpenRead(jpgPath);
                    var bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToHeight(stream, 220);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => PreviewBitmap = bitmap);
                    return true;
                }

                if (File.Exists(vpkPath))
                {
                    using var pak = new Package();
                    pak.Read(vpkPath);
                    var entry = pak.FindEntry("addonimage.jpg");
                    if (entry != null)
                    {
                        pak.ReadEntry(entry, out byte[] imageBytes);
                        using var ms = new MemoryStream(imageBytes);
                        var bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToHeight(ms, 220);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => PreviewBitmap = bitmap);
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        public void LoadLocalPreviewImage(string imagePath)
        {
            if (_previewBitmap != null) return;
            if (!File.Exists(imagePath)) return;

            Task.Run(() =>
            {
                try
                {
                    using var stream = File.OpenRead(imagePath);
                    var bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToHeight(stream, 220);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => PreviewBitmap = bitmap);
                }
                catch { }
            });
        }

        public string? WorkShopID;

        public bool IsSubscribed => Source == AddonSource.WorkShop;

        public string TagsOrde
        {
            get
            {
                string result = "";

                foreach (var tag in Tags)
                {
                    result += tag.Name;
                }
                return result;
            }
        }

        public AssetTag[] Tags { get; set; } = [];

        public string CurrentActiveVariantId { get; set; } = "0";

        private List<NekoVariant> _variants = [];
        public List<NekoVariant> Variants
        {
            get => _variants;
            set
            {
                this.RaiseAndSetIfChanged(ref _variants, value);
                this.RaisePropertyChanged(nameof(HasMultipleVariants));
            }
        }

        private NekoVariant? _activeVariant;
        public NekoVariant? ActiveVariant
        {
            get => _activeVariant;
            set => this.RaiseAndSetIfChanged(ref _activeVariant, value);
        }

        public bool HasMultipleVariants => _variants != null && _variants.Count > 1;

        public DateTime ModificationTime { get; set; }

        DateTime _CreationTime;

        readonly string _Type;

        public string Type { get =>  _Type; }

        readonly string _TypeDisplay;

        public string TypeDisplay { get => _TypeDisplay; }

        public DateTime CreationTime { get => _CreationTime; 
            set {
                if (this.RaiseAndSetIfChanged(ref _CreationTime, value) == value)
                {
                    CreationTimeStr = value.ToString();
                    this.RaisePropertyChanged(nameof(CreationTimeStr));
                }
            } 
        }

        public string CreationTimeStr { get; set; } = string.Empty;

        public DateTime LastUpdate { get; set; }
        
        public string LastUpdateStr => LastUpdate > DateTime.MinValue ? LastUpdate.ToString("g") : "";

        public long FileSizeRaw { get; set; }
        
        public string FileSize
        {
            get
            {
                if (FileSizeRaw <= 0) return "";
                string[] sizes = ["B", "KB", "MB", "GB", "TB"];
                double len = FileSizeRaw;
                int order = 0;
                while (len >= 1024 && order < sizes.Length - 1)
                {
                    order++;
                    len /= 1024;
                }
                return $"{len:0.##} {sizes[order]}";
            }
        }

        public int Subscriptions { get; set; }

        public string SubscriptionsStr => Subscriptions.ToString();

        public AddonAttribute(bool? enable, string fileName, AddonSource source, AddonInfo addonInfo, string? types = null)
        {
            _Enabled = enable;
            FileName = fileName;
            Source = source;
            AddonInfo = addonInfo;
            _Type = types ?? string.Empty;
            _TypeDisplay = NekoVpk.Core.WorkshopTagTaxonomy.ToShortDisplay(_Type);
        }

        public void UpdateAuthorName(string? newName)
        {
            if (AddonInfo.Author != newName)
            {
                AddonInfo.Author = newName;
                this.RaisePropertyChanged(nameof(Author));
            }
        }

        public void UpdateDate(string? newDate)
        {
            if (AddonInfo.Date != newDate)
            {
                AddonInfo.Date = newDate;
                this.RaisePropertyChanged(nameof(AddonInfo_Date));
            }
        }

        public string? SubFolder { get; set; }

        public string GetAbsolutePath(string gameDir)
        {
            string path = Path.Join(gameDir, "addons");

            if (!string.IsNullOrEmpty(SubFolder))
            {
                path = Path.Join(path, SubFolder);
            }
            else if (Source == AddonSource.WorkShop && !IsWorkshopSearchResult)
            {
                path = Path.Join(path, "workshop");
            }

            path = Path.Join(path, FileName);
            return path;
        }

        public Package LoadPackage(string gameDir)
        {
            Package package = new();
            package.Read(GetAbsolutePath(gameDir));
            return package;
        }

        public void ScanContent(Package pak)
        {

        }
    }
}