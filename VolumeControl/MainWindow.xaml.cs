using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace VolumeControl
{
    // ReSharper disable once RedundantExtendsListEntry
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            version_view_protocol.Content = "protocol v" + App.PROTOCOL_VERSION;
            version_view_app.Content = "application " + App.APPLICATION_VERSION;

            var ipAddress = App.GetLocalIPAddress();
            server_ip.Content = ipAddress;
            //Console.WriteLine("ipAddress: " + ipAddress);

            updateConnectionStatus();
        }

        private void DownloadLatest_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        public void updateConnectionStatus()
        {
            var app = App.instance;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (app.Server == null || !app.Server.isRunning())
                {
                    server_status.Content = "Offline";
                    start_button.IsEnabled = true;
                    stop_button.IsEnabled = false;
                    server_port.IsEnabled = true;
                }
                else
                {
                    server_status.Content = "Online";
                    start_button.IsEnabled = false;
                    stop_button.IsEnabled = true;
                    server_port.IsEnabled = false;
                }
            }));
        }

        private void start_button_Click(object sender, RoutedEventArgs e)
        {
            App.instance.startServer();
        }

        private void stop_button_Click(object sender, RoutedEventArgs e)
        {
            App.instance.stopServer();
        }

        private void exit_button_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void start_boot_Click(object sender, RoutedEventArgs e)
        {
            App.StartOnBoot();

            MessageBox.Show("PcVolumeControl will now automatically start when your computer boots.", "PcVolumeControl", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void stop_boot_Click(object sender, RoutedEventArgs e)
        {
            App.RemoveOnBoot();

            MessageBox.Show("PcVolumeControl will no longer start with your computer.", "PcVolumeControl", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
