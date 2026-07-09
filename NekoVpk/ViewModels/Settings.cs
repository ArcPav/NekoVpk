using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace NekoVpk.ViewModels
{
    public partial class Settings : ViewModelBase
    {
        public List<string> InstalledFonts { get; } = FontManager.Current.SystemFonts
            .Select(f => f.Name)
            .OrderBy(x => x)
            .ToList();

        public int LanguageIndex
        {
            get
            {
                string lang = NekoSettings.Default.Language;
                return lang switch
                {
                    "zh-CN" => 1,
                    "en-US" => 2,
                    "ja-JP" => 3,
                    _ => 0
                };
            }
            set
            {
                string lang = value switch
                {
                    1 => "zh-CN",
                    2 => "en-US",
                    3 => "ja-JP",
                    _ => "Auto"
                };
                
                if (NekoSettings.Default.Language != lang)
                {
                    NekoSettings.Default.Language = lang;
                    NekoVpk.Lang.I18nManager.Instance.SetLanguage(lang);
                    this.RaisePropertyChanged(nameof(LanguageIndex));
                    NekoSettings.Default.Save();
                }
            }
        }

        public string UserFont
        {
            get => NekoSettings.Default.UserFont;
            set
            {
                if (NekoSettings.Default.UserFont != value)
                {
                    NekoSettings.Default.UserFont = value;
                    this.RaisePropertyChanged(nameof(UserFont));
                    this.RaisePropertyChanged(nameof(UserFontFamily));
                    NekoSettings.Default.Save();
                }
            }
        }

        public FontFamily UserFontFamily
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

        public double UserFontSize
        {
            get => NekoSettings.Default.UserFontSize;
            set
            {
                if (value < 5) value = 5;
                if (value > 50) value = 50;

                if (Math.Abs(NekoSettings.Default.UserFontSize - value) > 0.01)
                {
                    NekoSettings.Default.UserFontSize = value;
                    this.RaisePropertyChanged(nameof(UserFontSize));
                    NekoSettings.Default.Save();
                }
            }
        }

        public string GameDir
        {
            get => NekoSettings.Default.GameDir;
            set
            {
                if (NekoSettings.Default.GameDir != value)
                {
                    NekoSettings.Default.GameDir = value;
                    this.RaisePropertyChanged(nameof(GameDir));
                }
            }
        }

        public short CompressionLevel
        {
            get => (short)(NekoSettings.Default.CompressionLevel - 1);
            set
            {
                if (CompressionLevel != (value))
                {
                    NekoSettings.Default.CompressionLevel = (short)(value+1);
                    this.RaisePropertyChanged(nameof(CompressionLevel));
                }
            }
        }

        public bool IgnoreVpkErrors
        {
            get => NekoSettings.Default.IgnoreVpkErrors;
            set
            {
                if (NekoSettings.Default.IgnoreVpkErrors != value)
                {
                    NekoSettings.Default.IgnoreVpkErrors = value;
                    this.RaisePropertyChanged(nameof(IgnoreVpkErrors));
                    NekoSettings.Default.Save(); 
                }
            }
        }

        public bool ClearSearchAfterDownload
        {
            get => NekoSettings.Default.ClearSearchAfterDownload;
            set
            {
                if (NekoSettings.Default.ClearSearchAfterDownload != value)
                {
                    NekoSettings.Default.ClearSearchAfterDownload = value;
                    this.RaisePropertyChanged(nameof(ClearSearchAfterDownload));
                    NekoSettings.Default.Save();
                }
            }
        }

        public bool SaveColumnWidths
        {
            get => NekoSettings.Default.SaveColumnWidths;
            set
            {
                if (NekoSettings.Default.SaveColumnWidths != value)
                {
                    NekoSettings.Default.SaveColumnWidths = value;
                    this.RaisePropertyChanged(nameof(SaveColumnWidths));
                    NekoSettings.Default.Save();
                }
            }
        }

        public bool EnableConflictDetection
        {
            get => NekoSettings.Default.EnableConflictDetection;
            set
            {
                if (NekoSettings.Default.EnableConflictDetection != value)
                {
                    NekoSettings.Default.EnableConflictDetection = value;
                    this.RaisePropertyChanged(nameof(EnableConflictDetection));
                    NekoSettings.Default.Save();
                }
            }
        }

        public int BackgroundStretchIndex
        {
            get
            {
                string s = NekoSettings.Default.BackgroundStretch;
                return s switch
                {
                    "UniformToFill" => 0,
                    "Uniform" => 1,
                    "Fill" => 2,
                    "None" => 3,
                    _ => 0
                };
            }
            set
            {
                string s = value switch
                {
                    0 => "UniformToFill",
                    1 => "Uniform",
                    2 => "Fill",
                    3 => "None",
                    _ => "UniformToFill"
                };
                
                if (NekoSettings.Default.BackgroundStretch != s)
                {
                    NekoSettings.Default.BackgroundStretch = s;
                    NekoSettings.Default.Save();
                    this.RaisePropertyChanged(nameof(BackgroundStretchIndex));
                }
            }
        }

        public string BackgroundImagePath
        {
            get => NekoSettings.Default.BackgroundImagePath;
            set
            {
                if (NekoSettings.Default.BackgroundImagePath != value)
                {
                    NekoSettings.Default.BackgroundImagePath = value;
                    this.RaisePropertyChanged(nameof(BackgroundImagePath));
                    NekoSettings.Default.Save();
                }
            }
        }

        public double BackgroundBrightness
        {
            get => NekoSettings.Default.BackgroundBrightness;
            set
            {
                if (NekoSettings.Default.BackgroundBrightness != value)
                {
                    NekoSettings.Default.BackgroundBrightness = value;
                    this.RaisePropertyChanged(nameof(BackgroundBrightness));
                    NekoSettings.Default.Save();
                }
            }
        }

        public string SteamApiKey
        {
            get => NekoSettings.Default.SteamApiKey;
            set
            {
                if (NekoSettings.Default.SteamApiKey != value)
                {
                    NekoSettings.Default.SteamApiKey = value;
                    this.RaisePropertyChanged(nameof(SteamApiKey));
                    NekoSettings.Default.Save();
                }
            }
        }

        private bool _isApiKeyVisible;
        public bool IsApiKeyVisible
        {
            get => _isApiKeyVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isApiKeyVisible, value);
                this.RaisePropertyChanged(nameof(ApiKeyPasswordChar));
            }
        }
        public char ApiKeyPasswordChar => IsApiKeyVisible ? default(char) : '●';

        public void ClearBackground()
        {
            BackgroundImagePath = string.Empty;
            BackgroundBrightness = 100;
        }
    }
}