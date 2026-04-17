using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace VBrowser
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private const string SetupCompletedKey = "SetupCompleted";

        private Window? _window;
        private SetupWindow? _setupWindow;
        private MainWindow? _mainWindow;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            InitializeComponent();
            UnhandledException += App_UnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            LogException("App.UnhandledException", e.Exception);
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            LogException("AppDomain.UnhandledException", e.ExceptionObject as Exception);
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException("TaskScheduler.UnobservedTaskException", e.Exception);
        }

        private static void LogException(string source, Exception? exception)
        {
            try
            {
                var localCache = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;
                var logPath = System.IO.Path.Combine(localCache, "runtime-errors.log");
                var payload = $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(logPath, payload);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            if (IsSetupCompleted())
            {
                LaunchMainWindow();
            }
            else
            {
                LaunchSetupWindow();
            }
        }

        public void CompleteInitialSetup()
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values[SetupCompletedKey] = true;

            LaunchMainWindow();

            _setupWindow?.Close();
            _setupWindow = null;
        }

        private bool IsSetupCompleted()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
            return localSettings.Values.TryGetValue(SetupCompletedKey, out var value)
                && value is bool completed
                && completed;
        }

        private void LaunchSetupWindow()
        {
            _setupWindow ??= new SetupWindow();
            _window = _setupWindow;
            _setupWindow.Activate();
        }

        private void LaunchMainWindow()
        {
            _mainWindow ??= new MainWindow();
            _window = _mainWindow;
            _mainWindow.Activate();
        }
    }
}
