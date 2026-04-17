using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Composition;
using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Microsoft.Win32;

namespace VBrowser
{
    public sealed partial class SettingsPage : Page
    {
        private MainWindow? _mainWindow;
        private const string UrlAssociationBasePath = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations";

        public SettingsPage()
        {
            InitializeComponent();
            LoadSettings();
            RefreshDefaultBrowserStatus();
            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            AnimatePageEntrance();
        }

        private void AnimatePageEntrance()
        {
            var visual = ElementCompositionPreview.GetElementVisual(this);
            var compositor = visual.Compositor;

            // Fade in
            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.InsertKeyFrame(0f, 0f);
            fadeAnim.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
            fadeAnim.Duration = TimeSpan.FromMilliseconds(250);

            // Slide up
            var slideAnim = compositor.CreateSpringVector3Animation();
            slideAnim.InitialValue = new Vector3(0, 30, 0);
            slideAnim.FinalValue = new Vector3(0, 0, 0);
            slideAnim.DampingRatio = 0.8f;
            slideAnim.Period = TimeSpan.FromMilliseconds(65);
            slideAnim.StopBehavior = AnimationStopBehavior.SetToFinalValue;

            visual.StartAnimation("Opacity", fadeAnim);
            visual.StartAnimation("Offset", slideAnim);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _mainWindow = e.Parameter as MainWindow;
            RefreshDefaultBrowserStatus();
        }

        private void LoadSettings()
        {
            // Load saved settings (using local settings or preferences)
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            
            if (localSettings.Values.ContainsKey("DarkMode"))
            {
                DarkModeToggle.IsOn = (bool)localSettings.Values["DarkMode"];
            }
            
            if (localSettings.Values.ContainsKey("MemorySaver"))
            {
                MemorySaverToggle.IsOn = (bool)localSettings.Values["MemorySaver"];
            }
            
            if (localSettings.Values.ContainsKey("HighEfficiency"))
            {
                HighEfficiencyToggle.IsOn = (bool)localSettings.Values["HighEfficiency"];
            }

            if (localSettings.Values.ContainsKey("HardwareAcceleration"))
            {
                HardwareAccelerationToggle.IsOn = (bool)localSettings.Values["HardwareAcceleration"];
            }

            if (localSettings.Values.ContainsKey("Homepage"))
            {
                HomepageTextBox.Text = localSettings.Values["Homepage"] as string ?? "https://www.google.com";
            }
            else
            {
                HomepageTextBox.Text = "https://www.google.com";
            }

            if (localSettings.Values.TryGetValue("SearchEngine", out var searchEngineValue) &&
                searchEngineValue is int searchIndex &&
                searchIndex >= 0 &&
                searchIndex < SearchEngineCombo.Items.Count)
            {
                SearchEngineCombo.SelectedIndex = searchIndex;
            }

            // Load vertical tabs setting
            if (localSettings.Values.TryGetValue("VerticalTabs", out var verticalTabsValue) &&
                verticalTabsValue is bool verticalTabs)
            {
                VerticalTabsToggle.IsOn = verticalTabs;
            }
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

        private void DarkModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["DarkMode"] = DarkModeToggle.IsOn;
            
            // Apply theme change to the root element
            ApplyTheme(DarkModeToggle.IsOn ? ElementTheme.Dark : ElementTheme.Light);
        }

        private void ApplyTheme(ElementTheme theme)
        {
            // Get the root element and apply theme
            if (this.XamlRoot != null && this.XamlRoot.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = theme;
            }
            
            // Also apply to this page
            this.RequestedTheme = theme;
        }

        private void VerticalTabsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["VerticalTabs"] = VerticalTabsToggle.IsOn;

            // Apply immediately to the MainWindow
            MainWindow.Instance?.SetVerticalTabsEnabled(VerticalTabsToggle.IsOn);
        }

        private void MemorySaverToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["MemorySaver"] = MemorySaverToggle.IsOn;
            
