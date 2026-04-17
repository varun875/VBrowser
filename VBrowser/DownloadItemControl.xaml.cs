using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;

namespace VBrowser
{
    public sealed partial class DownloadItemControl : UserControl, INotifyPropertyChanged
    {
        private DownloadItemViewModel? _viewModel;
        private MainWindow? _mainWindow;

        public DownloadItemControl()
        {
            InitializeComponent();
        }

        public DownloadItemControl(DownloadItemViewModel viewModel, MainWindow? mainWindow)
        {
            InitializeComponent();
            _viewModel = viewModel;
            _mainWindow = mainWindow;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateUI();
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            var dispatcherQueue = DispatcherQueue;
            if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
            {
                UpdateUI();
                OnPropertyChanged();
                return;
            }

            _ = dispatcherQueue.TryEnqueue(() =>
            {
                UpdateUI();
                OnPropertyChanged();
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateUI()
        {
            if (_viewModel == null)
            {
                return;
            }

            try
            {
                FileNameText.Text = _viewModel.FileName;
                SourceUrlText.Text = _viewModel.SourceUrl;
                StatusText.Text = _viewModel.StatusText;
                ProgressBar.Value = _viewModel.ProgressPercent;
                ProgressText.Text = _viewModel.ProgressText;
                SpeedText.Text = _viewModel.SpeedText;
                EtaText.Text = _viewModel.EtaText;
                ResultFilePathText.Text = _viewModel.ResultFilePath;

                PauseButton.IsEnabled = _viewModel.CanPause;
                ResumeButton.IsEnabled = _viewModel.CanResume;
                RetryButton.IsEnabled = _viewModel.CanRetry;
                CancelButton.IsEnabled = _viewModel.CanCancel;
                OpenFileButton.IsEnabled = _viewModel.CanOpenFile;
                OpenFolderButton.IsEnabled = _viewModel.CanOpenFolder;
            }
            catch
            {
                // Ignore errors during UI update
            }
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.Pause();
        }

        private void Resume_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.Resume();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.Cancel();
        }

        private void Retry_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || string.IsNullOrWhiteSpace(_viewModel.GetSourceUrl()))
            {
                return;
            }

            _mainWindow?.NavigateActiveTabTo(_viewModel.GetSourceUrl());
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            if (!_viewModel.CanOpenFile)
            {
                return;
            }

            if (!_viewModel.OpenFile())
            {
                ShowErrorDialog("Could not open the downloaded file.");
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            if (!_viewModel.CanOpenFolder)
            {
                return;
            }

            if (!_viewModel.OpenContainingFolder())
            {
                ShowErrorDialog("Could not open the download folder.");
            }
        }

        private void ShowErrorDialog(string message)
        {
            if (XamlRoot == null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Downloads",
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot
            };

            _ = dialog.ShowAsync();
        }
    }
}
