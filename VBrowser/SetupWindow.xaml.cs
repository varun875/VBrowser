using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Windowing;
using System;

namespace VBrowser
{
    public sealed partial class SetupWindow : Window
    {
        private const string DefaultHomepage = "https://www.google.com";
        private const int TotalSteps = 4;
        private int _currentStep;

        private readonly string[] _stepTitles =
        {
            "Welcome to VBrowser",
            "Pick your appearance",
            "Choose search and home",
            "Tune performance"
        };

        private readonly string[] _stepSubtitles =
        {
            "A quick guided setup, inspired by modern browser onboarding.",
            "Choose light or dark mode. You can switch it anytime in Settings.",
            "Set your default search engine and startup homepage.",
            "Balance memory use and efficiency."
        };

        public SetupWindow()
        {
            InitializeComponent();
            ConfigureWindowPresentation();
            LoadSavedDefaults();
            ApplySetupTheme();
            ShowStep(0);
            UpdateThemePreview();
            UpdateSummaryPreview();
        }

        private void ConfigureWindowPresentation()
        {
            try
            {
                AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                AppWindow.SetIcon("Assets/VBrowser.ico");

                if (AppWindow.Presenter is OverlappedPresenter overlappedPresenter)
                {
                    overlappedPresenter.Maximize();
                }
            }
            catch
            {
            }
        }

        private void LoadSavedDefaults()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

            SearchEngineCombo.SelectedIndex =
                localSettings.Values.TryGetValue("SearchEngine", out var searchValue) &&
                searchValue is int savedIndex &&
                savedIndex >= 0 &&
                savedIndex <= 2
                    ? savedIndex
                    : 0;

            HomepageTextBox.Text =
                localSettings.Values.TryGetValue("Homepage", out var homepageValue) && homepageValue is string homepage && !string.IsNullOrWhiteSpace(homepage)
                    ? homepage
                    : DefaultHomepage;

            DarkModeToggle.IsOn =
                localSettings.Values.TryGetValue("DarkMode", out var darkValue) &&
                darkValue is bool darkMode &&
                darkMode;

            MemorySaverToggle.IsOn =
                localSettings.Values.TryGetValue("MemorySaver", out var memorySaverValue) &&
                memorySaverValue is bool memorySaver &&
                memorySaver;

            HighEfficiencyToggle.IsOn =
                localSettings.Values.TryGetValue("HighEfficiency", out var highEfficiencyValue) &&
                highEfficiencyValue is bool highEfficiency &&
                highEfficiency;
        }

        private void UseDefaults_Click(object sender, RoutedEventArgs e)
        {
            SearchEngineCombo.SelectedIndex = 0;
            HomepageTextBox.Text = DefaultHomepage;
            DarkModeToggle.IsOn = false;
            MemorySaverToggle.IsOn = false;
            HighEfficiencyToggle.IsOn = false;

            ApplySetupTheme();
            UpdateThemePreview();
            UpdateSummaryPreview();
            ShowStep(0);
        }

        private void BackStep_Click(object sender, RoutedEventArgs e)
        {
            ShowStep(_currentStep - 1);
        }

        private void NextStep_Click(object sender, RoutedEventArgs e)
        {
            ShowStep(_currentStep + 1);
        }

