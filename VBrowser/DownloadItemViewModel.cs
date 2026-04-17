using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VBrowser
{
    public sealed class DownloadItemViewModel : INotifyPropertyChanged
    {
        private string _fileName = "Unknown file";
        private string _sourceUrl = string.Empty;
        private string _statusText = "Downloading";
        private long _bytesReceived;
        private long _totalBytesToReceive;
        private double _progressPercent;
        private string _progressText = string.Empty;
        private string _speedText = "Speed --";
        private string _etaText = "ETA --";
        private string _resultFilePath = string.Empty;
        private bool _canPause;
        private bool _canResume;
        private bool _canRetry;
        private bool _canCancel;
        private bool _canOpenFile;
        private bool _canOpenFolder;
        private bool _isCompleted;
        private bool _isInterrupted;

        public DownloadItemViewModel(DownloadItem downloadItem)
        {
            _downloadItem = downloadItem;
            SyncFromDownloadItem();
            _downloadItem.PropertyChanged += DownloadItem_PropertyChanged;
        }

        private readonly DownloadItem _downloadItem;

        public string FileName
        {
            get => _fileName;
            set => SetField(ref _fileName, value);
        }

        public string SourceUrl
        {
            get => _sourceUrl;
            set => SetField(ref _sourceUrl, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value);
        }

        public long BytesReceived
        {
            get => _bytesReceived;
            set => SetField(ref _bytesReceived, value);
        }

        public long TotalBytesToReceive
        {
            get => _totalBytesToReceive;
            set => SetField(ref _totalBytesToReceive, value);
        }

        public double ProgressPercent
        {
            get => _progressPercent;
            set => SetField(ref _progressPercent, value);
        }

        public string ProgressText
        {
            get => _progressText;
            set => SetField(ref _progressText, value);
        }

        public string SpeedText
        {
            get => _speedText;
            set => SetField(ref _speedText, value);
        }

        public string EtaText
        {
            get => _etaText;
            set => SetField(ref _etaText, value);
        }

        public string ResultFilePath
        {
            get => _resultFilePath;
            set => SetField(ref _resultFilePath, value);
        }

        public bool CanPause
        {
            get => _canPause;
            set => SetField(ref _canPause, value);
        }

        public bool CanResume
        {
            get => _canResume;
            set => SetField(ref _canResume, value);
        }

        public bool CanRetry
        {
            get => _canRetry;
            set => SetField(ref _canRetry, value);
        }

        public bool CanCancel
        {
            get => _canCancel;
            set => SetField(ref _canCancel, value);
        }

        public bool CanOpenFile
        {
            get => _canOpenFile;
            set => SetField(ref _canOpenFile, value);
        }

        public bool CanOpenFolder
        {
            get => _canOpenFolder;
            set => SetField(ref _canOpenFolder, value);
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetField(ref _isCompleted, value);
        }

        public bool IsInterrupted
        {
            get => _isInterrupted;
            set => SetField(ref _isInterrupted, value);
        }

        public void Pause() => _downloadItem.Pause();
        public void Resume() => _downloadItem.Resume();
        public void Cancel() => _downloadItem.Cancel();
        public bool OpenFile() => _downloadItem.OpenFile();
        public bool OpenContainingFolder() => _downloadItem.OpenContainingFolder();
        public string GetSourceUrl() => _downloadItem.SourceUrl;

        private void SyncFromDownloadItem()
        {
            FileName = _downloadItem.FileName;
            SourceUrl = _downloadItem.SourceUrl;
            StatusText = _downloadItem.StatusText;
            BytesReceived = _downloadItem.BytesReceived;
            TotalBytesToReceive = _downloadItem.TotalBytesToReceive;
            ProgressPercent = _downloadItem.ProgressPercent;
            ProgressText = _downloadItem.ProgressText;
            SpeedText = _downloadItem.SpeedText;
            EtaText = _downloadItem.EtaText;
            ResultFilePath = _downloadItem.ResultFilePath;
            CanPause = _downloadItem.CanPause;
            CanResume = _downloadItem.CanResume;
            CanRetry = _downloadItem.CanRetry;
            CanCancel = _downloadItem.CanCancel;
            CanOpenFile = _downloadItem.CanOpenFile;
            CanOpenFolder = _downloadItem.CanOpenFolder;
            IsCompleted = _downloadItem.IsCompleted;
            IsInterrupted = _downloadItem.IsInterrupted;
        }

        private void DownloadItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RunOnUiThread(() =>
            {
                try
                {
                    SyncFromDownloadItem();
                }
                catch
                {
                    // Ignore errors during property sync
                }
            });
        }

        private void RunOnUiThread(Action action)
        {
            var dispatcherQueue = MainWindow.Instance?.DispatcherQueue;
            if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
            {
                action();
                return;
            }

            _ = dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                }
                catch
                {
                }
            });
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged(propertyName);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
