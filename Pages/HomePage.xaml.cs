using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LumiaYT.Models;
using LumiaYT.Services;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace LumiaYT.Pages
{
    public sealed partial class HomePage : Page
    {
        private string _currentMode = "recommended";
        private string _nextPageToken = "";
        private bool _isLoading;
        private bool _isLoadingMore;
        private string _currentQuery = "";
        private CancellationTokenSource _suggestCts;
        private readonly ObservableCollection<VideoItem> _items = new ObservableCollection<VideoItem>();
        private ScrollViewer _listScrollViewer;

        public HomePage() { InitializeComponent(); VideoList.ItemsSource = _items; }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await RefreshUserButtonAsync();
            if (_items.Count == 0) await LoadRecommendedAsync(reset: true);
        }

        private async Task RefreshUserButtonAsync()
        {
            var settings = await SettingsService.GetAsync();
            var logged = !string.IsNullOrWhiteSpace(settings.RefreshToken) || !string.IsNullOrWhiteSpace(settings.AccessToken);
            if (logged && !string.IsNullOrWhiteSpace(settings.UserAvatarUrl))
            {
                try { UserButtonAvatar.Source = new BitmapImage(new Uri(settings.UserAvatarUrl)); UserButtonIcon.Visibility = Visibility.Collapsed; }
                catch { UserButtonAvatar.Source = null; UserButtonIcon.Visibility = Visibility.Visible; }
            }
            else
            {
                UserButtonAvatar.Source = null;
                UserButtonIcon.Visibility = Visibility.Visible;
            }
        }

        private void SetActiveTab(string mode)
        {
            _currentMode = mode;
            RecommendedTab.Opacity = mode == "recommended" ? 1.0 : 0.7;
            TrendingTab.Opacity = mode == "trending" ? 1.0 : 0.7;
            SubsTab.Opacity = mode == "subs" ? 1.0 : 0.7;
            SubsChannelsList.Visibility = mode == "subs" ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task LoadRecommendedAsync(bool reset)
        {
            if (_isLoading) return;
            SetActiveTab("recommended");
            await LoadFeedAsync(reset, async token =>
            {
                var settings = await SettingsService.GetAsync();
                var logged = !string.IsNullOrWhiteSpace(settings.AccessToken) || !string.IsNullOrWhiteSpace(settings.RefreshToken);
                var res = await YouTubeApiService.GetRecommendedAsync(20, token);
                if (res.Items.Count == 0 && !logged) return await YouTubeApiService.GetTrendingAsync("BR", 20, token);
                return res;
            }, "Carregando recomendações...", "Sem recomendações. Configure a API ou faça login.");
        }

        private async Task LoadTrendingAsync(bool reset)
        {
            if (_isLoading) return;
            SetActiveTab("trending");
            await LoadFeedAsync(reset, token => YouTubeApiService.GetTrendingAsync("BR", 20, token), "Carregando vídeos em alta...", "Sem vídeos em alta.");
        }

        private async Task LoadSearchAsync(string q, bool reset)
        {
            if (_isLoading) return;
            SetActiveTab("search");
            _currentQuery = q;
            await LoadFeedAsync(reset, token => YouTubeApiService.SearchAsync(q, 20, token), "Buscando...", "Sem resultados.");
        }

        private async Task LoadSubsAsync(bool reset)
        {
            if (_isLoading) return;
            SetActiveTab("subs");
            if (reset)
            {
                _ = LoadSubsChannelsAsync();
            }
            await LoadFeedAsync(reset, token => YouTubeApiService.GetSubscriptionFeedAsync(20, token), "Carregando inscrições...", "Entre na sua conta para ver inscrições.");
        }

        private async Task LoadSubsChannelsAsync()
        {
            try
            {
                var res = await YouTubeApiService.GetSubscriptionChannelsAsync(50, "");
                SubsChannelsList.ItemsSource = res.Items;
            }
            catch { }
        }

        private async Task LoadFeedAsync(bool reset, Func<string, Task<VideoFeedResult>> loader, string loadingText, string emptyText)
        {
            if (reset)
            {
                _items.Clear();
                _nextPageToken = "";
                SetLoading(true, loadingText);
            }
            else
            {
                if (_isLoadingMore || string.IsNullOrWhiteSpace(_nextPageToken)) return;
                _isLoadingMore = true;
                SetMoreLoading(true);
            }

            _isLoading = true;
            try
            {
                var res = await loader(reset ? "" : _nextPageToken);
                foreach (var it in res.Items)
                {
                    if (!_items.Any(x => x.Id == it.Id)) _items.Add(it);
                }
                _nextPageToken = res.NextPageToken ?? "";
                SetEmptyState(_items.Count == 0, emptyText);
                StatusText.Text = _items.Count > 0 ? _items.Count + " itens" + (string.IsNullOrWhiteSpace(_nextPageToken) ? "" : " • role para carregar mais") : "";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Erro: " + ex.Message;
            }
            finally
            {
                _isLoading = false;
                _isLoadingMore = false;
                SetLoading(false, StatusText.Text);
                SetMoreLoading(false);
            }
        }

        private async Task LoadMoreIfNeededAsync()
        {
            if (_isLoading || _isLoadingMore || string.IsNullOrWhiteSpace(_nextPageToken)) return;
            if (_currentMode == "search") await LoadSearchAsync(_currentQuery, false);
            else if (_currentMode == "subs") await LoadSubsAsync(false);
            else if (_currentMode == "trending") await LoadTrendingAsync(false);
            else await LoadRecommendedAsync(false);
        }

        private void SetLoading(bool loading, string status)
        {
            LoadingRing.IsActive = loading;
            if (!string.IsNullOrWhiteSpace(status) || !loading) StatusText.Text = status ?? "";
        }

        private void SetMoreLoading(bool loading)
        {
            var ring = FindDescendantByName(VideoList, "MoreLoadingRing") as ProgressRing;
            if (ring != null) ring.IsActive = loading;
        }

        private void SetEmptyState(bool show, string message)
        {
            EmptyStatePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateText.Text = message ?? "";
        }

        private async void Recommended_Click(object sender, RoutedEventArgs e) { await LoadRecommendedAsync(true); }
        private async void Trending_Click(object sender, RoutedEventArgs e) { await LoadTrendingAsync(true); }
        private async void Subs_Click(object sender, RoutedEventArgs e) { await LoadSubsAsync(true); }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            var q = (SearchBox.Text ?? "").Trim();
            if (q.Length == 0) return;
            await LoadSearchAsync(q, true);
        }

        private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            var q = (args.QueryText ?? sender.Text ?? "").Trim();
            if (q.Length == 0) return;
            await LoadSearchAsync(q, true);
        }

        private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            sender.Text = args.SelectedItem?.ToString() ?? sender.Text;
        }

        private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var text = sender.Text ?? "";
            _suggestCts?.Cancel();
            if (text.Trim().Length < 2) { sender.ItemsSource = null; return; }
            var cts = new CancellationTokenSource();
            _suggestCts = cts;
            try
            {
                await Task.Delay(300, cts.Token);
                var suggestions = await YouTubeApiService.GetSearchSuggestionsAsync(text.Trim());
                if (!cts.IsCancellationRequested)
                    sender.ItemsSource = suggestions.Take(8).ToList();
            }
            catch (TaskCanceledException) { }
            catch { }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await RefreshUserButtonAsync();
            if (_currentMode == "search") await LoadSearchAsync(_currentQuery, true);
            else if (_currentMode == "subs") await LoadSubsAsync(true);
            else if (_currentMode == "trending") await LoadTrendingAsync(true);
            else await LoadRecommendedAsync(true);
        }

        private void VideoList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is VideoItem item && !string.IsNullOrWhiteSpace(item.Id))
                Frame.Navigate(typeof(PlayerPage), item);
        }

        private async void SubsChannelsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ChannelItem ch && !string.IsNullOrWhiteSpace(ch.Id))
            {
                // quick search for channel videos
                await LoadSearchAsync(ch.Title, true);
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(SettingsPage)); }

        private void VideoList_Loaded(object sender, RoutedEventArgs e)
        {
            if (_listScrollViewer != null) return;
            _listScrollViewer = FindDescendantScrollViewer(VideoList);
            if (_listScrollViewer != null)
                _listScrollViewer.ViewChanged += ListScrollViewer_ViewChanged;
        }

        private async void ListScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            if (e.IsIntermediate) return;
            if (_listScrollViewer == null) return;
            var remaining = _listScrollViewer.ScrollableHeight - _listScrollViewer.VerticalOffset;
            if (remaining < 300) await LoadMoreIfNeededAsync();
        }

        private static ScrollViewer FindDescendantScrollViewer(DependencyObject root)
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv) return sv;
                var found = FindDescendantScrollViewer(child);
                if (found != null) return found;
            }
            return null;
        }

        private static DependencyObject FindDescendantByName(DependencyObject root, string name)
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement fe && fe.Name == name) return fe;
                var found = FindDescendantByName(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
