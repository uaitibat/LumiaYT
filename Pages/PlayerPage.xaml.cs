using System;
using System.Collections.Generic;
using System.Linq;
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
        private List<ResolvedStream> _streams = new List<ResolvedStream>();
        private bool _suppressQualityChange;

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
            QualityCombo.IsEnabled = false;

            EnsurePlayer();
            try
            {
                var streams = await StreamResolverService.ResolveAllAsync(item.Id);
                _streams = streams ?? new List<ResolvedStream>();
                var selected = StreamResolverService.PickDefault(_streams);

                if (selected != null && !string.IsNullOrWhiteSpace(selected.Url))
                {
                    PopulateQualityCombo(selected);
                    PlayStream(selected, false);
                }
                else
                {
                    StatusText.Text = "Não consegui resolver um stream direto compatível com W10M. " + (string.IsNullOrWhiteSpace(StreamResolverService.LastDebug) ? "" : "Debug: " + StreamResolverService.LastDebug);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Erro ao resolver stream: " + ex.Message + " " + StreamResolverService.LastDebug;
            }
        }

        private void PopulateQualityCombo(ResolvedStream selected)
        {
            _suppressQualityChange = true;
            try
            {
                var list = _streams
                    .Where(s => !s.IsAudioOnly && !s.IsVideoOnly)
                    .GroupBy(s => (s.QualityLabel ?? "") + "|" + (s.Source ?? "") + "|" + s.Height)
                    .Select(g => g.First())
                    .OrderByDescending(s => s.Height)
                    .ThenByDescending(s => s.MimeType != null && s.MimeType.IndexOf("mp4", StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                if (list.Count == 0) list = _streams.ToList();

                QualityCombo.ItemsSource = list;
                var idx = list.FindIndex(s => s.Url == selected.Url);
                QualityCombo.SelectedIndex = idx >= 0 ? idx : 0;
                QualityCombo.IsEnabled = list.Count > 1;
            }
            finally
            {
                _suppressQualityChange = false;
            }
        }

        private void PlayStream(ResolvedStream stream, bool keepPosition)
        {
            if (stream == null || string.IsNullOrWhiteSpace(stream.Url)) return;
            EnsurePlayer();

            var pos = TimeSpan.Zero;
            try { if (keepPosition && _player.PlaybackSession != null) pos = _player.PlaybackSession.Position; } catch { }

            var source = MediaSource.CreateFromUri(new Uri(stream.Url));
            _player.Source = source;
            if (keepPosition && pos.TotalSeconds > 1)
            {
                try { _player.PlaybackSession.Position = pos; } catch { }
            }
            _player.Play();
            StatusText.Text = "Reproduzindo " + (stream.QualityLabel ?? "Auto") + " via " + (stream.Source ?? "?") + ".";
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
                StatusText.Text = "Falha na reprodução (" + args.Error + "). Tente outra qualidade ou Abrir no navegador.";
            });
        }

        private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressQualityChange) return;
            var stream = QualityCombo.SelectedItem as ResolvedStream;
            if (stream == null) return;
            PlayStream(stream, true);
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
