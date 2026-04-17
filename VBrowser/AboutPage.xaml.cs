using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Diagnostics;

namespace VBrowser
{
    public sealed partial class AboutPage : Page
    {
        private MainWindow? _mainWindow;

        public AboutPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            _mainWindow = e.Parameter as MainWindow;
            
            // Start entrance animations
            PageLoadAnimation?.Begin();
            LogoFloatAnimation?.Begin();
            GlowPulseAnimation?.Begin();
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

        private async void ThirdPartyButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.XamlRoot == null) return;
            
            var dialog = new ContentDialog
            {
                Title = "Third-party Notices",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = @"VBrowser uses the following open source components:

• WinUI 3 - Windows App SDK
  Modern Windows UI framework

• WebView2 - Microsoft Edge WebView2
  Web rendering engine

• .NET 8 - Microsoft
  Development platform

All trademarks belong to their respective owners.

Thank you to the open source community for making this browser possible!",
                        FontSize = 14,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 22
                    },
                    MaxHeight = 400
                },
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            
            await dialog.ShowAsync();
        }
    }
}
