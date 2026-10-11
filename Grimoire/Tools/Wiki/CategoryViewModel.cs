using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Grimoire.Tools.Wiki
{
    public class CategoryViewModel : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Slug { get; set; }
        public WikiEntryType Type { get; set; }
        public List<string> Slugs { get; set; }
        public bool ShowRetry => IsLoaded && !IsLoading && AllEntries.Count == 0;
        public List<CategoryViewModel> Children { get; set; } = new List<CategoryViewModel>();
        public List<WikiEntry> AllEntries { get; set; } = new List<WikiEntry>();

        private List<WikiEntry> _filteredEntries = new List<WikiEntry>();
        public List<WikiEntry> FilteredEntries
        {
            get => _filteredEntries;
            private set
            {
                _filteredEntries = value;
                OnPropertyChanged(nameof(FilteredEntries));
            }
        }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(ChevronGlyph));
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ShowRetry));
            }
        }

        public bool IsLoaded { get; set; }
        public string ChevronGlyph => IsExpanded ? "▾" : "▸";
        public bool HasChildren => Children != null && Children.Count > 0;

        public string StatusText
        {
            get
            {
                if (IsLoaded) return AllEntries.Count.ToString();
                return "";
            }
        }

        public void ApplyFilter(string search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                FilteredEntries = AllEntries;
            }
            else
            {
                string s = search.Trim();
                FilteredEntries = AllEntries
                    .Where(e => e.Name != null && e.Name.IndexOf(s, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            foreach (var child in Children)
                child.ApplyFilter(search);
        }

        public void RaiseCountChanged()
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ShowRetry));
        }

        public void InjectEntry(WikiEntry entry)
        {
            if (entry == null) return;
            if (AllEntries.Any(e => e.WikiUrl != null && entry.WikiUrl != null && e.WikiUrl.Equals(entry.WikiUrl, StringComparison.OrdinalIgnoreCase)))
                return;
            AllEntries.Add(entry);
            FilteredEntries = new List<WikiEntry>(AllEntries);
            RaiseCountChanged();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
