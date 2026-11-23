using System;
using System.Threading;
using LumiaYT.Models;
using LumiaYT.Services;
using Windows.Storage.Pickers;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace LumiaYT.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private AppSettings _settings;
        private CancellationTokenSource _deviceCts;

        public SettingsPage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var autoMsg = await SettingsService.TryAutoImportAsync();
            _settings = await SettingsService.GetAsync();
            ApiKeyBox.Text = _settings.YouTubeApiKey ?? "";
            ClientIdBox.Text = _settings.GoogleClientId ?? "";
            ClientSecretBox.Password = _settings.GoogleClientSecret ?? "";
            await RefreshUiAsync();
            if (!string.IsNullOrWhiteSpace(autoMsg)) StatusText.Text = autoMsg;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CancelDeviceFlow();
        }

        private async System.Threading.Tasks.Task RefreshUiAsync()
        {
            _settings = await SettingsService.GetAsync();
            var logged = !string.IsNullOrWhiteSpace(_settings.RefreshToken) || !string.IsNullOrWhiteSpace(_settings.AccessToken);
            LoggedInPanel.Visibility = logged ? Visibility.Visible : Visibility.Collapsed;
            LoggedOutPanel.Visibility = logged ? Visibility.Collapsed : Visibility.Visible;

            if (logged)
            {
                if (string.IsNullOrWhiteSpace(_settings.UserDisplayName))
                    await AuthService.TryFetchUserProfileAsync(_settings);
                _settings = await SettingsService.GetAsync();

                UserNameText.Text = !string.IsNullOrWhiteSpace(_settings.UserChannelTitle)
                    ? _settings.UserChannelTitle
                    : (!string.IsNullOrWhiteSpace(_settings.UserDisplayName) ? _settings.UserDisplayName : "Conta Google");
                UserEmailText.Text = _settings.UserEmail ?? "";
                SetAvatar(_settings.UserAvatarUrl);

                _ = LoadLibraryAsync();
            }
            else
            {
                ApiKeyBox.Text = _settings.YouTubeApiKey ?? "";
                ClientIdBox.Text = _settings.GoogleClientId ?? "";
                ClientSecretBox.Password = _settings.GoogleClientSecret ?? "";
            }
        }

        private async System.Threading.Tasks.Task LoadLibraryAsync()
        {
            try
            {
                var likes = await YouTubeApiService.GetLikedVideosAsync(12);
                LikedList.ItemsSource = likes.Items;

                var watchLater = await YouTubeApiService.GetMyChannelSectionAsync("watchLater", 12);
                WatchLaterList.ItemsSource = watchLater.Items;

                // "History" is not directly exposed by Data API v3 for most apps; try activities as fallback.
                var history = await YouTubeApiService.GetMyActivitiesAsync(12);
                HistoryList.ItemsSource = history.Items;
            }
            catch { }
        }

        private void SetAvatar(string url)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                try { UserAvatar.Source = new BitmapImage(new Uri(url)); UserIcon.Visibility = Visibility.Collapsed; return; }
                catch { }
            }
            UserAvatar.Source = null;
            UserIcon.Visibility = Visibility.Visible;
        }

        private async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            _settings.YouTubeApiKey = (ApiKeyBox.Text ?? "").Trim();
            _settings.GoogleClientId = (ClientIdBox.Text ?? "").Trim();
            _settings.GoogleClientSecret = (ClientSecretBox.Password ?? "").Trim();
            await SettingsService.SaveAsync(_settings);
            StatusText.Text = "Configurações salvas.";
        }

        private async void Save_Click(object sender, RoutedEventArgs e) { await SaveSettingsAsync(); }

        private async void ImportFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker();
                picker.ViewMode = PickerViewMode.List;
                picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                picker.FileTypeFilter.Add(".txt");
                picker.FileTypeFilter.Add(".json");
                var file = await picker.PickSingleFileAsync();
                if (file == null) return;
                StatusText.Text = "Importando...";
                var msg = await SettingsService.ImportFromFileAsync(file);
                _settings = await SettingsService.GetAsync();
                ApiKeyBox.Text = _settings.YouTubeApiKey ?? "";
                ClientIdBox.Text = _settings.GoogleClientId ?? "";
                ClientSecretBox.Password = _settings.GoogleClientSecret ?? "";
                StatusText.Text = msg;
            }
            catch (Exception ex) { StatusText.Text = "Falha ao importar: " + ex.Message; }
        }

        private async void DeviceCodeLogin_Click(object sender, RoutedEventArgs e)
        {
            CancelDeviceFlow();
            await SaveSettingsAsync();

            LoginCodeButton.IsEnabled = false;
            StatusText.Text = "Gerando código de login...";
            DeviceCodePanel.Visibility = Visibility.Collapsed;

            var info = await AuthService.StartDeviceCodeFlowAsync();
            if (info == null)
            {
                StatusText.Text = "Falha ao gerar código. Verifique Client ID.";
                LoginCodeButton.IsEnabled = true;
                return;
            }

            DeviceCodePanel.Visibility = Visibility.Visible;
            DeviceUrlText.Text = info.VerificationUrl;
            DeviceCodeText.Text = info.UserCode;
            DeviceStatusText.Text = string.IsNullOrWhiteSpace(info.Message) ? "Abra o link em outro dispositivo e digite o código." : info.Message;
            StatusText.Text = "Aguardando autorização no navegador...";

            _deviceCts = new CancellationTokenSource();
            var ok = await AuthService.PollDeviceCodeAsync(info, _deviceCts.Token, msg =>
            {
                var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { DeviceStatusText.Text = msg; });
            });

            LoginCodeButton.IsEnabled = true;
            _settings = await SettingsService.GetAsync();
            if (ok)
            {
                StatusText.Text = "Login concluído.";
                DeviceCodePanel.Visibility = Visibility.Collapsed;
                await RefreshUiAsync();
            }
            else if (_deviceCts == null || _deviceCts.IsCancellationRequested)
            {
                StatusText.Text = "Login por código cancelado.";
            }
            else
            {
                StatusText.Text = "Login não concluído. Veja a mensagem acima.";
            }
        }

        private void CancelDeviceFlow()
        {
            if (_deviceCts != null)
            {
                try { _deviceCts.Cancel(); } catch { }
                try { _deviceCts.Dispose(); } catch { }
                _deviceCts = null;
            }
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            CancelDeviceFlow();
            await AuthService.LogoutAsync();
            _settings = await SettingsService.GetAsync();
            DeviceCodePanel.Visibility = Visibility.Collapsed;
            StatusText.Text = "Logout concluído.";
            await RefreshUiAsync();
        }

        private void Video_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is VideoItem item && !string.IsNullOrWhiteSpace(item.Id))
                Frame.Navigate(typeof(PlayerPage), item);
        }

        private void Back_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    }
}
