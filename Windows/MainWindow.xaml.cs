using ControlzEx.Standard;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TrueNASRemoteSignaller.Models;

namespace TrueNASRemoteSignaller.Windows {
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window {

		public static ServerInstance? SelectedInstance;
		public MainWindow() {
			InitializeComponent();
			_initialiseValues();
		}

		private void _initialiseValues() {
			boxControls.IsEnabled = false;
			btnConfigureServer.IsEnabled = false;
		}

		public void UpdateServerCombo(string indexSetting = "current") {
			// Verify that the combo exists
			if (comboServerSelect == null) throw new Exception("comboServerSelect is null.");

			// Get the current selection index
			int prevNumInstances = comboServerSelect.Items.Count;

			// Populate server selection combo
			try {
				comboServerSelect.ItemsSource = ServerInstance.GetServerInstances();
			}
			catch (Exception ex) {
				throw new Exception($"Failed to get servers - {ex.Message}");
			}
		}

		public async Task UpdateStatusLabel() {
			string status = "No Server Selected";
			// Get status
			if(SelectedInstance == null) {
				status = "No Server Selected";
			}
			else {
				if(SelectedInstance.IsApiConfigured()) {
					try {
						status = await SelectedInstance.GetSystemState(); // Get state from API
						TextInfo textInfo = CultureInfo.CurrentCulture.TextInfo;
						status = textInfo.ToTitleCase(status.ToLower().Replace("_", " ")); // Convert to title case
					}
					catch (Exception) {
						status = "No Connection";
					}
				}
				else {
					status = "Unknown (API Not Configured)";
				}
			}

			// Update label
			lblStatus.Content = status;

			// Update label colour
			switch(status.ToLower()) {
				case "ready":
					lblStatus.Foreground = new SolidColorBrush(Colors.Green);
					break;

				case "error":
				case "shutting down":
				case "no connection":
				case "failed":
					lblStatus.Foreground = new SolidColorBrush(Colors.Red);
					break;

				case "booting":
					lblStatus.Foreground = new SolidColorBrush(Colors.Yellow);
					break;

				default:
					lblStatus.Foreground = new SolidColorBrush(Colors.Black);
					break;
			}
		}

		public bool IsConfigWindowOpen() {
			return Application.Current.Windows.OfType<ServerConfigWindow>().Count() >= 1;
		}

		private void comboServerSelect_Initialized(object sender, EventArgs e) {
			try {
				UpdateServerCombo();
			}
			catch (Exception ex) {
				MessageBox.Show(ex.Message, "Failed to get servers", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}
		}

		private void btnAddServer_Click(object sender, RoutedEventArgs e) {
			ServerConfigWindow serverConfigWindow = new ServerConfigWindow("new");
			serverConfigWindow.Show();
        }

		private void btnConfigureServer_Click(object sender, RoutedEventArgs e) {
			// Check that a server has been selected
			if (comboServerSelect.SelectedItem == null) {
				MessageBox.Show("Please select a server to configure.", "Select a server", MessageBoxButton.OK, MessageBoxImage.Warning);
				comboServerSelect.Focus();
				return;
			}

			// Check that the selected item is a server instance
			if (!(comboServerSelect.SelectedItem is ServerInstance)) {
				MessageBox.Show("Invalid selection. Please select a server to configure", "Select a server", MessageBoxButton.OK, MessageBoxImage.Error);
				comboServerSelect.Focus();
				return;
			}

			// Check that a server config window isn't already open
			ServerConfigWindow? serverConfigWindow = Application.Current.Windows.OfType<ServerConfigWindow>().FirstOrDefault();
			if (serverConfigWindow != null) {
				serverConfigWindow.Activate();
				serverConfigWindow.Focus();
			}
			else {
				serverConfigWindow = new ServerConfigWindow("edit");
				serverConfigWindow.Show();
			}
		}

		private void Window_Closed(object sender, EventArgs e) {
			Application.Current.Shutdown();
		}

		private async void comboServerSelect_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			SelectedInstance = comboServerSelect.SelectedItem as ServerInstance;
			if(SelectedInstance == null) {
				boxControls.IsEnabled = false;
				btnConfigureServer.IsEnabled = false;
			}
			else {
				boxControls.IsEnabled = true;
				btnConfigureServer.IsEnabled = true;
			}

			await UpdateStatusLabel();
		}

		private void Button_Click(object sender, RoutedEventArgs e) {
			try {
				if (SelectedInstance != null) {
					SelectedInstance.SendWOL();
				}
			}
			catch (Exception ex) {
				MessageBox.Show(ex.Message, "Server Wakeup Failed", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			MessageBox.Show("Wake On LAN magic packet sent.", "Wakeup Request Sent", MessageBoxButton.OK, MessageBoxImage.Information);
		}

		private async void Button_Click_1(object sender, RoutedEventArgs e) {
			try {
				if (SelectedInstance != null) {
					await SelectedInstance.ShutdownServer();
				}
			}
			catch (Exception ex) {
				MessageBox.Show(ex.Message, "Server Shutdown Failed", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			MessageBox.Show("Shutdown request sent.", "Request Sent", MessageBoxButton.OK, MessageBoxImage.Information);
			await UpdateStatusLabel();
		}

		private async void Button_Click_2(object sender, RoutedEventArgs e) {
			try {
				if (SelectedInstance != null) {
					await SelectedInstance.RestartServer();
				}
			}
			catch (Exception ex) {
				MessageBox.Show(ex.Message, "Server Restart Failed", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			MessageBox.Show("Restart request sent.", "Request Sent", MessageBoxButton.OK, MessageBoxImage.Information);
			await UpdateStatusLabel();
		}

		private void Button_Click_3(object sender, RoutedEventArgs e) {
			try {
				if (SelectedInstance != null) {
					SelectedInstance.OpenWebUi();
				}
			}
			catch(Exception ex) {
				MessageBox.Show(ex.Message, "Cannot Open Web UI", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private async void Window_ContentRendered(object sender, EventArgs e) {
			while(true) {
				await UpdateStatusLabel();
				await Task.Delay(10000); // Wait 10 seconds
			}
		}
	}
}