using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using LumiaYT.Pages;

namespace LumiaYT
{
    sealed partial class App : Application
    {
        private bool _backHandlerRegistered;

        public App()
        {
            UnhandledException += OnUnhandledException;
            InitializeComponent();
            Suspending += OnSuspending;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            try
            {
                ApplicationData.Current.LocalSettings.Values["LastCrash"] =
                    (e.Message ?? "Erro inesperado") + "\n" + (e.Exception == null ? string.Empty : e.Exception.ToString());
            }
            catch { }
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                rootFrame.Navigated += OnNavigated;
                Window.Current.Content = rootFrame;
            }

            if (!_backHandlerRegistered)
            {
                SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;
                _backHandlerRegistered = true;
            }

            if (!e.PrelaunchActivated)
            {
                if (rootFrame.Content == null)
                {
                    rootFrame.Navigate(typeof(HomePage), e.Arguments);
                }
                UpdateBackButtonVisibility(rootFrame);
                Window.Current.Activate();
            }
        }

        private void OnNavigated(object sender, NavigationEventArgs e)
        {
            UpdateBackButtonVisibility(sender as Frame);
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame != null && rootFrame.CanGoBack)
            {
                e.Handled = true;
                rootFrame.GoBack();
                UpdateBackButtonVisibility(rootFrame);
            }
        }

        private static void UpdateBackButtonVisibility(Frame frame)
        {
            var nav = SystemNavigationManager.GetForCurrentView();
            nav.AppViewBackButtonVisibility = frame != null && frame.CanGoBack
                ? AppViewBackButtonVisibility.Visible
                : AppViewBackButtonVisibility.Collapsed;
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Falha ao carregar pagina: " + e.SourcePageType.FullName);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            deferral.Complete();
        }
    }
}
