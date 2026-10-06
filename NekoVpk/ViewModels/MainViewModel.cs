using NekoVpk.Core;
using NekoVpk.Views;
using SteamDatabase.ValvePak;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using Narod.SteamGameFinder;
using ReactiveUI;
using Avalonia.Collections;
using SevenZip;
using System.Diagnostics;
using DotNet.Globbing;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace NekoVpk.ViewModels;

public class SelectableTag(string name, string? display = null) : ReactiveObject
{
    public string Name { get; set; } = name;
    public string Display { get; set; } = display ?? name;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

public class TagCategory
{
    public SelectableTag MainTag { get; set; } 
    public List<SelectableTag> Tags { get; set; } = [];

    public TagCategory(string header, params string[] tags)
    {
        MainTag = new SelectableTag(header); 
        
        foreach (var t in tags)
        {
            Tags.Add(new SelectableTag(t)); 
        }
    }
}

public class AddonFolderItem : ReactiveObject
{
    public string Name { get; init; } = "";
    public int ItemCount { get; init; }
    public bool IsAddTile { get; init; }
    public bool IsFolder => !IsAddTile;
    public string ItemCountText => IsAddTile
        ? ""
        : string.Format(NekoVpk.Lang.I18nManager.Instance["FolderItemCount"], ItemCount);

    private bool _isNaming;
    public bool IsNaming
    {
        get => _isNaming;
        set => this.RaiseAndSetIfChanged(ref _isNaming, value);
    }
}

public class FolderDetailInfo
{
    public string Title { get; init; } = "";
    public bool IsSingle { get; init; }
    public string ModifiedText { get; init; } = "";
    public string SizeText { get; init; } = "";
    public string VpkCountText { get; init; } = "";

    public Avalonia.Media.Imaging.Bitmap? Icon { get; init; }

    public bool HasIcon => Icon != null;
    public string TypeText => NekoVpk.Lang.I18nManager.Instance["FolderTypeText"];
}

public partial class MainViewModel : ViewModelBase
{
    private readonly NekoVpk.Lang.I18nManager i18n = NekoVpk.Lang.I18nManager.Instance;


    public FontFamily UserFont
    {
        get
        {
            var fontName = NekoSettings.Default.UserFont;
            if (string.IsNullOrWhiteSpace(fontName))
            {
                return FontFamily.Default;
            }
            return new FontFamily(fontName);
        }
    }
    
    public double UserFontSize => NekoSettings.Default.UserFontSize;
    
    private static readonly HttpClient _httpClient = new HttpClient();
    private const int GameAppId = 550;
    private CancellationTokenSource? _downloadCts;
    private CancellationTokenSource? _searchCts;
    private int _searchRequestToken;
    private bool _isSearching;
    private int _collectionLoadToken;
    private volatile bool _cancelRequested;

    private HashSet<string> _localWorkshopIds = [];
    private Dictionary<string, string> _localWorkshopFilePaths = new(StringComparer.OrdinalIgnoreCase);

    private string? GetLocalVpkPath(string? workshopId)
        => !string.IsNullOrEmpty(workshopId) && _localWorkshopFilePaths.TryGetValue(workshopId, out var path) ? path : null;

    private string? FindInstalledWorkshopPath(string? workshopId)
    {
        if (string.IsNullOrEmpty(workshopId)) return null;

        if (_localWorkshopFilePaths.TryGetValue(workshopId, out var known) && File.Exists(known))
            return known;

        if (!string.IsNullOrWhiteSpace(GameDir))
        {
            string[] candidates =
            [
                Path.Join(GameDir, "addons", "workshop", workshopId + ".vpk"),
                Path.Join(GameDir, "addons", workshopId + ".vpk"),
            ];
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                _localWorkshopIds.Add(workshopId);
                _localWorkshopFilePaths[workshopId] = candidate;
                return candidate;
            }
        }

        _localWorkshopIds.Remove(workshopId);
        _localWorkshopFilePaths.Remove(workshopId);
        return null;
    }

    private bool IsWorkshopIdInstalled(string? workshopId) => FindInstalledWorkshopPath(workshopId) != null;

    public string GameDir 
    {
        get => NekoSettings.Default.GameDir;
        set {
            if (NekoSettings.Default.GameDir != value)
            {
                NekoSettings.Default.GameDir = value;
                this.RaisePropertyChanged(nameof(GameDir));
            }
        }
    }

    private List<AddonAttribute> _addonList = [];

    private DataGridCollectionView _Addons;

    public DataGridCollectionView Addons => _Addons;

    string? _SearchKeywords = "";

    public string? SearchKeywords { get => _SearchKeywords; set => this.RaiseAndSetIfChanged(ref _SearchKeywords, value); }

    public int[] ItemsPerPageOptions { get; } = [15, 30, 50, 100];

    private int _itemsPerPage = 30;
    public int ItemsPerPage
    {
        get => _itemsPerPage;
        set
        {
            bool changed = _itemsPerPage != value;
            this.RaiseAndSetIfChanged(ref _itemsPerPage, value);
            if (changed && IsOnlineMode && !IsDownloading && !IsInCollectionDetail)
                _ = SearchWorkshopAsync();
        }
    }

