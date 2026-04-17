using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace VBrowser
{
    public sealed partial class DownloadsPage : Page
    {
        private MainWindow? _mainWindow;
        private bool _subscriptionsActive;
        private readonly Dictionary<DownloadItemViewModel, DownloadItemControl> _itemControls = new();

        public List<DownloadItemViewModel> DownloadItems => DownloadManager.Instance.Downloads;

        public DownloadsPage()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DownloadsPage initialization failed: {ex}");
                Content = new Grid
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Downloads is temporarily unavailable.",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Opacity = 0.72
                        }
                    }
                };
                return;
            }

            try
            {
                Unloaded += DownloadsPage_Unloaded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DownloadsPage setup failed: {ex}");
                try
                {
                    Unloaded += DownloadsPage_Unloaded;
                }
                catch
                {
                }
            }

            Loaded += DownloadsPage_Loaded;
        }

        private void DownloadsPage_Loaded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"[Download] DownloadsPage_Loaded. Items count: {DownloadItems.Count}");
            try
            {
                EnsureDownloadSubscriptions();
                RebuildAllControls();
                UpdatePageState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DownloadsPage Loaded update failed: {ex}");
            }
        }

        private void DownloadsPage_Unloaded(object sender, RoutedEventArgs e)
        {
            RemoveDownloadSubscriptions();
            ClearAllControls();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _mainWindow = e.Parameter as MainWindow;
            Debug.WriteLine($"[Download] DownloadsPage OnNavigatedTo. Items count: {DownloadItems.Count}");
            try
            {
                EnsureDownloadSubscriptions();
                RebuildAllControls();
                UpdatePageState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DownloadsPage OnNavigatedTo update failed: {ex}");
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

        private void ClearFinished_Click(object sender, RoutedEventArgs e)
        {
            DownloadManager.Instance.ClearFinished();
        }

        private void RebuildAllControls()
        {
            ClearAllControls();
            foreach (var item in DownloadItems)
            {
                AddControlForItem(item);
            }
        }

        private void ClearAllControls()
        {
            foreach (var control in _itemControls.Values)
            {
                control.PropertyChanged -= Control_PropertyChanged;
            }
            _itemControls.Clear();
            DownloadsStackPanel.Children.Clear();
        }

        private void AddControlForItem(DownloadItemViewModel item)
        {
            try
            {
                var control = new DownloadItemControl(item, _mainWindow);
                control.PropertyChanged -= Control_PropertyChanged;
                control.PropertyChanged += Control_PropertyChanged;
                _itemControls[item] = control;
                DownloadsStackPanel.Children.Add(control);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Download] Failed to create control for item: {ex.Message}");
            }
        }

        private void Control_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            UpdatePageState();
        }

        private void EnsureDownloadSubscriptions()
        {
            if (_subscriptionsActive)
            {
                return;
            }

            DownloadManager.Instance.DownloadAdded += DownloadManager_DownloadAdded;
            DownloadManager.Instance.CollectionChanged += DownloadManager_CollectionChanged;
            _subscriptionsActive = true;
        }

        private void RemoveDownloadSubscriptions()
        {
            if (!_subscriptionsActive)
            {
                return;
            }

            DownloadManager.Instance.DownloadAdded -= DownloadManager_DownloadAdded;
            DownloadManager.Instance.CollectionChanged -= DownloadManager_CollectionChanged;
            _subscriptionsActive = false;
        }

        private void DownloadManager_DownloadAdded(DownloadItemViewModel item)
        {
            var dispatcherQueue = DispatcherQueue;
            if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
            {
                AddControlForItem(item);
                UpdatePageState();
                return;
            }

            _ = dispatcherQueue.TryEnqueue(() =>
            {
                AddControlForItem(item);
                UpdatePageState();
            });
        }

        private void DownloadManager_CollectionChanged()
        {
            var dispatcherQueue = DispatcherQueue;
            if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
            {
                RebuildAllControls();
                UpdatePageState();
                return;
            }

            _ = dispatcherQueue.TryEnqueue(() =>
            {
                RebuildAllControls();
                UpdatePageState();
            });
        }

        private void UpdatePageState()
        {
            if (EmptyStatePanel == null || ClearFinishedButton == null || SummaryText == null || DownloadsScrollViewer == null)
            {
                return;
            }

            bool hasItems = DownloadItems.Count > 0;
            bool hasFinishedItems = DownloadItems.Any(item => item.IsCompleted || item.IsInterrupted);

            DownloadsScrollViewer.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            EmptyStatePanel.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            ClearFinishedButton.IsEnabled = hasFinishedItems;

            if (!hasItems)
            {
                SummaryText.Text = "Start a file download to see live progress and quick actions here.";
                return;
            }

            int activeCount = DownloadItems.Count(item => !item.IsCompleted && !item.IsInterrupted);
            int finishedCount = DownloadItems.Count - activeCount;

            SummaryText.Text = $"{DownloadItems.Count} item{(DownloadItems.Count == 1 ? string.Empty : "s")} in your queue • " +
                $"{activeCount} active • {finishedCount} finished";
        }
    }
}
