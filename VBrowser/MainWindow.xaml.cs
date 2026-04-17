using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Windowing;
using System.Net;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VBrowser
{
    /// <summary>
    /// A simple browser window with navigation controls.
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private const string FallbackStartUrl = "https://www.google.com";
        private const string InternalFlagsUrl = "vbrowser://flags";
        private const double VerticalTabSidebarWidth = 240;
        private readonly string _defaultStartUrl;
        private List<TabViewItem> _tabs = new();
        private Dictionary<TabViewItem, BrowserPage> _tabPages = new();
        private HashSet<TabViewItem> _privateTabs = new();
        private int _activeTabIndex = 0;
        private bool _verticalTabsEnabled = false;
        private bool _sidebarExpanded = true;
        private string? _pendingCertErrorUrl = null;
        private bool _isUpdatingTabSelection = false;
        private bool _isBrowserFullscreen = false;
        private bool _wasCertWarningVisibleBeforeFullscreen = false;
        private readonly Dictionary<string, CoreWebView2PermissionState> _savedSitePermissionDecisions =
            new(StringComparer.OrdinalIgnoreCase);

        // Maps horizontal tab items to their vertical counterparts and vice-versa
        private Dictionary<TabViewItem, Button> _horizontalToVertical = new();
        private Dictionary<Button, TabViewItem> _verticalToHorizontal = new();

        public static MainWindow? Instance { get; private set; }

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();
            ConfigureWindowPresentation();
            ApplySavedTheme();
            LoadVerticalTabSetting();
            _defaultStartUrl = GetConfiguredHomePage();
            _tabs.Add(Tab1);
            _tabPages[Tab1] = CreateBrowserPage(_defaultStartUrl, false);

            // If vertical tabs enabled, sync the initial tab
            if (_verticalTabsEnabled)
            {
                SyncVerticalTab(Tab1, false);
            }

            // Show the first tab on startup.
            SwitchToTab(0);
        }

        private void ConfigureWindowPresentation()
        {
            try
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                AppWindow.SetIcon("Assets/VBrowser.ico");
            }
            catch
            {
            }
        }

        private void ApplySavedTheme()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            bool darkModeEnabled = localSettings.Values.TryGetValue("DarkMode", out var darkModeValue)
                && darkModeValue is bool isDarkMode
                && isDarkMode;

            if (Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = darkModeEnabled
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
            }
        }

        #region Vertical Tabs

        private void LoadVerticalTabSetting()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            _verticalTabsEnabled = localSettings.Values.TryGetValue("VerticalTabs", out var val)
                && val is bool enabled && enabled;

            _sidebarExpanded = localSettings.Values.TryGetValue("VerticalTabsSidebarExpanded", out var expVal)
                && expVal is bool exp ? exp : true;

            ApplyTabLayout();
        }

        public void SetVerticalTabsEnabled(bool enabled)
        {
            _verticalTabsEnabled = enabled;
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["VerticalTabs"] = enabled;

            ApplyTabLayout();
        }

        private void ApplyTabLayout()
        {
            if (_verticalTabsEnabled)
            {
                // Hide horizontal tab strip, show vertical sidebar
                BrowserTabView.Visibility = Visibility.Collapsed;
                ExpandSidebarButton.Visibility = Visibility.Visible;

                // Sync all existing tabs to vertical
                SyncAllTabsToVertical();

                if (_sidebarExpanded)
                {
                    ShowSidebar();
                }
                else
                {
                    HideSidebar();
                }

                UpdateVerticalTabStyles();
            }
            else
            {
                // Show horizontal tabs, hide vertical sidebar
                BrowserTabView.Visibility = Visibility.Visible;
                VerticalTabSidebar.Visibility = Visibility.Collapsed;
                VerticalTabColumn.Width = new GridLength(0);
                ExpandSidebarButton.Visibility = Visibility.Collapsed;

                // Clear vertical tab mappings
                VerticalTabContainer.Children.Clear();
                _horizontalToVertical.Clear();
                _verticalToHorizontal.Clear();
            }
        }

        private void SyncAllTabsToVertical()
        {
            VerticalTabContainer.Children.Clear();
            _horizontalToVertical.Clear();
            _verticalToHorizontal.Clear();

            foreach (var tab in _tabs)
            {
                bool isPrivate = _privateTabs.Contains(tab);
                SyncVerticalTab(tab, isPrivate);
            }
        }

        private void SyncVerticalTab(TabViewItem horizontalTab, bool isPrivate)
        {
            // Get title from horizontal tab
            string title = GetTabHeaderTitle(horizontalTab);

            var verticalTab = CreateVerticalTabButton(title, isPrivate);

            _horizontalToVertical[horizontalTab] = verticalTab;
            _verticalToHorizontal[verticalTab] = horizontalTab;

            VerticalTabContainer.Children.Add(verticalTab);
            UpdateVerticalTabIcon(horizontalTab);
        }

        private Button CreateVerticalTabButton(string title, bool isPrivate)
        {
            var tabButton = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 36,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 0, 6, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };

            if (isPrivate)
            {
                tabButton.Background = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(180, 55, 35, 95));
            }

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconHost = new ContentControl
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = "VerticalIconHost"
            };
            iconHost.SetValue(Grid.ColumnProperty, 0);

            if (isPrivate)
            {
                iconHost.Content = new FontIcon
                {
                    Glyph = "\uE72E",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 200, 170, 255))
                };
            }
            else
            {
                iconHost.Content = new SymbolIcon
                {
                    Symbol = Symbol.World,
                    Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 45, 45, 45))
                };
            }

            var text = new TextBlock
            {
                Text = isPrivate ? "Private" : title,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Tag = "VerticalTitleText",
                Foreground = isPrivate
                    ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 220, 200, 255))
                    : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 45, 45, 45))
            };
            text.SetValue(Grid.ColumnProperty, 1);

            var closeGlyph = new FontIcon
            {
                Glyph = "\uE711",
                FontSize = 10,
                Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 130, 130, 130))
            };

            var closeBtn = new Button
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Content = closeGlyph,
                Tag = "VerticalCloseButton",
                Opacity = 0.6
            };
            closeBtn.SetValue(Grid.ColumnProperty, 2);
            closeBtn.Click += (s, e) =>
            {
                // Find the horizontal tab this maps to and close it
                if (_verticalToHorizontal.TryGetValue(tabButton, out var hTab))
                {
                    CloseTab(hTab);
                }
            };

            grid.Children.Add(iconHost);
            grid.Children.Add(text);
            grid.Children.Add(closeBtn);

            tabButton.Content = grid;
            tabButton.Click += (s, e) =>
            {
                if (_verticalToHorizontal.TryGetValue(tabButton, out var hTab))
                {
                    var idx = _tabs.IndexOf(hTab);
                    if (idx >= 0) SwitchToTab(idx);
                }
            };

            tabButton.Tag = isPrivate ? "private" : "normal";

            return tabButton;
        }

        private void ShowSidebar()
        {
            VerticalTabSidebar.Visibility = Visibility.Visible;
            AnimateSidebarOpen();
            _sidebarExpanded = true;
            SaveSidebarState();
        }

        private void HideSidebar()
        {
            AnimateSidebarClose();
            _sidebarExpanded = false;
            SaveSidebarState();
        }

        private void AnimateSidebarOpen()
        {
            VerticalTabColumn.Width = new GridLength(VerticalTabSidebarWidth);

            var visual = ElementCompositionPreview.GetElementVisual(VerticalTabSidebar);
            var compositor = visual.Compositor;

            // Spring animation for natural, buttery feel
            var springAnim = compositor.CreateSpringVector3Animation();
            springAnim.FinalValue = new Vector3(0, 0, 0);
            springAnim.InitialValue = new Vector3(-(float)VerticalTabSidebarWidth, 0, 0);
            springAnim.DampingRatio = 0.85f;
            springAnim.Period = TimeSpan.FromMilliseconds(60);
            springAnim.StopBehavior = AnimationStopBehavior.SetToFinalValue;

            visual.StartAnimation("Offset", springAnim);

            // Fade in
            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.InsertKeyFrame(0f, 0f);
            fadeAnim.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
            fadeAnim.Duration = TimeSpan.FromMilliseconds(200);
            visual.StartAnimation("Opacity", fadeAnim);
        }

        private void AnimateSidebarClose()
        {
            var visual = ElementCompositionPreview.GetElementVisual(VerticalTabSidebar);
            var compositor = visual.Compositor;

            var slideAnim = compositor.CreateVector3KeyFrameAnimation();
            slideAnim.InsertKeyFrame(1f, new Vector3(-(float)VerticalTabSidebarWidth, 0, 0),
                compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f)));
            slideAnim.Duration = TimeSpan.FromMilliseconds(200);

            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.InsertKeyFrame(1f, 0f,
                compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.6f, 1f)));
            fadeAnim.Duration = TimeSpan.FromMilliseconds(180);

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Offset", slideAnim);
            visual.StartAnimation("Opacity", fadeAnim);
            batch.End();

            batch.Completed += (s, e) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    VerticalTabSidebar.Visibility = Visibility.Collapsed;
                    VerticalTabColumn.Width = new GridLength(0);
                    // Reset for next open
                    visual.Offset = new Vector3(0, 0, 0);
                    visual.Opacity = 1f;
                });
            };
        }

        private void SaveSidebarState()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            localSettings.Values["VerticalTabsSidebarExpanded"] = _sidebarExpanded;
        }

        private void ExpandSidebar_Click(object sender, RoutedEventArgs e)
        {
            ShowSidebar();
        }

        private void CollapseSidebar_Click(object sender, RoutedEventArgs e)
        {
            HideSidebar();
        }

        private void UpdateVerticalTabTitle(TabViewItem horizontalTab, string title)
        {
            if (!_horizontalToVertical.TryGetValue(horizontalTab, out var verticalTab))
                return;

            if (verticalTab.Content is not Grid grid)
                return;

            var textBlock = grid.Children
                .OfType<TextBlock>()
                .FirstOrDefault(child => string.Equals(child.Tag as string, "VerticalTitleText", StringComparison.Ordinal));
            if (textBlock != null)
            {
                bool isPrivate = _privateTabs.Contains(horizontalTab);
                textBlock.Text = isPrivate ? "Private" : (string.IsNullOrWhiteSpace(title) ? "New Tab" : title);
            }
        }

        private void UpdateVerticalTabIcon(TabViewItem horizontalTab)
        {
            if (!_horizontalToVertical.TryGetValue(horizontalTab, out var verticalTab))
            {
                return;
            }

            if (verticalTab.Content is not Grid grid)
            {
                return;
            }

            var iconHost = grid.Children
                .OfType<ContentControl>()
                .FirstOrDefault(child => string.Equals(child.Tag as string, "VerticalIconHost", StringComparison.Ordinal));
            if (iconHost == null)
            {
                return;
            }

            bool isPrivate = _privateTabs.Contains(horizontalTab);
            bool isDarkTheme = false;
            if (Content is FrameworkElement rootElement)
            {
                var effectiveTheme = rootElement.ActualTheme == ElementTheme.Default
                    ? rootElement.RequestedTheme
                    : rootElement.ActualTheme;
                isDarkTheme = effectiveTheme == ElementTheme.Dark;
            }

            var normalForeground = isDarkTheme
                ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 230, 230, 230))
                : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 45, 45, 45));

            if (isPrivate)
            {
                iconHost.Content = new FontIcon
                {
                    Glyph = "\uE72E",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 200, 170, 255))
                };
                return;
            }

            if (horizontalTab.IconSource is BitmapIconSource bitmapIconSource && bitmapIconSource.UriSource != null)
            {
                iconHost.Content = new Image
                {
                    Source = new BitmapImage(bitmapIconSource.UriSource),
                    Width = 14,
                    Height = 14,
                    Stretch = Stretch.UniformToFill
                };
                return;
            }

            if (horizontalTab.IconSource is FontIconSource fontIconSource)
            {
                iconHost.Content = new FontIcon
                {
                    Glyph = string.IsNullOrWhiteSpace(fontIconSource.Glyph) ? "\uE774" : fontIconSource.Glyph,
                    FontSize = 14,
                    Foreground = fontIconSource.Foreground ?? normalForeground
                };
                return;
            }

            if (horizontalTab.IconSource is SymbolIconSource symbolIconSource)
            {
                iconHost.Content = new SymbolIcon
                {
                    Symbol = symbolIconSource.Symbol,
                    Foreground = normalForeground
                };
                return;
            }

            iconHost.Content = new SymbolIcon
            {
                Symbol = Symbol.World,
                Foreground = normalForeground
            };
        }

        private void UpdateVerticalTabStyles()
        {
            if (!_verticalTabsEnabled) return;

            bool isDarkTheme = false;
            if (Content is FrameworkElement rootElement)
            {
                var effectiveTheme = rootElement.ActualTheme == ElementTheme.Default
                    ? rootElement.RequestedTheme
                    : rootElement.ActualTheme;
                isDarkTheme = effectiveTheme == ElementTheme.Dark;
            }

            var activeBackground = isDarkTheme
                ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 58, 58, 58))
                : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 232, 232, 232));
            var inactiveBackground = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            var activePrivateBackground = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 90, 60, 140));
            var inactivePrivateBackground = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(180, 55, 35, 95));
            var activeNormalForeground = isDarkTheme
                ? new SolidColorBrush(Microsoft.UI.Colors.White)
                : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 24));
            var inactiveNormalForeground = isDarkTheme
                ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 230, 230, 230))
                : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 45, 45, 45));

            var activePrivateForeground = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 240, 225, 255));
            var inactivePrivateForeground = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 220, 200, 255));

            for (int i = 0; i < _tabs.Count; i++)
            {
                if (!_horizontalToVertical.TryGetValue(_tabs[i], out var vTab))
                    continue;

                bool isPriv = _privateTabs.Contains(_tabs[i]);
                if (i == _activeTabIndex)
                {
                    vTab.Background = isPriv ? activePrivateBackground : activeBackground;
                }
                else
                {
                    vTab.Background = isPriv ? inactivePrivateBackground : inactiveBackground;
                }

                if (vTab.Content is Grid grid)
                {
                    var textBlock = grid.Children
                        .OfType<TextBlock>()
                        .FirstOrDefault(child => string.Equals(child.Tag as string, "VerticalTitleText", StringComparison.Ordinal));
                    if (textBlock != null)
                    {
                        textBlock.Foreground = isPriv
                            ? (i == _activeTabIndex ? activePrivateForeground : inactivePrivateForeground)
                            : (i == _activeTabIndex ? activeNormalForeground : inactiveNormalForeground);
                    }

                    var closeButton = grid.Children
                        .OfType<Button>()
                        .FirstOrDefault(child => string.Equals(child.Tag as string, "VerticalCloseButton", StringComparison.Ordinal));
                    if (closeButton?.Content is FontIcon closeGlyph)
                    {
                        closeGlyph.Foreground = isPriv
                            ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 230, 210, 255))
                            : (i == _activeTabIndex
                                ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 140, 140, 140))
                                : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 120, 120, 120)));
                    }
                }

                UpdateVerticalTabIcon(_tabs[i]);
            }
        }

        #endregion

        private void WebView_NavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
        {
            var tabIndex = GetTabIndexForWebView(sender);
            if (tabIndex < 0)
            {
                return;
            }

            // Block known unsafe URL schemes
            if (IsUnsafeUrl(args.Uri))
            {
                args.Cancel = true;
                ShowUnsafeUrlWarning(args.Uri);
                return;
            }

            // Start loading favicon immediately for faster display
            if (!string.Equals(args.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                UpdateTabFavicon(tabIndex, args.Uri);
            }

            if (tabIndex == _activeTabIndex)
            {
                if (!string.Equals(args.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
                {
                    AddressBar.Text = args.Uri;
                }
                AnimateLoadingProgress(true);
                CertWarningBar.Visibility = Visibility.Collapsed;
            }
        }

        private void WebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            var tabIndex = GetTabIndexForWebView(sender);
            if (tabIndex < 0)
            {
                return;
            }

            var completedUrl = sender.CoreWebView2?.Source ?? sender.Source?.ToString();
            if (args.IsSuccess &&
                !string.IsNullOrWhiteSpace(completedUrl) &&
                !string.Equals(completedUrl, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                // Skip history recording for private tabs
                bool isPrivateTab = tabIndex >= 0 && tabIndex < _tabs.Count && _privateTabs.Contains(_tabs[tabIndex]);
                if (!isPrivateTab)
                {
                    var historyTitle = sender.CoreWebView2?.DocumentTitle;
                    if (string.IsNullOrWhiteSpace(historyTitle))
                    {
                        historyTitle = GetHostFromUri(completedUrl) ?? completedUrl;
                    }

                    HistoryManager.Instance.AddEntry(historyTitle, completedUrl);
                }
            }

            if (args.IsSuccess && completedUrl != null && 
                (completedUrl.Contains("chromewebstore.google.com/detail/") || completedUrl.Contains("microsoftedge.microsoft.com/addons/detail/")))
            {
                // Inject the Web Store hack button
                string injectScript = @"
                    (function() {
                        if (document.getElementById('vbrowser-install-btn')) return;
                        
                        // Hide native buttons to prevent 'Download Interrupted' error
                        let style = document.createElement('style');
                        style.innerHTML = 'button[aria-label*=""Add to""], button[title*=""Get""], button[aria-label*=""Get""] { display: none !important; }';
                        document.head.appendChild(style);

                        let btn = document.createElement('button');
                        btn.id = 'vbrowser-install-btn';
                        btn.innerText = 'Add Extension to VBrowser';
                        btn.style.cssText = 'position:fixed;top:80px;right:40px;z-index:999999;padding:15px;background:#0078d4;color:white;border-radius:8px;font-size:16px;cursor:pointer;border:none;box-shadow:0 4px 6px rgba(0,0,0,0.3);font-family:segoe ui,sans-serif;font-weight:bold;';
                        btn.onclick = (e) => {
                            e.preventDefault();
                            e.stopPropagation();
                            
                            let extId = '';
                            if (window.location.hostname.includes('microsoftedge')) {
                                // edge: microsoftedge.microsoft.com/addons/detail/ublock-origin/odfafepnkbgakkbgdfbkmkdogmcaacl
                                extId = 'EDGE:' + window.location.pathname.split('/').pop().split('?')[0];
                            } else {
                                // chrome store
                                extId = window.location.pathname.split('/').pop().split('?')[0];
                            }
                            
                            btn.innerText = 'Installing to VBrowser...';
                            btn.style.background = '#888';
                            window.chrome.webview.postMessage('INSTALL_EXTENSION:' + extId);
                            setTimeout(() => {
                                btn.innerText = 'Extension Installed!';
                                btn.style.background = '#107c10';
                            }, 5000);
                        };
                        document.body.appendChild(btn);
                    })();
                ";
                _ = sender.ExecuteScriptAsync(injectScript);
            }

            if (tabIndex == _activeTabIndex)
            {
                if (string.IsNullOrWhiteSpace(completedUrl) ||
                    string.Equals(completedUrl, "about:blank", StringComparison.OrdinalIgnoreCase))
                {
                    completedUrl = GetActiveBrowserPage()?.GetDisplayUrl() ?? AddressBar.Text;
                }

                AddressBar.Text = completedUrl;
                AnimateLoadingProgress(false);
                UpdateNavigationButtons();
                UpdateSecurityIndicator(completedUrl);
            }

            UpdateTabTitle(tabIndex, sender);
            UpdateTabFavicon(tabIndex, completedUrl);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            GetActiveBrowserPage()?.GoBack();
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            GetActiveBrowserPage()?.GoForward();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            GetActiveBrowserPage()?.Reload();
        }

        private void GoButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToUrl();
        }

        private void AddressBar_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ShowClearButtonIfNeeded();
                return;
            }

            var suggestions = BuildAddressBarSuggestions(sender.Text);
            sender.ItemsSource = suggestions;
            sender.IsSuggestionListOpen = suggestions.Count > 0;
            ShowClearButtonIfNeeded();
        }

        private void AddressBar_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                NavigateToUrl(AddressBar.Text);
                e.Handled = true;
            }
        }

        private void AddressBar_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            var suggestion = args.SelectedItem switch
            {
                string text => text,
                null => string.Empty,
                _ => args.SelectedItem.ToString() ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(suggestion))
            {
                sender.Text = suggestion;
            }
        }

        private void AddressBar_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var chosenText = args.ChosenSuggestion switch
            {
                string text => text,
                null => string.Empty,
                _ => args.ChosenSuggestion.ToString() ?? string.Empty
            };

            var submittedText = !string.IsNullOrWhiteSpace(chosenText)
                ? chosenText
                : args.QueryText;

            NavigateToUrl(submittedText);
        }

        private static List<string> BuildAddressBarSuggestions(string? input)
        {
            var query = input?.Trim() ?? string.Empty;

            var suggestions = new List<string>();

            if (!string.IsNullOrWhiteSpace(query))
            {
                if (!query.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !query.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !query.Contains(' '))
                {
                    suggestions.Add($"https://{query}");
                }

                suggestions.Add(query);
            }

            if (!string.IsNullOrWhiteSpace(query) &&
                (InternalFlagsUrl.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 "flags".Contains(query, StringComparison.OrdinalIgnoreCase)))
            {
                suggestions.Add(InternalFlagsUrl);
            }

            suggestions.AddRange(
                HistoryManager.Instance.Entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Url))
                .Where(entry =>
                    string.IsNullOrWhiteSpace(query) ||
                    entry.Url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.Url)
            );

            return suggestions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList();
        }

        private void NavigateToUrl(string? inputOverride = null)
        {
            var input = string.IsNullOrWhiteSpace(inputOverride)
                ? AddressBar.Text?.Trim()
                : inputOverride.Trim();

            if (!string.IsNullOrWhiteSpace(input))
            {
                if (TryNavigateToInternalPage(input))
                {
                    return;
                }

                var target = ResolveAddressBarInput(input);
                GetActiveBrowserPage()?.NavigateTo(target);
            }
        }

        private bool TryNavigateToInternalPage(string input)
        {
            var candidate = input.Trim();

            if (string.Equals(candidate, InternalFlagsUrl, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate, "vbrowser:flags", StringComparison.OrdinalIgnoreCase))
            {
                OpenFlagsPage();
                return true;
            }

            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                string.Equals(uri.Scheme, "vbrowser", StringComparison.OrdinalIgnoreCase))
            {
                var path = uri.AbsolutePath.Trim('/');
                if (string.Equals(uri.Host, "flags", StringComparison.OrdinalIgnoreCase) ||
                    (string.IsNullOrWhiteSpace(uri.Host) && string.Equals(path, "flags", StringComparison.OrdinalIgnoreCase)))
                {
                    OpenFlagsPage();
                    return true;
                }
            }

            return false;
        }

        private void OpenFlagsPage()
        {
            MainFrame.Navigate(typeof(FlagsPage), this);
            AddressBar.Text = InternalFlagsUrl;
            UpdateSecurityIndicator(InternalFlagsUrl);
        }

        private string ResolveAddressBarInput(string input)
        {
            var candidate = input.Trim();

            if (TryGetDirectNavigationUrl(candidate, out var directUrl))
            {
                return directUrl;
            }

            return BuildSearchUrl(candidate);
        }

        private static bool TryGetDirectNavigationUrl(string input, out string directUrl)
        {
            directUrl = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            if (Uri.TryCreate(input, UriKind.Absolute, out var absoluteUri) &&
                (absoluteUri.Scheme == Uri.UriSchemeHttp ||
                 absoluteUri.Scheme == Uri.UriSchemeHttps ||
                 string.Equals(absoluteUri.Scheme, "about", StringComparison.OrdinalIgnoreCase)))
            {
                directUrl = absoluteUri.ToString();
                return true;
            }

            // Multi-word input is treated as a search query.
            if (input.Contains(' '))
            {
                return false;
            }

            var hostLikePart = input;
            var firstPathSeparator = hostLikePart.IndexOfAny(['/', '?', '#']);
            if (firstPathSeparator >= 0)
            {
                hostLikePart = hostLikePart[..firstPathSeparator];
            }

            bool looksLikeHost =
                hostLikePart.Contains('.') ||
                hostLikePart.Contains(':') ||
                string.Equals(hostLikePart, "localhost", StringComparison.OrdinalIgnoreCase) ||
                IPAddress.TryParse(hostLikePart, out _);

            if (!looksLikeHost)
            {
                return false;
            }

            if (Uri.TryCreate($"https://{input}", UriKind.Absolute, out var withHttps))
            {
                directUrl = withHttps.ToString();
                return true;
            }

            if (Uri.TryCreate($"http://{input}", UriKind.Absolute, out var withHttp))
            {
                directUrl = withHttp.ToString();
                return true;
            }

            return false;
        }

        private static string BuildSearchUrl(string query)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            return GetSelectedSearchEngineBaseUrl() + encodedQuery;
        }

        private static string GetSelectedSearchEngineBaseUrl()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            int selectedIndex = 0;

            if (localSettings.Values.TryGetValue("SearchEngine", out var savedValue) &&
                savedValue is int index &&
                index >= 0)
            {
                selectedIndex = index;
            }

            return selectedIndex switch
            {
                1 => "https://www.bing.com/search?q=",
                2 => "https://duckduckgo.com/?q=",
                3 => "https://search.yahoo.com/search?p=",
                4 => "https://yandex.com/search/?text=",
                _ => "https://www.google.com/search?q="
            };
        }

        private void UpdateNavigationButtons()
        {
            var webView = GetActiveBrowserPage()?.WebViewControl;
            if (webView != null)
            {
                BackButton.IsEnabled = webView.CanGoBack;
                ForwardButton.IsEnabled = webView.CanGoForward;
            }
            else
            {
                BackButton.IsEnabled = false;
                ForwardButton.IsEnabled = false;
            }
        }

        private void NewTab_Tapped(object sender, TappedRoutedEventArgs e)
        {
            NewTab_Click(sender, new RoutedEventArgs());
        }

        private void NewTab_Click(object sender, RoutedEventArgs e)
        {
            AddNewTab(_defaultStartUrl, false);
        }

        private void NewPrivateTab_Click(object sender, RoutedEventArgs e)
        {
            AddNewTab(_defaultStartUrl, true);
        }

        private void NewPrivateTab_Tapped(object sender, TappedRoutedEventArgs e)
        {
            NewPrivateTab_Click(sender, new RoutedEventArgs());
        }

        public void NewTabWithUrl(string url)
        {
            AddNewTab(url, false);
        }

        private void BrowserTabView_AddTabButtonClick(TabView sender, object args)
        {
            AddNewTab(_defaultStartUrl, false);
        }

        private void AddNewTab(string url, bool isPrivate)
        {
            var tabTitle = isPrivate ? "Private" : "New Tab";
            var newTab = CreateTabViewItem(tabTitle, isPrivate);
            _tabs.Add(newTab);
            _tabPages[newTab] = CreateBrowserPage(url, isPrivate);

            if (isPrivate)
            {
                _privateTabs.Add(newTab);
            }

            BrowserTabView.TabItems.Add(newTab);

            // Sync to vertical tabs if enabled
            if (_verticalTabsEnabled)
            {
                SyncVerticalTab(newTab, isPrivate);
            }

            // Switch to new tab
            SwitchToTab(_tabs.Count - 1);
        }

        private TabViewItem CreateTabViewItem(string title, bool isPrivate = false)
        {
            var resolvedTitle = isPrivate ? "Private" : (string.IsNullOrWhiteSpace(title) ? "New Tab" : title);

            var tabItem = new TabViewItem
            {
                Header = resolvedTitle,
                IconSource = CreateTabIconSource(isPrivate),
                IsClosable = true,
                Tag = isPrivate ? "private" : "normal"
            };

            if (isPrivate)
            {
                tabItem.Background = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(180, 55, 35, 95));
                tabItem.Foreground = new SolidColorBrush(
                    Microsoft.UI.ColorHelper.FromArgb(255, 220, 200, 255));
            }

            return tabItem;
        }

        private static IconSource CreateTabIconSource(bool isPrivate)
        {
            if (isPrivate)
            {
                return new FontIconSource
                {
                    Glyph = "\uE72E",
                    Foreground = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 200, 170, 255))
                };
            }

            return new SymbolIconSource
            {
                Symbol = Symbol.World
            };
        }

        private static BitmapIconSource CreateFaviconIconSource(Uri faviconUri)
        {
            return new BitmapIconSource
            {
                UriSource = faviconUri,
                ShowAsMonochrome = false
            };
        }

        private static string GetTabHeaderTitle(TabViewItem tabItem)
        {
            if (tabItem.Header is StackPanel panel)
            {
                var textBlock = panel.Children.OfType<TextBlock>().FirstOrDefault();
                if (textBlock != null)
                {
                    return textBlock.Text;
                }
            }

            if (tabItem.Header is TextBlock text)
            {
                return text.Text;
            }

            if (tabItem.Header is string headerText)
            {
                return headerText;
            }

            return "New Tab";
        }

        private static void SetTabHeaderTitle(TabViewItem tabItem, string title, bool isPrivate)
        {
            var resolvedTitle = isPrivate ? "Private" : (string.IsNullOrWhiteSpace(title) ? "New Tab" : title);
            tabItem.Header = resolvedTitle;

            if (isPrivate)
            {
                tabItem.IconSource = CreateTabIconSource(true);
                return;
            }

            tabItem.IconSource ??= CreateTabIconSource(false);
        }

        private void UpdateTabTitle(int tabIndex, WebView2 webView)
        {
            var title = webView.CoreWebView2?.DocumentTitle;

            if (string.IsNullOrWhiteSpace(title))
            {
                title = GetHostFromUri(webView.Source?.ToString());
            }

            SetTabTitle(tabIndex, title);
        }

        private void UpdateTabFavicon(int tabIndex, string? pageUrl)
        {
            if (tabIndex < 0 || tabIndex >= _tabs.Count)
            {
                return;
            }

            var tabItem = _tabs[tabIndex];
            bool isPrivate = _privateTabs.Contains(tabItem);
            if (isPrivate)
            {
                tabItem.IconSource = CreateTabIconSource(true);
                return;
            }

            if (TryCreateFaviconUri(pageUrl, out var faviconUri))
            {
                tabItem.IconSource = CreateFaviconIconSource(faviconUri);
            }
            else
            {
                tabItem.IconSource = CreateTabIconSource(false);
            }

            if (_verticalTabsEnabled)
            {
                UpdateVerticalTabIcon(tabItem);
            }
        }

        private static bool TryCreateFaviconUri(string? pageUrl, out Uri faviconUri)
        {
            faviconUri = null!;

            if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri) ||
                (pageUri.Scheme != Uri.UriSchemeHttp && pageUri.Scheme != Uri.UriSchemeHttps))
            {
                return false;
            }

            // Use DuckDuckGo's favicon service - faster than Google's
            var domain = pageUri.Host;
            faviconUri = new Uri($"https://icons.duckduckgo.com/ip3/{domain}.ico");
            return true;
        }

        private void SetTabTitle(int tabIndex, string? title)
        {
            if (tabIndex < 0 || tabIndex >= _tabs.Count)
            {
                return;
            }

            var tabItem = _tabs[tabIndex];
            bool isPrivate = _privateTabs.Contains(tabItem);
            var finalTitle = string.IsNullOrWhiteSpace(title) ? "New Tab" : title.Trim();
            SetTabHeaderTitle(tabItem, finalTitle, isPrivate);

            // Also update vertical tab title
            if (_verticalTabsEnabled)
            {
                UpdateVerticalTabTitle(tabItem, finalTitle);
                UpdateVerticalTabIcon(tabItem);
            }
        }

        private static string? GetHostFromUri(string? uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
            {
                return null;
            }

            var host = parsedUri.Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                host = host[4..];
            }

            return host;
        }

        private static string GetConfiguredHomePage()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

            if (localSettings.Values.TryGetValue("Homepage", out var homepageValue) &&
                homepageValue is string homepage &&
                !string.IsNullOrWhiteSpace(homepage))
            {
                var normalized = homepage.Trim();
                if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    normalized = "https://" + normalized;
                }

                return normalized;
            }

            return FallbackStartUrl;
        }

        private BrowserPage CreateBrowserPage(string initialUrl, bool isPrivate)
        {
            var page = new BrowserPage();
            page.IsPrivate = isPrivate;
            AttachBrowserEvents(page);
            page.NavigateTo(initialUrl);
            return page;
        }

        private void AttachBrowserEvents(BrowserPage page)
        {
            var webView = page.WebViewControl;
            if (webView == null)
            {
                return;
            }

            webView.NavigationStarting += WebView_NavigationStarting;
            webView.NavigationCompleted += WebView_NavigationCompleted;
            webView.WebMessageReceived += WebView_WebMessageReceived;

            webView.CoreWebView2Initialized += WebView_CoreWebView2Initialized;

            if (webView.CoreWebView2 != null)
            {
                AttachCoreWebViewEvents(webView.CoreWebView2);
            }
        }

        private async void WebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var message = args.TryGetWebMessageAsString();
                if (message == null) return;

                if (message.StartsWith("INSTALL_EXTENSION:"))
                {
                    var extId = message.Substring("INSTALL_EXTENSION:".Length);
                    string extPath = await ExtensionManager.DownloadAndUnpackExtensionAsync(extId);
                    if (extPath != null && sender.CoreWebView2?.Profile != null)
                    {
                        var profile = sender.CoreWebView2.Profile;
                        await profile.AddBrowserExtensionAsync(extPath);
                    }
                }
                else if (message == "GET_EXT_LIST:")
                {
                    var appData = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VBrowser", "Extensions");
                    System.IO.Directory.CreateDirectory(appData);
                    
                    var installed = new System.Collections.Generic.List<string>();
                    foreach (var dir in System.IO.Directory.GetDirectories(appData))
                    {
                        installed.Add(dir.Replace("\\", "\\\\"));
                    }
                    
                    string json = "{\"type\":\"extensions\",\"data\":[" + string.Join(",", installed.ConvertAll(d => "\"" + d.Replace("\\\\", "\\\\\\\\") + "\"")) + "]}";
                    sender.CoreWebView2.PostWebMessageAsJson(json);
                }
                else if (message.StartsWith("REMOVE_EXTENSION:"))
                {
                    var extPath = message.Substring("REMOVE_EXTENSION:".Length);
                    // Not natively supported by WebView2 to UNLOAD an extension on the fly,
                    // but we can delete the files so it doesn't load next time.
                    try 
                    {
                        if (System.IO.Directory.Exists(extPath))
                            System.IO.Directory.Delete(extPath, true); 
                    }
                    catch { }
                    
                    _ = sender.ExecuteScriptAsync("window.location.reload();");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExtInstall] Failed processing message: {ex.Message}");
            }
        }

        private void WebView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
        {
            if (args.Exception == null && sender.CoreWebView2 != null)
            {
                AttachCoreWebViewEvents(sender.CoreWebView2);
            }
        }

        private void AttachCoreWebViewEvents(CoreWebView2 coreWebView2)
        {
            coreWebView2.ServerCertificateErrorDetected -= CoreWebView2_ServerCertificateErrorDetected;
            coreWebView2.ServerCertificateErrorDetected += CoreWebView2_ServerCertificateErrorDetected;

            coreWebView2.PermissionRequested -= CoreWebView2_PermissionRequested;
            coreWebView2.PermissionRequested += CoreWebView2_PermissionRequested;

            coreWebView2.ContainsFullScreenElementChanged -= CoreWebView2_ContainsFullScreenElementChanged;
            coreWebView2.ContainsFullScreenElementChanged += CoreWebView2_ContainsFullScreenElementChanged;
            coreWebView2.SourceChanged -= CoreWebView2_SourceChanged;
            coreWebView2.SourceChanged += CoreWebView2_SourceChanged;

            // Wire download tracking at the window level as the authoritative handler.
            // BrowserPage also attaches its own handler — the HashSet dedup in
            // DownloadManager ensures each operation is tracked exactly once.
            coreWebView2.DownloadStarting -= MainWindow_DownloadStarting;
            coreWebView2.DownloadStarting += MainWindow_DownloadStarting;
        }

        private void DetachCoreWebViewEvents(CoreWebView2 coreWebView2)
        {
            coreWebView2.ServerCertificateErrorDetected -= CoreWebView2_ServerCertificateErrorDetected;
            coreWebView2.PermissionRequested -= CoreWebView2_PermissionRequested;
            coreWebView2.ContainsFullScreenElementChanged -= CoreWebView2_ContainsFullScreenElementChanged;
            coreWebView2.SourceChanged -= CoreWebView2_SourceChanged;
            coreWebView2.DownloadStarting -= MainWindow_DownloadStarting;
        }

        private void MainWindow_DownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"[Download][MainWindow] DownloadStarting fired. URI: {args.DownloadOperation?.Uri}, ResultFilePath: {args.ResultFilePath}");

            var deferral = args.GetDeferral();
            try
            {
                // Ensure a valid result file path so the download isn't silently discarded.
                if (string.IsNullOrWhiteSpace(args.ResultFilePath) && args.DownloadOperation != null)
                {
                    try
                    {
                        var downloadsPath = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            "Downloads");
                        var fileName = "download";
                        if (Uri.TryCreate(args.DownloadOperation.Uri, UriKind.Absolute, out var uri))
                        {
                            var segment = uri.Segments[^1];
                            if (!string.IsNullOrWhiteSpace(segment) && segment != "/")
                                fileName = Uri.UnescapeDataString(segment);
                        }
                        args.ResultFilePath = System.IO.Path.Combine(downloadsPath, fileName);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Download][MainWindow] ResultFilePath fallback failed: {ex.Message}");
                    }
                }

                args.Cancel = false;
                args.Handled = true;

                DownloadManager.Instance.TrackDownload(args);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Download][MainWindow] DownloadStarting handler failed: {ex}");
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void CoreWebView2_SourceChanged(CoreWebView2 sender, CoreWebView2SourceChangedEventArgs args)
        {
            var tabIndex = GetTabIndexForCoreWebView2(sender);
            if (tabIndex < 0 || tabIndex != _activeTabIndex)
            {
                return;
            }

            var source = sender.Source;
            if (string.IsNullOrWhiteSpace(source) ||
                string.Equals(source, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                AddressBar.Text = source;
                UpdateSecurityIndicator(source);
            });
        }

        private void CoreWebView2_ContainsFullScreenElementChanged(object? sender, object args)
        {
            if (sender is not CoreWebView2 coreWebView2)
            {
                return;
            }

            // Handle fullscreen for any tab - only one is visible at a time
            DispatcherQueue.TryEnqueue(() => SetBrowserFullscreen(coreWebView2.ContainsFullScreenElement));
        }

        private void SyncBrowserFullscreenForActiveTab()
        {
            var activeCore = GetActiveBrowserPage()?.WebViewControl?.CoreWebView2;
            var shouldBeFullscreen = activeCore?.ContainsFullScreenElement == true;
            SetBrowserFullscreen(shouldBeFullscreen);
        }

        private void SetBrowserFullscreen(bool isFullscreen)
        {
            if (_isBrowserFullscreen == isFullscreen)
            {
                return;
            }

            if (isFullscreen)
            {
                _wasCertWarningVisibleBeforeFullscreen =
                    _wasCertWarningVisibleBeforeFullscreen || CertWarningBar.Visibility == Visibility.Visible;

                _isBrowserFullscreen = true;
                ToolbarGrid.Visibility = Visibility.Collapsed;
                CertWarningBar.Visibility = Visibility.Collapsed;
                VerticalTabSidebar.Visibility = Visibility.Collapsed;
                ExpandSidebarButton.Visibility = Visibility.Collapsed;
                VerticalTabColumn.Width = new GridLength(0);

                TrySetWindowPresenter(AppWindowPresenterKind.FullScreen);
                return;
            }

            _isBrowserFullscreen = false;
            TrySetWindowPresenter(AppWindowPresenterKind.Overlapped);

            ToolbarGrid.Visibility = Visibility.Visible;
            ApplyTabLayout();

            if (_wasCertWarningVisibleBeforeFullscreen)
            {
                CertWarningBar.Visibility = Visibility.Visible;
                _wasCertWarningVisibleBeforeFullscreen = false;
            }
        }

        private void TrySetWindowPresenter(AppWindowPresenterKind presenterKind)
        {
            try
            {
                AppWindow.SetPresenter(presenterKind);
            }
            catch
            {
            }
        }

        private void DetachBrowserEvents(BrowserPage page)
        {
            var webView = page.WebViewControl;
            if (webView == null)
            {
                return;
            }

            webView.NavigationStarting -= WebView_NavigationStarting;
            webView.NavigationCompleted -= WebView_NavigationCompleted;
            webView.CoreWebView2Initialized -= WebView_CoreWebView2Initialized;

            if (webView.CoreWebView2 != null)
            {
                DetachCoreWebViewEvents(webView.CoreWebView2);
            }
        }

        private int GetTabIndexForCoreWebView2(CoreWebView2 coreWebView2)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabPages.TryGetValue(_tabs[i], out var page) &&
                    ReferenceEquals(page.WebViewControl?.CoreWebView2, coreWebView2))
                {
                    return i;
                }
            }

            return -1;
        }

        private BrowserPage? GetActiveBrowserPage()
        {
            if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
            {
                return null;
            }

            return _tabPages.TryGetValue(_tabs[_activeTabIndex], out var page)
                ? page
                : null;
        }

        public void ShowActiveTab()
        {
            if (_activeTabIndex < 0 || _activeTabIndex >= _tabs.Count)
            {
                return;
            }

            SwitchToTab(_activeTabIndex);
        }

        public void NavigateActiveTabTo(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            ShowActiveTab();
            GetActiveBrowserPage()?.NavigateTo(url);
        }

        private int GetTabIndexForWebView(WebView2 webView)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (_tabPages.TryGetValue(_tabs[i], out var page) && ReferenceEquals(page.WebViewControl, webView))
                {
                    return i;
                }
            }

            return -1;
        }

        private void SwitchToTab(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;

            bool isTabChange = _activeTabIndex != index;
            _activeTabIndex = index;

            if (IsBackgroundTabSuspensionEnabled())
            {
                UpdateBackgroundTabSuspension(index);
            }
            else
            {
                ResumeAllTabs();
            }

            if (!_isUpdatingTabSelection && !ReferenceEquals(BrowserTabView.SelectedItem, _tabs[index]))
            {
                _isUpdatingTabSelection = true;
                try
                {
                    BrowserTabView.SelectedItem = _tabs[index];
                }
                finally
                {
                    _isUpdatingTabSelection = false;
                }
            }

            bool isDarkTheme = false;
            if (Content is FrameworkElement rootElement)
            {
                var effectiveTheme = rootElement.ActualTheme == ElementTheme.Default
                    ? rootElement.RequestedTheme
                    : rootElement.ActualTheme;
                isDarkTheme = effectiveTheme == ElementTheme.Dark;
            }

            var activeTabBackground = isDarkTheme
                ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 58, 58, 58))
                : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 232, 232, 232));
            var inactiveTabBackground = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            // Private tab colors
            var activePrivateBackground = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 90, 60, 140));
            var inactivePrivateBackground = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(180, 55, 35, 95));
            var activeNormalForeground = isDarkTheme
                ? new SolidColorBrush(Microsoft.UI.Colors.White)
                : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 24, 24, 24));
            var inactiveNormalForeground = isDarkTheme
                ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 230, 230, 230))
                : new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 45, 45, 45));
            var activePrivateForeground = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 240, 225, 255));
            var inactivePrivateForeground = new SolidColorBrush(
                Microsoft.UI.ColorHelper.FromArgb(255, 220, 200, 255));
            
            // Update visual states for horizontal tabs
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool isPriv = _privateTabs.Contains(_tabs[i]);
                if (i == index)
                {
                    _tabs[i].Background = isPriv ? activePrivateBackground : activeTabBackground;
                    _tabs[i].Foreground = isPriv ? activePrivateForeground : activeNormalForeground;
                }
                else
                {
                    _tabs[i].Background = isPriv ? inactivePrivateBackground : inactiveTabBackground;
                    _tabs[i].Foreground = isPriv ? inactivePrivateForeground : inactiveNormalForeground;
                }
            }

            // Update vertical tab styles
            UpdateVerticalTabStyles();

            if (_tabPages.TryGetValue(_tabs[index], out var page))
            {
                MainFrame.Content = page;
                var displayUrl = page.GetDisplayUrl();
                AddressBar.Text = displayUrl;
                UpdateSecurityIndicator(displayUrl);

                // Animate the content transition on tab switch
                if (isTabChange)
                {
                    AnimateTabContentIn(page);
                }
            }

            CertWarningBar.Visibility = Visibility.Collapsed;
            AnimateLoadingProgress(false);
            UpdateNavigationButtons();
            SyncBrowserFullscreenForActiveTab();
        }

        private static bool IsBackgroundTabSuspensionEnabled()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            bool memorySaverEnabled = localSettings.Values.TryGetValue("MemorySaver", out var memorySaverValue)
                && memorySaverValue is bool memorySaver
                && memorySaver;
            bool highEfficiencyEnabled = localSettings.Values.TryGetValue("HighEfficiency", out var highEfficiencyValue)
                && highEfficiencyValue is bool highEfficiency
                && highEfficiency;

            return memorySaverEnabled || highEfficiencyEnabled;
        }

        private void UpdateBackgroundTabSuspension(int activeIndex)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (!_tabPages.TryGetValue(_tabs[i], out var page))
                {
                    continue;
                }

                if (i == activeIndex)
                {
                    page.ResumeIfSuspended();
                }
                else
                {
                    _ = page.TrySuspendAsync();
                }
            }
        }

        private void ResumeAllTabs()
        {
            foreach (var page in _tabPages.Values)
            {
                page.ResumeIfSuspended();
            }
        }

        /// <summary>
        /// GPU-accelerated cross-fade + subtle scale for tab content transitions.
        /// Runs entirely on the compositor thread for jank-free 60fps.
        /// </summary>
        private void AnimateTabContentIn(UIElement element)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;

            // Fade in
            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.InsertKeyFrame(0f, 0f);
            fadeAnim.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
            fadeAnim.Duration = TimeSpan.FromMilliseconds(180);

            // Subtle scale-up from 0.98 to 1.0 for a "pop-in" feel
            var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
            scaleAnim.InsertKeyFrame(0f, new Vector3(0.985f, 0.985f, 1f));
            scaleAnim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f), compositor.CreateCubicBezierEasingFunction(
                new Vector2(0f, 0f), new Vector2(0.2f, 1f)));
            scaleAnim.Duration = TimeSpan.FromMilliseconds(220);

            // Use center-based scaling
            visual.CenterPoint = new Vector3((float)(MainFrame.ActualWidth / 2), (float)(MainFrame.ActualHeight / 2), 0);

            visual.StartAnimation("Opacity", fadeAnim);
            visual.StartAnimation("Scale", scaleAnim);
        }

        private void CloseTab(TabViewItem tabItem)
        {
            var index = _tabs.IndexOf(tabItem);
            if (index < 0) return;

            bool wasActiveTab = index == _activeTabIndex;
            bool wasPrivate = _privateTabs.Contains(tabItem);

            if (_tabPages.TryGetValue(tabItem, out var pageToClose))
            {
                DetachBrowserEvents(pageToClose);
                _ = pageToClose.StopMediaAndUnloadAsync();

                // For private tabs, clear browsing data on close
                if (wasPrivate && pageToClose.WebViewControl?.CoreWebView2?.Profile != null)
                {
                    try
                    {
                        _ = pageToClose.WebViewControl.CoreWebView2.Profile.ClearBrowsingDataAsync();
                        _ = pageToClose.CleanupPrivateDataFolderAsync();
                    }
                    catch { }
                }
            }

            // Remove vertical tab mapping
            if (_verticalTabsEnabled && _horizontalToVertical.TryGetValue(tabItem, out var vTab))
            {
                VerticalTabContainer.Children.Remove(vTab);
                _verticalToHorizontal.Remove(vTab);
                _horizontalToVertical.Remove(tabItem);
            }

            _tabs.RemoveAt(index);
            _tabPages.Remove(tabItem);
            _privateTabs.Remove(tabItem);
            BrowserTabView.TabItems.Remove(tabItem);

            if (_tabs.Count == 0)
            {
                // Close window if no tabs left
                Close();
            }
            else
            {
                if (index < _activeTabIndex)
                {
                    _activeTabIndex--;
                }

                if (wasActiveTab)
                {
                    // Switch to previous tab if closing active tab
                    SwitchToTab(Math.Max(0, index - 1));
                }
            }
        }

        private void BrowserTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingTabSelection)
            {
                return;
            }

            if (BrowserTabView.SelectedItem is TabViewItem selectedTab)
            {
                var selectedIndex = _tabs.IndexOf(selectedTab);
                if (selectedIndex >= 0 && selectedIndex != _activeTabIndex)
                {
                    SwitchToTab(selectedIndex);
                }
            }
        }

        private void BrowserTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            if (args.Item is TabViewItem tabItem)
            {
                CloseTab(tabItem);
            }
        }

        private void CoreWebView2_PermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
        {
            if (!TryGetPermissionDisplayName(args.PermissionKind, out var permissionName))
            {
                return;
            }

            var origin = GetPermissionOrigin(args.Uri);
            var decisionKey = $"{origin}|{args.PermissionKind}";
            var tabIndex = GetTabIndexForCoreWebView2(sender);
            bool isPrivateTab = tabIndex >= 0 && tabIndex < _tabs.Count && _privateTabs.Contains(_tabs[tabIndex]);

            if (!isPrivateTab && _savedSitePermissionDecisions.TryGetValue(decisionKey, out var savedDecision))
            {
                args.State = savedDecision;
                args.Handled = true;
                return;
            }

            var deferral = args.GetDeferral();
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var (state, rememberDecision) = await ShowPermissionPromptAsync(origin, permissionName, isPrivateTab);
                    args.State = state;
                    args.Handled = true;

                    if (!isPrivateTab && rememberDecision)
                    {
                        _savedSitePermissionDecisions[decisionKey] = state;
                    }
                }
                catch
                {
                    args.State = CoreWebView2PermissionState.Deny;
                    args.Handled = true;
                }
                finally
                {
                    deferral.Complete();
                }
            });
        }

        private async System.Threading.Tasks.Task<(CoreWebView2PermissionState State, bool RememberDecision)> ShowPermissionPromptAsync(
            string origin,
            string permissionName,
            bool isPrivateTab)
        {
            var xamlRoot = Content?.XamlRoot;
            if (xamlRoot == null)
            {
                return (CoreWebView2PermissionState.Deny, false);
            }

            var rememberCheckBox = new CheckBox
            {
                Content = "Remember this decision for this site",
                IsChecked = true,
                Visibility = isPrivateTab ? Visibility.Collapsed : Visibility.Visible
            };
            var details = new TextBlock
            {
                Text = isPrivateTab
                    ? $"{origin} wants to use your {permissionName}. This is a private tab, so this choice will not be saved."
                    : $"{origin} wants to use your {permissionName}.",
                TextWrapping = TextWrapping.WrapWholeWords
            };

            var panel = new StackPanel
            {
                Spacing = 10
            };
            panel.Children.Add(details);
            panel.Children.Add(rememberCheckBox);

            var dialog = new ContentDialog
            {
                Title = "Site permission request",
                Content = panel,
                PrimaryButtonText = "Allow",
                SecondaryButtonText = "Block",
                CloseButtonText = "Allow once",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = xamlRoot
            };

            var result = await dialog.ShowAsync();
            var rememberDecision = !isPrivateTab && rememberCheckBox.IsChecked == true;

            return result switch
            {
                ContentDialogResult.Primary => (CoreWebView2PermissionState.Allow, rememberDecision),
                ContentDialogResult.Secondary => (CoreWebView2PermissionState.Deny, rememberDecision),
                _ => (CoreWebView2PermissionState.Allow, false)
            };
        }

        private static bool TryGetPermissionDisplayName(CoreWebView2PermissionKind permissionKind, out string permissionName)
        {
            permissionName = permissionKind switch
            {
                CoreWebView2PermissionKind.Camera => "camera",
                CoreWebView2PermissionKind.Microphone => "microphone",
                CoreWebView2PermissionKind.Geolocation => "location",
                _ => string.Empty
            };

            return !string.IsNullOrWhiteSpace(permissionName);
        }

        private static string GetPermissionOrigin(string? uri)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
            {
                if (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps)
                {
                    return $"{parsedUri.Scheme}://{parsedUri.Host}";
                }
            }

            return string.IsNullOrWhiteSpace(uri) ? "This site" : uri;
        }

        private void ExtensionsButton_Click(object sender, RoutedEventArgs e)
        {
            // Future implementation for managing Chromium extensions (requires specific WebView2 profile setup).
        }

        private void ManageExtensions_Click(object sender, RoutedEventArgs e)
        {
            AddNewTab("vbrowser://extensions", false);
        }

        private async void ClearBrowsingData_Click(object sender, RoutedEventArgs e)
        {
            var xamlRoot = Content?.XamlRoot;
            if (xamlRoot == null)
            {
                return;
            }

            var clearHistoryCheckBox = new CheckBox
            {
                Content = "Browsing history",
                IsChecked = true
            };

            var clearSiteDataCheckBox = new CheckBox
            {
                Content = "Cookies, cache, and website data",
                IsChecked = true
            };

            var clearPermissionsCheckBox = new CheckBox
            {
                Content = "Saved site permissions",
                IsChecked = true
            };

            var optionsPanel = new StackPanel
            {
                Spacing = 8
            };
            optionsPanel.Children.Add(new TextBlock
            {
                Text = "Choose what to clear:",
                TextWrapping = TextWrapping.WrapWholeWords
            });
            optionsPanel.Children.Add(clearHistoryCheckBox);
            optionsPanel.Children.Add(clearSiteDataCheckBox);
            optionsPanel.Children.Add(clearPermissionsCheckBox);

            var confirmDialog = new ContentDialog
            {
                Title = "Clear browsing data",
                Content = optionsPanel,
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };

            if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            if (clearHistoryCheckBox.IsChecked == true)
            {
                HistoryManager.Instance.Clear();
            }

            if (clearPermissionsCheckBox.IsChecked == true)
            {
                _savedSitePermissionDecisions.Clear();
            }

            if (clearSiteDataCheckBox.IsChecked == true)
            {
                var clearedProfiles = new HashSet<CoreWebView2Profile>();

                foreach (var page in _tabPages.Values)
                {
                    var profile = page.WebViewControl?.CoreWebView2?.Profile;
                    if (profile == null || !clearedProfiles.Add(profile))
                    {
                        continue;
                    }

                    try
                    {
                        await profile.ClearBrowsingDataAsync();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void History_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(typeof(HistoryPage), this);
        }

        private void Downloads_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainFrame != null)
                {
                    MainFrame.Navigate(typeof(DownloadsPage), this);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Downloads navigation error: {ex.Message}");
                ShowActiveTab();
                ShowDownloadsNavigationError(ex.Message);
            }
        }

        private async void ShowDownloadsNavigationError(string errorMessage)
        {
            var xamlRoot = this.Content?.XamlRoot;
            if (xamlRoot == null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Could not open Downloads",
                Content = $"VBrowser could not open the Downloads page right now.\n\n{errorMessage}",
                CloseButtonText = "OK",
                XamlRoot = xamlRoot
            };

            await dialog.ShowAsync();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            MainFrame.Navigate(typeof(SettingsPage), this);
        }

        private void CopyLink_Click(object sender, RoutedEventArgs e)
        {
            var url = GetActiveBrowserPage()?.GetDisplayUrl();
            if (string.IsNullOrWhiteSpace(url) ||
                string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                url = AddressBar.Text;
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var dataPackage = new DataPackage();
            dataPackage.SetText(url);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
        }

        private void Flags_Click(object sender, RoutedEventArgs e)
        {
            OpenFlagsPage();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (MainFrame != null)
                {
                    MainFrame.Navigate(typeof(AboutPage), this);
                }
            }
            catch (Exception ex)
            {
                // Log error or show message
                System.Diagnostics.Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }

        #region Security Indicator

        private void UpdateSecurityIndicator(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                SetSecurityState(SecurityState.None);
                return;
            }

            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                SetSecurityState(SecurityState.Secure);
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                SetSecurityState(SecurityState.Insecure);
            }
            else
            {
                SetSecurityState(SecurityState.None);
            }
        }

        private enum SecurityState { None, Secure, Insecure, CertError }

        private void SetSecurityState(SecurityState state)
        {
            switch (state)
            {
                case SecurityState.Secure:
                    SecurityIcon.Glyph = "\uE72E";  // Lock
                    SecurityIcon.Foreground = new SolidColorBrush(
                        Microsoft.UI.ColorHelper.FromArgb(255, 34, 139, 34));  // Green
                    ToolTipService.SetToolTip(SecurityIndicatorButton, "Connection is secure (HTTPS)");
                    break;

                case SecurityState.Insecure:
                    SecurityIcon.Glyph = "\uE785";  // Warning shield
                    SecurityIcon.Foreground = new SolidColorBrush(
                        Microsoft.UI.ColorHelper.FromArgb(255, 200, 80, 20));  // Orange
                    ToolTipService.SetToolTip(SecurityIndicatorButton, "Connection is not secure (HTTP)");
                    break;

                case SecurityState.CertError:
                    SecurityIcon.Glyph = "\uE730";  // Error
                    SecurityIcon.Foreground = new SolidColorBrush(
                        Microsoft.UI.ColorHelper.FromArgb(255, 220, 40, 40));  // Red
                    ToolTipService.SetToolTip(SecurityIndicatorButton, "Certificate error — connection may not be private");
                    break;

                default:
                    SecurityIcon.Glyph = "\uE774";  // Globe
                    SecurityIcon.Foreground = null;  // Use default theme color
                    ToolTipService.SetToolTip(SecurityIndicatorButton, null);
                    break;
            }
        }

        private void SecurityIndicator_Click(object sender, RoutedEventArgs e)
        {
            var url = AddressBar.Text;
            string message;
            string title;

            if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                title = "Secure Connection";
                message = "Your connection to this site is encrypted and secure.\n\nInformation you send (like passwords or credit card numbers) is private.";
            }
            else if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                title = "Not Secure";
                message = "Your connection to this site is NOT encrypted.\n\nDo not enter sensitive information (like passwords or credit cards) on this site.";
            }
            else
            {
                return;
            }

            var xamlRoot = this.Content?.XamlRoot;
            if (xamlRoot == null) return;

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = xamlRoot
            };
            _ = dialog.ShowAsync();
        }

        #endregion

        #region Certificate Error Handling

        private void CoreWebView2_ServerCertificateErrorDetected(
            CoreWebView2 sender, CoreWebView2ServerCertificateErrorDetectedEventArgs args)
        {
            // Don't auto-proceed — show warning to the user
            args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;

            // Store the URL so user can choose to proceed
            _pendingCertErrorUrl = args.RequestUri;

            DispatcherQueue.TryEnqueue(() =>
            {
                SetSecurityState(SecurityState.CertError);
                CertWarningText.Text = $"Certificate error on {GetHostFromUri(args.RequestUri) ?? args.RequestUri} — your connection is not private";

                if (_isBrowserFullscreen)
                {
                    _wasCertWarningVisibleBeforeFullscreen = true;
                    CertWarningBar.Visibility = Visibility.Collapsed;
                }
                else
                {
                    CertWarningBar.Visibility = Visibility.Visible;
                }
            });
        }

        private void CertProceed_Click(object sender, RoutedEventArgs e)
        {
            CertWarningBar.Visibility = Visibility.Collapsed;
            _wasCertWarningVisibleBeforeFullscreen = false;

            if (!string.IsNullOrWhiteSpace(_pendingCertErrorUrl))
            {
                // Navigate with certificate error override
                var page = GetActiveBrowserPage();
                if (page?.WebViewControl?.CoreWebView2 != null)
                {
                    // WebView2 doesn't have a direct "ignore cert" per-navigation API.
                    // We navigate again; the user accepted the risk.
                    page.WebViewControl.CoreWebView2.Navigate(_pendingCertErrorUrl);
                }
                _pendingCertErrorUrl = null;
            }
        }

        private void CertGoBack_Click(object sender, RoutedEventArgs e)
        {
            CertWarningBar.Visibility = Visibility.Collapsed;
            _wasCertWarningVisibleBeforeFullscreen = false;
            _pendingCertErrorUrl = null;
            GetActiveBrowserPage()?.GoBack();
        }

        #endregion

        #region Unsafe URL Blocking

        private static readonly HashSet<string> _unsafeSchemes = new(StringComparer.OrdinalIgnoreCase)
        {
            "javascript", "vbscript", "data"
        };

        private static bool IsUnsafeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            // Block dangerous URI schemes
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (_unsafeSchemes.Contains(uri.Scheme))
                    return true;
            }

            return false;
        }

        private async void ShowUnsafeUrlWarning(string url)
        {
            var xamlRoot = this.Content?.XamlRoot;
            if (xamlRoot == null) return;

            string scheme = "unknown";
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                scheme = uri.Scheme;

            var dialog = new ContentDialog
            {
                Title = "Navigation Blocked",
                Content = $"VBrowser blocked navigation to a potentially unsafe URL.\n\nScheme: {scheme}://\nThese URLs can be used to execute malicious code.",
                CloseButtonText = "OK",
                XamlRoot = xamlRoot
            };
            await dialog.ShowAsync();
        }

        #endregion

        #region Address Bar Focus Animations

        private void AddressBar_GotFocus(object sender, RoutedEventArgs e)
        {
            var currentUrl = GetActiveBrowserPage()?.GetDisplayUrl();
            if (!string.IsNullOrWhiteSpace(currentUrl) &&
                !string.Equals(currentUrl, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                AddressBar.Text = currentUrl;
            }

            SelectAddressBarAll();
            AnimateAddressBarFocus(true);
            ShowClearButtonIfNeeded();
        }

        private void AddressBar_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            SelectAddressBarAll();
            e.Handled = true;
        }

        private void SelectAddressBarAll()
        {
            var textBox = FindChild<TextBox>(AddressBar);
            if (textBox != null)
            {
                textBox.SelectAll();
            }
        }

        private void AddressBar_LostFocus(object sender, RoutedEventArgs e)
        {
            AnimateAddressBarFocus(false);
            HideClearButton();
        }

        private void ClearAddress_Click(object sender, RoutedEventArgs e)
        {
            AddressBar.Text = string.Empty;
            AddressBar.Focus(FocusState.Programmatic);
        }

        private void ShowClearButtonIfNeeded()
        {
            if (ClearAddressButton != null && !string.IsNullOrWhiteSpace(AddressBar.Text))
            {
                ClearAddressButton.Visibility = Visibility.Visible;
            }
            else if (ClearAddressButton != null)
            {
                ClearAddressButton.Visibility = Visibility.Collapsed;
            }
        }

        private void HideClearButton()
        {
            if (ClearAddressButton != null)
            {
                ClearAddressButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Enhanced glow/scale effect on the address bar when focused,
        /// similar to modern browser address bar emphasis.
        /// </summary>
        private void AnimateAddressBarFocus(bool focused)
        {
            var visual = ElementCompositionPreview.GetElementVisual(AddressBarBorder);
            var compositor = visual.Compositor;

            var targetScale = focused ? new Vector3(1.01f, 1.01f, 1f) : new Vector3(1f, 1f, 1f);

            visual.CenterPoint = new Vector3(
                (float)(AddressBarBorder.ActualWidth / 2),
                (float)(AddressBarBorder.ActualHeight / 2), 0);

            var scaleAnim = compositor.CreateSpringVector3Animation();
            scaleAnim.FinalValue = targetScale;
            scaleAnim.DampingRatio = 0.75f;
            scaleAnim.Period = TimeSpan.FromMilliseconds(45);
            scaleAnim.StopBehavior = AnimationStopBehavior.SetToFinalValue;

            visual.StartAnimation("Scale", scaleAnim);

            // Enhanced border color with smooth transition
            if (focused)
            {
                var accentColor = Microsoft.UI.ColorHelper.FromArgb(255, 0, 120, 212);
                var borderBrush = new SolidColorBrush(accentColor);
                AddressBarBorder.BorderBrush = borderBrush;
                AddressBarBorder.BorderThickness = new Thickness(2);
                
                // Add subtle shadow on focus
                AddressBarBorder.Translation = new System.Numerics.Vector3(0, 0, 32);
            }
            else
            {
                AddressBarBorder.BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
                AddressBarBorder.BorderThickness = new Thickness(1);
                AddressBarBorder.Translation = new System.Numerics.Vector3(0, 0, 0);
            }
        }

        #endregion

        #region Loading Animation Helpers

        /// <summary>
        /// Smoothly shows/hides the loading progress with a fade animation.
        /// </summary>
        private void AnimateLoadingProgress(bool visible)
        {
            var visual = ElementCompositionPreview.GetElementVisual(LoadingProgress);
            var compositor = visual.Compositor;

            if (visible)
            {
                LoadingProgress.Visibility = Visibility.Visible;
                var fadeIn = compositor.CreateScalarKeyFrameAnimation();
                fadeIn.InsertKeyFrame(0f, 0f);
                fadeIn.InsertKeyFrame(1f, 1f);
                fadeIn.Duration = TimeSpan.FromMilliseconds(150);
                visual.StartAnimation("Opacity", fadeIn);
            }
            else
            {
                var fadeOut = compositor.CreateScalarKeyFrameAnimation();
                fadeOut.InsertKeyFrame(1f, 0f);
                fadeOut.Duration = TimeSpan.FromMilliseconds(300);

                var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                visual.StartAnimation("Opacity", fadeOut);
                batch.End();

                batch.Completed += (s, e) =>
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        LoadingProgress.Visibility = Visibility.Collapsed;
                        visual.Opacity = 1f;
                    });
                };
            }
        }

        #endregion

        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    return typed;
                var result = FindChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}
