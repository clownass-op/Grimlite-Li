using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Grimoire.Tools.Wiki
{
    public partial class WikiView : UserControl
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly Dictionary<string, BitmapImage> _imageCache = new Dictionary<string, BitmapImage>();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        private static readonly Dictionary<string, Color> RarityColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["Common"] = Color.FromRgb(0xC6, 0xC8, 0xD4),
            ["Rare"] = Color.FromRgb(0x5C, 0xC9, 0x6B),
            ["Epic"] = Color.FromRgb(0xE0, 0xB1, 0x3B),
            ["Legendary"] = Color.FromRgb(0xE0, 0x5C, 0x5C),
            ["Non-Rare Legend"] = Color.FromRgb(0xB0, 0x7B, 0xE0),
            ["Ultra Rare"] = Color.FromRgb(0x6F, 0xA8, 0xF5),
            ["Artifact"] = Color.FromRgb(0xC9, 0x9A, 0x7A),
        };

        private ObservableCollection<CategoryViewModel> _categories = new ObservableCollection<CategoryViewModel>
        {
            new CategoryViewModel { Name = "Armor", Slug = "armors", Type = WikiEntryType.Item },
            new CategoryViewModel { Name = "Capes & Back Items", Slug = "capes-back-items", Type = WikiEntryType.Item },
            new CategoryViewModel { Name = "Helmets & Hoods", Slug = "helmets-hoods", Type = WikiEntryType.Item },
            new CategoryViewModel
            {
                Name = "Housing",
                Type = WikiEntryType.Item,
                Slugs = new List<string> { "houses", "floor-items", "wall-items" },
                Children = new List<CategoryViewModel>
                {
                    new CategoryViewModel { Name = "Houses", Slug = "houses", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Floor Items", Slug = "floor-items", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Wall Items", Slug = "wall-items", Type = WikiEntryType.Item },
                }
            },
            new CategoryViewModel { Name = "Misc. Items", Slug = "misc-items", Type = WikiEntryType.Item },
            new CategoryViewModel { Name = "Necklaces", Slug = "necklaces", Type = WikiEntryType.Item },
            new CategoryViewModel { Name = "Grounds", Slug = "grounds", Type = WikiEntryType.Item },
            new CategoryViewModel { Name = "Pets", Slug = "pets", Type = WikiEntryType.Item },
            new CategoryViewModel
            {
                Name = "Weapons",
                Type = WikiEntryType.Item,
                Slugs = new List<string>
                {
                    "axes", "bows", "daggers", "gauntlets", "guns", "handguns",
                    "maces", "polearms", "rifles", "staffs", "swords", "wands", "whips"
                },
                Children = new List<CategoryViewModel>
                {
                    new CategoryViewModel { Name = "Axes", Slug = "axes", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Bows", Slug = "bows", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Daggers", Slug = "daggers", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Gauntlets", Slug = "gauntlets", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Guns", Slug = "guns", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "HandGuns", Slug = "handguns", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Maces", Slug = "maces", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Polearms", Slug = "polearms", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Rifles", Slug = "rifles", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Staffs", Slug = "staffs", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Swords", Slug = "swords", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Wands", Slug = "wands", Type = WikiEntryType.Item },
                    new CategoryViewModel { Name = "Whips", Slug = "whips", Type = WikiEntryType.Item },
                }
            },
            new CategoryViewModel { Name = "Locations", Slug = "locations", Type = WikiEntryType.Location },
            new CategoryViewModel { Name = "Monsters", Slug = "monsters", Type = WikiEntryType.Monster },
            new CategoryViewModel { Name = "Shops", Slug = "shops", Type = WikiEntryType.Shop },
            new CategoryViewModel { Name = "Quests", Slug = "quests", Type = WikiEntryType.Quest },
            new CategoryViewModel { Name = "NPCs", Slug = "npcs", Type = WikiEntryType.NPC },
            new CategoryViewModel { Name = "Classes", Slug = "classes", Type = WikiEntryType.Class },
            new CategoryViewModel { Name = "Enhancements", Slug = "enhancements", Type = WikiEntryType.Enhancement },
        };
        private WikiEntry _selected;
        private List<(CategoryViewModel Category, FrameworkElement Header)> _categoryHeaderMap
            = new List<(CategoryViewModel, FrameworkElement)>();
        private enum Gender { Male, Female }
        private Gender _currentGender = Gender.Male;
        private bool _logVisible = true;
        private GridLength _logColumnWidth;
        private readonly DispatcherTimer _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };

        private List<ScreenInfo> _currentScreens = new List<ScreenInfo>();
        private int _currentScreenIndex = 0;

        private List<ScreenInfo> _currentNpcScreens = new List<ScreenInfo>();
        private int _currentNpcScreenIndex = 0;
        private List<NpcDialogueTab> _currentNpcDialogueTabs = new List<NpcDialogueTab>();

        private List<ShopTab> _currentShopTabs = new List<ShopTab>();
        private int _currentShopTabIndex = 0;

        private Point _dragStartPoint;
        private CategoryViewModel _draggedCategory;
        private bool _isDragging;
        private DragAdorner _dragAdorner;
        private AdornerLayer _dragAdornerLayer;
        private FrameworkElement _draggedHeaderElement;
        private Point _dragGrabOffset;
        private FrameworkElement _draggedContainerElement;
        private int _draggedOriginalIndex = -1;
        private int _lastLiveDropIndex = -1;
        private double _draggedItemHeight = 0;
        private struct DragRow
        {
            public CategoryViewModel Category;
            public FrameworkElement Container;
            public double Top;
            public double Height;
        }
        private List<DragRow> _dragRows;

        public WikiView()
        {
            InitializeComponent();
            WikiScraper.Log += OnScraperLog;
            Unloaded += (s, e) => WikiScraper.Log -= OnScraperLog;

            _logColumnWidth = logColumnDef.Width;
            PreviewKeyDown += WikiView_PreviewKeyDown;

            icCategories.ItemsSource = _categories;

            _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
        }

        private void WikiView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ToggleLogPanel();
                e.Handled = true;
            }
        }

        private void ToggleLogPanel()
        {
            _logVisible = !_logVisible;
            logPanel.Visibility = _logVisible ? Visibility.Visible : Visibility.Collapsed;
            logColumnDef.Width = _logVisible ? _logColumnWidth : new GridLength(0);
        }

        private void OnScraperLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                txtLog.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
                logScroll.ScrollToEnd();
            });
        }

        private async void CategoryHeader_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is CategoryViewModel category)) return;

            ShowRipple(fe);

            foreach (var c in _categories)
            {
                c.IsSelected = (c == category);
                foreach (var child in c.Children)
                    child.IsSelected = (child == category);
            }

            category.IsExpanded = !category.IsExpanded;

            if (category.IsExpanded && !category.IsLoaded && !category.IsLoading)
            {
                await LoadCategoryAsync(category);
            }

            _ = Dispatcher.BeginInvoke(new Action(UpdatePinnedCategory), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private async void CategoryRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is CategoryViewModel category)) return;

            category.IsLoaded = false;
            await LoadCategoryAsync(category);
        }

        private void CategoryChevron_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement fe) || !(fe.Tag is CategoryViewModel category)) return;

            ShowRipple(fe);

            category.IsExpanded = !category.IsExpanded;

            _ = Dispatcher.BeginInvoke(new Action(UpdatePinnedCategory), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void CategoryHeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is CategoryViewModel category)
            {
                _dragStartPoint = e.GetPosition(icCategories);
                _draggedCategory = category;
                _isDragging = false;
            }
        }

        private void CategoryHeader_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedCategory != null && !_isDragging)
            {
                Point pos = e.GetPosition(icCategories);
                if (Math.Abs(pos.X - _dragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(pos.Y - _dragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDragging = true;

                    if (sender is FrameworkElement header)
                    {
                        StartDragVisual(header, e.GetPosition(header));
                    }

                    try
                    {
                        DragDrop.DoDragDrop(icCategories, _draggedCategory, DragDropEffects.Move);
                    }
                    finally
                    {
                        EndDragVisual();
                    }

                    _draggedCategory = null;
                }
            }
        }

        private void StartDragVisual(FrameworkElement header, Point grabPointInHeader)
        {
            for (int i = 0; i < _categories.Count; i++)
            {
                var c = icCategories.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (c == null) continue;
                c.Opacity = 1.0;
                if (c.RenderTransform is TranslateTransform ttExisting)
                {
                    ttExisting.BeginAnimation(TranslateTransform.YProperty, null);
                    ttExisting.Y = 0;
                }
            }

            _dragAdornerLayer = AdornerLayer.GetAdornerLayer(icCategories);
            if (_dragAdornerLayer == null) return;

            double w = header.ActualWidth;
            double h = header.ActualHeight;

            _dragAdorner = new DragAdorner(icCategories, header, w, h);
            _dragAdornerLayer.Add(_dragAdorner);

            _dragGrabOffset = grabPointInHeader;
            _draggedHeaderElement = header;

            _draggedContainerElement = icCategories.ItemContainerGenerator.ContainerFromItem(_draggedCategory) as FrameworkElement;
            if (_draggedContainerElement != null)
                _draggedContainerElement.Opacity = 0;

            _draggedOriginalIndex = _categories.IndexOf(_draggedCategory);
            _draggedItemHeight = header.ActualHeight;
            _lastLiveDropIndex = _draggedOriginalIndex;

            CaptureDragLayout();

            icCategories.PreviewDragOver += DragVisual_PreviewDragOver;
            icCategories.PreviewGiveFeedback += DragVisual_GiveFeedback;
        }

        private void CaptureDragLayout()
        {
            _dragRows = new List<DragRow>();
            double cumulative = 0;
            for (int i = 0; i < _categories.Count; i++)
            {
                var container = icCategories.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                double hgt = container.ActualHeight;
                _dragRows.Add(new DragRow { Category = _categories[i], Container = container, Top = cumulative, Height = hgt });
                cumulative += hgt;
            }
        }

        private void DragVisual_PreviewDragOver(object sender, DragEventArgs e)
        {
        }

        private int GetDropIndexFromSnapshot(double mouseY)
        {
            if (_dragRows == null || _dragRows.Count == 0) return _categories.Count;

            for (int i = 0; i < _dragRows.Count; i++)
            {
                var row = _dragRows[i];
                double top = row.Top;
                double bottom = top + row.Height;

                if (mouseY < top) return i;
                if (mouseY < bottom)
                    return (mouseY - top) < (row.Height / 2) ? i : i + 1;
            }
            return _dragRows.Count;
        }

        private void DragVisual_GiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            e.UseDefaultCursors = false;
            Mouse.SetCursor(Cursors.Hand);
            e.Handled = true;

            if (_dragAdorner == null) return;

            if (GetCursorPos(out POINT screenPt))
            {
                Point local = icCategories.PointFromScreen(new Point(screenPt.X, screenPt.Y));

                _dragAdorner.UpdatePosition(local.X - _dragGrabOffset.X, local.Y - _dragGrabOffset.Y);

                int newDropIndex = GetDropIndexFromSnapshot(local.Y);
                if (newDropIndex != _lastLiveDropIndex)
                {
                    _lastLiveDropIndex = newDropIndex;
                    AnimateLiveReorder(newDropIndex);
                }
            }
        }

        private void EndDragVisual()
        {
            icCategories.PreviewDragOver -= DragVisual_PreviewDragOver;
            icCategories.PreviewGiveFeedback -= DragVisual_GiveFeedback;

            if (_dragAdornerLayer != null && _dragAdorner != null)
                _dragAdornerLayer.Remove(_dragAdorner);

            _dragAdorner = null;
            _dragAdornerLayer = null;

            for (int i = 0; i < _categories.Count; i++)
            {
                var container = icCategories.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;

                container.Opacity = 1.0;
                if (container.RenderTransform is TranslateTransform tt)
                {
                    tt.BeginAnimation(TranslateTransform.YProperty, null);
                    tt.Y = 0;
                }
            }

            _draggedHeaderElement = null;
            _draggedContainerElement = null;
            _dragRows = null;
            _draggedOriginalIndex = -1;
            _lastLiveDropIndex = -1;
        }

        private void AnimateLiveReorder(int dropIndex)
        {
            if (_draggedOriginalIndex < 0 || _dragRows == null || _dragRows.Count == 0) return;

            int a = _draggedOriginalIndex;
            if (a < 0 || a >= _dragRows.Count) return;

            int finalIndex = Math.Max(0, Math.Min(dropIndex, _dragRows.Count));
            if (a < finalIndex) finalIndex--;
            finalIndex = Math.Max(0, Math.Min(finalIndex, _dragRows.Count - 1));

            for (int i = 0; i < _dragRows.Count; i++)
            {
                if (i == a) continue;

                var container = _dragRows[i].Container;
                if (container == null) continue;
                double targetOffset = 0;

                if (a < finalIndex)
                {
                    if (i > a && i <= finalIndex)
                        targetOffset = -_draggedItemHeight;
                }
                else if (a > finalIndex)
                {
                    if (i >= finalIndex && i < a)
                        targetOffset = _draggedItemHeight;
                }

                AnimateContainerOffset(container, targetOffset);
            }
        }

        private void AnimateContainerOffset(FrameworkElement container, double targetY)
        {
            if (!(container.RenderTransform is TranslateTransform tt))
            {
                tt = new TranslateTransform();
                container.RenderTransform = tt;
            }

            var anim = new DoubleAnimation
            {
                To = targetY,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            tt.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        private void HardResetLiveReorderTransforms()
        {
            if (_dragRows == null) return;
            foreach (var row in _dragRows)
            {
                if (row.Container.RenderTransform is TranslateTransform tt)
                {
                    tt.BeginAnimation(TranslateTransform.YProperty, null);
                    tt.Y = 0;
                }
            }
        }

        private void icCategories_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(CategoryViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void icCategories_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(CategoryViewModel)))
                e.Effects = DragDropEffects.Move;
        }

        private void icCategories_DragLeave(object sender, DragEventArgs e) { }

        private void icCategories_Drop(object sender, DragEventArgs e)
        {
            if (!(e.Data.GetData(typeof(CategoryViewModel)) is CategoryViewModel dragged)) return;

            Point pos = e.GetPosition(icCategories);
            int dropIndex = GetDropIndexFromSnapshot(pos.Y);
            int oldIdx = _categories.IndexOf(dragged);

            HardResetLiveReorderTransforms();

            if (oldIdx < 0 || oldIdx == dropIndex)
            {
                e.Handled = true;
                return;
            }

            int newIdx = dropIndex;
            if (oldIdx < newIdx) newIdx--;
            if (newIdx < 0) newIdx = 0;
            if (newIdx >= _categories.Count) newIdx = _categories.Count - 1;

            if (oldIdx != newIdx)
                _categories.Move(oldIdx, newIdx);

            e.Handled = true;
        }

        private async Task LoadCategoryAsync(CategoryViewModel category)
        {
            category.IsLoading = true;
            try
            {
                if (category.Slugs != null && category.Slugs.Count > 0)
                {
                    var merged = new List<WikiEntry>();
                    var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var slug in category.Slugs)
                    {
                        var child = category.Children.FirstOrDefault(c =>
                            string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));

                        if (child != null)
                        {
                            child.IsLoading = true;
                            child.RaiseCountChanged();
                        }

                        List<WikiEntry> entries;
                        try
                        {
                            entries = await WikiScraper.ScrapeCategoryListAsync(slug, category.Type);
                        }
                        catch (Exception ex)
                        {
                            OnScraperLog($"FATAL ERROR scraping '{slug}' (part of '{category.Name}'): {ex.Message}");
                            if (child != null)
                            {
                                child.IsLoading = false;
                                child.RaiseCountChanged();
                            }
                            continue;
                        }

                        if (child != null)
                        {
                            foreach (var entry in entries)
                                entry.CategoryLabel = child.Name;

                            child.AllEntries = entries;
                            child.IsLoaded = true;
                            child.IsLoading = false;
                            child.ApplyFilter(txtSearch.Text);
                            child.RaiseCountChanged();
                        }

                        foreach (var entry in entries)
                        {
                            if (entry.WikiUrl != null && !seenUrls.Add(entry.WikiUrl)) continue;
                            merged.Add(entry);
                        }
                    }

                    category.AllEntries = merged;
                }
                else
                {
                    category.AllEntries = await WikiScraper.ScrapeCategoryListAsync(category.Slug, category.Type);
                    foreach (var entry in category.AllEntries)
                        entry.CategoryLabel = category.Name;
                }

                category.IsLoaded = true;
                category.ApplyFilter(txtSearch.Text);
            }
            catch (Exception ex)
            {
                OnScraperLog($"FATAL ERROR scraping '{category.Slug}': {ex.Message}");
            }
            finally
            {
                category.IsLoading = false;
                category.RaiseCountChanged();
            }
        }

        private void txtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void SearchDebounceTimer_Tick(object sender, EventArgs e)
        {
            _searchDebounceTimer.Stop();

            string search = txtSearch.Text;
            foreach (var category in _categories)
            {
                category.ApplyFilter(search);
            }
        }

        private void categoryScroll_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            UpdatePinnedCategory();
        }

        private void BuildCategoryHeaderMapIfNeeded()
        {
            if (_categoryHeaderMap.Count > 0) return;

            foreach (var category in _categories)
            {
                var container = icCategories.ItemContainerGenerator.ContainerFromItem(category) as DependencyObject;
                if (container == null) continue;

                var header = FindVisualChild<System.Windows.Controls.Button>(container);
                if (header != null)
                    _categoryHeaderMap.Add((category, header));
            }
        }

        private void UpdatePinnedCategory()
        {
            BuildCategoryHeaderMapIfNeeded();

            if (_categoryHeaderMap.Count == 0 || categoryScroll.VerticalOffset <= 1)
            {
                pinnedCategoryBar.Visibility = Visibility.Collapsed;
                return;
            }

            CategoryViewModel active = null;
            foreach (var (category, header) in _categoryHeaderMap)
            {
                Point pos;
                try
                {
                    pos = header.TransformToAncestor(categoryScroll).Transform(new Point(0, 0));
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (pos.Y <= 4)
                    active = category;
                else
                    break;
            }

            if (active != null)
            {
                txtPinnedCategory.Text = active.Name;
                pinnedCategoryBar.Visibility = Visibility.Visible;
            }
            else
            {
                pinnedCategoryBar.Visibility = Visibility.Collapsed;
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;

                var descendant = FindVisualChild<T>(child);
                if (descendant != null) return descendant;
            }
            return null;
        }

        private async void lstEntries_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (sender is System.Windows.Controls.ListBox lb && lb.SelectedItem is WikiEntry entry)
            {
                var container = lb.ItemContainerGenerator.ContainerFromItem(entry) as FrameworkElement;
                ShowRipple(container);

                _selected = entry;
                emptyState.Visibility = Visibility.Collapsed;

                await LoadDetail(entry);
            }
        }

        private static string GetStat(WikiEntry entry, string key)
        {
            if (entry.Stats == null) return null;
            foreach (var kv in entry.Stats)
            {
                if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
            }
            return null;
        }

        private async Task NavigateToEntry(string name, string url, WikiEntryType type = WikiEntryType.Item)
        {
            if (string.IsNullOrEmpty(url)) return;

            var target = new WikiEntry { Name = name, WikiUrl = url, Type = type };
            _selected = target;
            emptyState.Visibility = Visibility.Collapsed;

            await LoadDetail(target);

            CategoryViewModel matchedCategory = null;

            foreach (var c in _categories)
            {
                c.IsSelected = false;
                foreach (var child in c.Children)
                    child.IsSelected = false;
            }

            string slug = target.CategorySlug;
            if (string.IsNullOrEmpty(slug)) return;

            foreach (var c in _categories)
            {
                if (c.Slug != null && c.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase))
                {
                    c.IsSelected = true;
                    c.IsExpanded = true;
                    target.CategoryLabel = c.Name;
                    c.InjectEntry(target);
                    matchedCategory = c;
                    break;
                }

                foreach (var child in c.Children)
                {
                    if (child.Slug != null && child.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase))
                    {
                        child.IsSelected = true;
                        c.IsExpanded = true;
                        target.CategoryLabel = child.Name;
                        child.InjectEntry(target);
                        matchedCategory = child;
                        goto found;
                    }
                }
            }
            found:;

            if (matchedCategory != null)
                _ = Dispatcher.BeginInvoke(new Action(() => SelectItemInCategory(matchedCategory, target)));
        }

        private async Task LoadDetail(WikiEntry entry)
        {
            detailLoadingOverlay.Visibility = Visibility.Visible;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                await WikiScraper.ScrapeArmorDetailAsync(entry);
                if (_selected != entry) return;

                detailContent.Visibility = Visibility.Collapsed;
                locationDetailContent.Visibility = Visibility.Collapsed;
                monsterDetailContent.Visibility = Visibility.Collapsed;
                shopDetailContent.Visibility = Visibility.Collapsed;
                questDetailContent.Visibility = Visibility.Collapsed;
                npcDetailContent.Visibility = Visibility.Collapsed;
                enhancementDetailContent.Visibility = Visibility.Collapsed;

                if (entry.Type == WikiEntryType.Location)
                {
                    locationDetailContent.Visibility = Visibility.Visible;
                    LoadLocationDetail(entry);
                    return;
                }

                if (entry.Type == WikiEntryType.Monster)
                {
                    monsterDetailContent.Visibility = Visibility.Visible;
                    await LoadMonsterDetail(entry);
                    return;
                }

                if (entry.Type == WikiEntryType.Shop)
                {
                    shopDetailContent.Visibility = Visibility.Visible;
                    LoadShopDetail(entry);
                    return;
                }

                if (entry.Type == WikiEntryType.Quest)
                {
                    questDetailContent.Visibility = Visibility.Visible;
                    LoadQuestDetail(entry);
                    return;
                }

                if (entry.Type == WikiEntryType.NPC)
                {
                    npcDetailContent.Visibility = Visibility.Visible;
                    LoadNpcDetail(entry);
                    return;
                }

                if (entry.Type == WikiEntryType.Enhancement)
                {
                    enhancementDetailContent.Visibility = Visibility.Visible;
                    LoadEnhancementDetail(entry);
                    return;
                }

                detailContent.Visibility = Visibility.Visible;

                txtName.Text = WebUtility.HtmlDecode(entry.Name) ?? "Unknown";
                runBreadcrumbName.Text = txtName.Text;
                runBreadcrumbCategory.Text = string.IsNullOrEmpty(entry.CategoryLabel) ? "Items" : entry.CategoryLabel;

                icEventIcons.ItemsSource = entry.EventIconUrls;
                icEventIcons.Visibility = (entry.EventIconUrls != null && entry.EventIconUrls.Count > 0)
                    ? Visibility.Visible : Visibility.Collapsed;

                icTopIcons.ItemsSource = entry.TopIconUrls;

                string rarity = GetStat(entry, "Rarity");
                string rarityText = !string.IsNullOrEmpty(rarity) ? WebUtility.HtmlDecode(rarity) : "Common";
                runRarity.Text = rarityText;
                runRarity.Foreground = new SolidColorBrush(GetRarityColor(rarityText));

                RenderLinkedLines(locationsHost, entry.LocationLines);
                locationsHost.Visibility = (entry.LocationLines != null && entry.LocationLines.Count > 0)
                    ? Visibility.Visible : Visibility.Collapsed;

                string price = GetStat(entry, "Price");
                bool priceIsList = entry.PriceLines != null && entry.PriceLines.Count > 0;
                priceInline.Visibility = priceIsList ? Visibility.Collapsed : Visibility.Visible;
                priceHost.Visibility = priceIsList ? Visibility.Visible : Visibility.Collapsed;
                if (priceIsList)
                    RenderLinkedLines(priceHost, entry.PriceLines);
                else
                    runPrice.Text = !string.IsNullOrEmpty(price) ? WebUtility.HtmlDecode(price) : "—";

                bool sellbackHasOneLine = entry.SellbackLines != null && entry.SellbackLines.Count == 1;
                bool sellbackHasMultipleLines = entry.SellbackLines != null && entry.SellbackLines.Count > 1;

                if (sellbackHasOneLine)
                {
                    string line = entry.SellbackLines[0];
                    if (line.StartsWith("• "))
                        line = line.Substring(2);

                    runSellback.Text = line;
                    sellbackInline.Visibility = Visibility.Visible;
                    icSellback.Visibility = Visibility.Collapsed;
                }
                else
                {
                    sellbackInline.Visibility = Visibility.Collapsed;
                    icSellback.ItemsSource = entry.SellbackLines;
                    icSellback.Visibility = sellbackHasMultipleLines ? Visibility.Visible : Visibility.Collapsed;
                }

                runDescription.Text = !string.IsNullOrEmpty(entry.Description)
                    ? WebUtility.HtmlDecode(entry.Description)
                    : "(no description found)";

                RenderLinkedLines(notesHost, entry.NotesLines);
                notesHost.Visibility = (entry.NotesLines != null && entry.NotesLines.Count > 0)
                    ? Visibility.Visible : Visibility.Collapsed;
                await SetNotesIcon(imgNotesIcon, entry.NotesIconUrl);

                icSpecialEffects.ItemsSource = entry.SpecialEffectsLines;
                icSpecialEffects.Visibility = (entry.SpecialEffectsLines != null && entry.SpecialEffectsLines.Count > 0)
                    ? Visibility.Visible : Visibility.Collapsed;

                icClassSkills.ItemsSource = entry.SkillBlocks;
                icClassSkills.Visibility = (entry.SkillBlocks != null && entry.SkillBlocks.Count > 0)
                    ? Visibility.Visible : Visibility.Collapsed;

                bool hasFemale = !string.IsNullOrEmpty(entry.ImageUrlFemale);
                bool hasItemTabs = entry.ItemImageTabs != null && entry.ItemImageTabs.Count > 1;

                genderToggle.Visibility = hasFemale ? Visibility.Visible : Visibility.Collapsed;
                icItemImageTabs.Visibility = (!hasFemale && hasItemTabs) ? Visibility.Visible : Visibility.Collapsed;
                icItemImageTabs.ItemsSource = entry.ItemImageTabs;

                _currentGender = Gender.Male;
                UpdateGenderButtonStyle();

                txtImageUrl.Text = entry.ImageUrl ?? "(no image found)";
                await LoadImage(entry.ImageUrl);

                if (!hasFemale && hasItemTabs)
                {
                    string cat = entry.CategoryLabel ?? "";
                    bool isHouse = cat.Equals("Houses", StringComparison.OrdinalIgnoreCase);

                    if (isHouse && entry.ItemImageTabs.Count > 0)
                    {
                        imgArmor.Visibility = Visibility.Collapsed;
                        imgItemTab.Visibility = Visibility.Visible;
                        await LoadImageInto(imgItemTab, entry.ItemImageTabs[0].ImageUrl);
                        txtImageUrl.Text = entry.ItemImageTabs[0].ImageUrl ?? "(no image found)";
                    }

                    await Task.Delay(50);
                    var container = icItemImageTabs.ItemContainerGenerator.ContainerFromIndex(0);
                    if (container is Button firstButton)
                    {
                        firstButton.Background = (Brush)FindResource("Accent");
                        firstButton.Foreground = Brushes.White;
                    }
                }
            }
            finally
            {
                Mouse.OverrideCursor = null;
                detailLoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        // ===================== LOCATION DETAIL =====================

        private async void LoadLocationDetail(WikiEntry entry)
        {
            txtLocName.Text = WebUtility.HtmlDecode(entry.Name) ?? "Unknown";
            runLocBreadcrumbName.Text = txtLocName.Text;

            icLocTopIcons.ItemsSource = entry.TopIconUrls;
            icLocTopIcons.Visibility = (entry.TopIconUrls != null && entry.TopIconUrls.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            BindLinkList(icMonsters, entry.MonsterLines);
            BindLinkList(icNpcs, entry.NpcLines);
            BindLinkList(icQuests, entry.QuestLines);
            BindLinkList(icShops, entry.ShopLines);
            BindLinkList(icAccessPoints, entry.AccessPointLines);

            runMapName.Text = string.IsNullOrEmpty(entry.MapName) ? "—" : entry.MapName;
            runRoomLimit.Text = string.IsNullOrEmpty(entry.RoomLimit) ? "—" : entry.RoomLimit;

            RenderLinkedLines(locNotesHost, entry.NotesLines);
            locNotesHost.Visibility = (entry.NotesLines != null && entry.NotesLines.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;
            await SetNotesIcon(imgLocNotesIcon, entry.NotesIconUrl);

            linkedPreviewPanel.Visibility = Visibility.Collapsed;

            BuildScreenTabs(entry);
        }

        // ===================== MONSTER DETAIL =====================

        private async Task LoadMonsterDetail(WikiEntry entry)
        {
            txtMonsterName.Text = WebUtility.HtmlDecode(entry.Name) ?? "Unknown";
            runMonsterBreadcrumbName.Text = txtMonsterName.Text;

            icMonsterTopIcons.ItemsSource = entry.TopIconUrls;
            icMonsterTopIcons.Visibility = (entry.TopIconUrls != null && entry.TopIconUrls.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            if (entry.MonsterLocationLink != null && !string.IsNullOrEmpty(entry.MonsterLocationLink.Text))
            {
                btnMonsterLocation.Content = entry.MonsterLocationLink.Text;
                btnMonsterLocation.Tag = entry.MonsterLocationLink;
                btnMonsterLocation.IsEnabled = entry.MonsterLocationLink.HasLink;
                btnMonsterLocation.Visibility = Visibility.Visible;
            }
            else
            {
                btnMonsterLocation.Visibility = Visibility.Collapsed;
            }

            runMonsterLevel.Text = GetStat(entry, "Level") ?? "—";
            runMonsterDifficulty.Text = GetStat(entry, "Difficulty") ?? "—";
            runMonsterTotalHp.Text = GetStat(entry, "Total HP") ?? "—";

            icBaseStats.ItemsSource = entry.BaseStatsLines;
            icBaseStats.Visibility = (entry.BaseStatsLines != null && entry.BaseStatsLines.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            icAttacks.ItemsSource = entry.AttackLines;
            icAttacks.Visibility = (entry.AttackLines != null && entry.AttackLines.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            icPhaseGroups.ItemsSource = entry.PhaseGroups;
            icPhaseGroups.Visibility = (entry.PhaseGroups != null && entry.PhaseGroups.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            BindLinkList(icTempItemsDropped, entry.TempItemsDroppedLines);

            icItemsDropped.ItemsSource = entry.ItemsDroppedLines;
            icItemsDropped.Visibility = (entry.ItemsDroppedLines != null && entry.ItemsDroppedLines.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            RenderLinkedLines(monsterNotesHost, entry.NotesLines);
            monsterNotesHost.Visibility = (entry.NotesLines != null && entry.NotesLines.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;
            await SetNotesIcon(imgMonsterNotesIcon, entry.NotesIconUrl);

            monsterLinkedPreviewPanel.Visibility = Visibility.Collapsed;

            icMonsterImageTabs.ItemsSource = entry.MonsterImageTabs;
            icMonsterImageTabs.Visibility = (entry.MonsterImageTabs != null && entry.MonsterImageTabs.Count > 1)
                ? Visibility.Visible : Visibility.Collapsed;

            txtMonsterImageUrl.Text = entry.ImageUrl ?? "(no image found)";
            imgMonster.Source = null;
            await LoadImageInto(imgMonster, entry.ImageUrl);

            if (entry.MonsterImageTabs != null && entry.MonsterImageTabs.Count > 1)
            {
                await Task.Delay(50);

                var container = icMonsterImageTabs.ItemContainerGenerator.ContainerFromIndex(0);
                if (container != null)
                {
                    var firstButton = FindVisualChild<Button>(container);
                    if (firstButton != null)
                    {
                        firstButton.Background = (Brush)FindResource("Accent");
                        firstButton.Foreground = Brushes.White;
                    }
                }
            }
        }

        // ===================== SHOP DETAIL =====================

        private void LoadShopDetail(WikiEntry entry)
        {
            txtShopName.Text = WebUtility.HtmlDecode(entry.Name) ?? "Unknown";
            runShopBreadcrumbName.Text = txtShopName.Text;
            runShopBreadcrumbCategory.Text = string.IsNullOrEmpty(entry.CategoryLabel) ? "Shops" : entry.CategoryLabel;

            icShopTopIcons.ItemsSource = entry.TopIconUrls;
            icShopTopIcons.Visibility = (entry.TopIconUrls != null && entry.TopIconUrls.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            BindLinkList(icShopLocation, entry.ShopLocationLinks);

            shopLinkedPreviewPanel.Visibility = Visibility.Collapsed;

            BuildShopTabs(entry);
        }

        private void BuildShopTabs(WikiEntry entry)
        {
            _currentShopTabs = entry.ShopTabs ?? new List<ShopTab>();
            shopTabsPanel.Children.Clear();
            shopTablePanel.Children.Clear();
            _currentShopTabIndex = 0;

            shopTabsPanel.Visibility = (_currentShopTabs.Count > 1) ? Visibility.Visible : Visibility.Collapsed;

            if (_currentShopTabs.Count == 0)
            {
                shopTableHeader.Visibility = Visibility.Collapsed;
                shopTablePanel.Children.Add(new TextBlock
                {
                    Text = "(no items found on this shop page)",
                    Foreground = (Brush)FindResource("WikiMuted"),
                    FontSize = 12,
                    Margin = new Thickness(4, 10, 0, 0)
                });
                return;
            }

            shopTableHeader.Visibility = Visibility.Visible;

            for (int i = 0; i < _currentShopTabs.Count; i++)
            {
                var btn = new Button
                {
                    Content = _currentShopTabs[i].Label,
                    Tag = i,
                    Style = (Style)FindResource("WikiTabButton"),
                    Margin = new Thickness(0, 0, 3, 4)
                };
                btn.Click += ShopTab_Click;
                shopTabsPanel.Children.Add(btn);
            }

            RenderShopTabStyles();
            RenderCurrentShopTab();
        }

        private void ShopTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int idx)
            {
                _currentShopTabIndex = idx;
                RenderShopTabStyles();
                RenderCurrentShopTab();
            }
        }

        private void RenderShopTabStyles()
        {
            var activeBg = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48));
            var idleBg = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2C));
            var idleText = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3));

            for (int i = 0; i < shopTabsPanel.Children.Count; i++)
            {
                if (shopTabsPanel.Children[i] is Button btn)
                {
                    bool active = i == _currentShopTabIndex;
                    btn.Background = active ? activeBg : idleBg;
                    btn.Foreground = active ? Brushes.White : idleText;
                }
            }
        }

        private void RenderCurrentShopTab()
        {
            shopTablePanel.Children.Clear();
            if (_currentShopTabIndex < 0 || _currentShopTabIndex >= _currentShopTabs.Count) return;

            var tab = _currentShopTabs[_currentShopTabIndex];

            if (tab.Rows == null || tab.Rows.Count == 0)
            {
                shopTablePanel.Children.Add(new TextBlock
                {
                    Text = "(no items found in this category)",
                    Foreground = (Brush)FindResource("WikiMuted"),
                    FontSize = 12,
                    Margin = new Thickness(4, 10, 0, 0)
                });
                return;
            }

            foreach (var row in tab.Rows)
            {
                var rowBorder = new Border
                {
                    BorderBrush = (Brush)FindResource("Hairline"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(0, 10, 0, 10)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "ShopNameCol" });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var iconImg = new Image
                {
                    Height = 28,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(iconImg, 0);
                grid.Children.Add(iconImg);
                if (!string.IsNullOrEmpty(row.IconUrl))
                    _ = LoadImageInto(iconImg, row.IconUrl);

                var nameText = new TextBlock
                {
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };

                bool hasNameLink = row.NameLink?.HasLink ?? false;
                string nameStr = row.NameLink?.Text ?? "";

                if (hasNameLink)
                {
                    var hyperlink = new Hyperlink(new Run(nameStr))
                    {
                        Foreground = (Brush)FindResource("WikiLink"),
                        Tag = row.NameLink
                    };
                    hyperlink.TextDecorations = null;
                    hyperlink.Click += ShopItemHyperlink_Click;
                    nameText.Inlines.Add(hyperlink);
                }
                else
                {
                    nameText.Inlines.Add(new Run(nameStr) { Foreground = (Brush)FindResource("WikiText") });
                }

                if (row.NameLink?.IconUrls != null)
                {
                    foreach (var iconUrl in row.NameLink.IconUrls)
                    {
                        var badgeImg = new Image
                        {
                            Height = 13,
                            Stretch = Stretch.Uniform,
                            Margin = new Thickness(3, 0, 0, 0)
                        };
                        var container = new InlineUIContainer(badgeImg) { BaselineAlignment = BaselineAlignment.Center };
                        nameText.Inlines.Add(new Run(" "));
                        nameText.Inlines.Add(container);
                        _ = LoadImageInto(badgeImg, iconUrl);
                    }
                }

                Grid.SetColumn(nameText, 1);
                grid.Children.Add(nameText);

                var pricePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };

                if (row.PriceItems != null)
                {
                    for (int i = 0; i < row.PriceItems.Count; i++)
                    {
                        var priceItem = row.PriceItems[i];
                        bool isLast = i == row.PriceItems.Count - 1;

                        if (priceItem.HasLink)
                        {
                            var priceBtn = new Button
                            {
                                Content = priceItem.Text + (isLast ? "" : ","),
                                Tag = priceItem,
                                Style = (Style)FindResource("WikiLinkButton"),
                                Padding = new Thickness(0, 1, 0, 1),
                                HorizontalAlignment = HorizontalAlignment.Left
                            };
                            priceBtn.Click += ShopItemLink_Click;
                            pricePanel.Children.Add(priceBtn);
                        }
                        else
                        {
                            pricePanel.Children.Add(new TextBlock
                            {
                                Text = priceItem.Text + (isLast ? "" : ","),
                                Foreground = (Brush)FindResource("WikiText"),
                                FontSize = 13,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(4, 1, 0, 1),
                                HorizontalAlignment = HorizontalAlignment.Left

                            });
                        }
                    }
                }
                Grid.SetColumn(pricePanel, 3);
                grid.Children.Add(pricePanel);

                rowBorder.Child = grid;
                shopTablePanel.Children.Add(rowBorder);
            }
        }

        private async void ShopLocationLink_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is WikiLinkItem link) || !link.HasLink) return;
            await NavigateToEntry(link.Text, link.Url);
        }

        private async void ShopItemLink_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is WikiLinkItem link) || !link.HasLink) return;
            await NavigateToEntry(link.Text, link.Url);
        }

        private async void ShopItemHyperlink_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Hyperlink link) || !(link.Tag is WikiLinkItem item) || !item.HasLink) return;
            await NavigateToEntry(item.Text, item.Url);
        }

        private async void MonsterLink_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is WikiLinkItem link) || !link.HasLink) return;
            await NavigateToEntry(link.Text, link.Url);
        }

        private void SkillBlockHeader_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is SkillBlock block)) return;

            block.IsExpanded = !block.IsExpanded;
        }

        private async void MonsterImageTab_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string imageUrl)) return;

            var parent = VisualTreeHelper.GetParent(btn);
            while (parent != null && !(parent is ItemsControl))
                parent = VisualTreeHelper.GetParent(parent);

            if (parent is ItemsControl itemsControl)
            {
                foreach (var item in itemsControl.Items)
                {
                    var container = itemsControl.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
                    if (container != null)
                    {
                        var button = FindVisualChild<Button>(container);
                        if (button != null)
                        {
                            button.Background = (Brush)FindResource("WikiTabIdleBg");
                            button.Foreground = (Brush)FindResource("WikiTabIdleText");
                        }
                    }
                }
            }

            btn.Background = (Brush)FindResource("Accent");
            btn.Foreground = Brushes.White;

            await LoadImageInto(imgMonster, imageUrl);
            txtMonsterImageUrl.Text = imageUrl ?? "(no image found)";
        }

        private async Task SetNotesIcon(System.Windows.Controls.Image target, string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                target.Visibility = Visibility.Collapsed;
                target.Source = null;
                return;
            }

            target.Visibility = Visibility.Visible;
            await LoadImageInto(target, url);
        }

        private static void BindLinkList(System.Windows.Controls.ItemsControl host, List<WikiLinkItem> items)
        {
            host.ItemsSource = items;
            host.Visibility = (items != null && items.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void LocationLink_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is WikiLinkItem link) || !link.HasLink) return;
            await NavigateToEntry(link.Text, link.Url);
        }

        // ===================== SCREEN TABS =====================

        private void BuildScreenTabs(WikiEntry entry)
        {
            _currentScreens = entry.Screens ?? new List<ScreenInfo>();
            screenTabsPanel.Children.Clear();
            screenImagesPanel.Children.Clear();
            _currentScreenIndex = 0;

            bool hasScreens = _currentScreens.Count > 0;
            screensSection.Visibility = hasScreens ? Visibility.Visible : Visibility.Collapsed;
            if (!hasScreens) return;

            int count = _currentScreens.Count;

            double fontSize;
            Thickness padding;
            Thickness margin = new Thickness(0, 0, 3, 4);

            if (count <= 6)
            {
                fontSize = 12;
                padding = new Thickness(16, 7, 16, 7);
            }
            else if (count <= 10)
            {
                fontSize = 11;
                padding = new Thickness(12, 6, 12, 6);
            }
            else if (count <= 16)
            {
                fontSize = 10;
                padding = new Thickness(9, 5, 9, 5);
            }
            else
            {
                fontSize = 9;
                padding = new Thickness(7, 4, 7, 4);
            }

            for (int i = 0; i < count; i++)
            {
                var btn = new System.Windows.Controls.Button
                {
                    Content = _currentScreens[i].Label,
                    Tag = i,
                    Style = (Style)FindResource("WikiTabButton"),
                    FontSize = fontSize,
                    Padding = padding,
                    Margin = margin
                };
                btn.Click += ScreenTab_Click;
                screenTabsPanel.Children.Add(btn);
            }

            RenderScreenTabStyles();
            RenderCurrentScreen();
        }

        private void ScreenTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn && btn.Tag is int idx)
            {
                _currentScreenIndex = idx;
                RenderScreenTabStyles();
                RenderCurrentScreen();
            }
        }

        private void RenderScreenTabStyles()
        {
            var activeBg = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48));
            var idleBg = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2C));
            var idleText = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3));

            for (int i = 0; i < screenTabsPanel.Children.Count; i++)
            {
                if (screenTabsPanel.Children[i] is System.Windows.Controls.Button btn)
                {
                    bool active = i == _currentScreenIndex;
                    btn.Background = active ? activeBg : idleBg;
                    btn.Foreground = active ? Brushes.White : idleText;
                }
            }
        }

        private void RenderCurrentScreen()
        {
            screenImagesPanel.Children.Clear();
            if (_currentScreenIndex < 0 || _currentScreenIndex >= _currentScreens.Count) return;

            var screen = _currentScreens[_currentScreenIndex];

            if (screen.NoteLines != null && screen.NoteLines.Count > 0)
            {
                if (screen.NoteLines.Count == 1)
                {
                    screenImagesPanel.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = "Note: " + screen.NoteLines[0],
                        Foreground = (Brush)FindResource("WikiMuted"),
                        FontStyle = FontStyles.Italic,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 10)
                    });
                }
                else
                {
                    screenImagesPanel.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = "Notes:",
                        Foreground = (Brush)FindResource("WikiMuted"),
                        FontFamily = new FontFamily("Segoe UI Semibold"),
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 4)
                    });

                    foreach (var line in screen.NoteLines)
                    {
                        screenImagesPanel.Children.Add(new System.Windows.Controls.TextBlock
                        {
                            Text = "• " + line,
                            Foreground = (Brush)FindResource("WikiMuted"),
                            FontStyle = FontStyles.Italic,
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap,
                            Margin = new Thickness(4, 0, 0, 4)
                        });
                    }

                    screenImagesPanel.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Height = 6
                    });
                }
            }

            if (screen.Images == null || screen.Images.Count == 0)
            {
                screenImagesPanel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "(no image found for this screen)",
                    Foreground = (Brush)FindResource("WikiMuted"),
                    FontSize = 12
                });
                return;
            }

            for (int i = 0; i < screen.Images.Count; i++)
            {
                var sub = screen.Images[i];

                var img = new System.Windows.Controls.Image
                {
                    Stretch = Stretch.Uniform,
                    MaxWidth = 560,
                    MaxHeight = 420,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 4, 0, 12)
                };

                if (string.IsNullOrEmpty(sub.Label))
                {
                    screenImagesPanel.Children.Add(img);
                    _ = LoadImageInto(img, sub.ImageUrl);
                    continue;
                }

                bool startExpanded = i == 0;
                img.Visibility = startExpanded ? Visibility.Visible : Visibility.Collapsed;
                bool imageLoaded = false;

                var header = new System.Windows.Controls.TextBlock
                {
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = 12,
                    Foreground = (Brush)FindResource("WikiLink"),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 10, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    Text = (startExpanded ? "− " : "+ ") + sub.Label
                };

                header.MouseLeftButtonUp += async (s, e) =>
                {
                    bool expand = img.Visibility != Visibility.Visible;
                    img.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
                    header.Text = (expand ? "− " : "+ ") + sub.Label;

                    if (expand && !imageLoaded)
                    {
                        imageLoaded = true;
                        await LoadImageInto(img, sub.ImageUrl);
                    }
                };

                screenImagesPanel.Children.Add(header);
                screenImagesPanel.Children.Add(img);

                if (startExpanded)
                {
                    imageLoaded = true;
                    _ = LoadImageInto(img, sub.ImageUrl);
                }
            }
        }

        // ===================== QUEST DETAIL =====================

        private void LoadQuestDetail(WikiEntry entry)
        {
            txtQuestName.Text = WebUtility.HtmlDecode(entry.Name) ?? "Unknown";
            runQuestBreadcrumbName.Text = txtQuestName.Text;

            icQuestEventIcons.ItemsSource = entry.EventIconUrls;
            icQuestEventIcons.Visibility = (entry.EventIconUrls != null && entry.EventIconUrls.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            icQuestTopIcons.ItemsSource = entry.TopIconUrls;
            icQuestTopIcons.Visibility = (entry.TopIconUrls != null && entry.TopIconUrls.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            if (entry.QuestJumpLinks != null && entry.QuestJumpLinks.Count > 0)
            {
                txtQuestJumpTo.Text = "Jump to: " + string.Join(", ", entry.QuestJumpLinks);
                txtQuestJumpTo.Visibility = Visibility.Visible;
            }
            else
            {
                txtQuestJumpTo.Visibility = Visibility.Collapsed;
            }

            questLinkedPreviewPanel.Visibility = Visibility.Collapsed;

            RenderQuestGroups(entry.QuestGroups ?? new List<QuestGroup>());
        }

        private void RenderQuestGroups(List<QuestGroup> groups)
        {
            questGroupsPanel.Children.Clear();

            if (groups.Count == 0)
            {
                questGroupsPanel.Children.Add(new TextBlock
                {
                    Text = "(no quest data found on this page)",
                    Foreground = (Brush)FindResource("WikiMuted"),
                    FontSize = 12
                });
                return;
            }

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var groupPanel = new StackPanel { Margin = new Thickness(0, g == 0 ? 0 : 24, 0, 0) };

                if (g > 0)
                {
                    groupPanel.Children.Add(new Border
                    {
                        BorderBrush = (Brush)FindResource("WikiRule"),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                        Margin = new Thickness(0, 0, 0, 16)
                    });
                }

                if (group.LocationLines.Count > 0)
                {
                    groupPanel.Children.Add(MakeQuestFieldHeader("Quest Location:"));
                    foreach (var loc in group.LocationLines)
                    {
                        var line = MakeQuestLinkOrText(loc);
                        line.Margin = new Thickness(4, 0, 0, 2);
                        groupPanel.Children.Add(line);
                    }
                }

                if (group.BegunFromLink != null && !string.IsNullOrEmpty(group.BegunFromLink.Text))
                {
                    var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                    line.Children.Add(new TextBlock
                    {
                        Text = "Quests Begun From: ",
                        FontFamily = new FontFamily("Segoe UI Semibold"),
                        FontSize = 13,
                        Foreground = (Brush)FindResource("WikiText"),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    line.Children.Add(MakeQuestLinkOrText(group.BegunFromLink));
                    groupPanel.Children.Add(line);
                }

                if (group.RequirementLines.Count > 0)
                {
                    groupPanel.Children.Add(MakeQuestFieldHeader("Requirements:"));
                    foreach (var r in group.RequirementLines)
                        groupPanel.Children.Add(MakeQuestBulletLine(r));
                }

                if (group.NoteLines.Count > 0)
                {
                    groupPanel.Children.Add(MakeQuestFieldHeader("Note:"));
                    foreach (var n in group.NoteLines)
                        groupPanel.Children.Add(MakeQuestBulletLine(n));
                }

                if (group.Tabs.Count > 0)
                {
                    var tabStrip = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
                    var contentHost = new ContentControl();
                    var tabButtons = new List<Button>();

                    void RenderTab(int idx)
                    {
                        var activeBg = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48));
                        var idleBg = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2C));
                        var idleText = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3));

                        for (int i = 0; i < tabButtons.Count; i++)
                        {
                            tabButtons[i].Background = i == idx ? activeBg : idleBg;
                            tabButtons[i].Foreground = i == idx ? Brushes.White : idleText;
                        }

                        contentHost.Content = BuildQuestTabContent(group.Tabs[idx]);
                    }

                    for (int t = 0; t < group.Tabs.Count; t++)
                    {
                        int idx = t;
                        var btn = new Button
                        {
                            Content = group.Tabs[t].Label,
                            Style = (Style)FindResource("WikiTabButton"),
                            Margin = new Thickness(0, 0, 3, 4)
                        };
                        btn.Click += (s, e) => RenderTab(idx);
                        tabButtons.Add(btn);
                        tabStrip.Children.Add(btn);
                    }

                    groupPanel.Children.Add(tabStrip);

                    var contentBorder = new Border
                    {
                        Background = (Brush)FindResource("WikiTabIdleBg"),
                        BorderBrush = (Brush)FindResource("WikiRule"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(0, 5, 5, 5),
                        Padding = new Thickness(14)
                    };
                    contentBorder.Child = contentHost;
                    groupPanel.Children.Add(contentBorder);

                    RenderTab(0);
                }

                questGroupsPanel.Children.Add(groupPanel);
            }
        }

        private StackPanel BuildQuestTabContent(QuestTabInfo tab)
        {
            var panel = new StackPanel();

            if (tab.TopIconUrls.Count > 0)
            {
                var iconRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var url in tab.TopIconUrls)
                {
                    var img = new Image { Height = 34, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0) };
                    iconRow.Children.Add(img);
                    _ = LoadImageInto(img, url);
                }
                panel.Children.Add(iconRow);
            }

            if (tab.RequirementLines.Count > 0)
            {
                panel.Children.Add(MakeQuestFieldHeader("Requirements:"));
                foreach (var r in tab.RequirementLines)
                    panel.Children.Add(MakeQuestBulletLine(r));
            }

            if (!string.IsNullOrEmpty(tab.Description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = tab.Description,
                    FontSize = 13,
                    Foreground = (Brush)FindResource("WikiText"),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 19,
                    Margin = new Thickness(0, 8, 0, 0)
                });
            }

            if (!string.IsNullOrEmpty(tab.CompletionText))
            {
                bool expanded = false;
                var header = new TextBlock
                {
                    Text = "+  When Completed",
                    FontFamily = new FontFamily("Segoe UI Semibold"),
                    FontSize = 12.5,
                    Foreground = (Brush)FindResource("Accent"),
                    Cursor = Cursors.Hand,
                    Margin = new Thickness(0, 10, 0, 0)
                };
                var body = new TextBlock
                {
                    Text = tab.CompletionText,
                    FontSize = 12.5,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("WikiMuted"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4, 4, 0, 0),
                    Visibility = Visibility.Collapsed
                };
                header.MouseLeftButtonUp += (s, e) =>
                {
                    expanded = !expanded;
                    body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                    header.Text = (expanded ? "−  " : "+  ") + "When Completed";
                };
                panel.Children.Add(header);
                panel.Children.Add(body);
            }

            if (tab.ItemsRequired.Count > 0)
            {
                panel.Children.Add(MakeQuestFieldHeader("Items Required:"));
                foreach (var item in tab.ItemsRequired)
                {
                    panel.Children.Add(MakeQuestLinkOrTextBullet(new QuestSubLine { Text = item.Text, Url = item.Url }));
                    foreach (var sub in item.SubLines)
                    {
                        var subEl = MakeQuestLinkOrText(sub);
                        subEl.Margin = new Thickness(20, 0, 0, 2);
                        panel.Children.Add(subEl);
                    }
                }
            }

            if (tab.RewardLines.Count > 0)
            {
                panel.Children.Add(MakeQuestFieldHeader("Rewards:"));
                foreach (var r in tab.RewardLines)
                    panel.Children.Add(MakeQuestLinkOrTextBullet(new QuestSubLine { Text = r.Text, Url = r.Url }));
            }

            if (tab.ItemRewards.Count > 0)
            {
                panel.Children.Add(MakeQuestFieldHeader((tab.ItemRewardsLabel ?? "Items") + ":"));
                foreach (var ir in tab.ItemRewards)
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 2, 0, 2) };
                    row.Children.Add(MakeQuestLinkOrText(new QuestSubLine { Text = ir.Text, Url = ir.Url }));
                    foreach (var iconUrl in ir.IconUrls)
                    {
                        var img = new Image { Height = 13, Stretch = Stretch.Uniform, Margin = new Thickness(3, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                        row.Children.Add(img);
                        _ = LoadImageInto(img, iconUrl);
                    }
                    panel.Children.Add(row);
                }
            }

            if (!string.IsNullOrEmpty(tab.ThanksLine))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = tab.ThanksLine,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = (Brush)FindResource("WikiMuted"),
                    Margin = new Thickness(0, 10, 0, 0)
                });
            }

            return panel;
        }

        private TextBlock MakeQuestFieldHeader(string text) => new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI Semibold"),
            FontSize = 13,
            Foreground = (Brush)FindResource("WikiText"),
            Margin = new Thickness(0, 8, 0, 3)
        };

        private TextBlock MakeQuestBulletLine(string text) => new TextBlock
        {
            Text = "• " + text,
            FontSize = 13,
            Foreground = (Brush)FindResource("WikiText"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 0, 0, 2),
            LineHeight = 18
        };

        private Hyperlink MakeQuestHyperlink(QuestSubLine line)
        {
            var hyperlink = new Hyperlink(new Run(line.Text))
            {
                Foreground = (Brush)FindResource("WikiLink"),
                Tag = line
            };
            hyperlink.TextDecorations = null;
            hyperlink.Click += QuestLink_Click;
            return hyperlink;
        }

        /// <summary>Compact inline line — no bullet, used for Quest Location / Quests Begun From.</summary>
        private TextBlock MakeQuestLinkOrText(QuestSubLine line)
        {
            var tb = new TextBlock
            {
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 12, 1),
                VerticalAlignment = VerticalAlignment.Center
            };

            if (line.HasLink)
                tb.Inlines.Add(MakeQuestHyperlink(line));
            else
                tb.Inlines.Add(new Run(line.Text) { Foreground = (Brush)FindResource("WikiText") });

            return tb;
        }

        /// <summary>Compact bulleted inline line — used for Items Required / Rewards / sub-lines,
        /// matching the wiki's own flat "• Link text xN" single-line list rows.</summary>
        private TextBlock MakeQuestLinkOrTextBullet(QuestSubLine line)
        {
            var tb = new TextBlock
            {
                FontSize = 13,
                Foreground = (Brush)FindResource("WikiText"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 0, 0, 2),
                LineHeight = 18
            };

            tb.Inlines.Add(new Run("• "));

            if (line.HasLink)
                tb.Inlines.Add(MakeQuestHyperlink(line));
            else
                tb.Inlines.Add(new Run(line.Text));

            return tb;
        }

        private async void QuestLink_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Hyperlink link) || !(link.Tag is QuestSubLine questLink) || !questLink.HasLink) return;
            await NavigateToEntry(questLink.Text, questLink.Url);
        }

        // ===================== NPC DETAIL =====================

        private async void LoadNpcDetail(WikiEntry entry)
        {
            txtNpcName.Text = WebUtility.HtmlDecode(entry.Name) ?? "Unknown";
            runNpcBreadcrumbName.Text = txtNpcName.Text;

            icNpcEventIcons.ItemsSource = entry.EventIconUrls;
            icNpcEventIcons.Visibility = (entry.EventIconUrls != null && entry.EventIconUrls.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            icNpcTopIcons.ItemsSource = entry.TopIconUrls;
            icNpcTopIcons.Visibility = (entry.TopIconUrls != null && entry.TopIconUrls.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            BuildNpcDialogueTabs(entry.NpcDialogueTabs ?? new List<NpcDialogueTab>());

            BindLinkList(icNpcLocation, entry.NpcLocationLinks);

            RenderLinkedLines(npcNotesHost, entry.NotesLines);
            npcNotesHost.Visibility = (entry.NotesLines != null && entry.NotesLines.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;
            await SetNotesIcon(imgNpcNotesIcon, entry.NotesIconUrl);

            npcLinkedPreviewPanel.Visibility = Visibility.Collapsed;

            bool hasScreens = entry.Screens != null && entry.Screens.Count > 0;
            npcScreensSection.Visibility = hasScreens ? Visibility.Visible : Visibility.Collapsed;
            npcSingleImagePanel.Visibility = (!hasScreens && !string.IsNullOrEmpty(entry.ImageUrl))
                ? Visibility.Visible : Visibility.Collapsed;

            if (hasScreens)
            {
                BuildNpcScreenTabs(entry);
            }
            else if (!string.IsNullOrEmpty(entry.ImageUrl))
            {
                imgNpcSingle.Source = null;
                await LoadImageInto(imgNpcSingle, entry.ImageUrl);
            }

            if (!string.IsNullOrEmpty(entry.ThanksLine))
            {
                txtNpcThanks.Text = entry.ThanksLine;
                txtNpcThanks.Visibility = Visibility.Visible;
            }
            else
            {
                txtNpcThanks.Visibility = Visibility.Collapsed;
            }
        }

        private void LoadEnhancementDetail(WikiEntry entry)
        {
            txtEnhancementName.Text = WebUtility.HtmlDecode(entry.Name) ?? "Unknown";
            runEnhancementBreadcrumbName.Text = txtEnhancementName.Text;

            icEnhancementEventIcons.ItemsSource = entry.EventIconUrls;
            icEnhancementEventIcons.Visibility = (entry.EventIconUrls != null && entry.EventIconUrls.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            icEnhancementTopIcons.ItemsSource = entry.TopIconUrls;
            icEnhancementTopIcons.Visibility = (entry.TopIconUrls != null && entry.TopIconUrls.Count > 0)
                ? Visibility.Visible : Visibility.Collapsed;

            BindLinkList(icEnhancementLocation, entry.EnhancementLocationLinks);

            RenderEnhancementGroups(entry.EnhancementGroups ?? new List<EnhancementGroup>());
        }

        private void RenderEnhancementGroups(List<EnhancementGroup> groups)
        {
            enhancementGroupsPanel.Children.Clear();

            foreach (var group in groups)
            {
                var container = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

                var header = new Button
                {
                    Tag = group,
                    Cursor = Cursors.Hand,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(0),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0)
                };
                header.Click += EnhancementGroupHeader_Click;

                var headerBorder = new Border
                {
                    Background = (Brush)FindResource("Raised"),
                    BorderBrush = (Brush)FindResource("Hairline"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(10, 8, 10, 8),
                    Child = new TextBlock
                    {
                        FontFamily = new FontFamily("Segoe UI Semibold"),
                        FontSize = 12.5,
                        Foreground = (Brush)FindResource("Accent"),
                        TextWrapping = TextWrapping.Wrap
                    }
                };
                ((TextBlock)headerBorder.Child).Inlines.Add(new Run(group.ChevronPrefix));
                ((TextBlock)headerBorder.Child).Inlines.Add(new Run(group.HeaderTitle));
                header.Content = headerBorder;
                container.Children.Add(header);

                var contentBorder = new Border
                {
                    Background = (Brush)FindResource("WikiTabIdleBg"),
                    BorderBrush = (Brush)FindResource("Hairline"),
                    BorderThickness = new Thickness(1, 0, 1, 1),
                    CornerRadius = new CornerRadius(0, 0, 5, 5),
                    Padding = new Thickness(12),
                    Visibility = group.IsExpanded ? Visibility.Visible : Visibility.Collapsed
                };
                var contentStack = new StackPanel();
                contentBorder.Child = contentStack;

                foreach (var note in group.NoteLines)
                {
                    contentStack.Children.Add(new TextBlock
                    {
                        Text = note,
                        FontSize = 11.5,
                        FontStyle = FontStyles.Italic,
                        Foreground = (Brush)FindResource("TextMid"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                }

                if (!string.IsNullOrEmpty(group.TableHeader) || group.Rows.Count > 0)
                {
                    if (!string.IsNullOrEmpty(group.TableHeader))
                    {
                        contentStack.Children.Add(new TextBlock
                        {
                            Text = group.TableHeader,
                            FontFamily = new FontFamily("Segoe UI Semibold"),
                            FontSize = 12,
                            Foreground = (Brush)FindResource("TextMid"),
                            Margin = new Thickness(0, 0, 0, 6)
                        });
                    }

                    var table = BuildEnhancementTable(group);
                    if (table != null)
                        contentStack.Children.Add(table);
                }

                foreach (var desc in group.SkillDescriptionLines)
                {
                    contentStack.Children.Add(new TextBlock
                    {
                        Text = desc,
                        FontSize = 12,
                        Foreground = (Brush)FindResource("WikiText"),
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 18,
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }

                container.Children.Add(contentBorder);
                enhancementGroupsPanel.Children.Add(container);
            }
        }

        private FrameworkElement BuildEnhancementTable(EnhancementGroup group)
        {
            if (group.Rows.Count == 0) return null;

            var outerBorder = new Border
            {
                Background = (Brush)FindResource("Surface"),
                BorderBrush = (Brush)FindResource("Hairline"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 0, 8)
            };

            var outerStack = new StackPanel();
            outerBorder.Child = outerStack;

            var columns = group.ColumnHeaders;
            bool hasHeaders = columns.Count > 0;

            var tableGrid = new Grid();
            for (int i = 0; i < 8; i++)
            {
                tableGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = (i == 0 || i == 7) ? new GridLength(65) : new GridLength(50)
                });
            }

            if (hasHeaders)
            {
                var headerBd = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x33)),
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    Padding = new Thickness(10, 7, 10, 7)
                };
                var headerGrid = new Grid();
                for (int i = 0; i < columns.Count && i < 8; i++)
                {
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = (i == 0 || i == 7) ? new GridLength(65) : new GridLength(50)
                    });
                    headerGrid.Children.Add(new TextBlock
                    {
                        Text = columns[i],
                        FontFamily = new FontFamily("Segoe UI Semibold"),
                        FontSize = 11,
                        Foreground = (Brush)FindResource("TextMid"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    });
                    Grid.SetColumn(headerGrid.Children[headerGrid.Children.Count - 1], i);
                }
                headerBd.Child = headerGrid;
                outerStack.Children.Add(headerBd);

                var separatorBorder = new Border
                {
                    Height = 1,
                    Background = (Brush)FindResource("Hairline")
                };
                Grid.SetRow(separatorBorder, 0);
                Grid.SetColumnSpan(separatorBorder, 8);
                outerStack.Children.Add(separatorBorder);
            }

            foreach (var row in group.Rows)
            {
                var rowBd = new Border
                {
                    BorderBrush = (Brush)FindResource("Hairline"),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(4, 5, 4, 5)
                };
                var rowGrid = new Grid();
                for (int i = 0; i < 8; i++)
                {
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = (i == 0 || i == 7) ? new GridLength(65) : new GridLength(50)
                    });
                }

                string[] vals = { row.Level, row.STR, row.INT, row.DEX, row.WIS, row.END, row.LUK, row.Cost };
                for (int i = 0; i < vals.Length; i++)
                {
                    var txt = new TextBlock
                    {
                        Text = vals[i] ?? "—",
                        FontSize = 11.5,
                        Foreground = (Brush)FindResource("WikiText"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    };
                    rowGrid.Children.Add(txt);
                    Grid.SetColumn(txt, i);
                }

                rowBd.Child = rowGrid;
                outerStack.Children.Add(rowBd);
            }

            return outerBorder;
        }

        private void EnhancementGroupHeader_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is EnhancementGroup group)) return;

            group.IsExpanded = !group.IsExpanded;
            RenderEnhancementGroups(_selected?.EnhancementGroups ?? new List<EnhancementGroup>());
        }

        private async void EnhancementLocationLink_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is WikiLinkItem link) || !link.HasLink) return;
            await NavigateToEntry(link.Text, link.Url, WikiEntryType.Location);
        }

        private void BuildNpcDialogueTabs(List<NpcDialogueTab> tabs)
        {
            _currentNpcDialogueTabs = tabs ?? new List<NpcDialogueTab>();
            npcDialogueTabsPanel.Children.Clear();

            bool hasMultiple = _currentNpcDialogueTabs.Count > 1;
            npcDialogueTabsPanel.Visibility = hasMultiple ? Visibility.Visible : Visibility.Collapsed;

            if (_currentNpcDialogueTabs.Count == 0)
            {
                npcDialogueBorder.Visibility = Visibility.Collapsed;
                return;
            }
            npcDialogueBorder.Visibility = Visibility.Visible;

            if (hasMultiple)
            {
                for (int i = 0; i < _currentNpcDialogueTabs.Count; i++)
                {
                    var btn = new Button
                    {
                        Content = _currentNpcDialogueTabs[i].Label ?? $"Tab {i + 1}",
                        Tag = i,
                        Style = (Style)FindResource("WikiTabButton"),
                        Margin = new Thickness(0, 0, 3, 4)
                    };
                    btn.Click += NpcDialogueTab_Click;
                    npcDialogueTabsPanel.Children.Add(btn);
                }
                RenderNpcDialogueTabStyles(0);
            }

            RenderNpcDialogueTab(0);
        }

        private void NpcDialogueTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int idx)
            {
                RenderNpcDialogueTabStyles(idx);
                RenderNpcDialogueTab(idx);
            }
        }

        private void RenderNpcDialogueTabStyles(int activeIndex)
        {
            var activeBg = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48));
            var idleBg = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2C));
            var idleText = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3));

            for (int i = 0; i < npcDialogueTabsPanel.Children.Count; i++)
            {
                if (npcDialogueTabsPanel.Children[i] is Button btn)
                {
                    bool active = i == activeIndex;
                    btn.Background = active ? activeBg : idleBg;
                    btn.Foreground = active ? Brushes.White : idleText;
                }
            }
        }

        private void RenderNpcDialogueTab(int index)
        {
            if (index < 0 || index >= _currentNpcDialogueTabs.Count)
            {
                npcDialogueContentHost.Content = null;
                return;
            }

            var tab = _currentNpcDialogueTabs[index];
            var panel = new StackPanel();
            var speakerBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x5C, 0x5C));

            if (tab.Entries == null || tab.Entries.Count == 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "(no dialogue found)",
                    Foreground = (Brush)FindResource("WikiMuted"),
                    FontSize = 12
                });
                npcDialogueContentHost.Content = panel;
                return;
            }

            for (int i = 0; i < tab.Entries.Count; i++)
            {
                var entry = tab.Entries[i];
                var entryPanel = new StackPanel { Margin = new Thickness(0, i == 0 ? 0 : 14, 0, 0) };

                if (entry.HasCondition)
                {
                    entryPanel.Children.Add(new TextBlock
                    {
                        Text = entry.ConditionText,
                        FontFamily = new FontFamily("Segoe UI Semibold"),
                        FontSize = 12.5,
                        Foreground = (Brush)FindResource("Accent"),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 6)
                    });
                }

                if (!string.IsNullOrEmpty(entry.Speaker))
                {
                    entryPanel.Children.Add(new TextBlock
                    {
                        Text = entry.Speaker,
                        FontFamily = new FontFamily("Segoe UI Semibold"),
                        FontSize = 13,
                        Foreground = speakerBrush,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 0, 0, 2)
                    });
                }

                if (!string.IsNullOrEmpty(entry.DialogueText))
                {
                    entryPanel.Children.Add(new TextBlock
                    {
                        Text = entry.DialogueText,
                        FontSize = 13,
                        Foreground = (Brush)FindResource("WikiText"),
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 19
                    });
                }

                foreach (var talk in entry.TalkBlocks)
                {
                    var talkPanel = new StackPanel();
                    if (!string.IsNullOrEmpty(talk.Title))
                    {
                        talkPanel.Children.Add(new TextBlock
                        {
                            Text = talk.Title,
                            FontFamily = new FontFamily("Segoe UI Semibold"),
                            FontSize = 12,
                            Foreground = (Brush)FindResource("TextMid"),
                            TextDecorations = TextDecorations.Underline,
                            Margin = new Thickness(0, 0, 0, 4)
                        });
                    }
                    talkPanel.Children.Add(new TextBlock
                    {
                        Text = talk.Text,
                        FontSize = 12.5,
                        Foreground = (Brush)FindResource("WikiText"),
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 18
                    });

                    entryPanel.Children.Add(new Border
                    {
                        BorderBrush = (Brush)FindResource("Hairline"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Background = (Brush)FindResource("Surface"),
                        Padding = new Thickness(10, 8, 10, 8),
                        Margin = new Thickness(0, 8, 0, 0),
                        Child = talkPanel
                    });
                }

                if (entry.HasLinks)
                {
                    var linksPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                    foreach (var link in entry.Links)
                    {
                        var btn = new Button
                        {
                            Content = "- " + link.Text,
                            Tag = link,
                            IsEnabled = link.HasLink,
                            Style = (Style)FindResource("WikiLinkButton"),
                            Padding = new Thickness(0, 1, 0, 1)
                        };
                        btn.Click += NpcLink_Click;
                        linksPanel.Children.Add(btn);
                    }
                    entryPanel.Children.Add(linksPanel);
                }

                panel.Children.Add(entryPanel);
            }

            npcDialogueContentHost.Content = panel;
        }

        private async void NpcLink_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is WikiLinkItem link) || !link.HasLink) return;
            await NavigateToEntry(link.Text, link.Url);
        }

        private void BuildNpcScreenTabs(WikiEntry entry)
        {
            _currentNpcScreens = entry.Screens ?? new List<ScreenInfo>();
            npcScreenTabsPanel.Children.Clear();
            npcScreenImagesPanel.Children.Clear();
            _currentNpcScreenIndex = 0;

            if (_currentNpcScreens.Count == 0) return;

            npcScreenTabsPanel.Visibility = (_currentNpcScreens.Count > 1) ? Visibility.Visible : Visibility.Collapsed;

            for (int i = 0; i < _currentNpcScreens.Count; i++)
            {
                var btn = new Button
                {
                    Content = _currentNpcScreens[i].Label,
                    Tag = i,
                    Style = (Style)FindResource("WikiTabButton"),
                    FontSize = 12,
                    Padding = new Thickness(16, 7, 16, 7),
                    Margin = new Thickness(0, 0, 3, 4)
                };
                btn.Click += NpcScreenTab_Click;
                npcScreenTabsPanel.Children.Add(btn);
            }

            RenderNpcScreenTabStyles();
            RenderCurrentNpcScreen();
        }

        private void NpcScreenTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int idx)
            {
                _currentNpcScreenIndex = idx;
                RenderNpcScreenTabStyles();
                RenderCurrentNpcScreen();
            }
        }

        private void RenderNpcScreenTabStyles()
        {
            var activeBg = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48));
            var idleBg = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2C));
            var idleText = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3));

            for (int i = 0; i < npcScreenTabsPanel.Children.Count; i++)
            {
                if (npcScreenTabsPanel.Children[i] is Button btn)
                {
                    bool active = i == _currentNpcScreenIndex;
                    btn.Background = active ? activeBg : idleBg;
                    btn.Foreground = active ? Brushes.White : idleText;
                }
            }
        }

        private void RenderCurrentNpcScreen()
        {
            npcScreenImagesPanel.Children.Clear();
            if (_currentNpcScreenIndex < 0 || _currentNpcScreenIndex >= _currentNpcScreens.Count) return;

            var screen = _currentNpcScreens[_currentNpcScreenIndex];
            if (screen.Images == null || screen.Images.Count == 0)
            {
                npcScreenImagesPanel.Children.Add(new TextBlock
                {
                    Text = "(no image found for this screen)",
                    Foreground = (Brush)FindResource("WikiMuted"),
                    FontSize = 12
                });
                return;
            }

            foreach (var sub in screen.Images)
            {
                var img = new Image
                {
                    Stretch = Stretch.Uniform,
                    MaxWidth = 560,
                    MaxHeight = 420,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 4, 0, 12)
                };
                npcScreenImagesPanel.Children.Add(img);
                _ = LoadImageInto(img, sub.ImageUrl);
            }
        }

        // ===================== SHARED =====================

        private static Color GetRarityColor(string rarityText)
        {
            if (string.IsNullOrEmpty(rarityText)) return Color.FromRgb(0xC6, 0xC8, 0xD4);

            foreach (var kv in RarityColors)
            {
                if (rarityText.IndexOf(kv.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Value;
            }

            return Color.FromRgb(0xC6, 0xC8, 0xD4);
        }

        private async void ItemImageTab_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is string imageUrl)) return;

            var parent = VisualTreeHelper.GetParent(btn);
            while (parent != null && !(parent is ItemsControl))
                parent = VisualTreeHelper.GetParent(parent);

            if (parent is ItemsControl itemsControl)
            {
                foreach (var item in itemsControl.Items)
                {
                    var container = itemsControl.ItemContainerGenerator.ContainerFromItem(item) as DependencyObject;
                    if (container != null)
                    {
                        var button = FindVisualChild<Button>(container);
                        if (button != null)
                        {
                            button.Background = (Brush)FindResource("WikiTabIdleBg");
                            button.Foreground = (Brush)FindResource("WikiTabIdleText");
                        }
                    }
                }
            }

            btn.Background = (Brush)FindResource("Accent");
            btn.Foreground = Brushes.White;

            string cat = _selected?.CategoryLabel ?? "";
            bool isHouse = cat.Equals("Houses", StringComparison.OrdinalIgnoreCase);

            if (isHouse)
            {
                imgArmor.Visibility = Visibility.Collapsed;
                imgItemTab.Visibility = Visibility.Visible;
                await LoadImageInto(imgItemTab, imageUrl);
            }
            else
            {
                imgItemTab.Visibility = Visibility.Collapsed;
                imgArmor.Visibility = Visibility.Visible;
                await LoadImageInto(imgArmor, imageUrl);
            }
            txtImageUrl.Text = imageUrl ?? "(no image found)";
        }

        private async void btnMale_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            _currentGender = Gender.Male;
            UpdateGenderButtonStyle();
            await LoadImage(_selected.ImageUrl);
        }

        private async void btnFemale_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || string.IsNullOrEmpty(_selected.ImageUrlFemale)) return;
            _currentGender = Gender.Female;
            UpdateGenderButtonStyle();
            await LoadImage(_selected.ImageUrlFemale);
        }

        private void UpdateGenderButtonStyle()
        {
            var activeBg = new SolidColorBrush(Color.FromRgb(0xE1, 0x1D, 0x48));
            var idleBg = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2C));
            var idleText = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA3));

            bool isMale = _currentGender == Gender.Male;

            btnMale.Background = isMale ? activeBg : idleBg;
            btnMale.Foreground = isMale ? Brushes.White : idleText;
            btnFemale.Background = !isMale ? activeBg : idleBg;
            btnFemale.Foreground = !isMale ? Brushes.White : idleText;
        }

        private async Task LoadImage(string url)
        {
            imgItemTab.Visibility = Visibility.Collapsed;
            imgArmor.Visibility = Visibility.Visible;
            imgArmor.Source = null;
            if (string.IsNullOrEmpty(url)) return;
            await LoadImageInto(imgArmor, url);
        }

        private async Task LoadImageInto(System.Windows.Controls.Image target, string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                {
                    target.Source = null;
                    return;
                }

                if (_imageCache.TryGetValue(url, out var cached))
                {
                    target.Source = cached;
                    return;
                }

                byte[] data = await _httpClient.GetByteArrayAsync(url);
                using (var ms = new MemoryStream(data))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    _imageCache[url] = bmp;
                    target.Source = bmp;
                }
            }
            catch (Exception ex)
            {
                OnScraperLog($"Image load failed ({url}): {ex.Message}");
            }
        }

        private void RenderLinkedLines(StackPanel host, List<LinkedLine> lines, RoutedEventHandler linkClickHandler = null)
        {
            host.Children.Clear();
            if (lines == null) return;

            var clickHandler = linkClickHandler ?? LinkedRun_Click;

            foreach (var line in lines)
            {
                var tb = new TextBlock
                {
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(4 + line.Depth * 14, 0, 0, 2),
                    LineHeight = 18
                };
                tb.Inlines.Add(new Run(line.Depth == 0 ? "• " : "– "));

                foreach (var run in line.Runs)
                {
                    if (run.HasLink)
                    {
                        var hl = new Hyperlink(new Run(run.Text)) { Foreground = (Brush)FindResource("WikiLink"), Tag = run };
                        hl.TextDecorations = null;
                        hl.Click += clickHandler;
                        tb.Inlines.Add(hl);
                    }
                    else
                    {
                        tb.Inlines.Add(new Run(run.Text) { Foreground = (Brush)FindResource("WikiText") });
                    }
                }
                host.Children.Add(tb);
            }
        }

        private async void LinkedRun_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Hyperlink link) || !(link.Tag is TextRun run) || !run.HasLink) return;
            await NavigateToEntry(run.Text, run.Url);
        }



        private static void ShowRipple(FrameworkElement element)
        {
            if (element == null) return;

            var glow = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0xE1, 0x1D, 0x48),
                BlurRadius = 24,
                ShadowDepth = 0,
                Direction = 0,
            };

            element.Effect = glow;

            var anim = new DoubleAnimation
            {
                From = 0.85,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(450),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (s, args) =>
            {
                if (element.Effect == glow)
                    element.Effect = null;
            };

            glow.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, anim);
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private void SelectItemInCategory(CategoryViewModel category, WikiEntry entry)
        {
            foreach (var cat in _categories)
            {
                var catContainer = icCategories.ItemContainerGenerator.ContainerFromItem(cat) as FrameworkElement;
                if (catContainer == null) continue;

                if (cat == category || cat.Children.Contains(category))
                {
                    var listBox = FindChild<ListBox>(catContainer);
                    if (listBox != null && listBox.DataContext == (cat.Children.Contains(category) ? (object)category : cat))
                    {
                        listBox.SelectedItem = entry;
                        return;
                    }
                }

                foreach (var child in cat.Children)
                {
                    if (child == category)
                    {
                        var childContainer = FindChildInChildContainer(catContainer, child);
                        if (childContainer != null)
                        {
                            var listBox = FindChild<ListBox>(childContainer);
                            if (listBox != null)
                                listBox.SelectedItem = entry;
                        }
                        return;
                    }
                }
            }
        }

        private FrameworkElement FindChildInChildContainer(FrameworkElement parentContainer, CategoryViewModel child)
        {
            var childrenControl = FindChild<ItemsControl>(parentContainer);
            if (childrenControl == null) return null;
            var container = childrenControl.ItemContainerGenerator.ContainerFromItem(child) as FrameworkElement;
            return container;
        }
    }
}
