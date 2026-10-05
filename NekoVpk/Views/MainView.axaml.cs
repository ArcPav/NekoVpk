using Avalonia.Controls;
using Avalonia.Media.Imaging;
using NekoVpk.Core;
using NekoVpk.ViewModels;
using SteamDatabase.ValvePak;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using SevenZip;
using System.Linq;
using Avalonia.Threading;
using MsBox.Avalonia.Enums;
using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using System.Text.RegularExpressions;
using Avalonia.VisualTree;

namespace NekoVpk.Views;

public partial class MainView : UserControl
{
    private static readonly HttpClient _imageHttpClient = new HttpClient();
    private CancellationTokenSource? _imageCts;
    private static readonly Dictionary<string, object> _imageCache = [];

    private long _lastDoubleClickTime = 0;

    private DispatcherTimer _columnWidthSaveTimer;

    private readonly NekoVpk.Lang.I18nManager i18n = NekoVpk.Lang.I18nManager.Instance;

    public MainView()
    {
        InitializeComponent();

        FolderGridView.AddHandler(InputElement.PointerPressedEvent, FolderGridView_PointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

        AddHandler(InputElement.PointerPressedEvent, MainView_PointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DropEvent, OnDrop);

        _columnWidthSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _columnWidthSaveTimer.Tick += (s, e) => 
        {
            _columnWidthSaveTimer.Stop();
            SaveColumnWidths();
        };

        if (AddonList?.Columns != null)
        {
            foreach (var col in AddonList.Columns)
            {
                if (col == null) continue;
                col.PropertyChanged += (s, e) => 
                {
                    if (e.Property.Name == "Width" || e.Property.Name == "ActualWidth")
                    {
                        if (NekoSettings.Default?.SaveColumnWidths == true)
                        {
                            _columnWidthSaveTimer.Stop();
                            _columnWidthSaveTimer.Start();
                        }
                    }
                };
            }
        }

        this.Loaded += (s, e) => 
        {
            LoadColumnWidths();
        };
    }

    public override void EndInit()
    {
        base.EndInit();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged -= ViewModel_PropertyChanged;
            vm.PropertyChanged += ViewModel_PropertyChanged;
            vm.SearchCompleted -= ViewModel_SearchCompleted;
            vm.SearchCompleted += ViewModel_SearchCompleted;
            
            ReloadAddonList();
        }
        base.OnDataContextChanged(e);
    }

