using System;
using System.Threading.Tasks;
using LumiaYT.Models;
using LumiaYT.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace LumiaYT.Pages
{
    public sealed partial class HomePage : Page
    {
        private string _currentMode = "trending";

        public HomePage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (VideoList.ItemsSource == null) await LoadTrendingAsync();
        }

        private async Task LoadTrendingAsync()
        {
            _currentMode = "trending";
            SetLoading(true, "Carregando trending...");
            var items = await YouTubeApiService.GetTrendingAsync();
            VideoList.ItemsSource = items;
            SetLoading(false, items.Count > 0 ? items.Count + " vídeos." : "Sem resultados. Configure a API key em Configurações.");
        }

        private async Task LoadSearchAsync(string q)
        {
            _currentMode = "search";
            SetLoading(true, "Buscando...");
            var items = await YouTubeApiService.SearchAsync(q);
            VideoList.ItemsSource = items;
            SetLoading(false, items.Count > 0 ? items.Count + " resultados." : "Sem resultados / verifique API key.");
        }

        private async Task LoadSubsAsync()
        {
            _currentMode = "subs";
            SetLoading(true, "Carregando inscrições...");
            var items = await YouTubeApiService.GetSubscriptionsAsync();
            VideoList.ItemsSource = items;
            SetLoading(false, items.Count > 0 ? items.Count + " canais." : "Sem inscrições ou faça login em Configurações.");
        }

        private void SetLoading(bool loading, string status)
        {
            LoadingRing.IsActive = loading;
            StatusText.Text = status ?? "";
        }

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
                if (q.Length > 0) await LoadSearchAsync(q);
            }
        }

        private async void Subs_Click(object sender, RoutedEventArgs e) { await LoadSubsAsync(); }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMode == "search")
            {
                var q = (SearchBox.Text ?? "").Trim();
                if (q.Length > 0) { await LoadSearchAsync(q); return; }
            }
            if (_currentMode == "subs") { await LoadSubsAsync(); return; }
            await LoadTrendingAsync();
        }

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
