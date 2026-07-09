using System.ComponentModel;
using System.Globalization;
using System.Threading;

namespace NekoVpk.Lang
{
    public class I18nManager : INotifyPropertyChanged
    {
        public static I18nManager Instance { get; } = new I18nManager();

        public event PropertyChangedEventHandler? PropertyChanged;

        public string this[string key]
        {
            get
            {
                return Resources.ResourceManager.GetString(key, Culture) ?? key;
            }
        }

        public CultureInfo Culture
        {
            get => Resources.Culture ?? CultureInfo.InstalledUICulture;
            set
            {
                if (Equals(Resources.Culture, value)) return;
                
                Resources.Culture = value;
                Thread.CurrentThread.CurrentCulture = value;
                Thread.CurrentThread.CurrentUICulture = value;
                
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
            }
        }

        public void SetLanguage(string langCode)
        {
            if (string.IsNullOrEmpty(langCode) || langCode == "Auto")
            {
                Culture = CultureInfo.InstalledUICulture;
            }
            else
            {
                Culture = new CultureInfo(langCode);
            }
        }
    }
}