    private int _currentPage;
    public int CurrentPage
    {
        get => _currentPage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentPage, value);
            this.RaisePropertyChanged(nameof(CurrentPageDisplay));
            this.RaisePropertyChanged(nameof(CanGoPrevPage));
        }
    }

    public string CurrentPageDisplay => (CurrentPage + 1).ToString();

    public bool CanGoPrevPage => CurrentPage > 0;

    public bool ShowPagingControls => IsOnlineMode && !IsInCollectionDetail;

    private bool _hasMoreResults;
    public bool HasMoreResults
    {
        get => _hasMoreResults;
        private set => this.RaiseAndSetIfChanged(ref _hasMoreResults, value);
    }

    public async Task GoToPrevPageAsync()
    {
        if (!IsOnlineMode || IsDownloading || _isSearching || !CanGoPrevPage) return;
        CurrentPage--;
        await SearchWorkshopAsync(resetPage: false);
    }

    public async Task GoToNextPageAsync()
    {
        if (!IsOnlineMode || IsDownloading || _isSearching || !HasMoreResults) return;
        CurrentPage++;
        await SearchWorkshopAsync(resetPage: false);
    }

    public ObservableCollection<SteamSortOption> SortOptions { get; } = [];
    
    private SteamSortOption? _selectedSortOption;
    public SteamSortOption? SelectedSortOption
    {
        get => _selectedSortOption;
        set 
        { 
            bool changed = !ReferenceEquals(_selectedSortOption, value);
            this.RaiseAndSetIfChanged(ref _selectedSortOption, value);
            if (changed && IsOnlineMode && !IsDownloading && !IsInCollectionDetail) 
                _ = SearchWorkshopAsync(); 
        }
    }

    private void InitSortOptions()
    {
        SortOptions.Clear();
        SortOptions.Add(new SteamSortOption { NameKey = "SortTrend", QueryType = 3 });
        SortOptions.Add(new SteamSortOption { NameKey = "SortTopRated", QueryType = 0 });
        SortOptions.Add(new SteamSortOption { NameKey = "SortRecent", QueryType = 1 });
        SortOptions.Add(new SteamSortOption { NameKey = "SortUpdated", QueryType = 21 });
        
        _selectedSortOption = SortOptions[0];
    }

    public ObservableCollection<TagCategory> WorkshopTagCategories { get; } = [];

    private ObservableCollection<SteamCollectionItem> _collectionList = [];
    public ObservableCollection<SteamCollectionItem> CollectionList => _collectionList;

    public bool ShowDataGrid => (!IsCollectionMode || IsInCollectionDetail) && !ShowAddonGrid && !ShowFolderGrid;
    public bool ShowCollectionList => IsCollectionMode && !IsInCollectionDetail;

    private bool _isGridView;
    public bool IsGridView
    {
        get => _isGridView;
        set
        {
            this.RaiseAndSetIfChanged(ref _isGridView, value);
            this.RaisePropertyChanged(nameof(ShowDataGrid));
            this.RaisePropertyChanged(nameof(ShowAddonGrid));
            this.RaisePropertyChanged(nameof(ShowAddonDetail));
        }
    }

    public bool ShowAddonGrid => IsOnlineMode && IsGridView && (!IsCollectionMode || IsInCollectionDetail);

    public bool ShowAddonDetail => ShowDataGrid || ShowAddonGrid;

    private bool _isFolderMode;
    public bool IsFolderMode
    {
        get => _isFolderMode;
        set
        {
            if (_isFolderMode == value) return;
            this.RaiseAndSetIfChanged(ref _isFolderMode, value);
            _currentFolder = null;
            ClearFolderDetail();
            NotifyFolderStateChanged();
            if (!IsOnlineMode) LoadAddons();
        }
    }

    private string? _currentFolder;
    public string? CurrentFolder => _currentFolder;

    public bool ShowFolderGrid => IsFolderMode && !IsOnlineMode && _currentFolder == null;

    public bool IsInsideFolder => IsFolderMode && !IsOnlineMode && _currentFolder != null;

    public bool ShowEnableControls => !IsOnlineMode && !IsFolderMode;

    public bool ShowSelectAllButton => ShowEnableControls || ShowFolderGrid;

    public ObservableCollection<AddonFolderItem> FolderItems { get; } = [];

    private FolderDetailInfo? _folderDetail;
    public FolderDetailInfo? FolderDetail
    {
        get => _folderDetail;
        private set
        {
            this.RaiseAndSetIfChanged(ref _folderDetail, value);
            this.RaisePropertyChanged(nameof(ShowFolderDetail));
        }
    }

    public bool ShowFolderDetail => ShowFolderGrid && _folderDetail != null;

    public string AddonsTargetDir => IsInsideFolder
        ? Path.Join(GameDir, "addons", _currentFolder!)
        : Path.Join(GameDir, "addons");

    private void NotifyFolderStateChanged()
    {
        this.RaisePropertyChanged(nameof(CurrentFolder));
        this.RaisePropertyChanged(nameof(ShowEnableControls));
        this.RaisePropertyChanged(nameof(ShowSelectAllButton));
        this.RaisePropertyChanged(nameof(ShowFolderGrid));
        this.RaisePropertyChanged(nameof(ShowFolderDetail));
        this.RaisePropertyChanged(nameof(IsInsideFolder));
        this.RaisePropertyChanged(nameof(ShowDataGrid));
        this.RaisePropertyChanged(nameof(ShowAddonDetail));
    }

    private AddonAttribute? _selectedAddon;
    public AddonAttribute? SelectedAddon
    {
        get => _selectedAddon;
        set => this.RaiseAndSetIfChanged(ref _selectedAddon, value);
    }

    private bool _isCollectionMode;
    public bool IsCollectionMode
    {
        get => _isCollectionMode;
        set
        {
            bool changed = _isCollectionMode != value;
            this.RaiseAndSetIfChanged(ref _isCollectionMode, value);
            if (value) IsInCollectionDetail = false;
            this.RaisePropertyChanged(nameof(ShowDataGrid));
            this.RaisePropertyChanged(nameof(ShowCollectionList));
            this.RaisePropertyChanged(nameof(ShowAddonGrid));
            this.RaisePropertyChanged(nameof(ShowAddonDetail));
            if (changed && IsOnlineMode && !IsDownloading) _ = SearchWorkshopAsync();
        }
    }

    private bool _isInCollectionDetail;
    public bool IsInCollectionDetail
    {
        get => _isInCollectionDetail;
        set
        {
            if (!value) _collectionLoadToken++;
            this.RaiseAndSetIfChanged(ref _isInCollectionDetail, value);
            this.RaisePropertyChanged(nameof(ShowDataGrid));
            this.RaisePropertyChanged(nameof(ShowCollectionList));
            this.RaisePropertyChanged(nameof(ShowAddonGrid));
            this.RaisePropertyChanged(nameof(ShowAddonDetail));
            this.RaisePropertyChanged(nameof(ShowPagingControls));
        }
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set => this.RaiseAndSetIfChanged(ref _isDownloading, value);
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
    }

    private bool _isOnlineMode;
    public bool IsOnlineMode
    {
        get => _isOnlineMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _isOnlineMode, value);
            ToggleOnlineMode();
            this.RaisePropertyChanged(nameof(IsTagColumnVisible));
            this.RaisePropertyChanged(nameof(IsAddedTimeColumnVisible));
            this.RaisePropertyChanged(nameof(ShowDataGrid));
            this.RaisePropertyChanged(nameof(ShowAddonGrid));
            this.RaisePropertyChanged(nameof(ShowAddonDetail));
            this.RaisePropertyChanged(nameof(ShowPagingControls));
            this.RaisePropertyChanged(nameof(ShowFolderGrid));
            this.RaisePropertyChanged(nameof(ShowFolderDetail));
            this.RaisePropertyChanged(nameof(IsInsideFolder));
            this.RaisePropertyChanged(nameof(ShowEnableControls));
            this.RaisePropertyChanged(nameof(ShowSelectAllButton));
        }
    }

    private string _searchWatermark = "Search";

    private bool _showColumnTag = true;
    public bool ShowColumnTag 
    { 
        get => _showColumnTag; 
        set 
        { 
            this.RaiseAndSetIfChanged(ref _showColumnTag, value); 
            this.RaisePropertyChanged(nameof(IsTagColumnVisible)); 
            if (NekoSettings.Default.ShowColumnTag != value)
            {
                NekoSettings.Default.ShowColumnTag = value;
                NekoSettings.Default.Save();
            }
        } 
    }

    private bool _showColumnType = true;
    public bool ShowColumnType 
    { 
        get => _showColumnType; 
        set 
        { 
            this.RaiseAndSetIfChanged(ref _showColumnType, value); 
            this.RaisePropertyChanged(nameof(IsTypeColumnVisible)); 
            if (NekoSettings.Default.ShowColumnType != value)
            {
                NekoSettings.Default.ShowColumnType = value;
                NekoSettings.Default.Save();
            }
        } 
    }

    private bool _showColumnAddedTime = true;
    public bool ShowColumnAddedTime 
    { 
        get => _showColumnAddedTime; 
        set 
        { 
            this.RaiseAndSetIfChanged(ref _showColumnAddedTime, value); 
            this.RaisePropertyChanged(nameof(IsAddedTimeColumnVisible)); 
            if (NekoSettings.Default.ShowColumnAddedTime != value)
            {
                NekoSettings.Default.ShowColumnAddedTime = value;
                NekoSettings.Default.Save();
            }
        } 
    }

    private bool _showColumnSize = true;
    public bool ShowColumnSize 
    { 
        get => _showColumnSize; 
        set 
        { 
            this.RaiseAndSetIfChanged(ref _showColumnSize, value); 
            this.RaisePropertyChanged(nameof(IsSizeColumnVisible)); 
            if (NekoSettings.Default.ShowColumnSize != value)
            {
                NekoSettings.Default.ShowColumnSize = value;
                NekoSettings.Default.Save();
            }
        } 
    }

    public bool IsTagColumnVisible => ShowColumnTag && !IsOnlineMode;
    public bool IsAddedTimeColumnVisible => ShowColumnAddedTime && !IsOnlineMode;
    public bool IsTypeColumnVisible => ShowColumnType;
    public bool IsSizeColumnVisible => ShowColumnSize;
    public string SearchWatermark
    {
        get => _searchWatermark;
        set => this.RaiseAndSetIfChanged(ref _searchWatermark, value);
    }

    private bool _showNotImplemented;
    public bool ShowNotImplemented
    {
        get => _showNotImplemented;
        set => this.RaiseAndSetIfChanged(ref _showNotImplemented, value);
    }

    private async Task<ButtonResult> ShowCustomMessageBoxAsync(string title, string message, ButtonEnum buttons, MsBox.Avalonia.Enums.Icon icon = MsBox.Avalonia.Enums.Icon.None)
    {
        var box = new CustomMessageBox(title, message, buttons, icon);
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is not null)
        {
            return await box.ShowDialog<ButtonResult>(desktop.MainWindow);
        }
        return ButtonResult.None;
    }

    public async Task<bool> EnsureSteamApiKeyAsync()
    {
        if (!string.IsNullOrWhiteSpace(NekoSettings.Default.SteamApiKey)) return true;

        await ShowCustomMessageBoxAsync(
            i18n["MissingKeyTitle"],
            i18n["MissingKeyMsg"],
            ButtonEnum.Ok,
            MsBox.Avalonia.Enums.Icon.Warning);
        return false;
    }

    private async void ToggleOnlineMode()
    {
        _searchCts?.Cancel();
        _searchRequestToken++;
        _collectionLoadToken++;
        _isSearching = false;

        if (NekoSettings.Default.ClearSearchAfterDownload)
        {
            SearchKeywords = string.Empty;
        }

        if (IsOnlineMode)
        {
            if (!await EnsureSteamApiKeyAsync())
            {
                IsOnlineMode = false;
                return;
            }

            SearchWatermark = i18n["SearchWorkshopWatermark"];
            IsCollectionMode = false;
            IsInCollectionDetail = false;
            _addonList.Clear();
            _collectionList.Clear();
            Addons.Refresh();
            ShowNotImplemented = false;
            CurrentPage = 0;
            HasMoreResults = false;
        }
        else
        {
            SearchWatermark = i18n["SearchLocalWatermark"];
            IsCollectionMode = false;
            IsInCollectionDetail = false;
            ShowNotImplemented = false;
            LoadAddons();
        }
    }

    private Bitmap? _backgroundImage;
    public Bitmap? BackgroundImage
    {
        get => _backgroundImage;
        set
        {
            this.RaiseAndSetIfChanged(ref _backgroundImage, value);
            this.RaisePropertyChanged(nameof(HasBackgroundImage));
        }
    }

    public bool HasBackgroundImage => _backgroundImage != null;

    public double BackgroundDimOpacity => 1.0 - (NekoSettings.Default.BackgroundBrightness / 100.0);

    public Stretch BackgroundStretch
    {
        get
        {
            string s = NekoSettings.Default.BackgroundStretch;
            if (Enum.TryParse<Stretch>(s, out var result))
            {
                return result;
            }
            return Stretch.UniformToFill;
        }
    }

    public MainViewModel()
    {
        NekoSettings.Default.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(NekoSettings.Default.UserFont))
            {
                Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(UserFont)));
            }
            else if (e.PropertyName == nameof(NekoSettings.Default.UserFontSize))
            {
                Dispatcher.UIThread.Post(() => this.RaisePropertyChanged(nameof(UserFontSize)));
            }
        };

        if (NekoSettings.Default.GameDir == "")
        {
            NekoSettings.Default.GameDir = TryToFindGameDir() ?? NekoSettings.Default.GameDir;
        }

        _showColumnTag = NekoSettings.Default.ShowColumnTag;
        _showColumnType = NekoSettings.Default.ShowColumnType;
        _showColumnAddedTime = NekoSettings.Default.ShowColumnAddedTime;
        _showColumnSize = NekoSettings.Default.ShowColumnSize;

        _Addons = new(_addonList) { Filter = AddonsFilter };

        InitSortOptions();
        InitTags();
        UpdateBackground(); 
    }

    private void InitTags()
    {
        WorkshopTagCategories.Clear();

        WorkshopTagCategories.Add(new TagCategory("Survivors", 
            "Bill", "Francis", "Louis", "Zoey", "Coach", "Ellis", "Nick", "Rochelle"));

        WorkshopTagCategories.Add(new TagCategory("Infected", 
            "Common Infected", "Special Infected", "Boomer", "Charger", "Hunter", "Jockey", "Smoker", "Spitter", "Tank", "Witch"));

        var gameContent = new TagCategory("Game Content");
        gameContent.Tags.AddRange([
            new SelectableTag("Campaign", "Campaigns"),
            new SelectableTag("Weapon", "Weapons"),
            new SelectableTag("Items", "Items"),
            new SelectableTag("Sounds", "Sounds"),
            new SelectableTag("Scripts", "Scripts"),
            new SelectableTag("UI", "UI"),
            new SelectableTag("Model", "Models"),  
            new SelectableTag("Texture", "Textures")
        ]);
        WorkshopTagCategories.Add(gameContent);

        WorkshopTagCategories.Add(new TagCategory("Game Modes", 
            "Co-op", "Versus", "Survival", "Realism"));
        WorkshopTagCategories.Add(new TagCategory("Weapons Detail", 
            "Melee", "Pistol", "Rifle", "Shotgun", "SMG", "Sniper", "Throwable"));
        WorkshopTagCategories.Add(new TagCategory("Items Detail", 
            "Adrenaline", "Defibrillator", "Medkit", "Pills"));

        foreach (var category in WorkshopTagCategories)
        {
            category.MainTag.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(SelectableTag.IsSelected) && IsOnlineMode && !IsDownloading)
                {
                    _ = SearchWorkshopAndNotifyAsync();
                }
            };

            foreach (var tag in category.Tags)
            {
                tag.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SelectableTag.IsSelected) && IsOnlineMode && !IsDownloading)
                    {
                        _ = SearchWorkshopAndNotifyAsync();
                    }
                };
            }
        }
    }

    public event EventHandler? SearchCompleted;

    private const int MinVotesForRating = 25;

    private static string ComputeStars(SteamVoteData? voteData)
    {
        if (voteData == null) return "";
        int totalVotes = voteData.VotesUp + voteData.VotesDown;
        if (totalVotes < MinVotesForRating) return "";

        int starCount = Math.Min(5, (int)Math.Floor(voteData.Score * 5) + 1);
        if (starCount < 1) starCount = 1;
        return new string('★', starCount) + new string('☆', 5 - starCount);
    }

    private static readonly System.Text.RegularExpressions.Regex _bbcodeStripRegex =
        new System.Text.RegularExpressions.Regex(@"\[[^\]]*\]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string BuildDescriptionPreview(string? rawDescription, int maxChars = 200)
    {
        if (string.IsNullOrWhiteSpace(rawDescription)) return "";

        string flat = rawDescription.Replace("\r\n", " ").Replace("\n", " ");
        string stripped = _bbcodeStripRegex.Replace(flat, "");
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\s+", " ").Trim();

        if (stripped.Length <= maxChars) return stripped;

        int cut = stripped.LastIndexOf(' ', Math.Min(maxChars, stripped.Length - 1));
        if (cut <= 0) cut = maxChars;
        return stripped[..cut].TrimEnd() + "…";
    }

    private async Task SearchWorkshopAndNotifyAsync()
    {
        await SearchWorkshopAsync();
        SearchCompleted?.Invoke(this, EventArgs.Empty);
    }

    public async Task SearchWorkshopAsync(bool resetPage = true)
    {
        if (!IsOnlineMode) return;

        if (IsInCollectionDetail)
        {
            Addons.Refresh();
            return;
        }

        var selectedTags = WorkshopTagCategories
            .SelectMany(c => c.Tags.Append(c.MainTag)) 
            .Where(t => t.IsSelected)
            .Select(t => t.Name)
            .ToList();

        if (resetPage) CurrentPage = 0;

        int myToken = ++_searchRequestToken;

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var searchToken = _searchCts.Token;

        try
        {
            _isSearching = true;
            _addonList.Clear();
            _collectionList.Clear();
            Addons.Refresh();

            var apiKey = NekoSettings.Default.SteamApiKey;
            string? exactId = null;
            bool shouldAutoDownload = false;
            var keyword = (SearchKeywords ?? "").Trim();

            var urlMatch = System.Text.RegularExpressions.Regex.Match(keyword, @"[?&]id=(\d+)");
            if (urlMatch.Success)
            {
                exactId = urlMatch.Groups[1].Value;
                shouldAutoDownload = true;
            }
            else if (!string.IsNullOrWhiteSpace(keyword) && keyword.All(char.IsDigit))
            {
                exactId = keyword;
                shouldAutoDownload = false;
            }

            if (!string.IsNullOrEmpty(exactId))
            {
                string detailsUrl = $"https://api.steampowered.com/IPublishedFileService/GetDetails/v1/?key={apiKey}&publishedfileids[0]={exactId}&includechildren=true&includevotes=true&includetags=true";
                var detailJson = await _httpClient.GetStringAsync(detailsUrl, searchToken);

                if (myToken != _searchRequestToken) return;

                var detailResult = JsonSerializer.Deserialize<SteamApiResponse>(detailJson);
                var item = detailResult?.Response?.PublishedFileDetails?.FirstOrDefault();

                if (item != null && !string.IsNullOrEmpty(item.Title))
                {
                    if (IsCollectionMode)
                    {
                        string starStr = ComputeStars(item.VoteData);

                        var coll = new SteamCollectionItem
                        {
                            Id = item.PublishedFileId ?? "",
                            Title = item.Title ?? "",
                            PreviewUrl = item.PreviewUrl ?? "",
                            Description = item.Description?.Replace("\r\n", " ").Replace("\n", " ") ?? "",
                            DescriptionBBCode = item.Description ?? "",
                            DescriptionPreview = BuildDescriptionPreview(item.Description),
                            ItemCount = item.Children?.Count ?? 0,
                            CreatorId = item.Creator ?? "",
                            Children = item.Children,
                            Stars = starStr,
                            
                            Tags = item.Tags != null ? string.Join(", ", item.Tags.Select(t => t.Tag)) : "",
                            TimeCreatedStr = item.TimeCreated > 0 ? DateTimeOffset.FromUnixTimeSeconds(item.TimeCreated).DateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "",
                            TimeUpdatedStr = item.TimeUpdated > 0 ? DateTimeOffset.FromUnixTimeSeconds(item.TimeUpdated).DateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "",
                            Favorited = item.Favorited,
                            Views = item.Views,
                            Subscriptions = item.Subscriptions
                        };
                        coll.LoadImage();
                        _collectionList.Add(coll);

                        if (coll.Children != null && coll.Children.Count > 0)
                        {
                            bool allInstalled = true;
                            foreach (var child in coll.Children)
                            {
                                if (!string.IsNullOrEmpty(child.PublishedFileId) && !IsWorkshopIdInstalled(child.PublishedFileId))
                                {
                                    allInstalled = false;
                                    break;
                                }
                            }
                            coll.IsInstalled = allInstalled;
                        }

                        if (!string.IsNullOrEmpty(coll.CreatorId)) await UpdateAuthorNamesAsync([coll.CreatorId]);
                    }
                    else
                    {
                        var info = new AddonInfo { Title = item.Title, Author = item.Creator, Description = item.Description, Url0 = item.PreviewUrl };
                        string tagStr = item.Tags != null ? string.Join(", ", item.Tags.Select(t => t.Tag)) : "";

                        var attribute = new AddonAttribute(false, item.PublishedFileId + ".vpk", AddonSource.WorkShop, info, tagStr) { WorkShopID = item.PublishedFileId };
                        
                        if (!string.IsNullOrEmpty(attribute.WorkShopID) && IsWorkshopIdInstalled(attribute.WorkShopID)) attribute.IsInstalled = true;
                        attribute.IsWorkshopSearchResult = true;
                        if (long.TryParse(item.FileSizeStr, out long sizeBytes)) attribute.FileSizeRaw = sizeBytes;
                        attribute.Subscriptions = item.Subscriptions;
                        if (item.TimeUpdated > 0) attribute.LastUpdate = DateTimeOffset.FromUnixTimeSeconds(item.TimeUpdated).DateTime.ToLocalTime();

                        attribute.Stars = ComputeStars(item.VoteData);
                        attribute.LoadImage(GetLocalVpkPath(attribute.WorkShopID));

                        _addonList.Add(attribute);
                        Addons.Refresh();

                        if (!string.IsNullOrEmpty(item.Creator)) await UpdateAuthorNamesAsync([item.Creator]);

                        if (!attribute.IsInstalled && shouldAutoDownload)
                        {
                            _ = DownloadAddonAsync(attribute).ContinueWith(t => 
                            {
                                if (t.Result)
                                {
                                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                                        if (NekoSettings.Default.ClearSearchAfterDownload)
                                        {
                                            SearchKeywords = string.Empty;
                                            IsOnlineMode = false;
                                        }
                                    });
                                }
                            });
                        }
                    }
                    HasMoreResults = false;
                    return; 
                }
            }

            var appId = GameAppId;
            var searchText = Uri.EscapeDataString(keyword);
            int fileType = IsCollectionMode ? 1 : 0;
            int queryType = SelectedSortOption?.QueryType ?? 0;

            string url = $"https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/?" +
             $"key={apiKey}&appid={appId}&search_text={searchText}&" +
             $"return_tags=1&return_details=1&return_children=1&return_vote_data=1&numperpage={ItemsPerPage}&page={CurrentPage + 1}&query_type={queryType}" +
             $"&match_all_tags=0&filetype={fileType}";

            if (!IsCollectionMode)
            {
                for (int i = 0; i < selectedTags.Count; i++)
                {
                    url += $"&requiredtags[{i}]={Uri.EscapeDataString(selectedTags[i])}";
                }
            }

            var jsonStr = await _httpClient.GetStringAsync(url, searchToken);

            if (myToken != _searchRequestToken) return;

            var result = JsonSerializer.Deserialize<SteamApiResponse>(jsonStr);

            List<string> creatorIds = [];

            if (result?.Response?.PublishedFileDetails != null)
            {
                if (IsCollectionMode)
                {
                    foreach (var item in result.Response.PublishedFileDetails)
                    {
                        string starStr = ComputeStars(item.VoteData);

                        var coll = new SteamCollectionItem
                        {
                            Id = item.PublishedFileId ?? "",
                            Title = item.Title ?? "",
                            PreviewUrl = item.PreviewUrl ?? "",
                            Description = item.Description?.Replace("\r\n", " ").Replace("\n", " ") ?? "",
                            DescriptionBBCode = item.Description ?? "",
                            DescriptionPreview = BuildDescriptionPreview(item.Description),
                            ItemCount = item.Children?.Count ?? 0,
                            CreatorId = item.Creator ?? "",
                            Children = item.Children,
                            Stars = starStr,
                            
                            Tags = item.Tags != null ? string.Join(", ", item.Tags.Select(t => t.Tag)) : "",
                            TimeCreatedStr = item.TimeCreated > 0 ? DateTimeOffset.FromUnixTimeSeconds(item.TimeCreated).DateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "",
                            TimeUpdatedStr = item.TimeUpdated > 0 ? DateTimeOffset.FromUnixTimeSeconds(item.TimeUpdated).DateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "",
                            Favorited = item.Favorited,
                            Views = item.Views,
                            Subscriptions = item.Subscriptions
                        };
                        coll.LoadImage();
                        if (!string.IsNullOrEmpty(coll.CreatorId)) creatorIds.Add(coll.CreatorId);
                        _collectionList.Add(coll);

                        if (coll.Children != null && coll.Children.Count > 0)
                        {
                            bool allInstalled = true;
                            foreach (var child in coll.Children)
                            {
                                if (!string.IsNullOrEmpty(child.PublishedFileId) && !IsWorkshopIdInstalled(child.PublishedFileId))
                                {
                                    allInstalled = false;
                                    break;
                                }
                            }
                            coll.IsInstalled = allInstalled;
                        }
                    }
                }
                else
                {
                    foreach (var item in result.Response.PublishedFileDetails)
                    {
                        var info = new AddonInfo { Title = item.Title ?? "", Author = item.Creator, Description = item.Description, Url0 = item.PreviewUrl };
                        string tagStr = item.Tags != null ? string.Join(", ", item.Tags.Select(t => t.Tag)) : "";

                        var attribute = new AddonAttribute(false, item.PublishedFileId + ".vpk", AddonSource.WorkShop, info, tagStr) { WorkShopID = item.PublishedFileId };
                        
                        if (!string.IsNullOrEmpty(attribute.WorkShopID) && IsWorkshopIdInstalled(attribute.WorkShopID)) attribute.IsInstalled = true;
                        attribute.IsWorkshopSearchResult = true;
                        if (long.TryParse(item.FileSizeStr, out long sizeBytes)) attribute.FileSizeRaw = sizeBytes;
                        attribute.Subscriptions = item.Subscriptions;
                        if (item.TimeUpdated > 0) attribute.LastUpdate = DateTimeOffset.FromUnixTimeSeconds(item.TimeUpdated).DateTime.ToLocalTime();

                        attribute.Stars = ComputeStars(item.VoteData);
                        attribute.LoadImage(GetLocalVpkPath(attribute.WorkShopID));

                        if (!string.IsNullOrEmpty(item.Creator)) creatorIds.Add(item.Creator);
                        _addonList.Add(attribute);
                    }
                }
            }
            Addons.Refresh();

            int returnedCount = result?.Response?.PublishedFileDetails?.Count ?? 0;
            long totalCount = 0;
            long.TryParse(result?.Response?.TotalRaw?.ToString(), out totalCount);
            HasMoreResults = totalCount > 0
                ? returnedCount > 0 && (long)(CurrentPage + 1) * ItemsPerPage < totalCount
                : returnedCount >= ItemsPerPage;

            if (creatorIds.Count > 0)
            {
                await UpdateAuthorNamesAsync(creatorIds.Distinct().ToList());
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (myToken != _searchRequestToken) return;
            await ShowCustomMessageBoxAsync(i18n["SearchFailedTitle"], $"{ex.Message}", ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
        }
        finally
        {
            if (myToken == _searchRequestToken) _isSearching = false;
        }
    }

    private string _savedSearchKeyword = string.Empty;

    public async Task EnterCollectionAsync(SteamCollectionItem collection)
    {
        int myToken = ++_collectionLoadToken;
        _savedSearchKeyword = SearchKeywords ?? string.Empty;
        SearchKeywords = string.Empty;
        IsInCollectionDetail = true;
        _addonList.Clear();
        Addons.Refresh();

        if (collection.Children == null || collection.Children.Count == 0) return;

        var apiKey = NekoSettings.Default.SteamApiKey;
        var childIds = collection.Children.Select(c => c.PublishedFileId).Where(id => !string.IsNullOrEmpty(id)).ToList();
        var failedBatches = 0;

        for (int i = 0; i < childIds.Count; i += 50)
        {
            if (myToken != _collectionLoadToken) return;
            var batch = childIds.Skip(i).Take(50).ToList();
            try
            {
                string batchUrl = $"https://api.steampowered.com/IPublishedFileService/GetDetails/v1/?key={apiKey}&includetags=true&includevotes=true";
                for (int j = 0; j < batch.Count; j++)
                {
                    batchUrl += $"&publishedfileids[{j}]={batch[j]}";
                }

                var batchJson = await _httpClient.GetStringAsync(batchUrl);
                if (myToken != _collectionLoadToken) return;
                var batchResult = JsonSerializer.Deserialize<SteamApiResponse>(batchJson);

                if (batchResult?.Response?.PublishedFileDetails != null)
                {
                    List<string> creatorIds = [];
                    foreach (var childItem in batchResult.Response.PublishedFileDetails)
                    {
                        var info = new AddonInfo { Title = childItem.Title ?? "", Author = childItem.Creator, Description = childItem.Description, Url0 = childItem.PreviewUrl };
                        string tagStr = childItem.Tags != null ? string.Join(", ", childItem.Tags.Select(t => t.Tag)) : "";

                        var attribute = new AddonAttribute(false, childItem.PublishedFileId + ".vpk", AddonSource.WorkShop, info, tagStr) { WorkShopID = childItem.PublishedFileId };

                        if (!string.IsNullOrEmpty(attribute.WorkShopID) && IsWorkshopIdInstalled(attribute.WorkShopID)) attribute.IsInstalled = true;
                        attribute.IsWorkshopSearchResult = true;
                        if (long.TryParse(childItem.FileSizeStr, out long sizeBytes)) attribute.FileSizeRaw = sizeBytes;
                        attribute.Subscriptions = childItem.Subscriptions;
                        if (childItem.TimeUpdated > 0) attribute.LastUpdate = DateTimeOffset.FromUnixTimeSeconds(childItem.TimeUpdated).DateTime.ToLocalTime();

                        attribute.Stars = ComputeStars(childItem.VoteData);
                        attribute.LoadImage(GetLocalVpkPath(attribute.WorkShopID));

                        if (!string.IsNullOrEmpty(childItem.Creator)) creatorIds.Add(childItem.Creator);
                        _addonList.Add(attribute);
                    }
                    Addons.Refresh();
                    if (creatorIds.Count > 0) await UpdateAuthorNamesAsync(creatorIds.Distinct().ToList());
                }
            }
            catch (Exception ex)
            {
                if (myToken != _collectionLoadToken) return;
                App.Logger.Error(ex);
                failedBatches++;
            }
        }

        if (myToken != _collectionLoadToken) return;
        if (failedBatches > 0)
        {
            await ShowCustomMessageBoxAsync(i18n["SearchFailedTitle"], string.Format(i18n["CollectionPartialLoadMsg"], failedBatches), ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
        }
    }

    public void ExitCollection()
    {
        IsInCollectionDetail = false;
        SearchKeywords = _savedSearchKeyword;
        _addonList.Clear();
        Addons.Refresh();
    }

    private bool _isCollectionDownloading;

    public async Task DownloadCollectionAsync(SteamCollectionItem collection)
    {
        if (_isCollectionDownloading || IsDownloading) return;
        _isCollectionDownloading = true;
        _cancelRequested = false;

        var apiKey = NekoSettings.Default.SteamApiKey;
        List<string> failures = [];
        try
        {
            if (collection.Children != null && collection.Children.Count > 0)
            {
                var childIds = collection.Children.Select(c => c.PublishedFileId).Where(id => !string.IsNullOrEmpty(id)).ToList();
                for (int i = 0; i < childIds.Count; i += 50)
                {
                    var batch = childIds.Skip(i).Take(50).ToList();
                    string batchUrl = $"https://api.steampowered.com/IPublishedFileService/GetDetails/v1/?key={apiKey}&return_details=1";
                    for (int j = 0; j < batch.Count; j++)
                    {
                        batchUrl += $"&publishedfileids[{j}]={batch[j]}";
                    }
                    var batchJson = await _httpClient.GetStringAsync(batchUrl);
                    var batchResult = JsonSerializer.Deserialize<SteamApiResponse>(batchJson);

                    if (batchResult?.Response?.PublishedFileDetails != null)
                    {
                        foreach (var childItem in batchResult.Response.PublishedFileDetails)
                        {
                            var info = new AddonInfo { Title = childItem.Title ?? "", Author = childItem.Creator };
                            var attribute = new AddonAttribute(false, childItem.PublishedFileId + ".vpk", AddonSource.WorkShop, info, "") { WorkShopID = childItem.PublishedFileId };
                            if (IsWorkshopIdInstalled(attribute.WorkShopID)) continue;

                            string label = string.IsNullOrEmpty(childItem.Title) ? (childItem.PublishedFileId ?? "?") : childItem.Title;
                            await DownloadAddonAsync(attribute, error => failures.Add($"{label} - {error}"), childItem);
                            if (_cancelRequested) return;
                        }
                    }
                }
            }

            if (collection.Children != null && collection.Children.Count > 0)
            {
                bool allInstalled = true;
                foreach (var child in collection.Children)
                {
                    if (!string.IsNullOrEmpty(child.PublishedFileId) && !IsWorkshopIdInstalled(child.PublishedFileId))
                    {
                        allInstalled = false;
                        break;
                    }
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    collection.IsInstalled = allInstalled;
                });
            }
        }
        catch (Exception ex)
        {
            await ShowCustomMessageBoxAsync(i18n["DownloadFailedTitle"], ex.Message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
        }
        finally
        {
            _isCollectionDownloading = false;
        }

        if (failures.Count > 0)
        {
            var lines = failures.Take(10).ToList();
            string body = string.Join("\n", lines);
            if (failures.Count > lines.Count) body += $"\n… (+{failures.Count - lines.Count})";
            await ShowCustomMessageBoxAsync(i18n["DownloadFailedTitle"], body, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
        }
    }

    private async Task UpdateAuthorNamesAsync(List<string> steamIds)
    {
        try
        {
            var apiKey = NekoSettings.Default.SteamApiKey;
            var idsStr = string.Join(",", steamIds.Take(100));
            string url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={apiKey}&steamids={idsStr}";

            var jsonStr = await _httpClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<SteamUserResponse>(jsonStr);

            if (result?.Response?.Players != null)
            {
                foreach (var player in result.Response.Players)
                {
                    foreach (var addon in _addonList.Where(a => a.AddonInfo_Author == player.SteamId))
                    {
                        addon.UpdateAuthorName(player.PersonaName);
                    }
                    foreach (var coll in _collectionList.Where(c => c.CreatorId == player.SteamId))
                    {
                        coll.AuthorName = player.PersonaName ?? player.SteamId ?? "";
                    }
                }
                Addons.Refresh();
            }
        }
        catch { }
    }

    public void CancelDownload()
    {
        _cancelRequested = true;
        _downloadCts?.Cancel();
    }

    public async Task<bool> DownloadAddonAsync(AddonAttribute addon, Action<string>? errorSink = null, SteamPublishedFileDetails? prefetched = null)
    {
        if (addon.Source != AddonSource.WorkShop || string.IsNullOrEmpty(addon.WorkShopID)) return false;
        if (IsDownloading) return false;
        if (addon.IsInstalled) return false; 

        if (IsWorkshopIdInstalled(addon.WorkShopID))
        {
            addon.IsInstalled = true;
            return false;
        }

        IsDownloading = true;
        addon.IsDownloadingNow = true;
        DownloadProgress = 0;
        _downloadCts = new CancellationTokenSource();
        string? downloadTmpPath = null;

        try
        {
            var apiKey = NekoSettings.Default.SteamApiKey;
            string detailsUrl = $"https://api.steampowered.com/IPublishedFileService/GetDetails/v1/?key={apiKey}&publishedfileids[0]={addon.WorkShopID}";
            
            SteamPublishedFileDetails? details = prefetched;
            if (details == null || string.IsNullOrEmpty(details.FileUrl))
            {
                var jsonStr = await _httpClient.GetStringAsync(detailsUrl, _downloadCts.Token);
                var result = JsonSerializer.Deserialize<SteamApiResponse>(jsonStr);
                details = result?.Response?.PublishedFileDetails?.FirstOrDefault();
            }
            
            if (details == null || string.IsNullOrEmpty(details.FileUrl))
            {
                throw new Exception(NekoVpk.Lang.I18nManager.Instance["NoDownloadLinkMsg"]);
            }

            string fileName = $"{addon.WorkShopID}.vpk";
            
            string targetDir = Path.Join(GameDir, "addons");
            string targetPath = Path.Join(targetDir, fileName);

            if (!Directory.Exists(targetDir))
            {
                throw new Exception(string.Format(NekoVpk.Lang.I18nManager.Instance["AddonDirNotFoundMsg"], targetDir));
            }
            
            downloadTmpPath = targetPath + ".tmp";

            using (var response = await _httpClient.GetAsync(details.FileUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                using (var contentStream = await response.Content.ReadAsStreamAsync(_downloadCts.Token))
                using (var fileStream = new FileStream(downloadTmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int read;

                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, _downloadCts.Token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, _downloadCts.Token);
                        totalRead += read;

                        if (canReportProgress)
                        {
                            DownloadProgress = (double)totalRead / totalBytes * 100;
                        }
                    }
                }
            }

            File.Move(downloadTmpPath, targetPath, true);
            downloadTmpPath = null;

            try
            {
                await TryFillMissingAddonInfoAsync(targetPath, addon.WorkShopID!, details);
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex);
            }

            addon.IsInstalled = true;
            if (!string.IsNullOrEmpty(addon.WorkShopID))
            {
                _localWorkshopIds.Add(addon.WorkShopID);
                _localWorkshopFilePaths[addon.WorkShopID] = targetPath;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (errorSink != null)
            {
                errorSink(ex.Message);
            }
            else
            {
                await ShowCustomMessageBoxAsync(NekoVpk.Lang.I18nManager.Instance["DownloadFailedTitle"], ex.Message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
            }
            return false;
        }
        finally
        {
            if (downloadTmpPath != null)
            {
                try { File.Delete(downloadTmpPath); } catch { }
            }
            IsDownloading = false;
            addon.IsDownloadingNow = false;
            DownloadProgress = 0;
            _downloadCts = null;
        }
    }

    private async Task TryFillMissingAddonInfoAsync(string vpkPath, string workshopId, SteamPublishedFileDetails details)
    {
        var (hasAddonInfo, hasEmbeddedImage) = await Task.Run(() =>
        {
            Package pak = new();
            try
            {
                pak.Read(vpkPath);
                bool foundAddonInfo = false;
                bool foundImage = false;
                foreach (var entry in pak.Entries)
                {
                    foreach (var f in entry.Value)
                    {
                        var path = f.GetFullPath();
                        if (string.Equals(path, "addoninfo.txt", StringComparison.OrdinalIgnoreCase)) foundAddonInfo = true;
                        else if (string.Equals(path, "addonimage.jpg", StringComparison.OrdinalIgnoreCase)) foundImage = true;
                        if (foundAddonInfo && foundImage) return (foundAddonInfo, foundImage);
                    }
                }
                return (foundAddonInfo, foundImage);
            }
            finally
            {
                pak.Dispose();
            }
        });

        string sidecarImagePath = Path.ChangeExtension(vpkPath, ".jpg");
        bool hasSidecarImage = File.Exists(sidecarImagePath) && new FileInfo(sidecarImagePath).Length > 0;

        if (!hasSidecarImage && !hasEmbeddedImage && !string.IsNullOrWhiteSpace(details.PreviewUrl))
        {
            string tmpImagePath = sidecarImagePath + ".tmp";
            try
            {
                byte[] imageBytes = await _httpClient.GetByteArrayAsync(details.PreviewUrl);
                await File.WriteAllBytesAsync(tmpImagePath, imageBytes);
                File.Move(tmpImagePath, sidecarImagePath, true);
            }
            catch
            {
                if (File.Exists(tmpImagePath)) File.Delete(tmpImagePath);
            }
        }

        if (hasAddonInfo)
        {
            string existingLink = $"https://steamcommunity.com/sharedfiles/filedetails/?id={workshopId}";
            long existingDateUnix = details.TimeUpdated > 0 ? details.TimeUpdated : details.TimeCreated;
            string? existingDateStr = existingDateUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(existingDateUnix).ToString("yyyy-MM-dd")
                : null;

            if (!string.IsNullOrWhiteSpace(existingLink) && !string.IsNullOrWhiteSpace(existingDateStr))
            {
                try
                {
                    await Task.Run(() => PatchAddonInfoInVpk(vpkPath,
                        bytes => AddonInfo.SetLinkAndDate(bytes, existingLink, existingDateStr)));
                }
                catch (Exception ex)
                {
                    App.Logger.Error(ex);
                }
            }
            return;
        }

        string authorName = details.Creator ?? "";
        try
        {
            var apiKey = NekoSettings.Default.SteamApiKey;
            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(details.Creator))
            {
                string url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={apiKey}&steamids={details.Creator}";
                var jsonStr = await _httpClient.GetStringAsync(url);
                var result = JsonSerializer.Deserialize<SteamUserResponse>(jsonStr);
                var personaName = result?.Response?.Players?.FirstOrDefault()?.PersonaName;
                if (!string.IsNullOrEmpty(personaName)) authorName = personaName;
            }
        }
        catch
        {
        }

        long dateUnix = details.TimeUpdated > 0 ? details.TimeUpdated : details.TimeCreated;
        string? dateStr = dateUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(dateUnix).ToString("yyyy-MM-dd")
            : null;

        var info = new AddonInfo
        {
            Title = details.Title,
            Author = authorName,
            Link = $"https://steamcommunity.com/sharedfiles/filedetails/?id={workshopId}",
            Date = dateStr,
            SteamAppId = GameAppId.ToString(),
        };

        await Task.Run(() =>
        {
            Package? pak = null;
            string tmpInfoPath = vpkPath + ".addoninfo.tmp";
            string tmpVpkPath = vpkPath + ".tmp";
            try
            {
                pak = new Package();
                pak.Read(vpkPath);

                File.WriteAllBytes(tmpInfoPath, info.ToBytes());
                pak.AddFile("addoninfo.txt", new FileInfo(tmpInfoPath));

                pak.Write(tmpVpkPath, 1);
                pak.Dispose();
                pak = null;

                File.Move(tmpVpkPath, vpkPath, true);
            }
            finally
            {
                pak?.Dispose();
                if (File.Exists(tmpInfoPath)) File.Delete(tmpInfoPath);
                if (File.Exists(tmpVpkPath)) File.Delete(tmpVpkPath);
            }
        });
    }

    private static void PatchAddonInfoInVpk(string vpkPath, Func<byte[], byte[]> transform)
    {
        Package? pak = null;
        string tmpInfoPath = vpkPath + ".addoninfo.tmp";
        string tmpVpkPath = vpkPath + ".tmp";
        try
        {
            pak = new Package();
            pak.Read(vpkPath);

            PackageEntry? addonInfoEntry = null;
            foreach (var entity in pak.Entries)
            {
                foreach (var file in entity.Value)
                {
                    if (string.Equals(file.GetFullPath(), "addoninfo.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        addonInfoEntry = file;
                    }
                }
            }

            if (addonInfoEntry == null) return;

            pak.ReadEntry(addonInfoEntry, out byte[] original);
            byte[] updated = transform(original);

            File.WriteAllBytes(tmpInfoPath, updated);
            pak.RemoveFile(addonInfoEntry);
            pak.AddFile("addoninfo.txt", new FileInfo(tmpInfoPath));

            pak.Write(tmpVpkPath, 1);
            pak.Dispose();
            pak = null;

            File.Move(tmpVpkPath, vpkPath, true);
        }
        finally
        {
            pak?.Dispose();
            if (File.Exists(tmpInfoPath)) File.Delete(tmpInfoPath);
            if (File.Exists(tmpVpkPath)) File.Delete(tmpVpkPath);
        }
    }

    public enum UpdateCheckState
    {
        Idle,
        Checking,
        Downloading,
        Failed
    }

    private UpdateCheckState _updateCheckState = UpdateCheckState.Idle;
    public UpdateCheckState UpdateCheckStateValue
    {
        get => _updateCheckState;
        private set
        {
            this.RaiseAndSetIfChanged(ref _updateCheckState, value);
            this.RaisePropertyChanged(nameof(IsCheckingUpdates));
            this.RaisePropertyChanged(nameof(IsDownloadingUpdates));
            this.RaisePropertyChanged(nameof(IsUpdateCheckFailed));
            this.RaisePropertyChanged(nameof(IsUpdateCheckBusy));
        }
    }

    public bool IsCheckingUpdates => UpdateCheckStateValue == UpdateCheckState.Checking;
    public bool IsDownloadingUpdates => UpdateCheckStateValue == UpdateCheckState.Downloading;
    public bool IsUpdateCheckFailed => UpdateCheckStateValue == UpdateCheckState.Failed;
    public bool IsUpdateCheckBusy => UpdateCheckStateValue != UpdateCheckState.Idle;

    private static bool TryGetWorkshopIdAndDate(AddonAttribute addon, out string workshopId, out string date)
    {
        workshopId = "";
        date = "";
        if (string.IsNullOrWhiteSpace(addon.AddonInfo_Link) || string.IsNullOrWhiteSpace(addon.AddonInfo_Date)) return false;

        var match = Regex.Match(addon.AddonInfo_Link, @"[?&]id=(\d+)");
        if (!match.Success) return false;

        workshopId = match.Groups[1].Value;
        date = addon.AddonInfo_Date;
        return true;
    }

    public async Task CheckUpdatesGlobalAsync()
    {
        if (IsFolderMode)
        {
            await RunCheckUpdatesAsync(null, isSingleSelection: false, scopeLoader: () => LoadFolderAddonsForUpdateCheck());
            if (IsInsideFolder) LoadAddons();
        }
        else
        {
            await RunCheckUpdatesAsync(
                _addonList.Where(a => a.IsInstalled && string.IsNullOrEmpty(a.SubFolder)).ToList(),
                isSingleSelection: false);
        }
    }

    public async Task CheckUpdatesInFoldersAsync(IEnumerable<string> folderNames)
    {
        var names = folderNames.ToList();
        await RunCheckUpdatesAsync(null, isSingleSelection: false,
            scopeLoader: () => LoadFolderAddonsForUpdateCheck(names));
        if (IsInsideFolder) LoadAddons();
    }

    private List<AddonAttribute> LoadFolderAddonsForUpdateCheck(IReadOnlyCollection<string>? onlyFolders = null)
    {
        List<AddonAttribute> result = [];
        var addonDir = new DirectoryInfo(Path.Join(GameDir, "addons"));
        if (!addonDir.Exists) return result;

        foreach (var dir in AddonFolders.GetVisibleFolders(addonDir))
        {
            if (onlyFolders != null && !onlyFolders.Contains(dir.Name, StringComparer.OrdinalIgnoreCase)) continue;

            FileInfo[] files;
            try { files = dir.GetFiles("*.vpk"); }
            catch (Exception) { continue; }

            foreach (var file in files)
            {
                try
                {
                    using var pak = new Package();
                    pak.Read(file.FullName);

                    PackageEntry? infoEntry = null;
                    foreach (var entries in pak.Entries)
                    {
                        foreach (var entry in entries.Value)
                        {
                            if (entry.GetFullPath() == "addoninfo.txt") infoEntry = entry;
                        }
                    }
                    if (infoEntry == null) continue;

                    pak.ReadEntry(infoEntry, out byte[] bytes);
                    var info = AddonInfo.Load(bytes);
                    result.Add(new AddonAttribute(null, file.Name, AddonSource.Local, info)
                    {
                        SubFolder = dir.Name,
                        IsInstalled = true
                    });
                }
                catch (Exception ex)
                {
                    App.Logger.Error(ex);
                }
            }
        }
        return result;
    }

    public Task CheckUpdatesForAsync(IEnumerable<AddonAttribute> addons)
    {
        var list = addons.ToList();
        return RunCheckUpdatesAsync(list, isSingleSelection: list.Count == 1);
    }

    private async Task RunCheckUpdatesAsync(List<AddonAttribute>? scope, bool isSingleSelection,
        Func<List<AddonAttribute>>? scopeLoader = null)
    {
        if (UpdateCheckStateValue != UpdateCheckState.Idle) return;
        if (!await EnsureSteamApiKeyAsync()) return;

        if (scope == null)
        {
            UpdateCheckStateValue = UpdateCheckState.Checking;
            try { scope = scopeLoader != null ? await Task.Run(scopeLoader) : new List<AddonAttribute>(); }
            finally { UpdateCheckStateValue = UpdateCheckState.Idle; }
        }

        var idToAddon = new Dictionary<string, AddonAttribute>();
        foreach (var addon in scope)
        {
            if (addon.Source == AddonSource.WorkShop) continue;

            if (addon.IsInstalled &&
                TryGetWorkshopIdAndDate(addon, out string wsId, out _))
            {
                idToAddon[wsId] = addon;
            }
        }

        if (idToAddon.Count == 0)
        {
            if (isSingleSelection)
            {
                await ShowCustomMessageBoxAsync(i18n["CheckUpdatesUnsupportedTitle"], i18n["CheckUpdatesUnsupportedMsg"], ButtonEnum.Ok);
            }
            else
            {
                await ShowCustomMessageBoxAsync(i18n["CheckUpdatesNoneTitle"], i18n["CheckUpdatesNoneMsg"], ButtonEnum.Ok);
            }
            return;
        }

        UpdateCheckStateValue = UpdateCheckState.Checking;
        List<AddonAttribute> toUpdate = [];
        string? errorMsg = null;

        try
        {
            var apiKey = NekoSettings.Default.SteamApiKey;
            var ids = idToAddon.Keys.ToList();

            for (int i = 0; i < ids.Count; i += 50)
            {
                var batch = ids.Skip(i).Take(50).ToList();
                string batchUrl = $"https://api.steampowered.com/IPublishedFileService/GetDetails/v1/?key={apiKey}&return_details=1";
                for (int j = 0; j < batch.Count; j++)
                {
                    batchUrl += $"&publishedfileids[{j}]={batch[j]}";
                }

                var json = await _httpClient.GetStringAsync(batchUrl);
                var result = JsonSerializer.Deserialize<SteamApiResponse>(json);
                if (result?.Response?.PublishedFileDetails == null) continue;

                foreach (var item in result.Response.PublishedFileDetails)
                {
                    if (item.PublishedFileId == null || !idToAddon.TryGetValue(item.PublishedFileId, out var addon)) continue;

                    long remoteUnix = item.TimeUpdated > 0 ? item.TimeUpdated : item.TimeCreated;
                    if (remoteUnix <= 0) continue;

                    string remoteDateStr = DateTimeOffset.FromUnixTimeSeconds(remoteUnix).ToString("yyyy-MM-dd");
                    if (!string.Equals(remoteDateStr, addon.AddonInfo_Date, StringComparison.Ordinal))
                    {
                        toUpdate.Add(addon);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            errorMsg = ex.Message;
        }

        if (errorMsg != null)
        {
            UpdateCheckStateValue = UpdateCheckState.Failed;
            await Task.Delay(700);
            UpdateCheckStateValue = UpdateCheckState.Idle;
            await ShowCustomMessageBoxAsync(i18n["CheckUpdatesFailedTitle"], errorMsg, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
            return;
        }

        if (toUpdate.Count == 0)
        {
            UpdateCheckStateValue = UpdateCheckState.Idle;
            await ShowCustomMessageBoxAsync(i18n["CheckUpdatesNoneTitle"], i18n["CheckUpdatesNoneMsg"], ButtonEnum.Ok);
            return;
        }

        UpdateCheckStateValue = UpdateCheckState.Idle;

        var titleLines = toUpdate.Take(10).Select(a => a.Title).ToList();
        string listText = string.Join("\n", titleLines);
        if (toUpdate.Count > 10)
        {
            listText += "\n" + string.Format(i18n["CheckUpdatesMoreCount"], toUpdate.Count - 10);
        }
        string confirmMsg = $"{i18n["CheckUpdatesConfirmMsg"]}\n{listText}";

        var confirmResult = await ShowCustomMessageBoxAsync(i18n["CheckUpdatesFoundTitle"], confirmMsg, ButtonEnum.YesNo);
        if (confirmResult != ButtonResult.Yes) return;

        UpdateCheckStateValue = UpdateCheckState.Downloading;
        List<string> failedTitles = [];

        foreach (var addon in toUpdate)
        {
            bool ok = await ReplaceAddonWithLatestAsync(addon);
            if (!ok) failedTitles.Add(addon.Title);
        }

        UpdateCheckStateValue = UpdateCheckState.Idle;
        Addons.Refresh();

        if (failedTitles.Count > 0)
        {
            string failMsg = i18n["CheckUpdatesDownloadFailedMsg"] + "\n" + string.Join("\n", failedTitles);
            await ShowCustomMessageBoxAsync(i18n["CheckUpdatesFailedTitle"], failMsg, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
        }
    }

    private async Task<bool> ReplaceAddonWithLatestAsync(AddonAttribute addon)
    {
        string? updateTmpPath = null;
        try
        {
            if (!TryGetWorkshopIdAndDate(addon, out string workshopId, out _)) return false;

            var apiKey = NekoSettings.Default.SteamApiKey;
            string detailsUrl = $"https://api.steampowered.com/IPublishedFileService/GetDetails/v1/?key={apiKey}&publishedfileids[0]={workshopId}";

            var jsonStr = await _httpClient.GetStringAsync(detailsUrl);
            var result = JsonSerializer.Deserialize<SteamApiResponse>(jsonStr);
            var details = result?.Response?.PublishedFileDetails?.FirstOrDefault();

            if (details == null || string.IsNullOrEmpty(details.FileUrl)) return false;

            string targetPath = addon.GetAbsolutePath(GameDir);
            string tmpPath = targetPath + ".update.tmp";
            updateTmpPath = tmpPath;

            using (var response = await _httpClient.GetAsync(details.FileUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using var contentStream = await response.Content.ReadAsStreamAsync();
                using (var fileStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    await contentStream.CopyToAsync(fileStream);
                }
            }
            File.Move(tmpPath, targetPath, true);

            try
            {
                string oldBakPath = Path.ChangeExtension(targetPath, ".vpk.nekobak");
                if (File.Exists(oldBakPath))
                {
                    File.Delete(oldBakPath);
                }
            }
            catch (Exception bakEx)
            {
                App.Logger.Error(bakEx);
            }

            long dateUnix = details.TimeUpdated > 0 ? details.TimeUpdated : details.TimeCreated;
            string? newDate = dateUnix > 0 ? DateTimeOffset.FromUnixTimeSeconds(dateUnix).ToString("yyyy-MM-dd") : null;

            if (!string.IsNullOrEmpty(newDate) && !string.IsNullOrEmpty(addon.AddonInfo_Link))
            {
                string link = addon.AddonInfo_Link;
                await Task.Run(() => PatchAddonInfoInVpk(targetPath,
                    bytes => AddonInfo.SetLinkAndDate(bytes, link, newDate)));
                addon.UpdateDate(newDate);
            }

            return true;
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
            if (updateTmpPath != null)
            {
                try { File.Delete(updateTmpPath); } catch { }
            }
            return false;
        }
    }

    public void DeleteAddon(AddonAttribute addon)
    {
        string path = addon.IsWorkshopSearchResult
            ? FindInstalledWorkshopPath(addon.WorkShopID) ?? addon.GetAbsolutePath(GameDir)
            : addon.GetAbsolutePath(GameDir);
        
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        string jpgPath = Path.ChangeExtension(path, ".jpg");
        if (File.Exists(jpgPath))
        {
            File.Delete(jpgPath);
        }

        string bakPath = path + ".nekobak";
        if (File.Exists(bakPath))
        {
            File.Delete(bakPath);
        }

        if (!string.IsNullOrEmpty(addon.WorkShopID))
        {
            _localWorkshopIds.Remove(addon.WorkShopID);
            _localWorkshopFilePaths.Remove(addon.WorkShopID);
        }

        if (addon.IsWorkshopSearchResult)
        {
            addon.IsInstalled = false;
        }
        else
        {
            _addonList.Remove(addon);
        }

        AddonAttribute.dirty.Remove(addon);

        CheckConflicts();
        Addons.Refresh();
    }

    public void RefreshBackgroundDim() => this.RaisePropertyChanged(nameof(BackgroundDimOpacity));

    public void UpdateBackground()
    {
        var path = NekoSettings.Default.BackgroundImagePath;
        
        BackgroundImage?.Dispose();

        if (string.IsNullOrEmpty(path))
        {
            BackgroundImage = null;

            this.RaisePropertyChanged(nameof(BackgroundDimOpacity));
            this.RaisePropertyChanged(nameof(BackgroundStretch));
            return;
        }

        string? targetFile = null;

        try
        {
            if (File.Exists(path))
            {
                targetFile = path;
            }
            else if (Directory.Exists(path))
            {
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
                var files = Directory.EnumerateFiles(path)
                                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLower()))
                                    .ToList();

                if (files.Count > 0)
                {
                    var rand = new Random();
                    targetFile = files[rand.Next(files.Count)];
                }
            }

            if (targetFile != null && File.Exists(targetFile))
            {
                using var stream = File.OpenRead(targetFile);
                
                int decodeWidth = 2560; 
                
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop 
                    && desktop.MainWindow is not null)
                {
                    var screen = desktop.MainWindow.Screens.Primary ?? desktop.MainWindow.Screens.All.FirstOrDefault();
                    if (screen != null)
                    {
                        decodeWidth = Math.Max(1920, screen.Bounds.Width);
                    }
                }
                BackgroundImage = Bitmap.DecodeToWidth(stream, decodeWidth);
            }
            else
            {
                BackgroundImage = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载图片失败: {ex.Message}");
            BackgroundImage = null;
        }

        this.RaisePropertyChanged(nameof(BackgroundDimOpacity));
        this.RaisePropertyChanged(nameof(BackgroundStretch));
    }

    public static string? TryToFindGameDir()
    {
        try
        {
            SteamGameLocator steamGameLocator = new();
            if (steamGameLocator.getIsSteamInstalled())
            {
                SteamGameLocator.GameStruct result = steamGameLocator.getGameInfoByFolder("Left 4 Dead 2");
                if (result.steamGameLocation != null)
                {
                    return Path.Join(result.steamGameLocation, "left4dead2");
                }
            }
        }
        catch (Exception ex)
        {
            App.Logger.Warn(ex, "TryToFindGameDir");
        }
        return null;
    }

    private bool _isLoadingAddons;
    private bool _reloadRequested;

    public async void LoadAddons()
    {
        if (_isLoadingAddons)
        {
            _reloadRequested = true;
            return;
        }

        _isLoadingAddons = true;
        try
        {
            do
            {
                _reloadRequested = false;
                await LoadAddonsCoreAsync();
            } while (_reloadRequested);
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
            try
            {
                await ShowCustomMessageBoxAsync(i18n["ReadVpkFailedTitle"], ex.Message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
            }
            catch (Exception)
            {
            }
        }
        finally
        {
            _isLoadingAddons = false;
        }
    }

    private sealed record ScannedAddon(
        FileInfo FileEntry,
        AddonSource Source,
        AddonInfo Info,
        string Types,
        HashSet<string> ModifiedFiles,
        List<AssetTag> Tags,
        string ActiveId,
        List<NekoVariant> Variants);

    private sealed class ScanResult
    {
        public List<ScannedAddon> Addons { get; } = [];
        public HashSet<string> WorkshopIds { get; } = [];
        public Dictionary<string, string> WorkshopPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(string FileName, string Error)> Failures { get; } = [];
    }

    private async Task LoadAddonsCoreAsync()
    {
        TaggedAssets.Load();
        var addonDir = new DirectoryInfo(Path.Join(GameDir, "addons"));
        var workshopDir = new DirectoryInfo(Path.Join(GameDir, "addons", "workshop"));

        if (!addonDir.Exists)
        {
            await ShowCustomMessageBoxAsync(
            i18n["InvalidDirTitle"],
            i18n["InvalidDirMsg"],
            ButtonEnum.Ok, 
            MsBox.Avalonia.Enums.Icon.Error);
            return;
        }

        if (IsFolderMode)
        {
            RefreshFolderItems(addonDir);
            if (_currentFolder != null && !Directory.Exists(Path.Join(addonDir.FullName, _currentFolder)))
            {
                _currentFolder = null;
                NotifyFolderStateChanged();
            }
        }

        bool folderMode = IsFolderMode;
        string? subFolder = folderMode ? _currentFolder : null;

        ScanResult scan = await Task.Run(() => ScanAddonFiles(addonDir, workshopDir, folderMode, subFolder));

        AddonList addonList = new();
        try
        {
            addonList.Load(GameDir);
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
        }

        _localWorkshopIds = scan.WorkshopIds;
        _localWorkshopFilePaths = scan.WorkshopPaths;

        _addonList.Clear();
        AddonAttribute.dirty.Clear();
        foreach (var item in scan.Addons)
        {
            FileInfo fileInfo = item.FileEntry;
            string keyForAddonList = item.Source == AddonSource.WorkShop ? "workshop\\" + fileInfo.Name : fileInfo.Name;
            bool? addonEnabled = subFolder != null ? null : addonList.IsEnabled(keyForAddonList);

            AddonAttribute newItem = new(addonEnabled, fileInfo.Name, item.Source, item.Info, item.Types)
            {
                ModifiedFiles = item.ModifiedFiles,
                SubFolder = subFolder,
                Tags = [.. item.Tags.OrderBy(x => x.Name)],

                CurrentActiveVariantId = item.ActiveId,
                Variants = item.Variants,
                ActiveVariant = item.Variants.FirstOrDefault(v => v.Id == item.ActiveId) ?? item.Variants.FirstOrDefault()
            };

            var baseName = Path.ChangeExtension(fileInfo.Name, null);
            if (newItem.IsSubscribed || baseName.All(char.IsDigit))
            {
                newItem.WorkShopID = baseName;
            }

            newItem.ModificationTime = fileInfo.LastWriteTime;
            newItem.CreationTime = fileInfo.CreationTime;
            newItem.FileSizeRaw = fileInfo.Length;
            
            newItem.IsInstalled = true;

            newItem.LoadLocalPreviewImage(Path.ChangeExtension(fileInfo.FullName, ".jpg"));

            _addonList.Add(newItem);
        }
        CheckConflicts();
        Addons.Refresh();

        SelectedAddon = null;

        if (scan.Failures.Count > 0 && !NekoSettings.Default.IgnoreVpkErrors)
        {
            var names = scan.Failures.Take(8).Select(f => f.FileName).ToList();
            string fileList = string.Join("\n", names);
            if (scan.Failures.Count > names.Count)
            {
                fileList += $"\n… (+{scan.Failures.Count - names.Count})";
            }

            string msg = string.Format(i18n["ReadVpkFailedMsg"], fileList, scan.Failures[0].Error);
            var result = await ShowCustomMessageBoxAsync(i18n["ReadVpkFailedTitle"], msg, ButtonEnum.YesNo, MsBox.Avalonia.Enums.Icon.Info);
            if (result == ButtonResult.No)
            {
                NekoSettings.Default.IgnoreVpkErrors = true;
                NekoSettings.Default.Save();
            }
        }
    }

    private static ScanResult ScanAddonFiles(DirectoryInfo addonDir, DirectoryInfo workshopDir, bool folderMode, string? subFolder)
    {
        var scan = new ScanResult();
        List<FileInfo> files;

        if (folderMode)
        {
            foreach (var f in addonDir.GetFiles("*.vpk")) RegisterWorkshopId(scan.WorkshopIds, scan.WorkshopPaths, f, false);
            if (workshopDir.Exists)
            {
                foreach (var f in workshopDir.GetFiles("*.vpk")) RegisterWorkshopId(scan.WorkshopIds, scan.WorkshopPaths, f, true);
            }

            if (subFolder != null)
            {
                var folderDir = new DirectoryInfo(Path.Join(addonDir.FullName, subFolder));
                files = folderDir.Exists ? folderDir.GetFiles("*.vpk").ToList() : new List<FileInfo>();
            }
            else
            {
                files = new List<FileInfo>();
            }
        }
        else
        {
            files = addonDir.GetFiles("*.vpk").ToList();
            if (workshopDir.Exists)
                files.AddRange(workshopDir.GetFiles("*.vpk"));
        }

        foreach (FileInfo fileInfo in files)
        {
            AddonSource addonSource = AddonSource.Local;
            if (subFolder == null && fileInfo.Directory!.Name == workshopDir.Name)
            {
                addonSource = AddonSource.WorkShop;
            }

            if (!folderMode)
            {
                var idName = Path.ChangeExtension(fileInfo.Name, null);
                if (!string.IsNullOrEmpty(idName) && (addonSource == AddonSource.WorkShop || idName.All(char.IsDigit)))
                {
                    scan.WorkshopIds.Add(idName);
                    scan.WorkshopPaths[idName] = fileInfo.FullName;
                }
            }

            using Package pak = new();
            try
            {
                pak.Read(fileInfo.FullName);
            } 
            catch(Exception ex)
            {
                App.Logger.Warn(ex, $"读取 VPK 失败: {fileInfo.FullName}");
                scan.Failures.Add((fileInfo.Name, ex.Message));
                continue; 
            }

            List<AssetTag> tags = [];

            Func<string, bool, bool> checkPath = (p, isHidden) => {
                if (TaggedAssets.GetAssetTag(p, isHidden) is AssetTag tag)
                {
                    if (!tags.Contains(tag))
                        tags.Add(tag);
                }
                return false;
            };

            if (pak.Version != 1 && pak.Version != 2)
            {
                tags.Add(TaggedAssets.GetOrAddVersionTag(pak.Version));
            }

            PackageEntry? addonInfoEntry = null;
            List<string> neko7zIds = [];

            foreach (var entity in pak.Entries)
            {
                foreach (var file in entity.Value)
                {
                    if (file.GetFullPath() == "addoninfo.txt")
                    {
                        addonInfoEntry = file;
                    }
                    else if (file.TypeName == "neko7z")
                    {
                        if (!string.IsNullOrEmpty(file.FileName) && file.FileName.All(char.IsDigit))
                        {
                            if (!neko7zIds.Contains(file.FileName))
                                neko7zIds.Add(file.FileName);
                        }
                    }
                }
            }

            AddonInfo? addonInfo = null;
            if (addonInfoEntry != null)
            {
                try
                {
                    pak.ReadEntry(addonInfoEntry, out byte[] addonInfoContents);
                    addonInfo = AddonInfo.Load(addonInfoContents);
                }
                catch (Exception)
                {
                }
            }
            addonInfo ??= new();

            string activeId = "0";
            List<NekoVariant> variants = [];

            if (neko7zIds.Count > 1)
            {
                activeId = addonInfo.NekoVpkActive7z ?? "0";
                
                if (!neko7zIds.Contains(activeId))
                {
                    activeId = neko7zIds.OrderBy(x => int.TryParse(x, out int i) ? i : int.MaxValue).FirstOrDefault() ?? "0";
                }

                Dictionary<string, string> variantNames = [];
                if (!string.IsNullOrEmpty(addonInfo.NekoVpk7zName))
                {
                    var parts = addonInfo.NekoVpk7zName.Split('|');
                    foreach (var part in parts)
                    {
                        var kv = part.Split('=');
                        if (kv.Length == 2)
                        {
                            variantNames[kv[0].Trim()] = kv[1].Trim();
                        }
                    }
                }

                foreach (var id in neko7zIds.OrderBy(x => int.TryParse(x, out int i) ? i : int.MaxValue))
                {
                    variantNames.TryGetValue(id, out string? name);
                    variants.Add(new NekoVariant(id, name ?? ""));
                }
            }
            else if (neko7zIds.Count == 1)
            {
                activeId = neko7zIds[0];
            }

            HashSet<string> modifiedFiles = new(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in pak.Entries)
            {
                foreach (var file in entity.Value)
                {
                    var path = file.GetFullPath();
                    
                    if (path.Equals("addoninfo.txt", StringComparison.OrdinalIgnoreCase) || 
                        path.Equals("addonimage.jpg", StringComparison.OrdinalIgnoreCase)) 
                        continue;

                    if (file.TypeName == "neko7z")
                    {
                        if (file.FileName == activeId)
                        {
                            try
                            {
                                pak.ReadEntry(file, out byte[] neko7zBytes);
                                using var neko7zStream = new MemoryStream(neko7zBytes);
                                using var extractor = new SevenZipExtractor(neko7zStream);
                                foreach (var zipFile in extractor.ArchiveFileNames)
                                {
                                    checkPath(zipFile, false);
                                    modifiedFiles.Add(zipFile);
                                }
                            }
                            catch (Exception ex)
                            {
                                App.Logger.Warn(ex, $"读取内嵌 7z 失败: {fileInfo.FullName}");
                            }
                        }
                    }
                    else
                    {
                        checkPath(path, true);
                        modifiedFiles.Add(path);
                    }
                }
            }

            string types = string.Empty;
            foreach (var t in tags)
            {
                if (t.Type is null) { continue; }
                foreach (var t2 in t.Type)
                {
                    if (!types.Contains(t2))
                    {
                        if (types.Length > 0)
                            types += $", {t2}";
                        else
                            types = t2;
                    }
                }
            }

            scan.Addons.Add(new ScannedAddon(fileInfo, addonSource, addonInfo, types, modifiedFiles, tags, activeId, variants));
        }

        return scan;
    }

    private void RegisterLocalWorkshopId(FileInfo file, bool inWorkshopDir)
        => RegisterWorkshopId(_localWorkshopIds, _localWorkshopFilePaths, file, inWorkshopDir);

    private static void RegisterWorkshopId(HashSet<string> ids, Dictionary<string, string> paths, FileInfo file, bool inWorkshopDir)
    {
        var baseName = Path.GetFileNameWithoutExtension(file.Name);
        if (string.IsNullOrEmpty(baseName)) return;
        if (!inWorkshopDir && !baseName.All(char.IsDigit)) return;

        ids.Add(baseName);
        paths[baseName] = file.FullName;
    }

    private void ClearFolderDetail()
    {
        var previous = _folderDetail;
        FolderDetail = null;
        previous?.Icon?.Dispose();
    }

    public void UpdateFolderDetail(IReadOnlyList<AddonFolderItem> selected)
    {
        var folders = selected.Where(f => f.IsFolder).ToList();
        var previous = _folderDetail;

        if (folders.Count == 0)
        {
            FolderDetail = null;
        }
        else
        {
            string root = Path.Join(GameDir, "addons");
            long size = 0;
            int vpkCount = 0;
            foreach (var folder in folders)
            {
                size += AddonFolders.GetVpkSize(Path.Join(root, folder.Name));
                vpkCount += folder.ItemCount;
            }

            if (folders.Count == 1)
            {
                string path = Path.Join(root, folders[0].Name);
                string modified = "";
                try { modified = Directory.GetLastWriteTime(path).ToString("g"); }
                catch (Exception) { }

                FolderDetail = new FolderDetailInfo
                {
                    Title = folders[0].Name,
                    IsSingle = true,
                    ModifiedText = modified,
                    SizeText = AddonFolders.FormatSize(size),
                    VpkCountText = vpkCount.ToString(),
                    Icon = LoadFolderIcon(path)
                };
            }
            else
            {
                FolderDetail = new FolderDetailInfo
                {
                    Title = string.Format(i18n["FolderSelectedCount"], folders.Count),
                    IsSingle = false,
                    SizeText = AddonFolders.FormatSize(size),
                    VpkCountText = vpkCount.ToString()
                };
            }
        }

        previous?.Icon?.Dispose();
    }

    private static Avalonia.Media.Imaging.Bitmap? LoadFolderIcon(string folderPath)
    {
        string? iconPath = AddonFolders.FindCustomIconPath(folderPath);
        if (iconPath == null) return null;

        try
        {
            using var stream = File.OpenRead(iconPath);
            return Avalonia.Media.Imaging.Bitmap.DecodeToHeight(stream, 256);
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
            return null;
        }
    }

    private void RefreshFolderItems(DirectoryInfo addonDir)
    {
        ClearFolderDetail();
        FolderItems.Clear();
        foreach (var dir in AddonFolders.GetVisibleFolders(addonDir))
        {
            int count;
            try { count = dir.GetFiles("*.vpk").Length; }
            catch (Exception) { count = 0; }

            FolderItems.Add(new AddonFolderItem { Name = dir.Name, ItemCount = count });
        }
        FolderItems.Add(new AddonFolderItem { IsAddTile = true });
    }

    public void EnterFolder(string folderName)
    {
        if (!IsFolderMode || IsOnlineMode) return;
        _currentFolder = folderName;
        ClearFolderDetail();
        NotifyFolderStateChanged();
        LoadAddons();
    }

    public void ExitFolder()
    {
        if (_currentFolder == null) return;
        _currentFolder = null;
        ClearFolderDetail();
        NotifyFolderStateChanged();
        LoadAddons();
    }

    public string? CreateFolder(string? rawName)
    {
        var name = (rawName ?? string.Empty).Trim();
        var errorKey = AddonFolders.ValidateName(name);
        if (errorKey != null) return i18n[errorKey];

        try
        {
            var addonDir = new DirectoryInfo(Path.Join(GameDir, "addons"));
            if (!addonDir.Exists) return string.Format(i18n["AddonDirNotFoundMsg"], addonDir.FullName);

            var target = Path.Join(addonDir.FullName, name);
            if (Directory.Exists(target) || File.Exists(target)) return i18n["FolderNameExists"];

            Directory.CreateDirectory(target);
            RefreshFolderItems(addonDir);
            return null;
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
            return string.Format(i18n["CreateFolderFailedMsg"], ex.Message);
        }
    }

    public string? RenameFolder(string oldName, string? rawNewName)
    {
        var newName = (rawNewName ?? string.Empty).Trim();
        var errorKey = AddonFolders.ValidateName(newName);
        if (errorKey != null) return i18n[errorKey];

        try
        {
            var root = Path.Join(GameDir, "addons");
            var from = Path.Join(root, oldName);
            var to = Path.Join(root, newName);
            if (!Directory.Exists(from)) return string.Format(i18n["AddonDirNotFoundMsg"], from);
            if (string.Equals(oldName, newName, StringComparison.Ordinal)) return null;

            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
            {
                var tmp = Path.Join(root, newName + "." + Guid.NewGuid().ToString("N") + ".tmp");
                Directory.Move(from, tmp);
                Directory.Move(tmp, to);
            }
            else
            {
                if (Directory.Exists(to) || File.Exists(to)) return i18n["FolderNameExists"];
                Directory.Move(from, to);
            }

            RefreshFolderItems(new DirectoryInfo(root));
            return null;
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
            return string.Format(i18n["RenameFolderFailedMsg"], ex.Message);
        }
    }

    public int CountFolderFiles(string folderName)
    {
        try
        {
            return Directory.EnumerateFiles(Path.Join(GameDir, "addons", folderName), "*", SearchOption.AllDirectories).Count();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public string? DeleteFolder(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName) || AddonFolders.IsReservedName(folderName))
            return string.Format(i18n["DeleteFolderFailedMsg"], folderName);

        var root = Path.Join(GameDir, "addons");
        try
        {
            var path = Path.Join(root, folderName);
            if (Directory.Exists(path))
            {
                AddonFolders.ClearReadOnlyRecursive(path);
                Directory.Delete(path, true);
            }
            return null;
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
            return string.Format(i18n["DeleteFolderFailedMsg"], ex.Message);
        }
        finally
        {
            RefreshFolderItems(new DirectoryInfo(root));
        }
    }

    public List<string> GetMoveTargetFolders(string? exclude = null)
        => GetMoveTargetFolders(exclude == null ? Array.Empty<string>() : new[] { exclude });

    public List<string> GetMoveTargetFolders(IEnumerable<string> excludes)
    {
        var addonDir = new DirectoryInfo(Path.Join(GameDir, "addons"));
        if (!addonDir.Exists) return [];

        var excluded = new HashSet<string>(excludes, StringComparer.OrdinalIgnoreCase);
        return AddonFolders.GetVisibleFolders(addonDir)
            .Where(d => !excluded.Contains(d.Name))
            .Where(d => AddonFolders.IsStorageFolder(d))
            .Select(d => d.Name)
            .ToList();
    }

    private static void MoveVpkWithSidecars(string src, string dest, bool replaceExisting)
    {
        File.Move(src, dest, replaceExisting);

        if (replaceExisting)
        {
            DeleteIfExists(Path.ChangeExtension(dest, ".jpg"));
            DeleteIfExists(dest + ".nekobak");
        }

        MoveSidecarFile(Path.ChangeExtension(src, ".jpg"), Path.ChangeExtension(dest, ".jpg"));
        MoveSidecarFile(src + ".nekobak", dest + ".nekobak");
    }

    public static string MoveKey(string folder, string fileName) => folder + "\\" + fileName;

    public List<(string Folder, string FileName)> GetFolderMoveConflicts(IEnumerable<string> sourceFolders, string? targetFolder)
    {
        List<(string, string)> conflicts = [];
        string root = Path.Join(GameDir, "addons");
        string targetDir = targetFolder == null ? root : Path.Join(root, targetFolder);

        foreach (var folder in sourceFolders)
        {
            if (string.Equals(folder, targetFolder, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                foreach (var file in new DirectoryInfo(Path.Join(root, folder)).GetFiles("*.vpk"))
                {
                    if (File.Exists(Path.Join(targetDir, file.Name))) conflicts.Add((folder, file.Name));
                }
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex);
            }
        }
        return conflicts;
    }

    public List<string> MoveFolderContents(IEnumerable<string> sourceFolders, string? targetFolder,
        ISet<string> overwrite, ISet<string> declined)
    {
        List<string> errors = [];
        string root = Path.Join(GameDir, "addons");
        string targetDir = targetFolder == null ? root : Path.Join(root, targetFolder);
        if (!Directory.Exists(targetDir))
        {
            errors.Add(string.Format(i18n["AddonDirNotFoundMsg"], targetDir));
            return errors;
        }

        foreach (var folder in sourceFolders)
        {
            if (string.Equals(folder, targetFolder, StringComparison.OrdinalIgnoreCase)) continue;

            FileInfo[] files;
            try
            {
                files = new DirectoryInfo(Path.Join(root, folder)).GetFiles("*.vpk");
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex);
                errors.Add(string.Format(i18n["MoveFailedMsg"], folder, ex.Message));
                continue;
            }

            foreach (var file in files)
            {
                string key = MoveKey(folder, file.Name);
                string dest = Path.Join(targetDir, file.Name);
                try
                {
                    bool exists = File.Exists(dest);
                    if (exists)
                    {
                        if (declined.Contains(key)) continue;
                        if (!overwrite.Contains(key))
                        {
                            errors.Add(string.Format(i18n["MoveFileExistsMsg"], file.Name));
                            continue;
                        }
                    }

                    MoveVpkWithSidecars(file.FullName, dest, exists);
                    if (targetFolder == null) RegisterLocalWorkshopId(new FileInfo(dest), false);
                }
                catch (Exception ex)
                {
                    App.Logger.Error(ex);
                    errors.Add(string.Format(i18n["MoveFailedMsg"], file.Name, ex.Message));
                }
            }
        }

        RefreshFolderItems(new DirectoryInfo(root));
        return errors;
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
        }
    }

    public bool MoveTargetExists(AddonAttribute addon, string? folderName)
    {
        string targetDir = folderName == null
            ? Path.Join(GameDir, "addons")
            : Path.Join(GameDir, "addons", folderName);
        return File.Exists(Path.Join(targetDir, addon.FileName));
    }

    private static void MoveSidecarFile(string from, string to)
    {
        try
        {
            if (File.Exists(from) && !File.Exists(to)) File.Move(from, to);
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
        }
    }

    public List<string> MoveAddonsToFolder(IEnumerable<AddonAttribute> addons, string? folderName, bool overwrite = false)
    {
        List<string> errors = [];
        string targetDir = folderName == null
            ? Path.Join(GameDir, "addons")
            : Path.Join(GameDir, "addons", folderName);
        if (!Directory.Exists(targetDir))
        {
            errors.Add(string.Format(i18n["AddonDirNotFoundMsg"], targetDir));
            return errors;
        }

        bool anyMoved = false;
        foreach (var addon in addons.ToList())
        {
            try
            {
                string src = addon.GetAbsolutePath(GameDir);
                string dest = Path.Join(targetDir, addon.FileName);
                bool exists = File.Exists(dest);
                if (exists && !overwrite)
                {
                    errors.Add(string.Format(i18n["MoveFileExistsMsg"], addon.FileName));
                    continue;
                }

                MoveVpkWithSidecars(src, dest, exists);

                if (!string.IsNullOrEmpty(addon.WorkShopID)
                    && _localWorkshopFilePaths.TryGetValue(addon.WorkShopID, out var registered)
                    && string.Equals(registered, src, StringComparison.OrdinalIgnoreCase))
                {
                    _localWorkshopIds.Remove(addon.WorkShopID);
                    _localWorkshopFilePaths.Remove(addon.WorkShopID);
                }
                if (folderName == null) RegisterLocalWorkshopId(new FileInfo(dest), false);

                AddonAttribute.dirty.Remove(addon);
                _addonList.Remove(addon);
                anyMoved = true;
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex);
                errors.Add(string.Format(i18n["MoveFailedMsg"], addon.FileName, ex.Message));
            }
        }

        if (anyMoved)
        {
            CheckConflicts();
            Addons.Refresh();
            SelectedAddon = null;
        }
        return errors;
    }

    public void CheckConflicts()
    {
        if (!NekoSettings.Default.EnableConflictDetection)
        {
            foreach (var addon in _addonList)
            {
                addon.HasConflict = false;
            }
            return; 
        }
        if (IsOnlineMode) return;

        foreach (var addon in _addonList)
        {
            addon.HasConflict = false;
        }

        var enabledAddons = _addonList.Where(a => a.Enable == true).ToList();

        for (int i = 0; i < enabledAddons.Count; i++)
        {
            for (int j = i + 1; j < enabledAddons.Count; j++)
            {
                if (enabledAddons[i].ModifiedFiles.Overlaps(enabledAddons[j].ModifiedFiles))
                {
                    enabledAddons[i].HasConflict = true;
                    enabledAddons[j].HasConflict = true;
                }
            }
        }
    }

    bool AddonsFilter(object obj)
    {
        if (obj is AddonAttribute att)
        {
            if (IsOnlineMode && IsInCollectionDetail)
            {
                var selectedTags = WorkshopTagCategories
                    .SelectMany(c => c.Tags.Append(c.MainTag))
                    .Where(t => t.IsSelected)
                    .Select(t => t.Name)
                    .ToList();

                if (selectedTags.Count > 0)
                {
                    foreach (var tag in selectedTags)
                    {
                        if (att.Type == null || !att.Type.Contains(tag, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                }
            }

            if (String.IsNullOrEmpty(SearchKeywords))
            {
                return true;
            }

            if (IsOnlineMode && !IsInCollectionDetail)
            {
                return true;
            }

            foreach (var str in SearchKeywords.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                bool hit =
                    att.Tags.Any(t => t.Name.Contains(str, StringComparison.OrdinalIgnoreCase))
                    || att.Title.Contains(str, StringComparison.OrdinalIgnoreCase)
                    || (att.Author is not null && att.Author.Contains(str, StringComparison.OrdinalIgnoreCase))
                    || att.FileName.Contains(str, StringComparison.OrdinalIgnoreCase)
                    || att.Type.Contains(str, StringComparison.OrdinalIgnoreCase);

                if (!hit) return false;
            }
            return true;

        }
        return false;
    }
}

public class SteamApiResponse
{
    [JsonPropertyName("response")]
    public SteamQueryFilesResponse? Response { get; set; }
}

public class SteamQueryFilesResponse
{
    [JsonPropertyName("total")]
    public object? TotalRaw { get; set; }

    [JsonPropertyName("publishedfiledetails")]
    public List<SteamPublishedFileDetails>? PublishedFileDetails { get; set; }
}

public class SteamPublishedFileDetails
{
    [JsonPropertyName("publishedfileid")]
    public string? PublishedFileId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("preview_url")]
    public string? PreviewUrl { get; set; }

    [JsonPropertyName("file_description")]
    public string? Description { get; set; }

    [JsonPropertyName("tags")]
    public List<SteamTag>? Tags { get; set; }

    [JsonPropertyName("creator")]
    public string? Creator { get; set; } 

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    [JsonPropertyName("file_url")]
    public string? FileUrl { get; set; }

    [JsonPropertyName("time_updated")]
    public long TimeUpdated { get; set; }

    [JsonPropertyName("time_created")]
    public long TimeCreated { get; set; }

    [JsonPropertyName("favorited")]
    public int Favorited { get; set; }

    [JsonPropertyName("views")]
    public int Views { get; set; }

    [JsonPropertyName("file_size")]
    public object? FileSizeRaw { get; set; } 
    
    [JsonIgnore]
    public string FileSizeStr => FileSizeRaw?.ToString() ?? "0";

    [JsonPropertyName("subscriptions")]
    public int Subscriptions { get; set; }

    [JsonPropertyName("children")]
    public List<SteamPublishedFileChild>? Children { get; set; }

    [JsonPropertyName("vote_data")]
    public SteamVoteData? VoteData { get; set; }
}

public class SteamVoteData
{
    [JsonPropertyName("score")]
    public float Score { get; set; }

    [JsonPropertyName("votes_up")]
    public int VotesUp { get; set; }

    [JsonPropertyName("votes_down")]
    public int VotesDown { get; set; }
}

public class SteamPublishedFileChild
{
    [JsonPropertyName("publishedfileid")]
    public string? PublishedFileId { get; set; }
}

public class SteamCollectionItem : ReactiveObject
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string PreviewUrl { get; set; } = "";
    public string Description { get; set; } = "";

    private string _descriptionBBCode = "";
    public string DescriptionBBCode
    {
        get => _descriptionBBCode;
        set => this.RaiseAndSetIfChanged(ref _descriptionBBCode, value);
    }

    private string _tags = "";
    public string Tags
    {
        get => _tags;
        set => this.RaiseAndSetIfChanged(ref _tags, value);
    }

    private string _timeCreatedStr = "";
    public string TimeCreatedStr
    {
        get => _timeCreatedStr;
        set => this.RaiseAndSetIfChanged(ref _timeCreatedStr, value);
    }

    private string _timeUpdatedStr = "";
    public string TimeUpdatedStr
    {
        get => _timeUpdatedStr;
        set => this.RaiseAndSetIfChanged(ref _timeUpdatedStr, value);
    }

    private int _favorited;
    public int Favorited
    {
        get => _favorited;
        set => this.RaiseAndSetIfChanged(ref _favorited, value);
    }

    private int _views;
    public int Views
    {
        get => _views;
        set => this.RaiseAndSetIfChanged(ref _views, value);
    }

    private int _subscriptions;
    public int Subscriptions
    {
        get => _subscriptions;
        set => this.RaiseAndSetIfChanged(ref _subscriptions, value);
    }

    private int _itemCount;
    public int ItemCount
    {
        get => _itemCount;
        set
        {
            this.RaiseAndSetIfChanged(ref _itemCount, value);
            this.RaisePropertyChanged(nameof(ItemCountString));
        }
    }
    public string ItemCountString => string.Format(NekoVpk.Lang.I18nManager.Instance["CollectionItemCount"], ItemCount);
    public string CreatorId { get; set; } = "";
    public List<SteamPublishedFileChild>? Children { get; set; }
    public string Stars { get; set; } = "";
    public bool HasStars => !string.IsNullOrEmpty(Stars);
    public string DescriptionPreview { get; set; } = "";
    
    private string _authorName = "";
    public string AuthorName 
    {
        get => _authorName;
        set => this.RaiseAndSetIfChanged(ref _authorName, value);
    }

    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set => this.RaiseAndSetIfChanged(ref _isInstalled, value);
    }

    private Avalonia.Media.Imaging.Bitmap? _previewBitmap;
    public Avalonia.Media.Imaging.Bitmap? PreviewBitmap
    {
        get => _previewBitmap;
        set => this.RaiseAndSetIfChanged(ref _previewBitmap, value);
    }
    
    public void LoadImage()
    {
        if (!string.IsNullOrEmpty(PreviewUrl) && _previewBitmap == null)
        {
            Task.Run(async () =>
            {
                try
                {
                    var bytes = await ImageHttp.GetBytesAsync(PreviewUrl);
                    using var ms = new MemoryStream(bytes);
                    var bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToHeight(ms, 120);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => PreviewBitmap = bitmap);
                }
                catch { }
            });
        }
    }
}

public class SteamSortOption
{
    public string NameKey { get; set; } = "";
    public string FallbackName { get; set; } = "";
    public int QueryType { get; set; }
    
    public string DisplayName 
    {
        get
        {
            var val = NekoVpk.Lang.I18nManager.Instance[NameKey];
            return (string.IsNullOrEmpty(val) || val == NameKey) ? FallbackName : val;
        }
    }
}

public class SteamTag
{
    [JsonPropertyName("tag")]
    public string? Tag { get; set; }
}

public class SteamUserResponse
{
    [JsonPropertyName("response")]
    public SteamUserResponseData? Response { get; set; }
}

public class SteamUserResponseData
{
    [JsonPropertyName("players")]
    public List<SteamPlayer>? Players { get; set; }
}

public class SteamPlayer
{
    [JsonPropertyName("steamid")]
    public string? SteamId { get; set; }

    [JsonPropertyName("personaname")]
    public string? PersonaName { get; set; }
}