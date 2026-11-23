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
        private bool _advancedVisible;

        public SettingsPage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var autoMsg = await SettingsService.TryAutoImportAsync();
            _settings = await SettingsService.GetAsync();
            ApiKeyBox.Text = _settings.YouTubeApiKey ?? "";
            ClientIdBox.Text = _settings.GoogleClientId ?? "";
            ClientSecretBox.Password = _settings.GoogleClientSecret ?? "";
            RedirectUriText.Text = "Redirect URI (WAB): " + AuthService.GetRedirectUri();
            await RefreshAccountUiAsync();
            if (!string.IsNullOrWhiteSpace(autoMsg)) StatusText.Text = autoMsg;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CancelDeviceFlow();
        }

        private async System.Threading.Tasks.Task RefreshAccountUiAsync()
        {
            var logged = !string.IsNullOrWhiteSpace(_settings.RefreshToken) || !string.IsNullOrWhiteSpace(_settings.AccessToken);
            AuthStatusText.Text = logged ? "Conectado" : "Não conectado";
            LoginCodeButton.Visibility = logged ? Visibility.Collapsed : Visibility.Visible;
            LogoutButton.Visibility = logged ? Visibility.Visible : Visibility.Collapsed;

            if (logged)
            {
                if (string.IsNullOrWhiteSpace(_settings.UserDisplayName))
                    await AuthService.TryFetchUserProfileAsync(_settings);

                _settings = await SettingsService.GetAsync();
                UserNameText.Text = !string.IsNullOrWhiteSpace(_settings.UserChannelTitle) ? _settings.UserChannelTitle : (!string.IsNullOrWhiteSpace(_settings.UserDisplayName) ? _settings.UserDisplayName : "Conta Google");
                UserEmailText.Text = !string.IsNullOrWhiteSpace(_settings.UserEmail) ? _settings.UserEmail : "Conta conectada";
                if (!string.IsNullOrWhiteSpace(_settings.UserAvatarUrl))
                {
                    try { UserAvatar.Source = new BitmapImage(new Uri(_settings.UserAvatarUrl)); UserIcon.Visibility = Visibility.Collapsed; }
                    catch { UserAvatar.Source = null; UserIcon.Visibility = Visibility.Visible; }
                }
                else { UserAvatar.Source = null; UserIcon.Visibility = Visibility.Visible; }
            }
            else
            {
                UserNameText.Text = "Não conectado";
                UserEmailText.Text = "Faça login para ver suas inscrições e recomendações";
                UserAvatar.Source = null;
                UserIcon.Visibility = Visibility.Visible;
            }
        }

        private async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            _settings.YouTubeApiKey = (ApiKeyBox.Text ?? "").Trim();
            _settings.GoogleClientId = (ClientIdBox.Text ?? "").Trim();
            _settings.GoogleClientSecret = (ClientSecretBox.Password ?? "").Trim();
            await SettingsService.SaveAsync(_settings);
            StatusText.Text = "Configurações salvas.";
            await RefreshAccountUiAsync();
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
                await RefreshAccountUiAsync();
                StatusText.Text = msg;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Falha ao importar: " + ex.Message;
            }
        }

        private async void WebViewLogin_Click(object sender, RoutedEventArgs e)
        {
            CancelDeviceFlow();
            await SaveSettingsAsync();
            Frame.Navigate(typeof(LoginPage));
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            CancelDeviceFlow();
            await SaveSettingsAsync();
            StatusText.Text = "Abrindo login Google (WAB)...";
            var ok = await AuthService.LoginAsync();
            _settings = await SettingsService.GetAsync();
            await RefreshAccountUiAsync();
            StatusText.Text = ok ? "Login concluído." : "Falha no login WAB. Use 'Entrar com código'.";
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
            DeviceStatusText.Text = string.IsNullOrWhiteSpace(info.Message)
                ? "Abra o link em outro dispositivo e digite o código."
                : info.Message;
            StatusText.Text = "Aguardando autorização no navegador...";

            _deviceCts = new CancellationTokenSource();
            var ok = await AuthService.PollDeviceCodeAsync(info, _deviceCts.Token, msg =>
            {
                var _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => { DeviceStatusText.Text = msg; });
            });

            LoginCodeButton.IsEnabled = true;
            _settings = await SettingsService.GetAsync();
            await RefreshAccountUiAsync();
            if (ok)
            {
                StatusText.Text = "Login concluído.";
                DeviceStatusText.Text = "Conta conectada.";
                DeviceCodePanel.Visibility = Visibility.Collapsed;
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

        private void AdvancedToggle_Click(object sender, RoutedEventArgs e)
        {
            _advancedVisible = !_advancedVisible;
            AdvancedPanel.Visibility = _advancedVisible ? Visibility.Visible : Visibility.Collapsed;
            AdvancedToggle.Content = _advancedVisible ? "Ocultar opções avançadas" : "Mostrar opções avançadas";
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
            await RefreshAccountUiAsync();
            DeviceCodePanel.Visibility = Visibility.Collapsed;
            StatusText.Text = "Logout concluído.";
        }

        private void Back_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    }
}
