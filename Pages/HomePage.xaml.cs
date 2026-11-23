using System;
using System.Threading.Tasks;
using LumiaYT.Models;
using LumiaYT.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace LumiaYT.Pages
{
    public sealed partial class HomePage : Page
    {
        private string _currentMode = "recommended";

        public HomePage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await RefreshUserButtonAsync();
            if (VideoList.ItemsSource == null) await LoadRecommendedAsync();
            else if (_currentMode == "recommended" || _currentMode == "trending") await RefreshCurrentAsync();
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
        }

        private async Task LoadRecommendedAsync()
        {
            SetActiveTab("recommended");
            SetLoading(true, "Carregando recomendações...");
            var items = await YouTubeApiService.GetRecommendedAsync();
            VideoList.ItemsSource = items;
            SetEmptyState(items.Count == 0, "Sem resultados. Configure a API key em Você > Opções avançadas.");
            SetLoading(false, items.Count > 0 ? "Recomendados para você" : "");
        }

        private async Task LoadTrendingAsync()
        {
            SetActiveTab("trending");
            SetLoading(true, "Carregando vídeos em alta...");
            var items = await YouTubeApiService.GetTrendingAsync();
            VideoList.ItemsSource = items;
            SetEmptyState(items.Count == 0, "Sem resultados. Configure a API key em Você > Opções avançadas.");
            SetLoading(false, items.Count > 0 ? items.Count + " vídeos em alta." : "");
        }

        private async Task LoadSearchAsync(string q)
        {
            SetActiveTab("search");
            SetLoading(true, "Buscando...");
            var items = await YouTubeApiService.SearchAsync(q);
            VideoList.ItemsSource = items;
            SetEmptyState(items.Count == 0, "Sem resultados / verifique API key.");
            SetLoading(false, items.Count > 0 ? items.Count + " resultados para \"" + q + "\"." : "");
        }

        private async Task LoadSubsAsync()
        {
            SetActiveTab("subs");
            SetLoading(true, "Carregando inscrições...");
            var items = await YouTubeApiService.GetSubscriptionsAsync();
            VideoList.ItemsSource = items;
            var settings = await SettingsService.GetAsync();
            var logged = !string.IsNullOrWhiteSpace(settings.AccessToken) || !string.IsNullOrWhiteSpace(settings.RefreshToken);
            SetEmptyState(items.Count == 0, logged ? "Nenhuma inscrição encontrada." : "Entre na sua conta para ver inscrições.");
            SetLoading(false, items.Count > 0 ? items.Count + " inscrições." : "");
        }

        private async Task RefreshCurrentAsync()
        {
            if (_currentMode == "search")
            {
                var q = (SearchBox.Text ?? "").Trim();
                if (q.Length > 0) { await LoadSearchAsync(q); return; }
            }
            if (_currentMode == "subs") { await LoadSubsAsync(); return; }
            if (_currentMode == "trending") { await LoadTrendingAsync(); return; }
            await LoadRecommendedAsync();
        }

        private void SetLoading(bool loading, string status)
        {
            LoadingRing.IsActive = loading;
            StatusText.Text = status ?? "";
        }

        private void SetEmptyState(bool show, string message)
        {
            EmptyStatePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateText.Text = message ?? "";
        }

        private async void Recommended_Click(object sender, RoutedEventArgs e) { await LoadRecommendedAsync(); }
        private async void Trending_Click(object sender, RoutedEventArgs e) { await LoadTrendingAsync(); }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            var q = (SearchBox.Text ?? "").Trim();
            if (q.Length == 0) return;
            await LoadSearchAsync(q);
        }

        private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                var q = (SearchBox.Text ?? "").Trim();
                if (q.Length > 0) _ = LoadSearchAsync(q);
            }
        }

        private async void Subs_Click(object sender, RoutedEventArgs e) { await LoadSubsAsync(); }
        private async void Refresh_Click(object sender, RoutedEventArgs e) { await RefreshUserButtonAsync(); await RefreshCurrentAsync(); }

        private void VideoList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as VideoItem;
            if (item == null) return;
            if (!string.IsNullOrWhiteSpace(item.Id) && item.Id.Length == 11)
                Frame.Navigate(typeof(PlayerPage), item);
            else if (!string.IsNullOrWhiteSpace(item.ChannelId))
                StatusText.Text = "Canal: " + item.ChannelTitle;
        }

        private void Settings_Click(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(SettingsPage)); }
    }
}
