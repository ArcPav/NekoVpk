using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.IO;
using System;
using System.Linq;
using System.Diagnostics;

namespace NekoVpk.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            this.Bind(Window.FontFamilyProperty, new Avalonia.Data.Binding(nameof(ViewModels.Settings.UserFontFamily)));
            this.Bind(Window.FontSizeProperty, new Avalonia.Data.Binding(nameof(ViewModels.Settings.UserFontSize)));
            //ComboBox_CompressionLevel.ItemsSource = Enum.GetValues(typeof(SevenZip.CompressionLevel));
        }

        public async void SelectBackgroundImage()
        {
            var storage = this.StorageProvider;
            var result = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = NekoVpk.Lang.I18nManager.Instance["SelectBgImageTitle"], 
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
                AllowMultiple = false
            });

            if (result.Count > 0)
            {
                string path = result[0].Path.LocalPath;
                if (DataContext is ViewModels.Settings vm)
                {
                    vm.BackgroundImagePath = path;
                    vm.BackgroundBrightness = 50;
                }
            }
        }

        public async void SelectBackgroundFolder()
        {
            var storage = this.StorageProvider;
            var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = NekoVpk.Lang.I18nManager.Instance["SelectBgFolderTitle"],
                AllowMultiple = false
            });

            if (result.Count > 0)
            {
                string path = result[0].Path.LocalPath;
                if (DataContext is ViewModels.Settings vm)
                {
                    vm.BackgroundImagePath = path;
                    vm.BackgroundBrightness = 50;
                }
            }
        }

        private async void Browser_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var storageProvider = topLevel.StorageProvider;
            if (storageProvider is null) return;

            IStorageFolder? suggestedStartLocation = null;
            if (NekoSettings.Default.GameDir != "")
            {
                suggestedStartLocation = await storageProvider.TryGetFolderFromPathAsync(new Uri(NekoSettings.Default.GameDir));
            }
            var result = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                Title = NekoVpk.Lang.I18nManager.Instance["SelectGameDirTitle"],
                AllowMultiple = false,
                SuggestedStartLocation = suggestedStartLocation
            });


            if (result is not null && result.Count == 1)
            {
                var dirInfo = new DirectoryInfo(result[0].Path.LocalPath);
                var dirs = dirInfo.GetDirectories("addons");
                if (dirs.Length == 0)
                {
                    dirs = dirInfo.GetDirectories("left4dead2");
                    if (dirs.Length != 0)
                    {
                        GameDir.Text = dirs[0].FullName;
                        return;
                    }
                }
                GameDir.Text = dirInfo.FullName;
            }
        }

        private void OpenSteamApiKey_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "https://steamcommunity.com/dev/apikey",
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                //System.Diagnostics.Debug.WriteLine($"打开申请页面失败: {ex.Message}");
            }
        }
    }
}
