using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;

namespace VBrowser
{
    public sealed partial class BrowserPage : Page
    {
        private const string FallbackStartUrl = "https://www.google.com";

        private string _pendingNavigationUrl = GetConfiguredStartUrl();
        private bool _isWebViewInitialized;
        private bool _isInitializingWebView;
        private string? _privateUserDataFolder;

        private static readonly object SharedEnvironmentLock = new();
        private static Task<CoreWebView2Environment>? SharedEnvironmentTask;
        private static string? SharedEnvironmentArgs;

        private static readonly List<Window> AuthPopupWindows = new();
        private static readonly string[] OAuthPopupMarkers =
        {
            "accounts.google",
            "accounts.youtube",
            "oauth",
            "signin",
            "login",
            "auth",
            "authorize",
            "storagerelay",
            "gsi"
        };
        private static readonly object OAuthLogLock = new();

        public bool IsPrivate { get; set; }
        public WebView2? WebViewControl => WebView;

        public BrowserPage()
        {
            InitializeComponent();
            Loaded += BrowserPage_Loaded;
        }

        private void BrowserPage_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeWebView2();
        }

        private async void InitializeWebView2()
        {
            if (_isWebViewInitialized || _isInitializingWebView)
                return;

            _isInitializingWebView = true;

            try
            {
                await InitializeWebViewWithDRM();
                _isWebViewInitialized = WebView.CoreWebView2 != null;
            }
            catch
            {
                _isWebViewInitialized = false;
            }
            finally
            {
                _isInitializingWebView = false;
            }

            if (_isWebViewInitialized)
                NavigateTo(_pendingNavigationUrl);
        }

