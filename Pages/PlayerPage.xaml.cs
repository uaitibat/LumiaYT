using System;
using LumiaYT.Models;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace LumiaYT.Pages
{
    public sealed partial class PlayerPage : Page
    {
        public PlayerPage() { InitializeComponent(); }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            var item = e.Parameter as VideoItem;
            if (item == null) return;

            TitleText.Text = item.Title;
            ChannelText.Text = item.ChannelTitle;
            DescText.Text = item.Description;

            // MVP: use a public embed/stream fallback URL strategy is not implemented here.
            // For now, open via web URI where possible; proper stream extraction should be added next.
            StatusText.Text = "Player MVP: reprodução direta do YouTube precisa de resolvedor de stream compatível com W10M. " +
                              "Por enquanto, abra o vídeo no navegador/Venetium: https://www.youtube.com/watch?v=" + item.Id;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Player.MediaPlayer != null) { Player.MediaPlayer.Pause(); }
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
