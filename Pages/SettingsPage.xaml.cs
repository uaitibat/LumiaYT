using System;
using LumiaYT.Models;
using LumiaYT.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LumiaYT.Pages
{
    public sealed partial class SettingsPage : Page
    {
        private AppSettings _settings;

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

        private void UpdateAuthStatus()
        {
            var logged = !string.IsNullOrWhiteSpace(_settings.RefreshToken) || !string.IsNullOrWhiteSpace(_settings.AccessToken);
            AuthStatusText.Text = logged ? "Login: ativo (token salvo)" : "Login: não conectado";
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            _settings.YouTubeApiKey = (ApiKeyBox.Text ?? "").Trim();
            _settings.GoogleClientId = (ClientIdBox.Text ?? "").Trim();
            _settings.GoogleClientSecret = (ClientSecretBox.Password ?? "").Trim();
            await SettingsService.SaveAsync(_settings);
            StatusText.Text = "Configurações salvas.";
            UpdateAuthStatus();
        }

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            await Save_Click(sender, e);
            StatusText.Text = "Abrindo login Google...";
            var ok = await AuthService.LoginAsync();
            _settings = await SettingsService.GetAsync();
            UpdateAuthStatus();
            StatusText.Text = ok ? "Login concluído." : "Falha no login. Verifique Client ID/Secret e Redirect URI no Google Cloud.";
        }

        private async void Logout_Click(object sender, RoutedEventArgs e)
        {
            await AuthService.LogoutAsync();
            _settings = await SettingsService.GetAsync();
            UpdateAuthStatus();
            StatusText.Text = "Logout concluído.";
        }

        private void Back_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    }
}
