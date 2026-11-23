using System;
using LumiaYT.Models;
using LumiaYT.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LumiaYT.Pages
{
    public sealed partial class HomePage : Page
    {
        public HomePage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (VideoList.ItemsSource == null) await LoadTrendingAsync();
        }

        private async System.Threading.Tasks.Task LoadTrendingAsync()
        {
            StatusText.Text = "Carregando trending...";
            var items = await YouTubeApiService.GetTrendingAsync();
            VideoList.ItemsSource = items;
            StatusText.Text = items.Count > 0 ? items.Count + " vídeos." : "Sem resultados. Configure a API key em Config.";
        }

        private async void Trending_Click(object sender, RoutedEventArgs e) { await LoadTrendingAsync(); }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            var q = (SearchBox.Text ?? "").Trim();
            if (q.Length == 0) return;
            StatusText.Text = "Buscando...";
            var items = await YouTubeApiService.SearchAsync(q);
            VideoList.ItemsSource = items;
            StatusText.Text = items.Count > 0 ? items.Count + " resultados." : "Sem resultados / verifique API key.";
        }

        private async void Subs_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Carregando inscrições...";
            var items = await YouTubeApiService.GetSubscriptionsAsync();
            VideoList.ItemsSource = items;
            StatusText.Text = items.Count > 0 ? items.Count + " canais." : "Sem inscrições ou faça login em Config.";
        }

        private void VideoList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as VideoItem;
            if (item == null) return;
            if (!string.IsNullOrWhiteSpace(item.Id) && item.Id.Length == 11)
                Frame.Navigate(typeof(PlayerPage), item);
            else if (!string.IsNullOrWhiteSpace(item.ChannelId))
                StatusText.Text = "Canal: " + item.ChannelTitle + " (abrir canal pode ser adicionado depois)";
        }

        private void Settings_Click(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(SettingsPage)); }
    }
}