    private void ViewModel_SearchCompleted(object? sender, EventArgs e)
    {
        if (NekoSettings.Default?.AutoSizeColumnsOnSearch == true)
        {
            AutoSizeAllColumns();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsOnlineMode) ||
            e.PropertyName == nameof(MainViewModel.IsCollectionMode) ||
            e.PropertyName == nameof(MainViewModel.IsInCollectionDetail))
        {
            ResetDetailPanel();
        }
    }

    private void ResetDetailPanel()
    {
        AddonList.SelectedItem = null;
        AddonDetailPanel.IsVisible = false;

        if (CollectionListView != null) CollectionListView.SelectedItem = null;
        if (CollectionDetailPanel != null) CollectionDetailPanel.IsVisible = false;

        AddonImage.ClearValue(AnimatedImage.Avalonia.ImageBehavior.AnimatedSourceProperty);
        AddonImage.Source = null;
        _imageCts?.Cancel();
        CancelAssetTagChange();
    }

    private void DataGrid_CurrentCellChanged(object? sender, System.EventArgs e)
    {
        CancelAssetTagChange();
        
        _imageCts?.Cancel();

        if (AddonList.SelectedItem == null)
        {
            AddonDetailPanel.IsVisible = false;
            AddonImage.ClearValue(AnimatedImage.Avalonia.ImageBehavior.AnimatedSourceProperty);
            AddonImage.Source = null;
            return;
        }

        if (sender is DataGrid dg && dg.SelectedItem is AddonAttribute att)
        {
            if (CollectionDetailPanel != null) CollectionDetailPanel.IsVisible = false;

            AddonDetailPanel.IsVisible = true;
            AddonImage.ClearValue(AnimatedImage.Avalonia.ImageBehavior.AnimatedSourceProperty);
            
            if (att.Source == AddonSource.WorkShop)
            {
                AddonImage.Source = null;
                
                string localPath = att.GetAbsolutePath(NekoSettings.Default.GameDir);
                if (!File.Exists(localPath))
                {
                    if (!string.IsNullOrEmpty(att.PreviewUrl))
                    {
                        LoadOnlineImage(att.PreviewUrl);
                    }
                    return;
                }
            }

            Package? pak = null;
            try
            {
                pak = att.LoadPackage(NekoSettings.Default.GameDir);
            }
            catch (Exception ex)
            {
                pak = null;
            }

            if (pak == null) 
            {
                if (att.Source == AddonSource.WorkShop && !string.IsNullOrEmpty(att.PreviewUrl))
                {
                    LoadOnlineImage(att.PreviewUrl);
                }
                else
                {
                    AddonImage.Source = null;
                }
                return;
            }

            var entry = pak.FindEntry("addonimage.jpg");
            if (entry != null)
            {
                try 
                {
                    pak.ReadEntry(entry, out byte[] output);
                    AddonImage.Source = Bitmap.DecodeToHeight(new System.IO.MemoryStream(output), 128);
                }
                catch
                {
                    AddonImage.Source = null;
                }
            }
            else
            {
                FileInfo jpg = new(Path.ChangeExtension(att.GetAbsolutePath(NekoSettings.Default.GameDir), "jpg"));
                if (jpg.Exists)
                {
                    try
                    {
                        using var fileStream = jpg.OpenRead();
                        AddonImage.Source = Bitmap.DecodeToHeight(fileStream, 128);
                    }
                    catch
                    {
                         AddonImage.Source = null;
                    }
                }
                else
                {
                    if (att.Source == AddonSource.WorkShop && !string.IsNullOrEmpty(att.PreviewUrl))
                    {
                         LoadOnlineImage(att.PreviewUrl);
                    }
                    else
                    {
                         AddonImage.Source = null;
                    }
                }
            }
            pak.Dispose();
        }
    }

    private async void LoadOnlineImage(string url)
    {
        if (_imageCache.TryGetValue(url, out var cachedObj))
        {
            if (cachedObj is Bitmap cachedBitmap)
            {
                AddonImage.Source = cachedBitmap;
            }
            else if (cachedObj is string filePath)
            {
                var uriString = new Uri(filePath).AbsoluteUri;
                var binding = new Avalonia.Data.Binding { Source = uriString };
                AddonImage.Bind(AnimatedImage.Avalonia.ImageBehavior.AnimatedSourceProperty, binding);
            }
            return;
        }

        _imageCts = new CancellationTokenSource();
        var token = _imageCts.Token;

        try
        {
            await Task.Delay(100, token);
            var imageBytes = await _imageHttpClient.GetByteArrayAsync(url, token);

            if (_imageCache.Count > 50)
            {
                foreach (var cached in _imageCache.Values)
                {
                    if (cached is IDisposable disposable && !ReferenceEquals(cached, AddonImage.Source))
                    {
                        disposable.Dispose();
                    }
                }
                _imageCache.Clear();
            }

            bool isGif = url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || url.Contains(".gif?");
            if (!isGif && imageBytes.Length > 3 && imageBytes[0] == 'G' && imageBytes[1] == 'I' && imageBytes[2] == 'F')
            {
                isGif = true;
            }

            if (isGif)
            {
                var tempFile = TempCache.GetGifPath(url);
                if (!File.Exists(tempFile))
                {
                    await Task.Run(() => TempCache.WriteFile(tempFile, imageBytes), token);
                }
                
                _imageCache[url] = tempFile;

                if (!token.IsCancellationRequested)
                {
                    var uriString = new Uri(tempFile).AbsoluteUri;
                    var binding = new Avalonia.Data.Binding { Source = uriString };
                    AddonImage.Bind(AnimatedImage.Avalonia.ImageBehavior.AnimatedSourceProperty, binding);
                }
            }
            else
            {
                using var stream = new MemoryStream(imageBytes);
                var bitmap = Bitmap.DecodeToHeight(stream, 256);
                
                _imageCache[url] = bitmap;

                if (!token.IsCancellationRequested)
                {
                    AddonImage.Source = bitmap;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
        }
    }

    private async void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.IsOnlineMode)
        {
            await vm.SearchWorkshopAsync();
        }
        else
        {
            ReloadAddonList();
        }
    }

    private void ReloadAddonList()
    {
        if (DataContext is MainViewModel vm)
        {
            Dispatcher.UIThread.Post(() => {
                vm.LoadAddons();
            }, DispatcherPriority.Background);
        }
    }

    private void DataGrid_BeginningEdit(object? sender, Avalonia.Controls.DataGridBeginningEditEventArgs e)
    {
    }

    private void DataGrid_CellEditEnded(object? sender, Avalonia.Controls.DataGridCellEditEndedEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Commit)
        {
            SaveDirtyChanges();
        }
    }

    private void AddonList_Menu_LocateFile(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AddonList.SelectedItem is AddonAttribute att)
        {
            string gameDir = NekoSettings.Default.GameDir;
            string primaryPath = att.GetAbsolutePath(gameDir);
            FileInfo fileInfo = new(primaryPath);
            
            if (!fileInfo.Exists && att.Source == AddonSource.WorkShop)
            {
                string altPath = Path.Combine(gameDir, "addons", att.FileName);
                FileInfo altFileInfo = new(altPath);
                if (altFileInfo.Exists)
                {
                    fileInfo = altFileInfo;
                }
                else
                {
                    return;
                }
            }
            else if (!fileInfo.Exists) 
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo() {
                    FileName = "explorer.exe",
                    Arguments = $"/select, \"{fileInfo.FullName}\"",
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
    }

    private void AddonList_Menu_OpenPage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AddonList.SelectedItem is AddonAttribute att && !string.IsNullOrEmpty(att.Url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = att.Url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private async void AddonList_Menu_Download(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && AddonList.SelectedItems != null && AddonList.SelectedItems.Count > 0)
        {
            var selectedItems = AddonList.SelectedItems.Cast<AddonAttribute>().ToList();
            bool anySuccess = false;

            foreach (var att in selectedItems)
            {
                if (await vm.DownloadAddonAsync(att))
                {
                    anySuccess = true;
                }
            }

            if (anySuccess && NekoSettings.Default.ClearSearchAfterDownload)
            {
                vm.SearchKeywords = string.Empty;
                vm.IsOnlineMode = false;
            }
        }
    }

    private async void AddonList_Menu_Delete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && AddonList.SelectedItems != null && AddonList.SelectedItems.Count > 0)
        {
            var selectedItems = AddonList.SelectedItems.Cast<AddonAttribute>().ToList();
            int count = selectedItems.Count;

            string msg = count == 1 
                ? string.Format(i18n["DeleteSingleMsg"], selectedItems[0].Title, selectedItems[0].FileName)
                : string.Format(i18n["DeleteMultiMsg"], count);

            string title = i18n["DeleteConfirmTitle"];
            string warning = i18n["DeleteWarningSuffix"];

            var result = await ShowMessageBoxAsync(title, msg + warning, ButtonEnum.YesNo, MsBox.Avalonia.Enums.Icon.Warning, isDanger: true);

            if (result == ButtonResult.Yes)
            {
                foreach (var att in selectedItems)
                {
                    try
                    {
                        vm.DeleteAddon(att);
                    }
                    catch (Exception ex)
                    {
                        string errTitle = i18n["DeleteFailedTitle"];
                        string errMsg = string.Format(i18n["DeleteFailedMsg"], att.FileName, ex.Message);
                        
                        await ShowMessageBoxAsync(errTitle, errMsg, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
                        break;
                    }
                }
            }
        }
    }

    private async void AddonList_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (e.Source is Visual sourceVisual)
        {
            if (sourceVisual.FindAncestorOfType<DataGridRow>() == null)
            {
                return;
            }

            var cell = sourceVisual as DataGridCell ?? sourceVisual.FindAncestorOfType<DataGridCell>();
            if (cell?.Content is CheckBox)
            {
                return;
            }
        }

        if (Environment.TickCount64 - _lastDoubleClickTime < 500)
        {
            return;
        }
        _lastDoubleClickTime = Environment.TickCount64;

        var selectedItemsSnapshot = AddonList.SelectedItems.Cast<object>().ToList();
        foreach (var item in selectedItemsSnapshot)
        {
            if (item is AddonAttribute att)
            {
                bool hadConflict = att.HasConflict;
                await OpenAddonInteractiveAsync(att);
                if (hadConflict) break;
            }
        }
    }

    private static void TryShellStart(string fileName, string? verb = null)
    {
        try
        {
            var psi = new ProcessStartInfo { FileName = fileName, UseShellExecute = true };
            if (verb != null) psi.Verb = verb;
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private async Task OpenAddonInteractiveAsync(AddonAttribute att)
    {
        if (att.HasConflict)
        {
            if (DataContext is MainViewModel vm)
            {
                var enabledAddons = vm.Addons.Cast<AddonAttribute>().Where(a => a.Enable == true && a != att).ToList();

                var conflictData = await Task.Run(() =>
                {
                    var priorities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    string listPath = System.IO.Path.Combine(vm.GameDir, "addonlist.txt");
                    if (System.IO.File.Exists(listPath))
                    {
                        var lines = System.IO.File.ReadAllLines(listPath);
                        int index = 1;
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith("\"") && trimmed.Contains(".vpk", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 1)
                                {
                                    string key = parts[0].Replace('/', '\\');
                                    if (!priorities.ContainsKey(key)) priorities[key] = index++;
                                }
                            }
                        }
                    }

                    string attKey = att.Source == AddonSource.WorkShop ? "workshop\\" + att.FileName : att.FileName;
                    string prioStr = priorities.TryGetValue(attKey, out int p) ? p.ToString() : "?";

                    var conflictModEntries = new List<(int Priority, string Title)>();
                    var conflictFiles = new HashSet<string>();

                    foreach (var other in enabledAddons)
                    {
                        var overlaps = att.ModifiedFiles.Intersect(other.ModifiedFiles).ToList();
                        if (overlaps.Count > 0)
                        {
                            string otherKey = other.Source == AddonSource.WorkShop ? "workshop\\" + other.FileName : other.FileName;
                            int otherPrio = priorities.TryGetValue(otherKey, out int op) ? op : 999;

                            conflictModEntries.Add((otherPrio, other.Title));
                            foreach (var f in overlaps) conflictFiles.Add(f);
                        }
                    }

                    var orderedMods = conflictModEntries.OrderBy(x => x.Priority)
                        .Select(x => $"[{(x.Priority == 999 ? "?" : x.Priority.ToString())}] {x.Title}")
                        .ToList();

                    var actualFiles = conflictFiles.Where(f => !f.EndsWith("/") && !f.EndsWith("\\")).OrderBy(f => f).ToList();
                    var groupedFiles = actualFiles.Take(10).GroupBy(f => System.IO.Path.GetDirectoryName(f)?.Replace('\\', '/') ?? "");

                    var displayLines = new List<string>();
                    foreach (var group in groupedFiles)
                    {
                        string dir = string.IsNullOrEmpty(group.Key) ? "/" : group.Key + "/";
                        displayLines.Add($"{dir}");
                        foreach (var file in group)
                        {
                            displayLines.Add($"  {System.IO.Path.GetFileName(file)}");
                        }
                    }

                    return new {
                        CurrentPriorityStr = prioStr,
                        ConflictMods = orderedMods,
                        DisplayLines = displayLines,
                        FileCount = actualFiles.Count
                    };
                });

                string msg = $"{i18n["ConflictCoveredBy"]}\n{string.Join("\n", conflictData.ConflictMods)}\n\n{i18n["ConflictSpecificFiles"]}\n{string.Join("\n", conflictData.DisplayLines)}";

                if (conflictData.FileCount > 10)
                {
                    msg += "\n\n" + string.Format(i18n["ConflictMoreFiles"], conflictData.FileCount);
                }

                var box = new CustomMessageBox(
                    $"{i18n["ConflictDialogTitle"]} [{conflictData.CurrentPriorityStr}]",
                    msg,
                    i18n["ConflictDialogDisable"],
                    i18n["Cancel"],
                    i18n["Open"],
                    isDanger: true)
                {
                    DataContext = this.DataContext
                };

                if (this.VisualRoot is Window window)
                {
                    var result = await box.ShowDialog<ButtonResult>(window);
                    if (result == ButtonResult.Yes)
                    {
                        att.Enable = false;
                        AddonAttribute.dirty.Add(att);
                        SaveDirtyChanges();
                    }
                    else if (result == ButtonResult.Ok)
                    {
                        if (att.Source == AddonSource.WorkShop)
                        {
                            string localPath = att.GetAbsolutePath(NekoSettings.Default.GameDir);
                            if (!File.Exists(localPath))
                            {
                                if (!string.IsNullOrEmpty(att.WorkShopID))
                                {
                                    var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={att.WorkShopID}";
                                    TryShellStart(url);
                                }
                                return;
                            }
                        }

                        TryShellStart(att.GetAbsolutePath(NekoSettings.Default.GameDir), "open");
                    }
                }
            }
            return;
        }

        if (att.Source == AddonSource.WorkShop)
        {
            string localPath = att.GetAbsolutePath(NekoSettings.Default.GameDir);
            if (!File.Exists(localPath))
            {
                if (!string.IsNullOrEmpty(att.WorkShopID))
                {
                    var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={att.WorkShopID}";
                    TryShellStart(url);
                }
                return;
            }
        }

        TryShellStart(att.GetAbsolutePath(NekoSettings.Default.GameDir), "open");
    }

    private async void Button_Download_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && AddonList.SelectedItem is AddonAttribute att)
        {
            if (await vm.DownloadAddonAsync(att) && NekoSettings.Default.ClearSearchAfterDownload)
            {
                vm.SearchKeywords = string.Empty;
                vm.IsOnlineMode = false;
            }
        }
    }

    private void AddonGridView_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (Environment.TickCount64 - _lastDoubleClickTime < 500)
        {
            return;
        }
        _lastDoubleClickTime = Environment.TickCount64;

        if (AddonGridView.SelectedItem is AddonAttribute att)
        {
            if (att.IsInstalled)
            {
                try
                {
                    Process.Start(new ProcessStartInfo()
                    {
                        FileName = att.GetAbsolutePath(NekoSettings.Default.GameDir),
                        UseShellExecute = true,
                        Verb = "open",
                    });
                }
                catch { }
            }
            else if (!string.IsNullOrEmpty(att.Url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = att.Url,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }

    private void AddonGridView_Menu_OpenPage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AddonGridView.SelectedItem is AddonAttribute att && !string.IsNullOrEmpty(att.Url))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = att.Url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private async void AddonGridView_Menu_Download(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && AddonGridView.SelectedItems != null && AddonGridView.SelectedItems.Count > 0)
        {
            var selectedItems = AddonGridView.SelectedItems.Cast<AddonAttribute>().ToList();
            bool anySuccess = false;

            foreach (var att in selectedItems)
            {
                if (await vm.DownloadAddonAsync(att))
                {
                    anySuccess = true;
                }
            }

            if (anySuccess && NekoSettings.Default.ClearSearchAfterDownload)
            {
                vm.SearchKeywords = string.Empty;
                vm.IsOnlineMode = false;
            }
        }
    }

    private async void AddonGridView_Menu_Delete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && AddonGridView.SelectedItems != null && AddonGridView.SelectedItems.Count > 0)
        {
            var selectedItems = AddonGridView.SelectedItems.Cast<AddonAttribute>().ToList();
            int count = selectedItems.Count;

            string msg = count == 1
                ? string.Format(i18n["DeleteSingleMsg"], selectedItems[0].Title, selectedItems[0].FileName)
                : string.Format(i18n["DeleteMultiMsg"], count);

            string title = i18n["DeleteConfirmTitle"];
            string warning = i18n["DeleteWarningSuffix"];

            var result = await ShowMessageBoxAsync(title, msg + warning, ButtonEnum.YesNo, MsBox.Avalonia.Enums.Icon.Warning, isDanger: true);

            if (result == ButtonResult.Yes)
            {
                foreach (var att in selectedItems)
                {
                    try
                    {
                        vm.DeleteAddon(att);
                    }
                    catch (Exception ex)
                    {
                        string errTitle = i18n["DeleteFailedTitle"];
                        string errMsg = string.Format(i18n["DeleteFailedMsg"], att.FileName, ex.Message);

                        await ShowMessageBoxAsync(errTitle, errMsg, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
                        break;
                    }
                }
            }
        }
    }

    private void Button_CancelDownload_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.CancelDownload();
        }
    }

    private void AssetTag_Label_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Label label)
        {
            if (label.DataContext is AssetTag tag)
            {
                label.Classes.Add(tag.Color);
            }
        }
    }


    private readonly Dictionary<AssetTag, bool> ModifiedAssetTags = [];

    private void SetAssetTagEnabled(AssetTag tag, bool enabled)
    {
        if (!ModifiedAssetTags.ContainsKey(tag))
        {
            ModifiedAssetTags[tag] = tag.Enable;
        }

        tag.Enable = enabled;

        if (tag.Enable != ModifiedAssetTags[tag])
        {
            tag.IsModified = true;
        }
        else
        {
            tag.IsModified = false;
            ModifiedAssetTags.Remove(tag);
        }
    }

    private void AssetTag_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Label label && label.DataContext is AssetTag tag)
        {
            if (tag.Type == null || !tag.Type.Contains("Survivor"))
                return;

            SetAssetTagEnabled(tag, !tag.Enable);

            if (tag.Enable && DataContext is MainViewModel vm && vm.SelectedAddon is AddonAttribute att)
            {
                foreach (var other in att.Tags)
                {
                    if (other.Equals(tag) || !other.Enable)
                        continue;

                    if (AssetTagProperty.AreMutuallyExclusive(tag.Proporty, other.Proporty))
                    {
                        SetAssetTagEnabled(other, false);
                    }
                }
            }

            AssetTagModifiedPanel.IsVisible = ModifiedAssetTags.Count > 0;
        }
    }

    private async void VariantComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && AddonList.SelectedItem is AddonAttribute att && DataContext is MainViewModel vm)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is NekoVariant newVariant)
            {
                if (att.CurrentActiveVariantId == newVariant.Id) return;

                cb.IsEnabled = false;
                try
                {
                    await ChangeVariantAsync(att, att.CurrentActiveVariantId, newVariant.Id, vm.GameDir);
                    att.CurrentActiveVariantId = newVariant.Id;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                    string msg = FormatApplyError(ex, att.FileName);
                    await ShowMessageBoxAsync(i18n["ApplyFailedTitle"], msg, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
                    cb.SelectedItem = att.Variants.FirstOrDefault(v => v.Id == att.CurrentActiveVariantId);
                }
                finally
                {
                    cb.IsEnabled = true;
                }
            }
        }
    }

    private static void ReplaceVpkFile(FileInfo newFile, string originFilePath, DateTime creationTime, DateTime lastWriteTime)
    {
        string bakPath = Path.ChangeExtension(originFilePath, ".vpk.nekobak");
        bool movedToBackup = false;
        try
        {
            if (!NekoSettings.Default.SkipVariantBackup)
            {
                File.Move(originFilePath, bakPath, true);
                movedToBackup = true;
            }

            newFile.Refresh();
            newFile.LastWriteTime = lastWriteTime;
            newFile.CreationTime = creationTime;
            newFile.MoveTo(originFilePath, true);
        }
        catch
        {
            if (movedToBackup && !File.Exists(originFilePath) && File.Exists(bakPath))
            {
                try { File.Move(bakPath, originFilePath); } catch { }
            }
            throw;
        }
    }

    private string FormatApplyError(Exception ex, string fileName)
        => ex is IOException or UnauthorizedAccessException
            ? string.Format(i18n["ApplyFailedMsg"], ex.Message, fileName)
            : $"{ex.Message}\n\n{fileName}";

    private async Task ChangeVariantAsync(AddonAttribute att, string oldId, string newId, string gameDir)
    {
        await Task.Run(async () => 
        {
            Package? pkg = null;
            SevenZipCompressor? compressor = null;
            DirectoryInfo? tmpDir = null;
            try
            {
                pkg = att.LoadPackage(gameDir);
                tmpDir = new DirectoryInfo(pkg.FileName + "_varianttmp");
                if (tmpDir.Exists) tmpDir.Delete(true);
                tmpDir.Create();
                tmpDir.Attributes |= FileAttributes.Hidden;

                FileInfo oldTmpFile = new FileInfo(Path.Join(tmpDir.FullName, $"{oldId}.nekotmp"));
                FileInfo newTmpFile = new FileInfo(Path.Join(tmpDir.FullName, $"{newId}.nekotmp"));

                PackageEntry? oldEntry = null;
                PackageEntry? newEntry = null;
                PackageEntry? addonInfoEntry = null;

                if (pkg.Entries.TryGetValue("neko7z", out var neko7zEntries))
                {
                    oldEntry = neko7zEntries.FirstOrDefault(e => e.FileName == oldId);
                    newEntry = neko7zEntries.FirstOrDefault(e => e.FileName == newId);
                }

                foreach (var entry in pkg.Entries)
                {
                    foreach (var f in entry.Value)
                    {
                        if (f.GetFullPath() == "addoninfo.txt") addonInfoEntry = f;
                    }
                }

                var activeTags = att.Tags.Where(t => t.Enable).ToList();

                List<PackageEntry> filesToMoveToOld = [];
                foreach (var entryList in pkg.Entries.Values)
                {
                    foreach (var f in entryList)
                    {
                        var path = f.GetFullPath();
                        if (path == "addoninfo.txt" || f.TypeName == "neko7z") continue;

                        foreach (var tag in activeTags)
                        {
                            if (tag.Proporty.IsMatch(path))
                            {
                                filesToMoveToOld.Add(f);
                                break;
                            }
                        }
                    }
                }

                bool oldExists = oldEntry != null;
                if (oldExists)
                {
                    pkg.ExtratFile(oldEntry!, oldTmpFile);
                    oldTmpFile.Refresh();
                    if (oldTmpFile.Length == 0) oldExists = false;
                    pkg.RemoveFile(oldEntry!);
                }
                else
                {
                    oldTmpFile.Create().Close();
                }

                if (filesToMoveToOld.Count > 0)
                {
                    Dictionary<string, string> zipFilesOld = [];
                    foreach (var entry in filesToMoveToOld)
                    {
                        FileInfo outFile = new FileInfo(Path.Join(tmpDir.FullName, entry.GetFullPath()));
                        if (outFile.Directory != null && !outFile.Directory.Exists) outFile.Directory.Create();
                        pkg.ExtratFile(entry, outFile);
                        zipFilesOld.Add(entry.GetFullPath(), outFile.FullName);
                        pkg.RemoveFile(entry);
                    }

                    compressor = new SevenZipCompressor()
                    {
                        CompressionMode = oldExists ? CompressionMode.Append : CompressionMode.Create,
                        ArchiveFormat = OutArchiveFormat.SevenZip,
                        CompressionLevel = (CompressionLevel)NekoSettings.Default.CompressionLevel,
                        CompressionMethod = CompressionMethod.Lzma2,
                        EventSynchronization = EventSynchronizationStrategy.AlwaysSynchronous,
                    };
                    compressor.CompressFileDictionary(zipFilesOld, oldTmpFile.FullName);
                }

                bool newExists = newEntry != null;
                if (newExists)
                {
                    pkg.ExtratFile(newEntry!, newTmpFile);
                    newTmpFile.Refresh();
                    if (newTmpFile.Length == 0) newExists = false;
                    pkg.RemoveFile(newEntry!);
                }
                else
                {
                    newTmpFile.Create().Close();
                }

                Dictionary<int, string?> disableZipFiles = [];
                List<string> filesToExtractFromNew = [];
                
                if (newExists && newTmpFile.Exists && newTmpFile.Length > 0)
                {
                    using (var newExtractor = new SevenZipExtractor(newTmpFile.FullName, InArchiveFormat.SevenZip))
                    {
                        foreach (var zipFileData in newExtractor.ArchiveFileData)
                        {
                            foreach (var tag in activeTags)
                            {
                                if (tag.Proporty.IsMatch(zipFileData.FileName))
                                {
                                    disableZipFiles.Add(zipFileData.Index, null);
                                    filesToExtractFromNew.Add(zipFileData.FileName);
                                    break;
                                }
                            }
                        }

                        if (disableZipFiles.Count > 0)
                        {
                            await newExtractor.ExtractFilesAsync(tmpDir.FullName, disableZipFiles.Keys.ToArray());
                        }
                    }

                    if (disableZipFiles.Count > 0)
                    {
                        compressor = new SevenZipCompressor()
                        {
                            CompressionMode = CompressionMode.Append,
                            ArchiveFormat = OutArchiveFormat.SevenZip,
                            CompressionLevel = (CompressionLevel)NekoSettings.Default.CompressionLevel,
                            CompressionMethod = CompressionMethod.Lzma2,
                            EventSynchronization = EventSynchronizationStrategy.AlwaysSynchronous,
                        };
                        compressor.ModifyArchive(newTmpFile.FullName, disableZipFiles);
                    }
                }

                foreach (var v in filesToExtractFromNew)
                {
                    FileInfo file = new FileInfo(Path.Join(tmpDir.FullName, v));
                    pkg.AddFile(v, file); 
                }

                if (addonInfoEntry != null)
                {
                    pkg.ReadEntry(addonInfoEntry, out byte[] addonInfoBytes);
                    byte[] newBytes = AddonInfo.UpdateActive7z(addonInfoBytes, newId);
                    FileInfo infoTmp = new FileInfo(Path.Join(tmpDir.FullName, "addoninfo.txt"));
                    File.WriteAllBytes(infoTmp.FullName, newBytes);
                    pkg.RemoveFile(addonInfoEntry);
                    pkg.AddFile("addoninfo.txt", infoTmp);
                }

                oldTmpFile.Refresh();
                if (oldTmpFile.Exists && oldTmpFile.Length > 0)
                {
                    pkg.AddFile(pkg.GenNekoDir() + $"{oldId}.neko7z", oldTmpFile);
                }

                newTmpFile.Refresh();
                if (newTmpFile.Exists && newTmpFile.Length > 0)
                {
                    pkg.AddFile(pkg.GenNekoDir() + $"{newId}.neko7z", newTmpFile);
                }

                string originFilePath = pkg.FileName + ".vpk";
                FileInfo srcPakFile = new(originFilePath);
                FileInfo outVpkFile = new FileInfo(Path.Join(tmpDir.FullName, "out.vpk"));
                
                pkg.Write(outVpkFile.FullName, 1);
                pkg.Dispose();

                ReplaceVpkFile(outVpkFile, originFilePath, srcPakFile.CreationTime, srcPakFile.LastWriteTime);
            }
            finally
            {
                pkg?.Dispose();
                if (tmpDir is not null && tmpDir.Exists)
                {
                    try { tmpDir.Delete(true); } catch { }
                }
            }
        });
    }

    private async void Button_AssetTagModifiedPanel_Apply(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AddonList.SelectedItem is AddonAttribute att && DataContext is MainViewModel vm)
        {
            Package? pkg = null;
            SevenZipExtractor? extractor = null;
            SevenZipCompressor? compressor = null;
            DirectoryInfo? tmpDir = null; 
            try
            {
                pkg = att.LoadPackage(vm.GameDir);
                tmpDir = new(pkg.FileName + "_nekotmp");

                if (tmpDir.Exists)
                    tmpDir.Delete(true);
                tmpDir.Create();
                tmpDir.Attributes |= FileAttributes.Hidden;

                string activeId = att.CurrentActiveVariantId ?? "0";
                FileInfo tmpFile = new(Path.Join(tmpDir.FullName, att.FileName + ".nekotmp"));

                foreach (var entry in pkg.Entries)
                {
                    if (entry.Key == "neko7z")
                    {
                        var activeEntry = entry.Value.FirstOrDefault(x => x.FileName == activeId);
                        if (activeEntry != null)
                        {
                            pkg.ExtratFile(activeEntry, tmpFile);
                            tmpFile.Refresh();
                            pkg.RemoveFile(activeEntry);
                            extractor = new SevenZipExtractor(tmpFile.FullName, InArchiveFormat.SevenZip);
                            break;
                        }
                    }
                }
            
                if (!tmpFile.Exists) tmpFile.Create().Close();
                tmpFile.Attributes |= FileAttributes.Temporary;

                Dictionary<int, string?> disableZipFiles = [];
                List<PackageEntry> disableEntries = [];
                List<string> vpkFiles = [];
                Dictionary<string, string> zipFiles = [];

                if (extractor is not null)
                {
                    foreach (var zipFileData in extractor.ArchiveFileData)
                    {
                        foreach (var tag in ModifiedAssetTags.Keys)
                        {
                            if (tag.Enable && tag.Proporty.IsMatch(zipFileData.FileName))
                            {
                                disableZipFiles.Add(zipFileData.Index, null);
                                vpkFiles.Add(zipFileData.FileName);
                                break;
                            }
                        }
                    }
                }
                foreach (var entry in pkg.Entries)
                {
                    foreach (var f in entry.Value)
                    {
                        foreach (var tag in ModifiedAssetTags.Keys)
                        {
                            if (!tag.Enable && tag.Proporty.IsMatch(f.GetFullPath()))
                            {
                                disableEntries.Add(f);
                                break;
                            }
                        }
                    }
                }

                compressor = new SevenZipCompressor()
                {
                    CompressionMode = extractor is null ? CompressionMode.Create : CompressionMode.Append,
                    ArchiveFormat = OutArchiveFormat.SevenZip,
                    CompressionLevel = (CompressionLevel)NekoSettings.Default.CompressionLevel,
                    CompressionMethod = CompressionMethod.Lzma2,
                    EventSynchronization = EventSynchronizationStrategy.AlwaysSynchronous,
                };

                if (extractor != null && disableZipFiles.Count > 0) {
                    await extractor.ExtractFilesAsync(tmpDir.FullName, disableZipFiles.Keys.ToArray());
                    foreach (var v in vpkFiles)
                    {
                        FileInfo file = new (Path.Join(tmpDir.FullName, v));
                        if (pkg.AddFile(v, file) != null)
                        {
                        }
                    }
                    int originCount = extractor.ArchiveFileNames.Count;
                    compressor.ModifyArchive(tmpFile.FullName, disableZipFiles);

                    extractor = new SevenZipExtractor(tmpFile.FullName, InArchiveFormat.SevenZip);
                    if ( extractor.ArchiveFileNames.Count != originCount - disableZipFiles.Count)
                    {
                        throw new Exception("Modified archive has an unexpected number of files.");
                    }
                }

                foreach (var entry in disableEntries)
                {
                    FileInfo outFile = new(Path.Join(tmpDir.FullName, entry.GetFullPath()));
                    pkg.ExtratFile(entry, outFile);

                    zipFiles.Add(entry.GetFullPath(), outFile.FullName);
                    pkg.RemoveFile(entry);
                }
                if (zipFiles.Count > 0)
                {
                    compressor.CompressFileDictionary(zipFiles, tmpFile.FullName);
                }

                if (zipFiles.Count > 0 || disableZipFiles.Count > 0)
                {
                    tmpFile.Refresh();
                    pkg.AddFile(pkg.GenNekoDir() + $"{activeId}.neko7z", tmpFile);
                    tmpFile.Delete();
                }

                string originFilePath = pkg.FileName + ".vpk";
                FileInfo srcPakFile = new(originFilePath);
                pkg.Write(tmpFile.FullName, 1);
                pkg.Dispose();

                ReplaceVpkFile(tmpFile, originFilePath, srcPakFile.CreationTime, srcPakFile.LastWriteTime);

                foreach (var tag in ModifiedAssetTags.Keys)
                {
                    tag.IsModified = false; 
                }

                ModifiedAssetTags.Clear();
                AssetTagModifiedPanel.IsVisible = false;
            }
            catch (Exception ex)
            {
                CancelAssetTagChange();
                Debug.WriteLine(ex);

                await ShowMessageBoxAsync(i18n["ApplyFailedTitle"], FormatApplyError(ex, att.FileName), ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
            }
            finally
            {
                pkg?.Dispose();
                extractor?.Dispose();
                if (tmpDir is not null && tmpDir.Exists)
                    try { tmpDir.Delete(true); } catch { }
            }
        }
    }

    private void CancelAssetTagChange()
    {
        AssetTagModifiedPanel.IsVisible = false;
        foreach (var tag in ModifiedAssetTags)
        {
            tag.Key.Enable = tag.Value;
            tag.Key.IsModified = false;
        }
        ModifiedAssetTags.Clear();
    }

    private void Button_AssetTagModifiedPanel_Cancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CancelAssetTagChange();
    }

    private void SubmitAddonSearch()
    {
        if (DataContext is MainViewModel vm)
        {
            if (!vm.IsOnlineMode)
            {
                vm.Addons.Refresh();
            }
        }
    }

    private async void Button_AddonSearch_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.IsOnlineMode)
        {
            await vm.SearchWorkshopAsync();
        }
        else
        {
            SubmitAddonSearch();
        }
        if (NekoSettings.Default?.AutoSizeColumnsOnSearch == true)
        {
            AutoSizeAllColumns();
        }
    }

    private async void TextBox_AddonSearch_KeyUp(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            await ExecuteSearchAsync();
        }
    }

    private async void Button_Search_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await ExecuteSearchAsync();
    }

    private async Task ExecuteSearchAsync()
    {
        if (DataContext is MainViewModel vm && vm.IsOnlineMode)
        {
            await vm.SearchWorkshopAsync();
        }
        else
        {
            SubmitAddonSearch();
        }
        if (NekoSettings.Default?.AutoSizeColumnsOnSearch == true)
        {
            AutoSizeAllColumns();
        }
    }

    private void TextBox_AddonSearch_TextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && !vm.IsOnlineMode)
        {
            SubmitAddonSearch();
        }
    }

    private async void Button_CheckUpdates_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        await vm.CheckUpdatesGlobalAsync();
    }

    private async void AddonList_Menu_CheckUpdates(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && AddonList.SelectedItems != null && AddonList.SelectedItems.Count > 0)
        {
            var selectedItems = AddonList.SelectedItems.Cast<AddonAttribute>().ToList();
            await vm.CheckUpdatesForAsync(selectedItems);
        }
    }

    private void Button_ClearSearch_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.SearchKeywords = string.Empty;
    }

    private void DataGrid_Sorting(object? sender, Avalonia.Controls.DataGridColumnEventArgs e)
    {
        
    }

    private async void Settings_Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.Classes.Add("Breathing");
        }

        if (this.VisualRoot is Window window)
        {
            var settingsViewModel = new ViewModels.Settings();
            settingsViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ViewModels.Settings.BackgroundBrightness) && DataContext is MainViewModel main)
                {
                    main.RefreshBackgroundDim();
                }
            };
            var settingsWindow = new SettingsWindow()
            {
                DataContext = settingsViewModel,
                Background = window.Background,
            };
            
            string oldGameDir = NekoSettings.Default.GameDir;
            await settingsWindow.ShowDialog(window);
            
            if (DataContext is MainViewModel vm)
            {
                vm.UpdateBackground();
                vm.CheckConflicts();

                if (!vm.IsOnlineMode && NekoSettings.Default.GameDir != oldGameDir)
                {
                    ReloadAddonList();
                }
            }
        }

        if (sender is Button b)
        {
            b.Classes.Remove("Breathing");
        }
    }


    private void SetAllEnabled(bool enabled)
    {
        if (DataContext is MainViewModel vm)
        {
            foreach (AddonAttribute item in vm.Addons)
            {
                item.Enable = enabled;
            }
            SaveDirtyChanges();
        }
    }

    private void SaveDirtyChanges()
    {
        if (AddonAttribute.dirty.Count == 0) return;

        bool saved = false;
        try
        {
            AddonList addonList = new();
            addonList.Load(NekoSettings.Default.GameDir);
            bool modified = false;

            foreach (var v in AddonAttribute.dirty)
            {
                if (v.Enable.HasValue && string.IsNullOrEmpty(v.SubFolder))
                {
                    modified = true;
                    string key = v.FileName;
                    if (v.Source == AddonSource.WorkShop)
                    {
                        key = "workshop\\" + v.FileName;
                    }
                    addonList.SetEnable(key, (bool)v.Enable);
                }
            }

            if (modified)
            {
                addonList.Save(NekoSettings.Default.GameDir);
            }

            AddonAttribute.dirty.Clear();
            saved = true;
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex);
            _ = ShowMessageBoxAsync(
                i18n["ApplyFailedTitle"],
                string.Format(i18n["ApplyFailedMsg"], ex.Message, "addonlist.txt"),
                ButtonEnum.Ok,
                MsBox.Avalonia.Enums.Icon.Error);
        }

        if (saved && DataContext is MainViewModel vm)
            vm.CheckConflicts();
    }

    private void MenuItem_SelectAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetAllEnabled(true);
    }
    private void MenuItem_DeselectAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetAllEnabled(false);
    }
    private void MenuItem_InvertSelection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            foreach (AddonAttribute item in vm.Addons)
            {
                if (item.Enable.HasValue)
                {
                    item.Enable = !item.Enable.Value;
                }
                else
                {
                    item.Enable = true;
                }
            }
            SaveDirtyChanges();
        }
    }

    private async Task<ButtonResult> ShowMessageBoxAsync(string title, string message, ButtonEnum buttons, MsBox.Avalonia.Enums.Icon icon, bool isDanger = false)
    {
        var customBox = new CustomMessageBox(title, message, buttons, icon, isDanger)
        {
            DataContext = this.DataContext
        };

        if (this.VisualRoot is Window window)
        {
            return await customBox.ShowDialog<ButtonResult>(window);
        }
        
        return ButtonResult.None; 
    }
    
    private void Button_ResetTags_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            foreach (var category in vm.WorkshopTagCategories)
            {
                foreach (var tag in category.Tags)
                {
                    tag.IsSelected = false;
                }
            }
        }
    }
    
    private async void Button_PrevPage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await vm.GoToPrevPageAsync();
        }
    }

    private async void Button_NextPage_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await vm.GoToNextPageAsync();
        }
    }

    private void Menu_AutoSizeAllColumns_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AutoSizeAllColumns();
    }

    private void AutoSizeAllColumns()
    {
        if (AddonList?.Columns == null) return;

        if (AddonList.Columns.Count > 0)
        {
            try { AddonList.ScrollIntoView(null, AddonList.Columns[0]); } catch { }
        }

        string? tagHeader = i18n?["ColumnTag"];
        string? typeHeader = i18n?["ColumnType"];

        foreach (var column in AddonList.Columns)
        {
            if (column == null) continue;

            bool isTagColumn = (tagHeader != null && column.Header?.ToString() == tagHeader) || column.SortMemberPath == "TagsOrde";
            bool isTypeColumn = (typeHeader != null && column.Header?.ToString() == typeHeader) || column.SortMemberPath == "TypeDisplay";

            if (isTagColumn)
            {
                column.Width = new Avalonia.Controls.DataGridLength(136, Avalonia.Controls.DataGridLengthUnitType.Pixel);
            }
            else if (isTypeColumn)
            {
                column.Width = new Avalonia.Controls.DataGridLength(160, Avalonia.Controls.DataGridLengthUnitType.Pixel);
            }
            else
            {
                column.Width = new Avalonia.Controls.DataGridLength(1, Avalonia.Controls.DataGridLengthUnitType.Auto);
            }
        }

        if (NekoSettings.Default?.SaveColumnWidths == true)
        {
            SaveColumnWidths();
        }
    }

    private void LoadColumnWidths()
    {
        if (NekoSettings.Default == null || !NekoSettings.Default.SaveColumnWidths) return;
        
        var savedWidths = NekoSettings.Default.DataGridColumnWidths;
        if (string.IsNullOrWhiteSpace(savedWidths)) return;
        
        if (AddonList?.Columns == null) return;

        var widths = savedWidths.Split(',');
        for (int i = 0; i < AddonList.Columns.Count && i < widths.Length; i++)
        {
            var col = AddonList.Columns[i];
            if (col != null)
            {
                col.Width = DeserializeWidth(widths[i]);
            }
        }
    }

    private void SaveColumnWidths()
    {
        if (NekoSettings.Default == null || !NekoSettings.Default.SaveColumnWidths) return;
        if (AddonList?.Columns == null) return;
        
        var widths = AddonList.Columns.Select(c => SerializeWidth(c?.Width ?? Avalonia.Controls.DataGridLength.Auto)).ToArray();
        NekoSettings.Default.DataGridColumnWidths = string.Join(",", widths);
        NekoSettings.Default.Save();
    }

    private string SerializeWidth(Avalonia.Controls.DataGridLength length)
    {
        if (length.IsAuto) return "Auto";
        if (length.IsStar) return length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "*";
        return length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private Avalonia.Controls.DataGridLength DeserializeWidth(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return Avalonia.Controls.DataGridLength.Auto;
        if (str == "Auto") return new Avalonia.Controls.DataGridLength(1, Avalonia.Controls.DataGridLengthUnitType.Auto);
        if (str.EndsWith("*"))
        {
            if (double.TryParse(str.TrimEnd('*'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                return new Avalonia.Controls.DataGridLength(val, Avalonia.Controls.DataGridLengthUnitType.Star);
        }
        else if (double.TryParse(str, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
        {
            return new Avalonia.Controls.DataGridLength(val, Avalonia.Controls.DataGridLengthUnitType.Pixel);
        }
        return Avalonia.Controls.DataGridLength.Auto;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFiles()?.Any(f => f.Name.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase)) == true)
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private const string MoveMenuTag = "NekoMoveToFolder";

    private void Button_ExitFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ExitFolder();
        }
    }

    private void MainView_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind != PointerUpdateKind.XButton1Pressed) return;
        if (DataContext is not MainViewModel vm) return;

        if (vm.IsInsideFolder) vm.ExitFolder();
        else if (vm.IsInCollectionDetail) vm.ExitCollection();
        else return;

        e.Handled = true;
    }

    private AddonFolderItem? _folderMenuTarget;
    private bool _folderPressedWithLeft;
    private Flyout? _folderNameFlyout;

    private void FolderGridView_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _folderPressedWithLeft = e.GetCurrentPoint(FolderGridView).Properties.IsLeftButtonPressed;
        _folderMenuTarget = (e.Source as StyledElement)?.DataContext as AddonFolderItem;
    }

    private void FolderGridView_Tapped(object? sender, TappedEventArgs e)
    {
        if (!_folderPressedWithLeft) return;
        if (DataContext is not MainViewModel vm) return;
        if (e.Source is not StyledElement { DataContext: AddonFolderItem { IsAddTile: true } item } source) return;

        Control? anchor = (source as Visual)?.FindAncestorOfType<ListBoxItem>(true) ?? source as Control;
        if (anchor != null)
        {
            item.IsNaming = true;

            ShowFolderNameFlyout(anchor, i18n["NewFolder"], string.Empty, name => vm.CreateFolder(name));

            if (_folderNameFlyout != null)
            {
                EventHandler? closedHandler = null;
                closedHandler = (s, args) =>
                {
                    _folderNameFlyout.Closed -= closedHandler;
                    item.IsNaming = false;
                };
                _folderNameFlyout.Closed += closedHandler;
            }
        }
    }

    private void FolderGridView_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (!_folderPressedWithLeft) return;
        if (DataContext is not MainViewModel vm) return;
        if (e.Source is StyledElement { DataContext: AddonFolderItem { IsFolder: true } item })
        {
            vm.EnterFolder(item.Name);
        }
    }

    private void Menu_ThemeColor_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control anchor) return;

        string current = (NekoSettings.Default.ThemeColor ?? string.Empty).Trim();
        bool hasCurrent = Rgb.TryParse(current, out var currentRgb);
        bool followSystem = ThemeColor.IsSystem(current);

        var flyout = new Flyout { Placement = PlacementMode.Right };

        void ApplyTheme(string? hex)
        {
            NekoSettings.Default.ThemeColor = hex ?? string.Empty;
            NekoSettings.Default.Save();
            ThemeColor.Apply(hex);
        }

        var swatches = new WrapPanel { Width = 5 * 36 };
        void AddSwatch(string? hex, string tip)
        {
            Rgb.TryParse(hex ?? ThemeColor.DefaultPreviewHex, out var rgb);
            bool selected = hex == null ? !hasCurrent && !followSystem : hasCurrent && currentRgb == rgb;

            var swatch = new Border
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(selected ? 3 : 0),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(swatch, tip);
            swatch.Tapped += (_, _) =>
            {
                ApplyTheme(hex);
                flyout.Hide();
            };
            swatches.Children.Add(swatch);
        }
        AddSwatch(null, i18n["ThemeColorDefault"]);
        foreach (var hex in ThemeColorMath.PresetHexes) AddSwatch(hex, hex);

        var systemContent = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        if (ThemeColor.TryGetSystemAccent(out var systemRgb))
        {
            systemContent.Children.Add(new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(systemRgb.R, systemRgb.G, systemRgb.B))
            });
        }
        systemContent.Children.Add(new TextBlock { Text = i18n["ThemeColorSystem"], VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });

        var systemCheck = new CheckBox { Content = systemContent, IsChecked = followSystem };
        systemCheck.Click += (_, _) =>
        {
            ApplyTheme(systemCheck.IsChecked == true ? ThemeColor.SystemValue : null);
            flyout.Hide();
        };

        var box = new TextBox { Width = 120, MaxLength = 7, Watermark = "#RRGGBB", Text = hasCurrent ? currentRgb.ToHex() : string.Empty };
        var okButton = new Button { Content = i18n["MsgBoxOk"] };
        var error = new TextBlock
        {
            Width = 180,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.IndianRed,
            Text = i18n["ThemeColorInvalid"],
            IsVisible = false
        };

        void SubmitCustom()
        {
            if (!Rgb.TryParse(box.Text, out var rgb))
            {
                error.IsVisible = true;
                return;
            }
            ApplyTheme(rgb.ToHex());
            flyout.Hide();
        }
        okButton.Click += (_, _) => SubmitCustom();
        box.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                args.Handled = true;
                SubmitCustom();
            }
        };

        var customRow = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        customRow.Children.Add(box);
        customRow.Children.Add(okButton);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = i18n["ThemeColor"], FontWeight = FontWeight.Bold });
        panel.Children.Add(swatches);
        panel.Children.Add(systemCheck);
        panel.Children.Add(customRow);
        panel.Children.Add(error);

        flyout.Content = panel;
        flyout.ShowAt(anchor);
    }

    private void FolderMenu_SelectAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ApplyFolderSelection(_ => true);
    private void FolderMenu_DeselectAll_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ApplyFolderSelection(_ => false);
    private void FolderMenu_InvertSelection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => ApplyFolderSelection(selected => !selected);

    private void ApplyFolderSelection(Func<bool, bool> keep)
    {
        if (DataContext is not MainViewModel vm) return;

        var selection = FolderGridView.Selection;
        var wanted = AddonFolders.PickFolderIndices(
            vm.FolderItems.Select(i => i.IsFolder).ToList(),
            selection.IsSelected,
            keep);

        selection.BeginBatchUpdate();
        try
        {
            selection.Clear();
            foreach (int index in wanted) selection.Select(index);
        }
        finally
        {
            selection.EndBatchUpdate();
        }
    }

    private void FolderGridView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        var selected = FolderGridView.SelectedItems?.OfType<AddonFolderItem>().ToList()
                       ?? new List<AddonFolderItem>();
        vm.UpdateFolderDetail(selected);
    }

    private List<AddonFolderItem> GetSelectedFolderItems()
    {
        var list = FolderGridView.SelectedItems?.OfType<AddonFolderItem>().Where(i => i.IsFolder).ToList()
                   ?? new List<AddonFolderItem>();
        if (_folderMenuTarget is { IsFolder: true } target && !list.Contains(target))
        {
            list = [target];
        }
        return list;
    }

    private void FolderGridView_ContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu || DataContext is not MainViewModel vm
            || _folderMenuTarget is not { IsFolder: true })
        {
            e.Cancel = true;
            return;
        }

        var selected = GetSelectedFolderItems();
        if (selected.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        foreach (var old in menu.Items.OfType<MenuItem>().Where(m => MoveMenuTag.Equals(m.Tag)).ToList())
        {
            menu.Items.Remove(old);
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is "open" or "rename") item.IsVisible = selected.Count == 1;
        }

        if (selected.Sum(i => i.ItemCount) > 0)
        {
            var names = selected.Select(i => i.Name).ToList();
            var targets = new List<(string Label, string? Folder)> { ("addons", null) };
            foreach (var folder in vm.GetMoveTargetFolders(names))
            {
                targets.Add((folder, folder));
            }

            var moveItem = new MenuItem { Header = i18n["MoveToFolder"], Tag = MoveMenuTag };
            foreach (var (label, folder) in targets)
            {
                string? target = folder;
                var sub = new MenuItem { Header = new TextBlock { Text = label } };
                sub.Click += (_, _) => MoveFoldersContentTo(names, target);
                moveItem.Items.Add(sub);
            }
            menu.Items.Insert(2, moveItem);
        }
    }

    private void FolderMenu_Open(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var selected = GetSelectedFolderItems();
        if (DataContext is MainViewModel vm && selected.Count == 1)
        {
            vm.EnterFolder(selected[0].Name);
        }
    }

    private void FolderMenu_Rename(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var selected = GetSelectedFolderItems();
        if (DataContext is not MainViewModel vm || selected.Count != 1) return;

        var target = selected[0];
        Control anchor = FolderGridView.ContainerFromItem(target) as Control ?? FolderGridView;
        string oldName = target.Name;
        ShowFolderNameFlyout(anchor, i18n["FolderRename"], oldName, name => vm.RenameFolder(oldName, name));
    }

    private async void FolderMenu_CheckUpdates(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var selected = GetSelectedFolderItems();
        if (DataContext is MainViewModel vm && selected.Count > 0)
        {
            await vm.CheckUpdatesInFoldersAsync(selected.Select(i => i.Name).ToList());
        }
    }

    private async void FolderMenu_Delete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var selected = GetSelectedFolderItems();
        if (DataContext is not MainViewModel vm || selected.Count == 0) return;

        var names = selected.Select(i => i.Name).ToList();
        int files = names.Sum(n => vm.CountFolderFiles(n));

        string msg;
        if (names.Count == 1)
        {
            msg = files == 0
                ? string.Format(i18n["FolderDeleteEmptyMsg"], names[0])
                : string.Format(i18n["FolderDeleteMsg"], names[0], files);
        }
        else
        {
            msg = string.Format(i18n["FolderDeleteMultiMsg"], names.Count, files);
        }

        var result = await ShowMessageBoxAsync(i18n["DeleteConfirmTitle"], msg, ButtonEnum.YesNo,
            MsBox.Avalonia.Enums.Icon.Warning, isDanger: true);
        if (result != ButtonResult.Yes) return;

        foreach (var name in names)
        {
            string? error = vm.DeleteFolder(name);
            if (error != null)
            {
                await ShowMessageBoxAsync(i18n["DeleteFailedTitle"], error, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
                break;
            }
        }
    }

    private async void MoveFoldersContentTo(List<string> folders, string? target)
    {
        if (DataContext is not MainViewModel vm) return;

        var overwrite = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var declined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, fileName) in vm.GetFolderMoveConflicts(folders, target))
        {
            string shown = folders.Count > 1 ? MainViewModel.MoveKey(folder, fileName) : fileName;
            var answer = await ShowMessageBoxAsync(
                i18n["ConfirmOverwriteTitle"],
                string.Format(i18n["ConfirmOverwriteMsg"], shown),
                ButtonEnum.YesNo, MsBox.Avalonia.Enums.Icon.Warning);

            (answer == ButtonResult.Yes ? overwrite : declined).Add(MainViewModel.MoveKey(folder, fileName));
        }

        var errors = vm.MoveFolderContents(folders, target, overwrite, declined);
        if (errors.Count > 0)
        {
            await ShowMessageBoxAsync(i18n["MoveFailedTitle"], string.Join("\n", errors),
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Warning);
        }
    }

    private void ShowFolderNameFlyout(Control anchor, string title, string initialText, Func<string?, string?> submit)
    {
        if (_folderNameFlyout?.IsOpen == true) return;

        var box = new TextBox
        {
            Width = 240,
            MaxLength = 64,
            Text = initialText,
            Watermark = i18n["NewFolderWatermark"]
        };
        var error = new TextBlock
        {
            Width = 240,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.IndianRed,
            IsVisible = false
        };
        var okButton = new Button
        {
            Content = i18n["MsgBoxOk"],
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.Bold });
        panel.Children.Add(box);
        panel.Children.Add(error);
        panel.Children.Add(okButton);

        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Bottom };
        _folderNameFlyout = flyout;

        void Submit()
        {
            string? message = submit(box.Text);
            if (message != null)
            {
                error.Text = message;
                error.IsVisible = true;
                return;
            }
            flyout.Hide();
        }

        okButton.Click += (_, _) => Submit();
        box.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                args.Handled = true;
                Submit();
            }
        };
        flyout.Opened += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        flyout.ShowAt(anchor);
    }

    private List<AddonAttribute> GetMovableSelection()
    {
        if (AddonList.SelectedItems == null) return [];

        return AddonList.SelectedItems.Cast<AddonAttribute>()
            .Where(a => a.IsInstalled && !a.IsWorkshopSearchResult && a.Source == AddonSource.Local)
            .ToList();
    }

    private void AddonList_ContextMenu_Opening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        foreach (var old in menu.Items.OfType<MenuItem>().Where(m => MoveMenuTag.Equals(m.Tag)).ToList())
        {
            menu.Items.Remove(old);
        }

        if (DataContext is not MainViewModel vm || vm.IsOnlineMode) return;
        if (vm.IsFolderMode && !vm.IsInsideFolder) return;

        var movable = GetMovableSelection();
        if (movable.Count == 0) return;

        var targets = new List<(string Label, string? Folder)>();
        if (vm.IsInsideFolder) targets.Add(("addons", null));
        foreach (var folder in vm.GetMoveTargetFolders(vm.CurrentFolder))
        {
            targets.Add((folder, folder));
        }
        if (targets.Count == 0) return;

        var moveItem = new MenuItem { Header = i18n["MoveToFolder"], Tag = MoveMenuTag };
        foreach (var (label, folder) in targets)
        {
            string? target = folder;
            var item = new MenuItem { Header = new TextBlock { Text = label } };
            item.Click += (_, _) => MoveSelectedToFolder(movable, target);
            moveItem.Items.Add(item);
        }
        menu.Items.Add(moveItem);
    }

    private async void MoveSelectedToFolder(List<AddonAttribute> addons, string? folder)
    {
        if (DataContext is not MainViewModel vm) return;

        var plain = new List<AddonAttribute>();
        var overwrite = new List<AddonAttribute>();
        foreach (var addon in addons)
        {
            if (!vm.MoveTargetExists(addon, folder))
            {
                plain.Add(addon);
                continue;
            }

            var answer = await ShowMessageBoxAsync(
                i18n["ConfirmOverwriteTitle"],
                string.Format(i18n["ConfirmOverwriteMsg"], addon.FileName),
                ButtonEnum.YesNo, MsBox.Avalonia.Enums.Icon.Warning);
            if (answer == ButtonResult.Yes) overwrite.Add(addon);
        }

        var errors = vm.MoveAddonsToFolder(plain, folder);
        errors.AddRange(vm.MoveAddonsToFolder(overwrite, folder, overwrite: true));
        if (errors.Count > 0)
        {
            await ShowMessageBoxAsync(i18n["MoveFailedTitle"], string.Join("\n", errors),
                ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Warning);
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is MainViewModel vm && e.DataTransfer.TryGetFiles() is { } files)
        {
            string targetDir = vm.AddonsTargetDir;
            
            if (!Directory.Exists(targetDir)) return;

            bool anyImported = false;
            List<string> errorMessages = [];

            foreach (var file in files)
            {
                if (file.Name.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
                {
                    var localPath = file.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(localPath))
                    {
                        try
                        {
                            string destFile = Path.Combine(targetDir, file.Name);
                        if (File.Exists(destFile))
                        {
                            var result = await ShowMessageBoxAsync(
                                i18n["ConfirmOverwriteTitle"],
                                string.Format(i18n["ConfirmOverwriteMsg"], file.Name),
                                ButtonEnum.YesNo,
                                MsBox.Avalonia.Enums.Icon.Warning);

                            if (result != ButtonResult.Yes)
                            {
                                continue;
                            }
                        }
                            File.Move(localPath, destFile, true);
                            anyImported = true;

                            try
                            {
                                string oldBakFile = Path.ChangeExtension(destFile, ".vpk.nekobak");
                                if (File.Exists(oldBakFile))
                                {
                                    File.Delete(oldBakFile);
                                }
                            }
                            catch (Exception bakEx)
                            {
                                Debug.WriteLine(bakEx);
                            }
                        }
                        catch (Exception ex)
                        {
                            errorMessages.Add(string.Format(i18n["ImportErrorMsg"], file.Name, ex.Message));
                        }
                    }
                }
            }

            if (errorMessages.Count > 0)
            {
               await ShowMessageBoxAsync(i18n["ImportErrorTitle"], string.Join("\n", errorMessages), ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Warning);
            }

            if (anyImported)
            {
                ReloadAddonList();
            }
        }
    }

    private void Button_ExitCollection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ExitCollection();
        }
    }

    private void CollectionListView_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (CollectionListView.SelectedItem is SteamCollectionItem collection && DataContext is MainViewModel vm)
        {
            if (CollectionDetailPanel != null) CollectionDetailPanel.IsVisible = false;
            _ = vm.EnterCollectionAsync(collection);
        }
    }

    private void CollectionListView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CollectionListView.SelectedItem is SteamCollectionItem)
        {
            if (AddonDetailPanel != null) AddonDetailPanel.IsVisible = false;
            if (CollectionDetailPanel != null) CollectionDetailPanel.IsVisible = true;
        }
        else
        {
            if (CollectionDetailPanel != null) CollectionDetailPanel.IsVisible = false;
        }
    }

    private void Button_DownloadCollection_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (CollectionListView.SelectedItem is SteamCollectionItem collection && DataContext is MainViewModel vm)
        {
            _ = vm.DownloadCollectionAsync(collection);
        }
    }

    private void CollectionList_Menu_Download(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (CollectionListView.SelectedItem is SteamCollectionItem collection && DataContext is MainViewModel vm)
        {
            _ = vm.DownloadCollectionAsync(collection);
        }
    }

    private void CollectionList_Menu_OpenPage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (CollectionListView.SelectedItem is SteamCollectionItem collection)
        {
            try
            {
                var url = $"https://steamcommunity.com/sharedfiles/filedetails/?id={collection.Id}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }
}

public class SteamBBCodeHelper
{
    private static readonly System.Text.RegularExpressions.Regex _bbcodeRegex = 
        new System.Text.RegularExpressions.Regex(@"\[(/?[a-zA-Z0-9*_]+)(?:=([^\]]+))?\]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex _urlRegex = 
        new System.Text.RegularExpressions.Regex(@"(https?://[-A-Za-z0-9+&@#/%?=~_|!:,.;]*[-A-Za-z0-9+&@#/%=~_|])", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Net.Http.HttpClient _sharedHttpClient = new System.Net.Http.HttpClient();

    public static readonly AttachedProperty<string> BBCodeProperty =
        AvaloniaProperty.RegisterAttached<SteamBBCodeHelper, SelectableTextBlock, string>(
            "BBCode", string.Empty);

    static SteamBBCodeHelper()
    {
        BBCodeProperty.Changed.AddClassHandler<SelectableTextBlock>((sender, e) =>
        {
            if (sender.Inlines != null)
            {
                sender.Inlines.Clear();
                if (e.NewValue is string bbcode && !string.IsNullOrEmpty(bbcode))
                {
                    ParseBBCode(bbcode, sender.Inlines);
                }
            }
        });
    }

    public static string GetBBCode(SelectableTextBlock element) => element.GetValue(BBCodeProperty);
    public static void SetBBCode(SelectableTextBlock element, string value) => element.SetValue(BBCodeProperty, value);

    private static void ParseBBCode(string text, InlineCollection inlines)
    {
        var stack = new Stack<(Span Span, string Tag, string Param)>();
        var rootSpan = new Span();
        inlines.Add(rootSpan);
        stack.Push((rootSpan, "root", string.Empty));

        int lastPos = 0;
        bool noparse = false;

        foreach (System.Text.RegularExpressions.Match match in _bbcodeRegex.Matches(text))
        {
            if (noparse && match.Groups[1].Value.ToLower() != "/noparse")
            {
                continue;
            }

            if (match.Index > lastPos)
            {
                string textPart = text.Substring(lastPos, match.Index - lastPos);
                if (System.Linq.Enumerable.Any(stack, s => s.Tag == "url" || s.Tag == "img" || s.Tag == "previewyoutube"))
                    stack.Peek().Span.Inlines.Add(new Run { Text = textPart });
                else
                    AddTextWithAutoLinks(stack.Peek().Span.Inlines, textPart);
            }

            string fullTag = match.Value;
            string tag = match.Groups[1].Value.ToLower();
            string param = match.Groups[2].Value.Trim('\"', '\'');

            if (tag == "noparse")
            {
                noparse = true;
            }
            else if (tag == "/noparse")
            {
                noparse = false;
            }
            else if (tag.StartsWith("/"))
            {
                string closeTag = tag.Substring(1);
                
                if (System.Linq.Enumerable.Any(stack, s => s.Tag == closeTag))
                {
                    while (stack.Count > 1)
                    {
                        var popped = stack.Pop();
                        OnTagClosed(popped.Span, popped.Tag, popped.Param);
                        if (popped.Tag == closeTag) break;
                    }
                }
                else
                {
                    if (System.Linq.Enumerable.Any(stack, s => s.Tag == "url" || s.Tag == "img" || s.Tag == "previewyoutube"))
                        stack.Peek().Span.Inlines.Add(new Run { Text = fullTag });
                    else
                        AddTextWithAutoLinks(stack.Peek().Span.Inlines, fullTag);
                }
            }
            else
            {
                var newSpan = new Span();
                bool isSelfClosing = tag == "*" || tag == "hr";

                if (ApplyFormatting(newSpan, tag, param))
                {
                    stack.Peek().Span.Inlines.Add(newSpan);
                    if (!isSelfClosing)
                    {
                        stack.Push((newSpan, tag, param));
                    }
                }
                else
                {
                    if (System.Linq.Enumerable.Any(stack, s => s.Tag == "url" || s.Tag == "img" || s.Tag == "previewyoutube"))
                        stack.Peek().Span.Inlines.Add(new Run { Text = fullTag });
                    else
                        AddTextWithAutoLinks(stack.Peek().Span.Inlines, fullTag);
                }
            }
            lastPos = match.Index + match.Length;
        }

        if (lastPos < text.Length)
        {
            string textPart = text.Substring(lastPos);
            if (System.Linq.Enumerable.Any(stack, s => s.Tag == "url" || s.Tag == "img" || s.Tag == "previewyoutube"))
                stack.Peek().Span.Inlines.Add(new Run { Text = textPart });
            else
                AddTextWithAutoLinks(stack.Peek().Span.Inlines, textPart);
        }

        while (stack.Count > 1)
        {
            var popped = stack.Pop();
            OnTagClosed(popped.Span, popped.Tag, popped.Param);
        }
    }

    private static void AddTextWithAutoLinks(InlineCollection inlines, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        int lastPos = 0;

        foreach (System.Text.RegularExpressions.Match match in _urlRegex.Matches(text))
        {
            if (match.Index > lastPos)
            {
                inlines.Add(new Run { Text = text.Substring(lastPos, match.Index - lastPos) });
            }

            string url = match.Value;
            inlines.Add(CreateLinkInline(url));

            lastPos = match.Index + match.Length;
        }

        if (lastPos < text.Length)
        {
            inlines.Add(new Run { Text = text.Substring(lastPos) });
        }
    }

    private static bool IsHostOrSubdomain(string host, string domain)
        => host.Equals(domain, StringComparison.OrdinalIgnoreCase)
           || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private static Inline CreateLinkInline(string url, System.Collections.Generic.List<Inline>? innerInlines = null)
    {
        try 
        {
            var uri = new Uri(url);
            var tb = new TextBlock 
            { 
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = TextDecorations.Underline,
                Foreground = Avalonia.Media.Brush.Parse("#54a5d4"),
                Margin = new Avalonia.Thickness(0)
            };
            
            if (innerInlines != null && innerInlines.Count > 0)
            {
                tb.Inlines.AddRange(innerInlines);
            }
            else
            {
                tb.Text = url;
            }
            
            var btn = new Avalonia.Controls.HyperlinkButton 
            { 
                Content = tb,
                Padding = new Avalonia.Thickness(0),
                Margin = new Avalonia.Thickness(0, 4, 0, -4.5), 
                MinHeight = 0,
                MinWidth = 0,
                BorderThickness = new Avalonia.Thickness(0),
                Background = Avalonia.Media.Brushes.Transparent,
                CornerRadius = new Avalonia.CornerRadius(0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };

            btn.Click += async delegate (object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                e.Handled = true;

                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;

                bool isSteamLink = IsHostOrSubdomain(uri.Host, "steamcommunity.com") ||
                                   IsHostOrSubdomain(uri.Host, "steampowered.com") ||
                                   uri.Host.Equals("s.team", StringComparison.OrdinalIgnoreCase);

                if (!isSteamLink)
                {
                    if (Avalonia.Controls.TopLevel.GetTopLevel(btn) is Avalonia.Controls.Window window)
                    {
                        var i18n = NekoVpk.Lang.I18nManager.Instance;
                        
                        var msgBox = new CustomMessageBox(
                            i18n["JumpConfirmTitle"],
                            $"{i18n["JumpConfirmMsg"]}\n{url}",
                            MsBox.Avalonia.Enums.ButtonEnum.YesNo, 
                            MsBox.Avalonia.Enums.Icon.Info);

                        var result = await msgBox.ShowDialog<MsBox.Avalonia.Enums.ButtonResult>(window);
                        
                        if (result != MsBox.Avalonia.Enums.ButtonResult.Ok && result != MsBox.Avalonia.Enums.ButtonResult.Yes)
                        {
                            return;
                        }
                    }
                }

                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = uri.AbsoluteUri,
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            
            return new Avalonia.Controls.Documents.InlineUIContainer { Child = btn };
        }
        catch 
        {
            var span = new Avalonia.Controls.Documents.Span 
            { 
                TextDecorations = Avalonia.Media.TextDecorations.Underline,
                Foreground = Avalonia.Media.Brush.Parse("#54a5d4")
            };
            if (innerInlines != null && innerInlines.Count > 0)
            {
                span.Inlines.AddRange(innerInlines);
            }
            else
            {
                span.Inlines.Add(new Avalonia.Controls.Documents.Run { Text = url });
            }
            return span;
        }
    }

    private static bool ApplyFormatting(Span span, string tag, string param)
    {
        switch (tag)
        {
            case "b":
                span.FontWeight = FontWeight.Bold;
                return true;
            case "i":
                span.FontStyle = FontStyle.Italic;
                return true;
            case "u":
                span.TextDecorations = TextDecorations.Underline;
                return true;
            case "strike":
                span.TextDecorations = TextDecorations.Strikethrough;
                return true;
            case "h1":
                span.FontSize = 24;
                span.FontWeight = FontWeight.Bold;
                span.Foreground = Brush.Parse("#54a5d4"); 
                return true;
            case "h2":
                span.FontSize = 20;
                span.FontWeight = FontWeight.Bold;
                span.Foreground = Brush.Parse("#54a5d4");
                return true;
            case "h3":
                span.FontSize = 18;
                span.FontWeight = FontWeight.Bold;
                span.Foreground = Brush.Parse("#54a5d4");
                return true;
            case "spoiler":
                span.Background = Brushes.Black;
                span.Foreground = Brushes.Black;
                return true;
            case "color":
                if (!string.IsNullOrEmpty(param))
                {
                    try { span.Foreground = Brush.Parse(param.StartsWith("#") ? param : $"#{param}"); }
                    catch { }
                }
                return true;
            case "code":
                span.FontFamily = FontFamily.Parse("Consolas, Courier New, monospace");
                span.Background = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128));
                return true;
            case "quote":
                span.FontStyle = FontStyle.Italic;
                span.Foreground = Brushes.LightGray;
                string author = !string.IsNullOrEmpty(param) ? param : "Quote";
                span.Inlines.Add(new Run { Text = $"💬 {author}:\n", FontWeight = FontWeight.Bold, FontStyle = FontStyle.Normal });
                return true;
            case "list":
                span.Inlines.Add(new Run { Text = "\n" });
                return true;
            case "table":
            case "tr":
                span.Inlines.Add(new Run { Text = "\n" });
                return true;
            case "th":
            case "td":
                span.Inlines.Add(new Run { Text = " | " });
                return true;
            case "hr":
                span.Inlines.Add(new Run { Text = "\n───────────────────────────────────\n", Foreground = Brushes.Gray });
                return true;
            case "*":
                span.Inlines.Add(new Run { Text = "\n  • " });
                return true;
            case "url":
            case "img":
            case "previewyoutube":
                return true;
            default:
                return false;
        }
    }

    private static void OnTagClosed(Span span, string tag, string param)
    {
        switch (tag)
        {
            case "url":
                string url = !string.IsNullOrEmpty(param) ? param : GetTextFromInlines(span.Inlines).Trim();
                var inlinesList = System.Linq.Enumerable.ToList(span.Inlines);
                span.Inlines.Clear();
                span.Inlines.Add(CreateLinkInline(url, inlinesList));
                break;

            case "img":
                string imgUrl = GetTextFromInlines(span.Inlines).Trim();
                span.Inlines.Clear();
                
                var imgControl = new Image 
                { 
                    MaxHeight = 250, 
                    Stretch = Stretch.Uniform, 
                    Margin = new Thickness(0, 8) 
                };
                
                LoadImageAsync(imgUrl, imgControl);
                span.Inlines.Add(new InlineUIContainer { Child = imgControl });
                break;

            case "previewyoutube":
                string ytUrl = GetTextFromInlines(span.Inlines).Trim();
                span.Inlines.Clear();
                span.Foreground = Brushes.Red;
                span.Inlines.Add(new Run { Text = $"🎥 [YouTube 视频: {ytUrl}]" });
                break;

            case "quote":
            case "list":
            case "table":
            case "tr":
                span.Inlines.Add(new Run { Text = "\n" });
                break;
        }
    }

    private static string GetTextFromInlines(InlineCollection inlines)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var inline in inlines)
        {
            if (inline is Run run) sb.Append(run.Text);
            else if (inline is Span s) sb.Append(GetTextFromInlines(s.Inlines));
        }
        return sb.ToString();
    }

    private static async void LoadImageAsync(string url, Image imgControl)
    {
        try
        {
            var bytes = await _sharedHttpClient.GetByteArrayAsync(url);

            bool isGif = url.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || url.Contains(".gif?");
            if (!isGif && bytes.Length > 3 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
            {
                isGif = true;
            }

            if (isGif)
            {
                var tempFile = TempCache.GetGifPath(url);
                if (!System.IO.File.Exists(tempFile))
                {
                    await System.Threading.Tasks.Task.Run(() => TempCache.WriteFile(tempFile, bytes));
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var uriString = new Uri(tempFile).AbsoluteUri;
                    var binding = new Avalonia.Data.Binding { Source = uriString };
                    imgControl.Bind(AnimatedImage.Avalonia.ImageBehavior.AnimatedSourceProperty, binding);
                });
            }
            else
            {
                using var ms = new System.IO.MemoryStream(bytes);
                var bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(ms, 600);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => imgControl.Source = bitmap);
            }
        }
        catch 
        {
        }
    }
}