        private void DarkModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            ApplySetupTheme();
            UpdateThemePreview();
        }

        private void PerformanceToggle_Toggled(object sender, RoutedEventArgs e)
        {
            UpdateSummaryPreview();
        }

        private void SearchEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSummaryPreview();
        }

        private void HomepageTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSummaryPreview();
        }

        private void StartBrowsing_Click(object sender, RoutedEventArgs e)
        {
            SavePreferences();

            if (Application.Current is App app)
            {
                app.CompleteInitialSetup();
            }
            else
            {
                Close();
            }
        }

        private void SavePreferences()
        {
            var localSettings = Windows.Storage.ApplicationData.Current.LocalSettings;

            localSettings.Values["SearchEngine"] = SearchEngineCombo.SelectedIndex < 0 ? 0 : SearchEngineCombo.SelectedIndex;
            localSettings.Values["Homepage"] = NormalizeHomepage(HomepageTextBox.Text);
            localSettings.Values["DarkMode"] = DarkModeToggle.IsOn;
            localSettings.Values["MemorySaver"] = MemorySaverToggle.IsOn;
            localSettings.Values["HighEfficiency"] = HighEfficiencyToggle.IsOn;
        }

        private void ShowStep(int step)
        {
            if (step < 0)
            {
                step = 0;
            }

            if (step > TotalSteps - 1)
            {
                step = TotalSteps - 1;
            }

            _currentStep = step;

            Step1Panel.Visibility = _currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step4Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

            StepTitleText.Text = _stepTitles[_currentStep];
            StepSubtitleText.Text = _stepSubtitles[_currentStep];
            StepCounterText.Text = $"Step {_currentStep + 1} of {TotalSteps}";

            BackStepButton.IsEnabled = _currentStep > 0;
            NextStepButton.Visibility = _currentStep < TotalSteps - 1 ? Visibility.Visible : Visibility.Collapsed;
            StartBrowsingButton.Visibility = _currentStep == TotalSteps - 1 ? Visibility.Visible : Visibility.Collapsed;

            UpdateStepIndicators();
            UpdateSummaryPreview();
        }

        private void UpdateStepIndicators()
        {
            SetStepIndicatorState(StepDot1, StepLabel1, 0);
            SetStepIndicatorState(StepDot2, StepLabel2, 1);
            SetStepIndicatorState(StepDot3, StepLabel3, 2);
            SetStepIndicatorState(StepDot4, StepLabel4, 3);
        }

        private void SetStepIndicatorState(Shape dot, TextBlock label, int stepIndex)
        {
            bool activeOrDone = stepIndex <= _currentStep;

            dot.Fill = (Brush)Application.Current.Resources[
                activeOrDone ? "AccentFillColorDefaultBrush" : "CardStrokeColorDefaultBrush"];

            label.Opacity = activeOrDone ? 1 : 0.7;
            label.FontWeight = activeOrDone ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }

        private void UpdateThemePreview()
        {
            if (DarkModeToggle.IsOn)
            {
                ThemePreviewTitleText.Text = "Dark mode enabled";
                ThemePreviewBodyText.Text = "VBrowser will open with a low-glare look that is easier on your eyes at night.";
            }
            else
            {
                ThemePreviewTitleText.Text = "Light mode enabled";
                ThemePreviewBodyText.Text = "VBrowser will open with a bright, high-contrast theme that is great for daytime use.";
            }
        }

        private void ApplySetupTheme()
        {
            if (Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = DarkModeToggle.IsOn
                    ? ElementTheme.Dark
                    : ElementTheme.Light;
            }
        }

        private void UpdateSummaryPreview()
        {
            SummaryTextBlock.Text =
                $"Search: {GetSelectedSearchEngineName()}\n" +
                $"Homepage: {NormalizeHomepage(HomepageTextBox.Text)}\n" +
                $"Dark mode: {(DarkModeToggle.IsOn ? "On" : "Off")}\n" +
                $"Memory saver: {(MemorySaverToggle.IsOn ? "On" : "Off")}\n" +
                $"High efficiency: {(HighEfficiencyToggle.IsOn ? "On" : "Off")}";
        }

        private string GetSelectedSearchEngineName()
        {
            return SearchEngineCombo.SelectedItem is ComboBoxItem item
                ? item.Content?.ToString() ?? "Google"
                : "Google";
        }

        private static string NormalizeHomepage(string? homepage)
        {
            if (string.IsNullOrWhiteSpace(homepage))
            {
                return DefaultHomepage;
            }

            var normalized = homepage.Trim();

            if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://" + normalized;
            }

            return normalized;
        }
    }
}