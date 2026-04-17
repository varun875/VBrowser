using System;
using System.Collections.ObjectModel;

namespace VBrowser
{
    public sealed class HistoryManager
    {
        private const int MaxEntries = 1000;
        private static readonly Lazy<HistoryManager> LazyInstance = new(() => new HistoryManager());

        public static HistoryManager Instance => LazyInstance.Value;

        public ObservableCollection<HistoryEntry> Entries { get; } = new();

        private HistoryManager()
        {
        }

        public void AddEntry(string? title, string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            var normalizedUrl = url.Trim();
            if (normalizedUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var normalizedTitle = string.IsNullOrWhiteSpace(title)
                ? normalizedUrl
                : title.Trim();

            Entries.Insert(0, new HistoryEntry
            {
                Title = normalizedTitle,
                Url = normalizedUrl,
                VisitedAt = DateTimeOffset.Now
            });

            if (Entries.Count > MaxEntries)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }
        }

        public void Clear()
        {
            Entries.Clear();
        }
    }
}