            // Show restart required dialog
            ShowRestartDialog();
        }

        private void HighEfficiencyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["HighEfficiency"] = HighEfficiencyToggle.IsOn;
            
            // Show restart required dialog
            ShowRestartDialog();
        }

        private void HardwareAccelerationToggle_Toggled(object sender, RoutedEventArgs e)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["HardwareAcceleration"] = HardwareAccelerationToggle.IsOn;

            // Show restart required dialog
            ShowRestartDialog();
        }

        private void ShowRestartDialog()
        {
            // Get XamlRoot with fallback options
            var xamlRoot = this.XamlRoot ?? this.Content?.XamlRoot ?? Frame?.XamlRoot;
            if (xamlRoot == null) return; // Can't show dialog without XamlRoot

            var dialog = new ContentDialog
            {
                Title = "Restart Required",
                Content = "Please restart the browser for performance changes to take effect.",
                CloseButtonText = "OK",
                XamlRoot = xamlRoot
            };
            _ = dialog.ShowAsync();
        }

        private void ClearDataButton_Click(object sender, RoutedEventArgs e)
        {
            // Get XamlRoot with fallback
            var xamlRoot = this.XamlRoot ?? this.Content?.XamlRoot ?? Frame?.XamlRoot;
            if (xamlRoot == null) return;

            // Show confirmation dialog
            var dialog = new ContentDialog
            {
                Title = "Clear Browsing Data",
                Content = "Are you sure you want to clear all browsing data?",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
                XamlRoot = xamlRoot
            };
            
            // In a real app, this would clear cookies, cache, history, etc.
        }

        private void SearchEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["SearchEngine"] = SearchEngineCombo.SelectedIndex;
        }

        private void SaveHomepageButton_Click(object sender, RoutedEventArgs e)
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["Homepage"] = HomepageTextBox.Text;
            
            // Get XamlRoot with fallback
            var xamlRoot = this.XamlRoot ?? this.Content?.XamlRoot ?? Frame?.XamlRoot;
            if (xamlRoot == null) return;

            // Show confirmation
            var dialog = new ContentDialog
            {
                Title = "Saved",
                Content = "Homepage URL has been saved.",
                CloseButtonText = "OK",
                XamlRoot = xamlRoot
            };
            _ = dialog.ShowAsync();
        }

        private void RefreshDefaultBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshDefaultBrowserStatus();
        }

        private void OpenDefaultAppsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:defaultapps")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                ShowInfoDialog(
                    "Could not open Settings",
                    "Please open Windows Settings > Apps > Default apps and set VBrowser for HTTP and HTTPS.");
            }
        }

        private void RefreshDefaultBrowserStatus()
        {
            var status = EvaluateDefaultBrowserStatus();
            DefaultBrowserStatusText.Text = status.Title;
            DefaultBrowserDetailsText.Text = status.Details;
        }

        private static (string Title, string Details) EvaluateDefaultBrowserStatus()
        {
            try
            {
                var httpProgId = GetUserChoiceProgId("http");
                var httpsProgId = GetUserChoiceProgId("https");

                if (string.IsNullOrWhiteSpace(httpProgId) || string.IsNullOrWhiteSpace(httpsProgId))
                {
                    return (
                        "Status unknown",
                        "Windows did not return current HTTP/HTTPS associations.");
                }

                var httpCommand = GetOpenCommand(httpProgId);
                var httpsCommand = GetOpenCommand(httpsProgId);
                var httpLabel = GetAssociationLabel(httpProgId, httpCommand);
                var httpsLabel = GetAssociationLabel(httpsProgId, httpsCommand);

                var currentProcessPath = Environment.ProcessPath ??
                    Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                var currentProcessName = Path.GetFileNameWithoutExtension(currentProcessPath);

                bool httpIsVBrowser = IsLikelyCurrentBrowser(httpProgId, httpCommand, currentProcessPath, currentProcessName);
                bool httpsIsVBrowser = IsLikelyCurrentBrowser(httpsProgId, httpsCommand, currentProcessPath, currentProcessName);

                if (httpIsVBrowser && httpsIsVBrowser)
                {
                    return (
                        "VBrowser is your default browser",
                        $"HTTP={httpLabel}, HTTPS={httpsLabel}.");
                }

                if (!httpIsVBrowser && !httpsIsVBrowser)
                {
                    return (
                        "VBrowser is not the default browser",
                        $"Current defaults: HTTP={httpLabel}, HTTPS={httpsLabel}.");
                }

                return (
                    "Partially configured",
                    $"Only one protocol is using VBrowser. Current: HTTP={httpLabel}, HTTPS={httpsLabel}.");
            }
            catch
            {
                return (
                    "Status unknown",
                    "Could not read Windows default-browser associations.");
            }
        }

        private static string? GetUserChoiceProgId(string scheme)
        {
            var userChoiceProgId = GetProgIdFromKey($@"{UrlAssociationBasePath}\{scheme}\UserChoice");
            if (!string.IsNullOrWhiteSpace(userChoiceProgId))
            {
                return userChoiceProgId;
            }

            return GetProgIdFromKey($@"{UrlAssociationBasePath}\{scheme}\UserChoiceLatest");
        }

        private static string? GetProgIdFromKey(string registryPath)
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            return key?.GetValue("ProgId") as string;
        }

        private static string? GetOpenCommand(string progId)
        {
            if (string.IsNullOrWhiteSpace(progId))
            {
                return null;
            }

            using var key = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            return key?.GetValue(null) as string;
        }

        private static bool IsLikelyCurrentBrowser(
            string progId,
            string? openCommand,
            string currentProcessPath,
            string currentProcessName)
        {
            if (progId.Contains("VBrowser", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(openCommand))
            {
                return false;
            }

            if (openCommand.Contains("VBrowser", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var commandExecutable = ExtractExecutablePath(openCommand);
            if (string.IsNullOrWhiteSpace(commandExecutable))
            {
                return false;
            }

            var commandExecutableName = Path.GetFileNameWithoutExtension(commandExecutable);
            if (!string.IsNullOrWhiteSpace(currentProcessName) &&
                string.Equals(commandExecutableName, currentProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(currentProcessPath))
            {
                return false;
            }

            try
            {
                var normalizedCurrent = Path.GetFullPath(currentProcessPath);
                var normalizedCommand = Path.GetFullPath(commandExecutable);

                return string.Equals(normalizedCurrent, normalizedCommand, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string? ExtractExecutablePath(string command)
        {
            var trimmed = command.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            if (trimmed[0] == '"')
            {
                int endQuoteIndex = trimmed.IndexOf('"', 1);
                if (endQuoteIndex > 1)
                {
                    return trimmed[1..endQuoteIndex];
                }
            }

            int firstSpaceIndex = trimmed.IndexOf(' ');
            return firstSpaceIndex > 0
                ? trimmed[..firstSpaceIndex]
                : trimmed;
        }

        private static string GetAssociationLabel(string progId, string? openCommand)
        {
            if (string.IsNullOrWhiteSpace(openCommand))
            {
                return progId;
            }

            var executablePath = ExtractExecutablePath(openCommand);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return progId;
            }

            var fileName = Path.GetFileName(executablePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return progId;
            }

            return $"{progId} ({fileName})";
        }

        private void ShowInfoDialog(string title, string content)
        {
            var xamlRoot = this.XamlRoot ?? this.Content?.XamlRoot ?? Frame?.XamlRoot;
            if (xamlRoot == null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                XamlRoot = xamlRoot
            };

            _ = dialog.ShowAsync();
        }
    }
}
