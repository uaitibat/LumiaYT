using System;
using LumiaYT.Models;
using LumiaYT.Services;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LumiaYT.Pages
{
    public sealed partial class PlayerPage : Page
    {
        private MediaPlayer _player;
        private string _videoId;

        public PlayerPage() { InitializeComponent(); }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var item = e.Parameter as VideoItem;
            if (item == null) return;

            _videoId = item.Id;
            TitleText.Text = item.Title ?? "";
            ChannelText.Text = item.ChannelTitle ?? "";
            DescText.Text = item.Description ?? "";
            StatusText.Text = "Resolvendo stream compatível...";

            EnsurePlayer();
            try
            {
                var stream = await StreamResolverService.ResolveAsync(item.Id);
                var dbg = StreamResolverService.LastDebug;
                if (stream != null && !string.IsNullOrWhiteSpace(stream.Url))
                {
                    var source = MediaSource.CreateFromUri(new Uri(stream.Url));
                    _player.Source = source;
                    _player.Play();
                    StatusText.Text = "Reproduzindo " + (stream.QualityLabel ?? "") + " via " + (stream.Source ?? "?") + ".";
                }
                else
                {
                    StatusText.Text = "Não consegui resolver um stream direto compatível com W10M. " + (string.IsNullOrWhiteSpace(dbg) ? "" : "Debug: " + dbg);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Erro ao resolver stream: " + ex.Message + " " + StreamResolverService.LastDebug;
            }
        }

        private void EnsurePlayer()
        {
            if (_player != null) return;
            _player = new MediaPlayer();
            _player.AutoPlay = true;
            _player.MediaFailed += Player_MediaFailed;
            _player.MediaOpened += Player_MediaOpened;
            Player.SetMediaPlayer(_player);
        }

        private async void Player_MediaOpened(MediaPlayer sender, object args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                if (StatusText.Text.StartsWith("Resolvendo")) StatusText.Text = "Reproduzindo.";
            });
        }

        private async void Player_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                StatusText.Text = "Falha na reprodução (" + args.Error + "). Tente Abrir no navegador.";
            });
        }

        private async void OpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_videoId)) return;
            await Launcher.LaunchUriAsync(new Uri("https://www.youtube.com/watch?v=" + _videoId));
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            CleanupPlayer();
        }

        private void CleanupPlayer()
        {
            try
            {
                if (_player != null)
                {
                    _player.Pause();
                    _player.Source = null;
                    Player.SetMediaPlayer(null);
                    _player.MediaFailed -= Player_MediaFailed;
                    _player.MediaOpened -= Player_MediaOpened;
                    _player.Dispose();
                    _player = null;
                }
            }
            catch { }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            CleanupPlayer();
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
