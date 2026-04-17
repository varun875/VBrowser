using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Composition;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Numerics;

namespace VBrowser
{
    public sealed partial class HistoryPage : Page
    {
        private MainWindow? _mainWindow;

        public ObservableCollection<HistoryEntry> HistoryItems => HistoryManager.Instance.Entries;

        public HistoryPage()
        {
            InitializeComponent();
            HistoryItems.CollectionChanged += HistoryItems_CollectionChanged;
            UpdateEmptyState();
            Loaded += HistoryPage_Loaded;
        }

        private void HistoryPage_Loaded(object sender, RoutedEventArgs e)
        {
            var visual = ElementCompositionPreview.GetElementVisual(this);
            var compositor = visual.Compositor;

            var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.InsertKeyFrame(0f, 0f);
            fadeAnim.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f)));
            fadeAnim.Duration = TimeSpan.FromMilliseconds(250);

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
            UpdateEmptyState();
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

        private void OpenHistoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is HistoryEntry entry)
            {
                _mainWindow?.NavigateActiveTabTo(entry.Url);
            }
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            HistoryManager.Instance.Clear();
        }

        private void HistoryItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            bool hasItems = HistoryItems.Count > 0;
            HistoryList.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}