using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace VBrowser
{
    public sealed partial class FlagsPage : Page
    {
        private MainWindow? _mainWindow;
        private bool _isInitializing;

        public FlagsPage()
        {
            InitializeComponent();
            LoadFlags();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _mainWindow = e.Parameter as MainWindow;
        }

        private void LoadFlags()
        {
            _isInitializing = true;

            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

            HardwareAccelerationToggle.IsOn =
                !localSettings.Values.TryGetValue("HardwareAcceleration", out var hardwareAccelerationValue) ||
                (hardwareAccelerationValue is bool hardwareAcceleration && hardwareAcceleration);

            ParallelDownloadsToggle.IsOn =
                !localSettings.Values.TryGetValue("ParallelDownloads", out var parallelDownloadsValue) ||
                (parallelDownloadsValue is bool parallelDownloads && parallelDownloads);

            MemorySaverToggle.IsOn =
                localSettings.Values.TryGetValue("MemorySaver", out var memorySaverValue) &&
                memorySaverValue is bool memorySaver &&
                memorySaver;

            HighEfficiencyToggle.IsOn =
                localSettings.Values.TryGetValue("HighEfficiency", out var highEfficiencyValue) &&
                highEfficiencyValue is bool highEfficiency &&
                highEfficiency;

            _isInitializing = false;
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mainWindow != null)
            {
                _mainWindow.ShowActiveTab();
                return;
            }

            if (Frame?.CanGoBack == true)
            {
                Frame.GoBack();
            }
        }

        private void HardwareAccelerationToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SaveFlagWithRestartPrompt("HardwareAcceleration", HardwareAccelerationToggle.IsOn);
        }

        private void ParallelDownloadsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SaveFlagWithRestartPrompt("ParallelDownloads", ParallelDownloadsToggle.IsOn);
        }

        private void MemorySaverToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SaveFlagWithRestartPrompt("MemorySaver", MemorySaverToggle.IsOn);
        }

        private void HighEfficiencyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            SaveFlagWithRestartPrompt("HighEfficiency", HighEfficiencyToggle.IsOn);
        }

        private void ResetAllFlags_Click(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            HardwareAccelerationToggle.IsOn = true;
            ParallelDownloadsToggle.IsOn = true;
            MemorySaverToggle.IsOn = false;
            HighEfficiencyToggle.IsOn = false;

            _isInitializing = false;

            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["HardwareAcceleration"] = true;
            localSettings.Values["ParallelDownloads"] = true;
            localSettings.Values["MemorySaver"] = false;
            localSettings.Values["HighEfficiency"] = false;

            ShowRestartDialog("Flags were reset to defaults. Restart the browser to apply all changes.");
        }

        private static void SaveBooleanFlag(string key, bool value)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values[key] = value;
        }

        private void SaveFlagWithRestartPrompt(string key, bool value)
        {
            SaveBooleanFlag(key, value);
            ShowRestartDialog("Restart the browser for this flag change to take full effect.");
        }

        private void ShowRestartDialog(string message)
        {
            var xamlRoot = XamlRoot ?? Content?.XamlRoot ?? Frame?.XamlRoot;
            if (xamlRoot == null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Restart Required",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = xamlRoot
            };

            _ = dialog.ShowAsync();
        }
    }
}
