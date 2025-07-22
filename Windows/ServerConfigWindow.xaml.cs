using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MahApps.Metro.Controls;
using TrueNASRemoteSignaller.Models;
using TrueNASRemoteSignaller.Utilities;

namespace TrueNASRemoteSignaller.Windows {
	/// <summary>
	/// Interaction logic for ServerConfigWindow.xaml
	/// </summary>
	public partial class ServerConfigWindow : Window {
		private ServerInstance? _serverInstance;
		private static string _mode;

		public ServerConfigWindow(string mode) {
			_mode = mode.ToLower();
			InitializeComponent();
			_setInitialValues();
		}

		private void _setInitialValues() {
			if(_mode == "edit") {
				_serverInstance = MainWindow.SelectedInstance;
				this.Title = $"TrueNAS Remote Signaller - Server Configuration ({_serverInstance.ServerName})";
				txtServerName.Text = _serverInstance.ServerName;
				txtServerMAC.Text = _serverInstance.MACAddress;
				txtBroadcast.Text = _serverInstance.BroadcastIP;
				txtAPIEndpoint.Text = _serverInstance.APIEndpoint;
				txtAPIKey.Text = _serverInstance.GetUnencryptedKey();
				toggleAPIType.IsOn = _serverInstance.IsUsingWebsocket();
			}
			else { // New
				this.Title = $"TrueNAS Remote Signaller - New Server";
				txtServerMAC.Text = "A1-B2-C3-D4-E5-F6";
				txtBroadcast.Text = "192.168.0.255";
				txtAPIEndpoint.Text = "https://truenas.local";
				btnDelete.IsEnabled = false;
				toggleAPIType.IsOn = true;
			}
		}

		private void btnSave_Click(object sender, RoutedEventArgs e) {
			// Verify that a name has been provided
			if(string.IsNullOrEmpty(txtServerName.Text)) {
				MessageBox.Show("Please provide a name for this server.", "Missing fields", MessageBoxButton.OK, MessageBoxImage.Warning);
				txtServerName.Focus();
				return;
			}

			// Check that the instance is not null
			if (_serverInstance == null) {
				try {
					_serverInstance = new ServerInstance(txtServerName.Text);
				}
				catch (Exception ex) {
					MessageBox.Show(ex.Message, "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}
			}

			// Validate attributes
			if(!NetworkService.IsValidIPv4(txtBroadcast.Text?.Trim()) && !string.IsNullOrEmpty(txtBroadcast.Text)) {
				MessageBox.Show("Invalid Broadcast Address. Example: 192.168.0.255", "Invalid Fields", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}
			else if(!NetworkService.IsValidMacAddress(txtServerMAC.Text?.Trim()) && !string.IsNullOrEmpty(txtServerMAC.Text)) {
				MessageBox.Show("Invalid MAC address. Example: A1-B2-C3-D4-E5-F6", "Invalid Fields", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			 // Set attributes
			_serverInstance.ServerName = txtServerName.Text?.Trim() ?? "Unnamed server";
			_serverInstance.MACAddress = txtServerMAC.Text?.Trim() ?? string.Empty;
			_serverInstance.BroadcastIP = txtBroadcast.Text?.Trim() ?? string.Empty;
			_serverInstance.APIEndpoint = txtAPIEndpoint.Text?.Trim() ?? string.Empty;
			_serverInstance.APIKey = ApiKeyProtector.Encrypt(txtAPIKey.Text?.Trim() ?? string.Empty);
			_serverInstance.APIType = toggleAPIType.IsOn ? "WEBSOCKET" : "REST";

			// Attempt to save changes
			_serverInstance.SaveInstance();

			// Refresh server selection box
			Application.Current.Windows.OfType<MainWindow>().First().UpdateServerCombo();

			// Alert user and close
			MessageBox.Show("Server saved successfully!", "Server saved", MessageBoxButton.OK, MessageBoxImage.Information);
			this.Close();
		}

		private void btnDelete_Click(object sender, RoutedEventArgs e) {
			// Check that the instance is not null
			if (_serverInstance == null) {
				MessageBox.Show("Cannot delete a server that has not been saved.", "Server not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
				return;
			}

			// Ask user to confirm deletion
			MessageBoxResult result = MessageBox.Show($"Are you sure you want to delete server {_serverInstance.ServerName}?", "Delete Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

			if (result == MessageBoxResult.Yes) {

				// Attempt to delete instance
				try {
					_serverInstance.DeleteInstance();
				}
				catch(Exception ex) {
					MessageBox.Show(ex.Message, "Deletion Failed", MessageBoxButton.OK, MessageBoxImage.Error);
					return;
				}

				// Refresh server selection box
				Application.Current.Windows.OfType<MainWindow>().First().UpdateServerCombo();

				// Success message
				MessageBox.Show($"Server {_serverInstance.ServerName} deleted.", "Server Deleted", MessageBoxButton.OK, MessageBoxImage.Information);

				this.Close();
			}
		}

		private void Window_Initialized(object sender, EventArgs e) {
			//_setTextValues();
		}

		private async void btnTestConnection_Click(object sender, RoutedEventArgs e) {
			if(string.IsNullOrEmpty(txtAPIKey.Text) || string.IsNullOrEmpty(txtAPIEndpoint.Text)) {
				MessageBox.Show("API not configured. Please ensure that the API Key and Base URI fields have been set.", "Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			// Create a test instance
			ServerInstance testInstance = new ServerInstance("api-test");
			testInstance.APIKey = ApiKeyProtector.Encrypt(txtAPIKey.Text.Trim());
			testInstance.APIEndpoint = txtAPIEndpoint.Text.Trim();
			testInstance.APIType = toggleAPIType.IsOn ? "WEBSOCKET" : "REST";

			MessageBox.Show("Connection test started; results will be recieved in 10 seconds or less.", "Test Started", MessageBoxButton.OK, MessageBoxImage.Information);

			try {
				await testInstance.GetSystemState();
			}
			catch(TimeoutException) {
				MessageBox.Show("Request timed out. No response from the server after 10 seconds.", "Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}
			catch (Exception ex) {
				MessageBox.Show($"Test failed. Reason: {ex.Message}", "Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			MessageBox.Show("Conneciton established. The connection test was successful.", "Test Succeeded", MessageBoxButton.OK, MessageBoxImage.Information);

		}
    }
}
