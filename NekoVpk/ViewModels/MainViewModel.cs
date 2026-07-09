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
    private CancellationTokenSource? _downloadCts;

    private HashSet<string> _localWorkshopIds = [];

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

    public ObservableCollection<SteamSortOption> SortOptions { get; } = [];
    
    private SteamSortOption? _selectedSortOption;
    public SteamSortOption? SelectedSortOption
    {
        get => _selectedSortOption;
        set 
        { 
            this.RaiseAndSetIfChanged(ref _selectedSortOption, value);
            if (IsOnlineMode && !IsDownloading && !IsInCollectionDetail) 
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

    public bool ShowDataGrid => !IsCollectionMode || IsInCollectionDetail;
    public bool ShowCollectionList => IsCollectionMode && !IsInCollectionDetail;

    private bool _isCollectionMode;
    public bool IsCollectionMode
    {
        get => _isCollectionMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _isCollectionMode, value);
            if (value) IsInCollectionDetail = false;
            this.RaisePropertyChanged(nameof(ShowDataGrid));
            this.RaisePropertyChanged(nameof(ShowCollectionList));
            if (IsOnlineMode && !IsDownloading) _ = SearchWorkshopAsync();
        }
    }

    private bool _isInCollectionDetail;
    public bool IsInCollectionDetail
    {
        get => _isInCollectionDetail;
        set
        {
            this.RaiseAndSetIfChanged(ref _isInCollectionDetail, value);
            this.RaisePropertyChanged(nameof(ShowDataGrid));
            this.RaisePropertyChanged(nameof(ShowCollectionList));
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
        }
    }

    private string _searchWatermark = "Search";

    private bool _showColumnTag = true;
    public bool ShowColumnTag 
    { 
        get => _showColumnTag; 
        set { this.RaiseAndSetIfChanged(ref _showColumnTag, value); this.RaisePropertyChanged(nameof(IsTagColumnVisible)); } 
    }

    private bool _showColumnType = true;
    public bool ShowColumnType 
    { 
        get => _showColumnType; 
        set { this.RaiseAndSetIfChanged(ref _showColumnType, value); this.RaisePropertyChanged(nameof(IsTypeColumnVisible)); } 
    }

    private bool _showColumnAddedTime = true;
    public bool ShowColumnAddedTime 
    { 
        get => _showColumnAddedTime; 
        set { this.RaiseAndSetIfChanged(ref _showColumnAddedTime, value); this.RaisePropertyChanged(nameof(IsAddedTimeColumnVisible)); } 
    }

    private bool _showColumnSize = true;
    public bool ShowColumnSize 
    { 
        get => _showColumnSize; 
        set { this.RaiseAndSetIfChanged(ref _showColumnSize, value); this.RaisePropertyChanged(nameof(IsSizeColumnVisible)); } 
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

    private async void ToggleOnlineMode()
    {
        if (NekoSettings.Default.ClearSearchAfterDownload)
        {
            SearchKeywords = string.Empty;
        }

        if (IsOnlineMode)
        {
            if (string.IsNullOrWhiteSpace(NekoSettings.Default.SteamApiKey))
            {
                await ShowCustomMessageBoxAsync(
                i18n["MissingKeyTitle"], 
                i18n["MissingKeyMsg"], 
                ButtonEnum.Ok, 
                MsBox.Avalonia.Enums.Icon.Warning);
                
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
        set => this.RaiseAndSetIfChanged(ref _backgroundImage, value);
    }

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
                    _ = SearchWorkshopAsync();
                }
            };

            foreach (var tag in category.Tags)
            {
                tag.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SelectableTag.IsSelected) && IsOnlineMode && !IsDownloading)
                    {
                        _ = SearchWorkshopAsync();
                    }
                };
            }
        }
    }

    public async Task SearchWorkshopAsync()
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

        if (string.IsNullOrWhiteSpace(SearchKeywords) && selectedTags.Count == 0) return;

        try
        {
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
                string detailsUrl = $"https://api.steampowered.com/IPublishedFileService/GetDetails/v1/?key={apiKey}&publishedfileids[0]={exactId}&return_children=1&return_vote_data=1";
                var detailJson = await _httpClient.GetStringAsync(detailsUrl);
                var detailResult = JsonSerializer.Deserialize<SteamApiResponse>(detailJson);
                var item = detailResult?.Response?.PublishedFileDetails?.FirstOrDefault();

                if (item != null && !string.IsNullOrEmpty(item.Title))
                {
                    if (IsCollectionMode)
                    {
                        int stars = item.VoteData != null ? (int)Math.Round(item.VoteData.Score * 5) : 0;
                        string starStr = new string('★', stars) + new string('☆', 5 - stars);

                        var coll = new SteamCollectionItem
                        {
                            Id = item.PublishedFileId ?? "",
                            Title = item.Title ?? "",
                            PreviewUrl = item.PreviewUrl ?? "",
                            Description = item.Description?.Replace("\r\n", " ").Replace("\n", " ") ?? "",
                            DescriptionBBCode = item.Description ?? "",
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
                                if (!string.IsNullOrEmpty(child.PublishedFileId) && !_localWorkshopIds.Contains(child.PublishedFileId))
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
                        
                        if (!string.IsNullOrEmpty(attribute.WorkShopID) && _localWorkshopIds.Contains(attribute.WorkShopID)) attribute.IsInstalled = true;
                        if (long.TryParse(item.FileSizeStr, out long sizeBytes)) attribute.FileSizeRaw = sizeBytes;
                        attribute.Subscriptions = item.Subscriptions;
                        if (item.TimeUpdated > 0) attribute.LastUpdate = DateTimeOffset.FromUnixTimeSeconds(item.TimeUpdated).DateTime.ToLocalTime();

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
                                        if (NekoSettings.Default.ClearSearchAfterDownload) SearchKeywords = string.Empty;
                                        IsOnlineMode = false;
                                    });
                                }
                            });
                        }
                    }
                    return; 
                }
            }

            var appId = 550;
            var searchText = Uri.EscapeDataString(keyword);
            int fileType = IsCollectionMode ? 1 : 0;
            int queryType = SelectedSortOption?.QueryType ?? 0;

            string url = $"https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/?" +
             $"key={apiKey}&appid={appId}&search_text={searchText}&" +
             $"return_tags=1&return_details=1&return_children=1&return_vote_data=1&numperpage=50&page=0&query_type={queryType}" +
             $"&match_all_tags=0&filetype={fileType}";

            if (!IsCollectionMode)
            {
                for (int i = 0; i < selectedTags.Count; i++)
                {
                    url += $"&requiredtags[{i}]={Uri.EscapeDataString(selectedTags[i])}";
                }
            }

            var jsonStr = await _httpClient.GetStringAsync(url);
            var result = JsonSerializer.Deserialize<SteamApiResponse>(jsonStr);

            List<string> creatorIds = [];

            if (result?.Response?.PublishedFileDetails != null)
            {
                if (IsCollectionMode)
                {
                    foreach (var item in result.Response.PublishedFileDetails)
                    {
                        int stars = item.VoteData != null ? (int)Math.Round(item.VoteData.Score * 5) : 0;
                        string starStr = new string('★', stars) + new string('☆', 5 - stars);

                        var coll = new SteamCollectionItem
                        {
                            Id = item.PublishedFileId ?? "",
                            Title = item.Title ?? "",
                            PreviewUrl = item.PreviewUrl ?? "",
                            Description = item.Description?.Replace("\r\n", " ").Replace("\n", " ") ?? "",
                            DescriptionBBCode = item.Description ?? "",
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
                                if (!string.IsNullOrEmpty(child.PublishedFileId) && !_localWorkshopIds.Contains(child.PublishedFileId))
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
                        
                        if (!string.IsNullOrEmpty(attribute.WorkShopID) && _localWorkshopIds.Contains(attribute.WorkShopID)) attribute.IsInstalled = true;
                        if (long.TryParse(item.FileSizeStr, out long sizeBytes)) attribute.FileSizeRaw = sizeBytes;
                        attribute.Subscriptions = item.Subscriptions;
                        if (item.TimeUpdated > 0) attribute.LastUpdate = DateTimeOffset.FromUnixTimeSeconds(item.TimeUpdated).DateTime.ToLocalTime();

                        if (!string.IsNullOrEmpty(item.Creator)) creatorIds.Add(item.Creator);
                        _addonList.Add(attribute);
                    }
                }
            }
            Addons.Refresh();

            if (creatorIds.Count > 0)
            {
                await UpdateAuthorNamesAsync(creatorIds.Distinct().ToList());
            }
        }
        catch (Exception ex)
        {
            await ShowCustomMessageBoxAsync(i18n["SearchFailedTitle"], $"{ex.Message}", ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
        }
    }

    private string _savedSearchKeyword = string.Empty;

    public async Task EnterCollectionAsync(SteamCollectionItem collection)
    {
        try
        {
            _savedSearchKeyword = SearchKeywords ?? string.Empty;
            SearchKeywords = string.Empty;
            IsInCollectionDetail = true;
            _addonList.Clear();
            Addons.Refresh();

            var apiKey = NekoSettings.Default.SteamApiKey;

            if (collection.Children != null && collection.Children.Count > 0)
            {
                var childIds = collection.Children.Select(c => c.PublishedFileId).Where(id => !string.IsNullOrEmpty(id)).ToList();
                for (int i = 0; i < childIds.Count; i += 50)
                {
                    var batch = childIds.Skip(i).Take(50).ToList();
                    string batchUrl = $"https://api.steampowered.com/IPublishedFileService/GetDetails/v1/?key={apiKey}&return_details=1&return_tags=1";
                    for (int j = 0; j < batch.Count; j++)
                    {
                        batchUrl += $"&publishedfileids[{j}]={batch[j]}";
                    }
                    var batchJson = await _httpClient.GetStringAsync(batchUrl);
                    var batchResult = JsonSerializer.Deserialize<SteamApiResponse>(batchJson);
                    
                    if (batchResult?.Response?.PublishedFileDetails != null)
                    {
                        List<string> creatorIds = [];
                        foreach (var childItem in batchResult.Response.PublishedFileDetails)
                        {
                            var info = new AddonInfo { Title = childItem.Title ?? "", Author = childItem.Creator, Description = childItem.Description, Url0 = childItem.PreviewUrl };
                            string tagStr = childItem.Tags != null ? string.Join(", ", childItem.Tags.Select(t => t.Tag)) : "";

                            var attribute = new AddonAttribute(false, childItem.PublishedFileId + ".vpk", AddonSource.WorkShop, info, tagStr) { WorkShopID = childItem.PublishedFileId };
                            
                            if (!string.IsNullOrEmpty(attribute.WorkShopID) && _localWorkshopIds.Contains(attribute.WorkShopID)) attribute.IsInstalled = true;
                            if (long.TryParse(childItem.FileSizeStr, out long sizeBytes)) attribute.FileSizeRaw = sizeBytes;
                            attribute.Subscriptions = childItem.Subscriptions;
                            if (childItem.TimeUpdated > 0) attribute.LastUpdate = DateTimeOffset.FromUnixTimeSeconds(childItem.TimeUpdated).DateTime.ToLocalTime();

                            if (!string.IsNullOrEmpty(childItem.Creator)) creatorIds.Add(childItem.Creator);
                            _addonList.Add(attribute);
                        }
                        Addons.Refresh();
                        if (creatorIds.Count > 0) await UpdateAuthorNamesAsync(creatorIds.Distinct().ToList());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await ShowCustomMessageBoxAsync(i18n["SearchFailedTitle"], $"{ex.Message}", ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
            IsInCollectionDetail = false;
        }
    }

    public void ExitCollection()
    {
        IsInCollectionDetail = false;
        SearchKeywords = _savedSearchKeyword;
        _addonList.Clear();
        Addons.Refresh();
    }

    public async Task DownloadCollectionAsync(SteamCollectionItem collection)
    {
        var apiKey = NekoSettings.Default.SteamApiKey;
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
                            if (!string.IsNullOrEmpty(attribute.WorkShopID) && _localWorkshopIds.Contains(attribute.WorkShopID)) continue;
                            
                            await DownloadAddonAsync(attribute);
                        }
                    }
                }
            }

            if (collection.Children != null && collection.Children.Count > 0)
            {
                bool allInstalled = true;
                foreach (var child in collection.Children)
                {
                    if (!string.IsNullOrEmpty(child.PublishedFileId) && !_localWorkshopIds.Contains(child.PublishedFileId))
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
        _downloadCts?.Cancel();
    }

    public async Task<bool> DownloadAddonAsync(AddonAttribute addon)
    {
        if (addon.Source != AddonSource.WorkShop || string.IsNullOrEmpty(addon.WorkShopID)) return false;
        if (IsDownloading) return false;
        if (addon.IsInstalled) return false; 

        IsDownloading = true;
        DownloadProgress = 0;
        _downloadCts = new CancellationTokenSource();

        string? originalAuthor = addon.Author;

        try
        {
            var apiKey = NekoSettings.Default.SteamApiKey;
            string detailsUrl = $"https://api.steampowered.com/IPublishedFileService/GetDetails/v1/?key={apiKey}&publishedfileids[0]={addon.WorkShopID}";
            
            var jsonStr = await _httpClient.GetStringAsync(detailsUrl, _downloadCts.Token);
            var result = JsonSerializer.Deserialize<SteamApiResponse>(jsonStr);
            
            var details = result?.Response?.PublishedFileDetails?.FirstOrDefault();
            
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
            
            addon.UpdateAuthorName(NekoVpk.Lang.I18nManager.Instance["DownloadingState"]);

            using (var response = await _httpClient.GetAsync(details.FileUrl, HttpCompletionOption.ResponseHeadersRead, _downloadCts.Token))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                using (var contentStream = await response.Content.ReadAsStreamAsync(_downloadCts.Token))
                using (var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
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

            addon.IsInstalled = true;
            if (!string.IsNullOrEmpty(addon.WorkShopID))
            {
                _localWorkshopIds.Add(addon.WorkShopID);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            await ShowCustomMessageBoxAsync(NekoVpk.Lang.I18nManager.Instance["DownloadFailedTitle"], ex.Message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
            return false;
        }
        finally
        {
            IsDownloading = false;
            DownloadProgress = 0;
            _downloadCts = null;
            addon.UpdateAuthorName(originalAuthor);
        }
    }

    public void DeleteAddon(AddonAttribute addon)
    {
        string path = addon.GetAbsolutePath(GameDir);
        
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

        _addonList.Remove(addon);
        
        if (!string.IsNullOrEmpty(addon.WorkShopID))
        {
            _localWorkshopIds.Remove(addon.WorkShopID);
        }
        CheckConflicts();
        Addons.Refresh();
    }

    public void UpdateBackground()
    {
        var path = NekoSettings.Default.BackgroundImagePath;
        
        BackgroundImage?.Dispose();

        if (string.IsNullOrEmpty(path))
        {
            BackgroundImage = null;
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
        SteamGameLocator steamGameLocator = new();
        if (steamGameLocator.getIsSteamInstalled())
        {
            SteamGameLocator.GameStruct result = steamGameLocator.getGameInfoByFolder("Left 4 Dead 2");
            if (result.steamGameLocation != null)
            {
                return Path.Join(result.steamGameLocation, "left4dead2");
            }
        }
        return null;
    }

    public async void LoadAddons()
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

        _localWorkshopIds.Clear();

        var files = addonDir.GetFiles("*.vpk").ToList();
        if (workshopDir.Exists)
            files.AddRange(workshopDir.GetFiles("*.vpk"));

        AddonList addonList = new();
        try
        {
            addonList.Load(GameDir);
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
        }

        _addonList.Clear();
        foreach (FileInfo fileInfo in files)
        {
            bool? addonEnabled = null;
            AddonSource addonSource = AddonSource.Local;

            string keyForAddonList = fileInfo.Name;

            if (fileInfo.Directory!.Name == workshopDir.Name)
            {
                addonSource = AddonSource.WorkShop;
                keyForAddonList = "workshop\\" + fileInfo.Name;
            }
            else
            {
                addonSource = AddonSource.Local;
                keyForAddonList = fileInfo.Name;
            }

            addonEnabled = addonList.IsEnabled(keyForAddonList);

            Package pak = new();
            try
            {
                pak.Read(fileInfo.FullName);
            } 
            catch(Exception ex)
            {
                if (!NekoSettings.Default.IgnoreVpkErrors) 
                {
                    string msg = string.Format(i18n["ReadVpkFailedMsg"], fileInfo.Name, ex.Message);
                    var box = new CustomMessageBox(
                        i18n["ReadVpkFailedTitle"],
                        msg, 
                        ButtonEnum.YesNo, MsBox.Avalonia.Enums.Icon.Info);

                    ButtonResult result = ButtonResult.None;
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is not null)
                    {
                        result = await box.ShowDialog<ButtonResult>(desktop.MainWindow);
                    }

                    if (result == ButtonResult.No)
                    {
                        NekoSettings.Default.IgnoreVpkErrors = true;
                        NekoSettings.Default.Save(); 
                    }
                }
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
                string glob = $"VPK-Version-{pak.Version}";
                AssetTag? versionTag = TaggedAssets.GetAssetTag(glob, false);
                if (versionTag is null)
                {
                    TaggedAssets.Tags.Add(new AssetTagProperty("0x" + Convert.ToString(pak.Version, 16).ToUpper(), [Glob.Parse(glob)]));
                    versionTag = new AssetTag(TaggedAssets.Tags.Count - 1, false);
                }
                tags.Add(versionTag);
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
                    //App.Logger.Error($"加载模组信息出现错误 \"{fileInfo.FullName}\".\n{ex.Message}");
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
                            pak.ReadEntry(file, out byte[] neko7zBytes);
                            SevenZipExtractor extractor = new(new MemoryStream(neko7zBytes));
                            var archiveFileNames = extractor.ArchiveFileNames;
                            foreach (var zipFile in archiveFileNames)
                            {
                                checkPath(zipFile, false);
                                modifiedFiles.Add(zipFile);
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

            AddonAttribute newItem = new(addonEnabled, fileInfo.Name, addonSource, addonInfo, types)
            {
                ModifiedFiles = modifiedFiles,
                Tags = [.. tags.OrderBy(x => x.Name)],

                CurrentActiveVariantId = activeId,
                Variants = variants,
                ActiveVariant = variants.FirstOrDefault(v => v.Id == activeId) ?? variants.FirstOrDefault()
            };

            var baseName = Path.ChangeExtension(fileInfo.Name, null);
            if (newItem.IsSubscribed || baseName.All(char.IsDigit))
            {
                newItem.WorkShopID = baseName;
                
                if (!string.IsNullOrEmpty(newItem.WorkShopID))
                {
                    _localWorkshopIds.Add(newItem.WorkShopID);
                }
            }

            newItem.ModificationTime = fileInfo.LastWriteTime;
            newItem.CreationTime = fileInfo.CreationTime;
            newItem.FileSizeRaw = fileInfo.Length;
            
            newItem.IsInstalled = true;

            _addonList.Add(newItem);
        }
        CheckConflicts();
        Addons.Refresh();
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

            var keywordList = new List<string>(SearchKeywords.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            int match = 0;
            foreach (var str in keywordList)
            {
                foreach(var tag in att.Tags)
                {
                    if (tag.Name.Contains(str, StringComparison.OrdinalIgnoreCase))
                    {
                        match++; continue;
                    } 
                }

                if (match >= keywordList.Count) return true;
                if (att.Title.Contains(str, StringComparison.OrdinalIgnoreCase))
                {
                    match++; continue;
                }

                if (match >= keywordList.Count) return true;
                if (att.Author is not null 
                    && att.Author.Contains(str, StringComparison.OrdinalIgnoreCase))
                {
                    match++; continue;
                }

                if (match >= keywordList.Count) return true;
                if (att.FileName.Contains(str, StringComparison.OrdinalIgnoreCase))
                {
                    match++; continue;
                }

                if (match >= keywordList.Count) return true;
                if (att.Type.Contains(str, StringComparison.OrdinalIgnoreCase))
                {
                    match++; continue;
                }
            }

            if (match >= keywordList.Count) return true;

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
    public string Stars { get; set; } = "★★★★★";
    
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
                    using var client = new HttpClient();
                    var bytes = await client.GetByteArrayAsync(PreviewUrl);
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