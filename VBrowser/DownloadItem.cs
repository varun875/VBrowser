using Microsoft.Web.WebView2.Core;
using Microsoft.UI.Dispatching;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace VBrowser
{
    public sealed class DownloadItem : INotifyPropertyChanged
    {
        private readonly CoreWebView2DownloadOperation? _operation;
        private bool _isPausedByUser;
        private string _resultFilePath;
        private long _bytesReceived;
        private long _totalBytesToReceive;
        private CoreWebView2DownloadState _state;
        private string _statusText = "Downloading";
        private DateTimeOffset? _lastSpeedSampleTime;
        private long _lastSpeedSampleBytes;
        private double _speedBytesPerSecond;
        private readonly DispatcherQueue? _dispatcherQueue;
        
        // 🔑 NEW: Flag to know if BrowserPage is managing updates (observer mode)
        private readonly bool _isManagedExternally;

        // 🔑 NEW: Unique ID for deduplication across UI collections
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public DownloadItem(CoreWebView2DownloadOperation operation)
        {
            _operation = operation;
            _dispatcherQueue = MainWindow.Instance?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            _isManagedExternally = false; // Legacy auto mode
            
            SourceUrl = SafeGet(() => operation.Uri, string.Empty) ?? string.Empty;
            _resultFilePath = SafeGet(() => operation.ResultFilePath, string.Empty) ?? string.Empty;
            _state = SafeGet(() => operation.State, CoreWebView2DownloadState.InProgress);

            // Subscribe to WebView2 events (auto mode only)
            _operation.BytesReceivedChanged += Operation_BytesReceivedChanged;
            _operation.StateChanged += Operation_StateChanged;

            SafeUpdateFromOperation();
        }

        // 🔑 NEW: Constructor for observer mode (BrowserPage creates & updates manually)
        public DownloadItem(string id, string url, string fileName, string filePath, long totalBytes)
        {
            Id = id;
            SourceUrl = url;
            _resultFilePath = filePath;
            _totalBytesToReceive = totalBytes;
            _state = CoreWebView2DownloadState.InProgress;
            _statusText = "Downloading";
            _dispatcherQueue = MainWindow.Instance?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
            _isManagedExternally = true; // Observer mode — BrowserPage will call UpdateProgress/UpdateStatus
            
            // Set filename from path or fallback
            if (string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(filePath))
            {
                try { fileName = Path.GetFileName(filePath); } catch { }
            }
            FileName = fileName ?? "Unknown file";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string SourceUrl { get; }

        public string ResultFilePath
        {
            get => _resultFilePath;
            set => SetField(ref _resultFilePath, value); // 🔑 Changed to public set
        }

        public string FileName { get; private set; } = "Unknown file";

        public long BytesReceived
        {
            get => _bytesReceived;
            set => SetField(ref _bytesReceived, value); // 🔑 Changed to public set
        }

        public long TotalBytesToReceive
        {
            get => _totalBytesToReceive;
            set => SetField(ref _totalBytesToReceive, value); // 🔑 Changed to public set
        }

        public double ProgressPercent => TotalBytesToReceive > 0
            ? (BytesReceived * 100d) / TotalBytesToReceive
            : 0;

        public string ProgressText => TotalBytesToReceive > 0
            ? $"{FormatBytes(BytesReceived)} / {FormatBytes(TotalBytesToReceive)}"
            : FormatBytes(BytesReceived);

        public string SpeedText
        {
            get
            {
                if (_state != CoreWebView2DownloadState.InProgress || _isPausedByUser)
                {
                    return "Speed --";
                }

                if (_speedBytesPerSecond <= 1)
                {
                    return "Speed calculating...";
                }

                return $"Speed {FormatBytes((long)_speedBytesPerSecond)}/s";
            }
        }

        public string EtaText
        {
            get
            {
                if (_state == CoreWebView2DownloadState.Completed)
                {
                    return "ETA done";
                }

                if (_state != CoreWebView2DownloadState.InProgress || _isPausedByUser)
                {
                    return "ETA --";
                }

                if (TotalBytesToReceive <= 0 || _speedBytesPerSecond <= 1 || BytesReceived >= TotalBytesToReceive)
                {
                    return "ETA --";
                }

                var remainingBytes = TotalBytesToReceive - BytesReceived;
                var remainingSeconds = remainingBytes / _speedBytesPerSecond;

                if (double.IsNaN(remainingSeconds) || double.IsInfinity(remainingSeconds) || remainingSeconds < 0)
                {
                    return "ETA --";
                }

                return $"ETA {FormatDuration(TimeSpan.FromSeconds(remainingSeconds))}";
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetField(ref _statusText, value); // 🔑 Changed to public set
        }

        public CoreWebView2DownloadState State
        {
            get => _state;
            set
            {
                if (SetField(ref _state, value))
                {
                    UpdateComputedProperties(); // Refresh CanPause/CanResume/etc.
                }
            }
        }

        public bool IsCompleted => _state == CoreWebView2DownloadState.Completed;
        public bool IsInterrupted => _state == CoreWebView2DownloadState.Interrupted;
        public bool CanPause => _state == CoreWebView2DownloadState.InProgress && !_isPausedByUser;
        public bool CanResume => (_isPausedByUser && _state == CoreWebView2DownloadState.InProgress)
            || (_state == CoreWebView2DownloadState.Interrupted && CanOperationResume());
        public bool CanRetry => _state == CoreWebView2DownloadState.Interrupted;
        public bool CanOpenFile => IsCompleted && File.Exists(ResultFilePath);
        public bool CanOpenFolder
        {
            get
            {
                try
                {
                    var folder = Path.GetDirectoryName(ResultFilePath);
                    return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder);
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }
        public bool CanCancel => _state == CoreWebView2DownloadState.InProgress;

        // 🔑 NEW: Public update methods for observer mode (BrowserPage calls these)
        public void UpdateProgress(long bytesReceived, long totalBytes)
        {
            if (_isManagedExternally)
            {
                BytesReceived = bytesReceived;
                TotalBytesToReceive = totalBytes;
                UpdateSpeedEstimate();
            }
        }

        public void UpdateStatus(CoreWebView2DownloadState newState, string? errorMessage = null)
        {
            if (_isManagedExternally)
            {
                State = newState;

                if (newState == CoreWebView2DownloadState.Completed)
                {
                    StatusText = "Completed";
                }
                else if (newState == CoreWebView2DownloadState.Interrupted)
                {
                    if (IsCancellationMessage(errorMessage))
                    {
                        StatusText = "Cancelled";
                    }
                    else if (!string.IsNullOrWhiteSpace(errorMessage))
                    {
                        StatusText = $"Failed: {errorMessage}";
                    }
                    else
                    {
                        StatusText = "Interrupted";
                    }
                }

                UpdateComputedProperties();
            }
        }

        // Legacy action methods (only work in auto mode with _operation)
        public void Pause()
        {
            if (!CanPause || _isManagedExternally) return;
            try { _operation?.Pause(); _isPausedByUser = true; UpdateComputedProperties(); } catch { }
        }

        public void Resume()
        {
            if (!CanResume || _isManagedExternally) return;
            try { _operation?.Resume(); _isPausedByUser = false; UpdateComputedProperties(); } catch { }
        }

        public void Cancel()
        {
            if (!CanCancel || _isManagedExternally) return;
            try { _operation?.Cancel(); } catch { }
        }

        public bool OpenFile()
        {
            if (!CanOpenFile) return false;
            try { Process.Start(new ProcessStartInfo(ResultFilePath) { UseShellExecute = true }); return true; } catch { return false; }
        }

        public bool OpenContainingFolder()
        {
            try
            {
                var folder = Path.GetDirectoryName(ResultFilePath);
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
                
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ResultFilePath}\"") { UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        // 🔹 Auto-mode event handlers (only fire if _isManagedExternally == false)
        private void Operation_BytesReceivedChanged(CoreWebView2DownloadOperation sender, object args)
        {
            if (!_isManagedExternally) RunOnUiThread(SafeUpdateFromOperation);
        }

        private void Operation_StateChanged(CoreWebView2DownloadOperation sender, object args)
        {
            if (!_isManagedExternally)
            {
                RunOnUiThread(() =>
                {
                    if (_state != CoreWebView2DownloadState.InProgress) _isPausedByUser = false;
                    SafeUpdateFromOperation();
                });
            }
        }

        private void SafeUpdateFromOperation()
        {
            try { UpdateFromOperation(); }
            catch (Exception) { StatusText = "Unavailable"; UpdateComputedProperties(); }
        }

        private void UpdateFromOperation()
        {
            if (_operation == null) return;
            
            ResultFilePath = SafeGet(() => _operation.ResultFilePath, ResultFilePath) ?? ResultFilePath;
            
            // Update FileName if path changed
            if (!string.IsNullOrWhiteSpace(ResultFilePath))
            {
                try { FileName = Path.GetFileName(ResultFilePath); OnPropertyChanged(nameof(FileName)); } catch { }
            }
            
            BytesReceived = SafeGet(() => _operation.BytesReceived, BytesReceived);
            TotalBytesToReceive = SafeGet(() => _operation.TotalBytesToReceive, TotalBytesToReceive);
            _state = SafeGet(() => _operation.State, _state);

            UpdateSpeedEstimate();
            StatusText = BuildStatusText();
            UpdateComputedProperties();
        }

        private void UpdateSpeedEstimate()
        {
            if (_state != CoreWebView2DownloadState.InProgress || _isPausedByUser)
            {
                _speedBytesPerSecond = 0;
                _lastSpeedSampleBytes = BytesReceived;
                _lastSpeedSampleTime = DateTimeOffset.UtcNow;
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (!_lastSpeedSampleTime.HasValue)
            {
                _lastSpeedSampleTime = now;
                _lastSpeedSampleBytes = BytesReceived;
                _speedBytesPerSecond = 0;
                return;
            }

            var elapsedSeconds = (now - _lastSpeedSampleTime.Value).TotalSeconds;
            var byteDelta = BytesReceived - _lastSpeedSampleBytes;

            if (elapsedSeconds < 0.2 || byteDelta < 0) return;

            var instantaneousSpeed = byteDelta / elapsedSeconds;
            if (instantaneousSpeed < 0) return;

            _speedBytesPerSecond = _speedBytesPerSecond <= 0
                ? instantaneousSpeed
                : (_speedBytesPerSecond * 0.7) + (instantaneousSpeed * 0.3);

            _lastSpeedSampleTime = now;
            _lastSpeedSampleBytes = BytesReceived;
        }

        private string BuildStatusText()
        {
            if (_state == CoreWebView2DownloadState.Completed) return "Completed";
            if (_state == CoreWebView2DownloadState.Interrupted)
            {
                var reason = SafeGet(() => _operation?.InterruptReason.ToString(), "Unknown");
                if (IsCancellationMessage(reason)) return "Cancelled";
                if (CanOperationResume()) return "Interrupted (can resume)";
                return $"Failed: {reason}";
            }
            return _isPausedByUser ? "Paused" : "Downloading";
        }

        private bool CanOperationResume() => SafeGet(() => _operation?.CanResume ?? false, false);

        private static bool IsCancellationMessage(string? message)
        {
            return !string.IsNullOrWhiteSpace(message)
                && message.Contains("cancel", StringComparison.OrdinalIgnoreCase);
        }

        private void RunOnUiThread(Action action)
        {
            if (_dispatcherQueue == null || _dispatcherQueue.HasThreadAccess) { action(); return; }
            _ = _dispatcherQueue.TryEnqueue(() => { try { action(); } catch { } });
        }

        private static T SafeGet<T>(Func<T> getter, T fallback)
        {
            try { return getter(); } catch { return fallback; }
        }

        private void UpdateComputedProperties()
        {
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(SpeedText));
            OnPropertyChanged(nameof(EtaText));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsInterrupted));
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(CanOpenFile));
            OnPropertyChanged(nameof(CanOpenFolder));
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(StatusText)); // Ensure status text updates on state change
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "Unknown";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1) { size /= 1024; unitIndex++; }
            return $"{size:0.#} {units[unitIndex]}";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalSeconds < 1) return "<1s";
            if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            if (duration.TotalMinutes >= 1) return $"{duration.Minutes}m {duration.Seconds}s";
            return $"{duration.Seconds}s";
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = "") =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}