using System;
using LumiaYT.Services;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LumiaYT.Pages
{
    public sealed partial class LoginPage : Page
    {
        private string _redirectUri = AuthService.DefaultWebRedirectUri;
        private bool _handled;

        public LoginPage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _handled = false;
            var settings = await SettingsService.GetAsync();
            var clientId = AuthService.GetEffectiveClientId(settings);
            _redirectUri = AuthService.GetEffectiveWebRedirectUri(settings);

            var authUrl = AuthService.BuildWebAuthUrl(clientId, _redirectUri);
            StatusText.Text = "Carregando login Google...";
            AuthWebView.Navigate(new Uri(authUrl));
        }

        private async void AuthWebView_NavigationStarting(WebView sender, WebViewNavigationStartingEventArgs args)
        {
            if (_handled || args.Uri == null) return;
            var uri = args.Uri.AbsoluteUri;
            if (!uri.StartsWith(_redirectUri, StringComparison.OrdinalIgnoreCase)) return;

            _handled = true;
            args.Cancel = true;
            StatusText.Text = "Recebendo código...";

            var query = args.Uri.Query;
            var code = AuthService.GetQueryParam(query, "code");
            if (string.IsNullOrWhiteSpace(code))
            {
                var err = AuthService.GetQueryParam(query, "error");
                StatusText.Text = "Login falhou: " + (string.IsNullOrWhiteSpace(err) ? "código não recebido" : err);
                return;
            }

            var ok = await AuthService.ExchangeCodeForRedirectAsync(code, _redirectUri);
            StatusText.Text = ok ? "Login concluído. Voltando..." : "Falha ao trocar código por token.";
            if (ok)
            {
                await System.Threading.Tasks.Task.Delay(800);
                if (Frame.CanGoBack) Frame.GoBack();
            }
        }

        private void AuthWebView_NavigationCompleted(WebView sender, WebViewNavigationCompletedEventArgs args)
        {
            if (!_handled && !args.IsSuccess)
                StatusText.Text = "Erro ao carregar página: " + args.WebErrorStatus;
            else if (!_handled)
                StatusText.Text = "Faça login com sua conta Google.";
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
