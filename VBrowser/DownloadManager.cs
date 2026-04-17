using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace VBrowser
{
    public sealed class DownloadManager
    {
        private static readonly Lazy<DownloadManager> LazyInstance = new(() => new DownloadManager());
        private readonly DispatcherQueue? _dispatcherQueue;
        private readonly HashSet<string> _trackedUrls = new(StringComparer.OrdinalIgnoreCase);

        public static DownloadManager Instance => LazyInstance.Value;

        public List<DownloadItemViewModel> Downloads { get; } = new();

        public event Action<DownloadItemViewModel>? DownloadAdded;
        public event Action? CollectionChanged;

        private DownloadManager()
        {
            _dispatcherQueue = MainWindow.Instance?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        }

        public void TrackDownload(CoreWebView2DownloadStartingEventArgs args)
        {
            var operation = args.DownloadOperation;
            if (operation == null)
            {
                Debug.WriteLine("[Download] DownloadOperation is null. Tracking aborted.");
                return;
            }

            var uri = operation.Uri;
            Debug.WriteLine($"[Download] TrackDownload called for URI: {uri}");

            // Deduplicate by URL to prevent WebView2 from firing duplicate DownloadStarting events
            lock (_trackedUrls)
            {
                if (!_trackedUrls.Add(uri))
                {
                    Debug.WriteLine($"[Download] URL already tracked, skipping: {uri}");
                    return;
                }
            }

            Debug.WriteLine($"[Download] URL added to tracked set. Current tracked count: {_trackedUrls.Count}");

            RunOnUiThread(() =>
            {
                try
                {
                    Debug.WriteLine($"[Download] UI Thread executing download insertion for URI: {uri}");
                    var downloadItem = new DownloadItem(operation);
                    var viewModel = new DownloadItemViewModel(downloadItem);
                    Downloads.Insert(0, viewModel);
                    DownloadAdded?.Invoke(viewModel);
                    CollectionChanged?.Invoke();
                    Debug.WriteLine($"[Download] Successfully inserted viewModel. Current Downloads collection count: {Downloads.Count}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Download] TrackDownload failed to create or insert item: {ex}");
                    lock (_trackedUrls)
                    {
                        _trackedUrls.Remove(uri);
                    }
                }
            });
        }

        public void ClearFinished()
        {
            void RemoveFinishedItems()
            {
                bool changed = false;
                for (int i = Downloads.Count - 1; i >= 0; i--)
                {
                    if (Downloads[i].IsCompleted || Downloads[i].IsInterrupted)
                    {
                        Downloads.RemoveAt(i);
                        changed = true;
                    }
                }
                if (changed)
                {
                    CollectionChanged?.Invoke();
                }
            }

            RunOnUiThread(RemoveFinishedItems);
        }

        private void RunOnUiThread(Action action)
        {
            var dispatcherQueue = MainWindow.Instance?.DispatcherQueue
                ?? _dispatcherQueue
                ?? DispatcherQueue.GetForCurrentThread();

            if (dispatcherQueue == null || dispatcherQueue.HasThreadAccess)
            {
                action();
                return;
            }

            if (!dispatcherQueue.TryEnqueue(() => action()))
            {
                Debug.WriteLine("DownloadManager failed to enqueue UI work.");
            }
        }
    }
}