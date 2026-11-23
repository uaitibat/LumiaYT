using System;
using System.Threading;
using LumiaYT.Models;
using LumiaYT.Services;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
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
            _settings = await SettingsService.GetAsync();
            ApiKeyBox.Text = _settings.YouTubeApiKey ?? "";
            ClientIdBox.Text = _settings.GoogleClientId ?? "";
            ClientSecretBox.Password = _settings.GoogleClientSecret ?? "";
            RedirectUriText.Text = "Redirect URI (WAB): " + AuthService.GetRedirectUri();
            UpdateAuthStatus();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CancelDeviceFlow();
        }

        private void UpdateAuthStatus()
        {
            var logged = !string.IsNullOrWhiteSpace(_settings.RefreshToken) || !string.IsNullOrWhiteSpace(_settings.AccessToken);
            AuthStatusText.Text = logged ? "Login: ativo (token salvo)" : "Login: não conectado";
        }

        private async System.Threading.Tasks.Task SaveSettingsAsync()
        {
            _settings.YouTubeApiKey = (ApiKeyBox.Text ?? "").Trim();
            _settings.GoogleClientId = (ClientIdBox.Text ?? "").Trim();
            _settings.GoogleClientSecret = (ClientSecretBox.Password ?? "").Trim();
            await SettingsService.SaveAsync(_settings);
            StatusText.Text = "Configurações salvas.";
            UpdateAuthStatus();
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            await SaveSettingsAsync();
        }

        private async void WebViewLogin_Click(object sender, RoutedEventArgs e)
        {
            CancelDeviceFlow();
            await SaveSettingsAsync();
            if (string.IsNullOrWhiteSpace(_settings.GoogleClientId))
            {
                StatusText.Text = "Preencha o Client ID antes.";
                return;
            }
            Frame.Navigate(typeof(LoginPage));
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            CancelDeviceFlow();
            await SaveSettingsAsync();
            StatusText.Text = "Abrindo login Google (WAB)...";
            var ok = await AuthService.LoginAsync();
            _settings = await SettingsService.GetAsync();
            UpdateAuthStatus();
            StatusText.Text = ok ? "Login concluído." : "Falha no login WAB. No W10M, use 'Login por código' ou 'Login (WebView)'.";
        }

        private async void DeviceCodeLogin_Click(object sender, RoutedEventArgs e)
        {
            CancelDeviceFlow();
            await SaveSettingsAsync();

            if (string.IsNullOrWhiteSpace(_settings.GoogleClientId))
            {
                StatusText.Text = "Preencha o Client ID antes.";
                return;
            }

            DeviceCodeButton.IsEnabled = false;
            StatusText.Text = "Gerando código de login...";
            DeviceCodePanel.Visibility = Visibility.Collapsed;

            var info = await AuthService.StartDeviceCodeFlowAsync();
            if (info == null)
            {
                StatusText.Text = "Falha ao gerar código. Use um OAuth Client ID tipo 'TVs and Limited Input devices' no Google Cloud.";
                DeviceCodeButton.IsEnabled = true;
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

            DeviceCodeButton.IsEnabled = true;
            _settings = await SettingsService.GetAsync();
            UpdateAuthStatus();
            if (ok)
            {
                StatusText.Text = "Login por código concluído.";
                DeviceStatusText.Text = "Conta conectada. Você pode voltar.";
            }
            else if (_deviceCts == null || _deviceCts.IsCancellationRequested)
            {
                StatusText.Text = "Login por código cancelado.";
            }
            else
            {
                StatusText.Text = "Login por código não concluído. Veja a mensagem acima.";
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
            UpdateAuthStatus();
            DeviceCodePanel.Visibility = Visibility.Collapsed;
            StatusText.Text = "Logout concluído.";
        }

        private void Back_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    }
}
