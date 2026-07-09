using Avalonia;
using System.Collections.Generic;
using System.IO;
using SteamDatabase.ValvePak;
using ReactiveUI;
using NekoVpk.Core;
using System;

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
        public static List<AddonAttribute> dirty = [];

        protected AddonInfo AddonInfo;

        protected bool? _Enabled;

        public bool? Enable
        {
            get => _Enabled;
            set
            {
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

        public string ConflictBackgroundBrush => HasConflict ? "#1AFF0000" : "Transparent";

        private bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set => this.RaiseAndSetIfChanged(ref _isInstalled, value);
        }

        public string FileName { get; }
        public AddonSource Source { get; }

        public string Title { get => AddonInfo.Title ?? FileName; }

        public string? Version { get => AddonInfo.Version; }
        
        public string? Author 
        { 
            get => AddonInfo.Author; 
        }

        public string? AddonInfo_Author => AddonInfo.Author;

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

        public AddonAttribute(bool? enable, string fileName, AddonSource source, AddonInfo addonInfo, string? types = null)
        {
            _Enabled = enable;
            FileName = fileName;
            Source = source;
            AddonInfo = addonInfo;
            _Type = types ?? string.Empty;
        }

        public void UpdateAuthorName(string? newName)
        {
            if (AddonInfo.Author != newName)
            {
                AddonInfo.Author = newName;
                this.RaisePropertyChanged(nameof(Author));
            }
        }

        public string GetAbsolutePath(string gameDir)
        {
            string path = Path.Join(gameDir, "addons");

            if (Source == AddonSource.WorkShop)
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