        private async Task InitializeWebViewWithDRM()
        {
            try
            {
                var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                bool memorySaverEnabled = localSettings.Values.ContainsKey("MemorySaver") && (bool)localSettings.Values["MemorySaver"];
                bool highEfficiencyEnabled = localSettings.Values.ContainsKey("HighEfficiency") && (bool)localSettings.Values["HighEfficiency"];
                bool parallelDownloadsEnabled =
                    !localSettings.Values.ContainsKey("ParallelDownloads") ||
                    (bool)localSettings.Values["ParallelDownloads"];
                bool hardwareAccelerationEnabled =
                    !localSettings.Values.ContainsKey("HardwareAcceleration") ||
                    (bool)localSettings.Values["HardwareAcceleration"];

                var options = new CoreWebView2EnvironmentOptions();

                var features = new List<string>
                {
                    "WidevineContentEncryption",
                    "MediaFoundationWidevinePlayback",
                    "HardwareSecureDecryption",
                    "WidevineCdmHwSecureDataPath"
                };

                if (parallelDownloadsEnabled)
                    features.Add("ParallelDownloading");
                if (memorySaverEnabled)
                    features.Add("MemorySaverMode");
                if (highEfficiencyEnabled)
                    features.Add("HighEfficiencyMode");

                var disabledFeatures = new List<string>
                {
                    "PreloadMediaEngagementData",
                    "PreconnectOnAnchorClick",
                    "ThirdPartyStoragePartitioning",
                    "ThirdPartyCookiesBlocking",
                    "PrivacySandboxAdsAPIsOverride"
                };

                if (memorySaverEnabled || highEfficiencyEnabled)
                {
                    disabledFeatures.Add("BackForwardCache");
                    disabledFeatures.Add("SpareRendererForSitePerProcess");
                }

                int rendererProcessLimit = highEfficiencyEnabled ? 2 : (memorySaverEnabled ? 3 : 4);
                string gpuArguments = hardwareAccelerationEnabled
                    ? " --enable-zero-copy --ignore-gpu-blocklist --enable-gpu-rasterization --enable-hardware-overlays"
                    : " --disable-gpu --disable-gpu-compositing --disable-accelerated-video-decode";

                options.AdditionalBrowserArguments =
                    $"--enable-features={string.Join(",", features)}" +
                    $" --disable-features={string.Join(",", disabledFeatures)}" +
                    $" --renderer-process-limit={rendererProcessLimit}" +
                    gpuArguments;

                options.AreBrowserExtensionsEnabled = true;

                CoreWebView2Environment environment;
                if (IsPrivate)
                {
                    var userDataFolder = EnsurePrivateUserDataFolder();
                    environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, options);
                }
                else
                {
                    environment = await GetOrCreateSharedEnvironmentAsync(options);
                }

                await WebView.EnsureCoreWebView2Async(environment);
                ConfigureCoreWebView();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebView2] DRM init failed, falling back: {ex.Message}");
                await WebView.EnsureCoreWebView2Async();
                ConfigureCoreWebView();
            }
        }

        private void ConfigureCoreWebView()
        {
            var core = WebView.CoreWebView2;
            if (core == null) return;

            try
            {
                core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;
                core.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.None;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebView2] Profile config failed: {ex.Message}");
            }

            try
            {
                core.Settings.AreDefaultScriptDialogsEnabled = true;
                core.Settings.IsScriptEnabled = true;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.Settings.IsSwipeNavigationEnabled = true;

                if (IsPrivate)
                {
                    core.Settings.IsGeneralAutofillEnabled = false;
                    core.Settings.IsPasswordAutosaveEnabled = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebView2] Settings config failed: {ex.Message}");
            }

            TryWireEvent(() => { core.ProcessFailed -= CoreWebView2_ProcessFailed; core.ProcessFailed += CoreWebView2_ProcessFailed; }, nameof(core.ProcessFailed));
            TryWireEvent(() => { core.DownloadStarting -= CoreWebView2_DownloadStarting; core.DownloadStarting += CoreWebView2_DownloadStarting; }, nameof(core.DownloadStarting));
            TryWireEvent(() => { core.NewWindowRequested -= CoreWebView2_NewWindowRequested; core.NewWindowRequested += CoreWebView2_NewWindowRequested; }, nameof(core.NewWindowRequested));
            TryWireEvent(() => { core.PermissionRequested -= CoreWebView2_PermissionRequested; core.PermissionRequested += CoreWebView2_PermissionRequested; }, nameof(core.PermissionRequested));

            try
            {
                core.AddWebResourceRequestedFilter("vbrowser://*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested -= CoreWebView2_WebResourceRequested;
                core.WebResourceRequested += CoreWebView2_WebResourceRequested;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebView2] WebResourceRequested wiring failed: {ex.Message}");
            }

            try
            {
                _ = core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.DiskCache);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebView2] Cache clear failed: {ex.Message}");
            }
        }

        private void CoreWebView2_WebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
        {
            if (args.Request.Uri.Equals("vbrowser://extensions", StringComparison.OrdinalIgnoreCase) ||
                args.Request.Uri.Equals("vbrowser://extensions/", StringComparison.OrdinalIgnoreCase))
            {
                string html = @"
<!DOCTYPE html>
<html>
<head>
    <title>Extensions - VBrowser</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; background-color: #f3f3f3; margin: 0; padding: 40px; color: #333; }
        .container { max-width: 800px; margin: 0 auto; }
        h1 { font-size: 28px; margin-bottom: 20px; font-weight: 600; }
        .extension-card { background: white; border-radius: 8px; padding: 20px; margin-bottom: 16px; box-shadow: 0 2px 4px rgba(0,0,0,0.05); display: flex; align-items: center; justify-content: space-between; }
        .ext-info { display: flex; flex-direction: column; }
        .ext-name { font-size: 18px; font-weight: bold; margin-bottom: 4px; }
        .ext-id { font-size: 12px; color: #888; }
        .empty-state { text-align: center; padding: 60px 20px; color: #666; background: white; border-radius: 8px; font-size: 16px; }
        .btn { background: #0078d4; color: white; border: none; padding: 8px 16px; border-radius: 4px; cursor: pointer; font-size: 14px; font-weight: 500; }
        .btn:hover { background: #006cbe; }
        .remove-btn { background: #d13438; }
        .remove-btn:hover { background: #a80000; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>Installed Extensions</h1>
        <div id='ext-list'><div class='empty-state'>Loading...</div></div>
    </div>
    <script>
        window.chrome.webview.addEventListener('message', event => {
            const extList = document.getElementById('ext-list');
            if (event.data.type === 'extensions') {
                const exts = event.data.data;
                if (exts.length === 0) {
                    extList.innerHTML = '<div class=\'empty-state\'>No extensions installed. Visit the Chrome Web Store or Edge Add-ons Store to install one.</div>';
                    return;
                }
                extList.innerHTML = '';
                exts.forEach(ext => {
                    const card = document.createElement('div');
                    card.className = 'extension-card';
                    card.innerHTML = `
                        <div class='ext-info'>
                            <span class='ext-name'>Extension Folder: &nbsp;</span>
                            <span class='ext-id'>${ext}</span>
                        </div>
                        <button class='btn remove-btn'>Remove</button>
                    `;
                    const removeButton = card.querySelector('.remove-btn');
                    if (removeButton) {
                        removeButton.addEventListener('click', () => removeExt(ext));
                    }
                    extList.appendChild(card);
                });
            }
        });
        
        function removeExt(path) {
            window.chrome.webview.postMessage('REMOVE_EXTENSION:' + path);
        }

        // Request list on load
        window.chrome.webview.postMessage('GET_EXT_LIST:');
    </script>
</body>
</html>";
                var utf8 = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(html));
                var randomAccessStream = utf8.AsRandomAccessStream();
                args.Response = sender.Environment.CreateWebResourceResponse(
                    randomAccessStream, 200, "OK", "Content-Type: text/html");
            }
        }

        private static void TryWireEvent(Action wire, string eventName)
        {
            try { wire(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[WebView2] Event wiring failed for {eventName}: {ex.Message}"); }
        }

        private string EnsurePrivateUserDataFolder()
        {
            if (!string.IsNullOrWhiteSpace(_privateUserDataFolder))
                return _privateUserDataFolder;

            var privateRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VBrowser", "PrivateProfiles");
            Directory.CreateDirectory(privateRoot);

            _privateUserDataFolder = Path.Combine(privateRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_privateUserDataFolder);
            return _privateUserDataFolder;
        }

        private static Task<CoreWebView2Environment> GetOrCreateSharedEnvironmentAsync(CoreWebView2EnvironmentOptions options)
        {
            var argsSignature = options.AdditionalBrowserArguments ?? string.Empty;

            lock (SharedEnvironmentLock)
            {
                if (SharedEnvironmentTask == null ||
                    SharedEnvironmentTask.IsFaulted ||
                    !string.Equals(SharedEnvironmentArgs, argsSignature, StringComparison.Ordinal))
                {
                    SharedEnvironmentArgs = argsSignature;
                    SharedEnvironmentTask = CoreWebView2Environment.CreateWithOptionsAsync(null, null, options).AsTask();
                }

                return SharedEnvironmentTask;
            }
        }

        private void CoreWebView2_DownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
        {
            HandleDownloadStarting(args);
        }

        // ✅ FIXED: Let WebView2 manage downloads natively while observing for custom UI
        private void HandleDownloadStarting(CoreWebView2DownloadStartingEventArgs args)
        {
            System.Diagnostics.Debug.WriteLine($"[Download] DownloadStarting event fired for URI: {args.DownloadOperation?.Uri}");

            // 🔑 CRITICAL: Do NOT take ownership. Let WebView2 handle the download natively.
            args.Handled = false;

            // Track the download using the existing DownloadManager
            try
            {
                DownloadManager.Instance.TrackDownload(args);
                System.Diagnostics.Debug.WriteLine("[Download] Successfully called TrackDownload");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Download] TrackDownload failed: {ex}");
            }
        }

        private async void CoreWebView2_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
        {
            var url = args.Uri ?? string.Empty;
            LogOAuthEvent($"NewWindowRequested: {url}");

            if (IsOAuthPopupUrl(url))
            {
                var deferral = args.GetDeferral();
                try
                {
                    var popupCore = await CreateOAuthPopupCoreWebViewAsync(sender.Environment);
                    if (popupCore != null)
                    {
                        args.NewWindow = popupCore;
                        args.Handled = true;
                        LogOAuthEvent("OAuth popup bound via args.NewWindow.");
                        return;
                    }
                }
                catch
                {
                    LogOAuthEvent("OAuth popup creation failed.");
                }
                finally
                {
                    deferral.Complete();
                }

                args.Handled = true;
                MainWindow.Instance?.NewTabWithUrl(url);
                LogOAuthEvent("Fell back to opening OAuth URL in new tab.");
                return;
            }

            args.Handled = true;
            MainWindow.Instance?.NewTabWithUrl(url);
        }

        private static bool IsOAuthPopupUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            foreach (var marker in OAuthPopupMarkers)
            {
                if (url.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool TryConvertStorageRelayUrl(string? url, out string relayTargetUrl)
        {
            relayTargetUrl = string.Empty;
            const string storageRelayPrefix = "storagerelay://";

            if (string.IsNullOrWhiteSpace(url) ||
                !url.StartsWith(storageRelayPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var payload = url[storageRelayPrefix.Length..];
            try { payload = Uri.UnescapeDataString(payload); } catch { }

            if (payload.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                payload.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                relayTargetUrl = payload;
                LogOAuthEvent($"Converted storagerelay URI to: {relayTargetUrl}");
                return true;
            }

            if (payload.StartsWith("https/", StringComparison.OrdinalIgnoreCase))
            {
                relayTargetUrl = "https://" + payload["https/".Length..];
                LogOAuthEvent($"Converted storagerelay URI (https/) to: {relayTargetUrl}");
                return true;
            }

            if (payload.StartsWith("http/", StringComparison.OrdinalIgnoreCase))
            {
                relayTargetUrl = "http://" + payload["http/".Length..];
                LogOAuthEvent($"Converted storagerelay URI (http/) to: {relayTargetUrl}");
                return true;
            }

            return false;
        }

        private async Task<CoreWebView2?> CreateOAuthPopupCoreWebViewAsync(CoreWebView2Environment environment)
        {
            var popupWebView = new WebView2();
            var popupWindow = new Window { Title = "Sign in" };

            popupWindow.Content = popupWebView;
            popupWindow.Closed += (_, __) =>
            {
                AuthPopupWindows.Remove(popupWindow);
                try { popupWebView.Close(); } catch { }
            };

            AuthPopupWindows.Add(popupWindow);
            popupWindow.Activate();

            await popupWebView.EnsureCoreWebView2Async(environment);
            var popupCore = popupWebView.CoreWebView2;
            if (popupCore == null)
            {
                popupWindow.Close();
                return null;
            }

            popupCore.Settings.AreDefaultScriptDialogsEnabled = true;
            popupCore.Settings.IsScriptEnabled = true;
            popupCore.Settings.AreBrowserAcceleratorKeysEnabled = false;
            popupCore.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.None;

            popupCore.NavigationStarting += (popupSender, navArgs) =>
            {
                LogOAuthEvent($"Popup NavigationStarting: {navArgs.Uri}");
                if (TryConvertStorageRelayUrl(navArgs.Uri, out var relayTargetUrl))
                {
                    navArgs.Cancel = true;
                    try { popupSender.Navigate(relayTargetUrl); }
                    catch { LogOAuthEvent($"Failed to navigate converted storagerelay target: {relayTargetUrl}"); }
                }
            };

            popupCore.NavigationCompleted += (_, navArgs) =>
            {
                LogOAuthEvent($"Popup NavigationCompleted: Success={navArgs.IsSuccess}, Status={navArgs.WebErrorStatus}");
            };

            popupCore.WindowCloseRequested += (_, __) =>
            {
                LogOAuthEvent("Popup WindowCloseRequested received.");
                popupWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    try { popupWindow.Close(); } catch { }
                });
            };

            return popupCore;
        }

        private static void LogOAuthEvent(string message)
        {
            try
            {
                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "VBrowser");
                Directory.CreateDirectory(logDirectory);

                var logPath = Path.Combine(logDirectory, "oauth-debug.log");
                var entry = $"{DateTime.Now:O} {message}{Environment.NewLine}";

                lock (OAuthLogLock)
                {
                    File.AppendAllText(logPath, entry);
                }
            }
            catch { }
        }

        private void CoreWebView2_ProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
        {
            if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
                WebView?.Reload();
        }

        private void CoreWebView2_PermissionRequested(CoreWebView2 sender, CoreWebView2PermissionRequestedEventArgs args)
        {
            // Let WebView2 prompt the user for camera, mic, geolocation, etc.
            // No-op handler intentionally — default behavior is preserved.
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var targetUrl = e.Parameter as string;
            if (string.IsNullOrWhiteSpace(targetUrl))
                targetUrl = GetConfiguredStartUrl();

            NavigateTo(targetUrl);
        }

        public void NavigateTo(string url)
        {
            _pendingNavigationUrl = NormalizeUrl(url);

            if (!_isWebViewInitialized || WebView == null)
                return;

            if (WebView.Source != null &&
                string.Equals(WebView.Source.ToString(), _pendingNavigationUrl, StringComparison.OrdinalIgnoreCase))
                return;

            WebView.Source = new Uri(_pendingNavigationUrl);
        }

        public void Reload() => WebView?.Reload();
        public void GoBack() { if (WebView?.CanGoBack == true) WebView.GoBack(); }
        public void GoForward() { if (WebView?.CanGoForward == true) WebView.GoForward(); }
        public void Refresh() => WebView?.Reload();

        public string GetDisplayUrl()
        {
            var currentUrl = WebView?.CoreWebView2?.Source;
            if (!string.IsNullOrWhiteSpace(currentUrl) &&
                !string.Equals(currentUrl, "about:blank", StringComparison.OrdinalIgnoreCase))
                return currentUrl;

            currentUrl = WebView?.Source?.ToString();
            if (!string.IsNullOrWhiteSpace(currentUrl) &&
                !string.Equals(currentUrl, "about:blank", StringComparison.OrdinalIgnoreCase))
                return currentUrl;

            return string.IsNullOrWhiteSpace(_pendingNavigationUrl)
                ? GetConfiguredStartUrl()
                : _pendingNavigationUrl;
        }

        private static string NormalizeUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return GetConfiguredStartUrl();

            var normalized = url.Trim();

            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("vbrowser://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://" + normalized;
            }

            return normalized;
        }

        private static string GetConfiguredStartUrl()
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

        public async Task CleanupPrivateDataFolderAsync()
        {
            if (!IsPrivate || string.IsNullOrWhiteSpace(_privateUserDataFolder))
                return;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    if (Directory.Exists(_privateUserDataFolder))
                        Directory.Delete(_privateUserDataFolder, recursive: true);
                    return;
                }
                catch (IOException) { await Task.Delay(150); }
                catch (UnauthorizedAccessException) { await Task.Delay(150); }
            }
        }

        public void ClearBrowsingData()
        {
            if (WebView?.CoreWebView2 == null) return;

            _ = WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.DiskCache |
                CoreWebView2BrowsingDataKinds.Cookies |
                CoreWebView2BrowsingDataKinds.DownloadHistory |
                CoreWebView2BrowsingDataKinds.AllDomStorage |
                CoreWebView2BrowsingDataKinds.AllProfile);
        }

        public async Task StopMediaAndUnloadAsync()
        {
            var webView = WebView;
            var core = webView?.CoreWebView2;
            if (core == null) return;

            const string stopMediaScript = @"(() => {
                try {
                    const mediaElements = document.querySelectorAll('video, audio');
                    for (const element of mediaElements) {
                        try {
                            element.pause();
                            if (!isNaN(element.currentTime)) element.currentTime = 0;
                        } catch {}
                    }
                    for (const element of document.querySelectorAll('video')) {
                        try {
                            const stream = element.srcObject;
                            if (stream && typeof stream.getTracks === 'function') {
                                for (const track of stream.getTracks()) track.stop();
                                element.srcObject = null;
                            }
                        } catch {}
                    }
                } catch {}
            })();";

            try { await core.ExecuteScriptAsync(stopMediaScript); } catch { }
            try { core.Stop(); } catch { }

            try { core.Navigate("about:blank"); }
            catch
            {
                try { if (webView != null) webView.Source = new Uri("about:blank"); } catch { }
            }

            try { await core.TrySuspendAsync(); } catch { }
        }

        public async Task<bool> TrySuspendAsync()
        {
            var core = WebView?.CoreWebView2;
            if (core == null) return false;

            try { return await core.TrySuspendAsync(); }
            catch { return false; }
        }

        public void ResumeIfSuspended()
        {
            var core = WebView?.CoreWebView2;
            if (core == null) return;

            try { core.Resume(); } catch { }
        }
    }
}
