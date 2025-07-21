using EasyWakeOnLan;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace TrueNASRemoteSignaller {
	public class ServerInstance {
		public int InstanceID { get; set; }
		public string ServerName { get; set; }
		public string? MACAddress { get; set; }
		public string? BroadcastIP { get; set; }
		public string? APIEndpoint { get; set; }
		public string? APIKey { get; set; }
		public string? APIType { get; set; }

		public ServerInstance(string serverName, int instanceID = -1) {
			ServerName = serverName;
			if (instanceID == -1) {
				this.InstanceID = _getNextInstanceID();
			}
			else {
				InstanceID = instanceID;
			}
		}

		private static List<ServerInstance> _deserialiseInstances() {
			List<ServerInstance> instances = new List<ServerInstance>();

			// Check that a value has been assigned to the SavedServers setting
			if (string.IsNullOrEmpty(Properties.Settings.Default.SavedServers)) {
				return instances;
			}

			// Attempt to deserialise instances from the app settings
			instances = JsonSerializer.Deserialize<List<ServerInstance>>(Properties.Settings.Default.SavedServers);

			// Verify that instances is not null
			if (instances == null) {
				throw new Exception("Unable to deserliase instances");
			}

			return instances;
		}

		private static string _serialiseinstances(List<ServerInstance> instances) {
			return JsonSerializer.Serialize(instances);
		}

		private static void _updateInstancesSetting(List<ServerInstance> instances) {
			// Verify that instances is not null
			if(instances == null) {
				throw new Exception("Null value provided for saving");
			}

			// Attempt to update the settings with the new list
			string seralisedInstances = _serialiseinstances(instances);

			// Verify that the serialised value returned is not null
			if (string.IsNullOrEmpty(seralisedInstances)) {
				throw new Exception("Null value returned during serialisation");
			}

			// Set the new serialised value
			Properties.Settings.Default.SavedServers = seralisedInstances;
			Properties.Settings.Default.Save();
		}

		private static int _getNextInstanceID() {
			List<ServerInstance> instances = _deserialiseInstances();
			int highestID = instances.OrderByDescending(i => i.InstanceID).FirstOrDefault()?.InstanceID ?? 0;

			if(highestID == 0) {
				return 1;
			}

			return highestID + 1;
		}

		public static List<ServerInstance> GetServerInstances() {
			return _deserialiseInstances().OrderBy(s => s.ServerName).ToList();
		}

		public static void DeleteAllInstances() {
			_updateInstancesSetting(new List<ServerInstance>());
		}

		public string GetUnencryptedKey() {
			return ApiKeyProtector.Decrypt(APIKey);
		}

		public bool IsUsingWebsocket() {
			return APIType?.ToUpper() == "WEBSOCKET";
		}

		public void OpenWebUi() {
			if (string.IsNullOrEmpty(APIEndpoint)) {
				throw new Exception("No Base URI set. Please fill this field in order to open the Web UI.");
			}

			if (Uri.IsWellFormedUriString(APIEndpoint, UriKind.Absolute)) {
				Process.Start(new ProcessStartInfo(APIEndpoint) { UseShellExecute = true });
			}
			else {
				throw new Exception("Invalid URL provided. Cannot open the Web UI.");
			}
		}

		public async Task ShutdownServer() {
			if(!IsApiConfigured()) {
				throw new Exception("This server has not been configured for API ineractions. Please ensure that the Base URI and API Key fields have been set.");
			}

			if(IsUsingWebsocket()) {
				using ClientWebSocket client = NetworkService.GetWebSocketClient();
				await client.ConnectAsync(new Uri(NetworkService.ConvertToWebSocketUrl(APIEndpoint) + "/websocket"), CancellationToken.None);

				// Send connect handshake
				await NetworkService.SendWebsocketAsync(client, new {
					msg = "connect",
					version = "1",
					support = new[] { "1" }
				});

				// Authenticate with API key
				await NetworkService.SendWebsocketAsync(client, new {
					id = 2,
					msg = "method",
					method = "auth.login_with_api_key",
					@params = new[] { GetUnencryptedKey() }
				});

				// Send system.shutdown with reason
				await NetworkService.SendWebsocketAsync(client, new {
					id = 3,
					msg = "method",
					method = "system.shutdown",
					@params = new[] { "TrueNAS Signaller remote shutdown"}
				});
			}
			else {
				await NetworkService.SendRestApiRequestAsync(this, "/api/v2.0/system/shutdown", "{\"reason\": \"TrueNAS Signaller remote shutdown\"}", "POST");
			}
		}

		public async Task RestartServer() {
			if (!IsApiConfigured()) {
				throw new Exception("This server has not been configured for API ineractions. Please ensure that the Base URI and API Key fields have been set.");
			}

			if (IsUsingWebsocket()) {
				using ClientWebSocket client = NetworkService.GetWebSocketClient();
				await client.ConnectAsync(new Uri(NetworkService.ConvertToWebSocketUrl(APIEndpoint) + "/websocket"), CancellationToken.None);

				// Send connect handshake
				await NetworkService.SendWebsocketAsync(client, new {
					msg = "connect",
					version = "1",
					support = new[] { "1" }
				});

				// Authenticate with API key (used as username, empty password)
				await NetworkService.SendWebsocketAsync(client, new {
					id = 2,
					msg = "method",
					method = "auth.login_with_api_key",
					@params = new[] { GetUnencryptedKey() }
				});

				// Send system.shutdown with reason
				await NetworkService.SendWebsocketAsync(client, new {
					id = 3,
					msg = "method",
					method = "system.reboot",
					@params = new[] { "TrueNAS Signaller remote reboot" }
				});
			}
			else {
				await NetworkService.SendRestApiRequestAsync(this, "/api/v2.0/system/reboot", "{\"reason\": \"TrueNAS Signaller remote restart\"}", "POST");
			}
		}

		public async Task<string> GetSystemState() {
			if (!IsApiConfigured()) {
				throw new Exception("This server has not been configured for API ineractions. Please ensure that the Base URI and API Key fields have been set.");
			}

			string state;
			if (IsUsingWebsocket()) {
				using ClientWebSocket client = NetworkService.GetWebSocketClient();
				await client.ConnectAsync(new Uri(NetworkService.ConvertToWebSocketUrl(APIEndpoint) + "/websocket"), CancellationToken.None);

				// Send connect handshake
				await NetworkService.SendWebsocketAsync(client, new {
					msg = "connect",
					version = "1",
					support = new[] { "1" }
				});

				// Authenticate with API key (used as username, empty password)
				await NetworkService.SendWebsocketAsync(client, new {
					id = 2,
					msg = "method",
					method = "auth.login_with_api_key",
					@params = new[] { GetUnencryptedKey() }
				});

				// Send system.shutdown with reason
				string res = await NetworkService.SendWebsocketAsync(client, new {
					id = 3,
					msg = "method",
					method = "system.state"
				});

				state = JsonDocument.Parse(res).RootElement.GetProperty("result").GetString();

			}
			else {
				state = await NetworkService.SendRestApiRequestAsync(this, "/api/v2.0/system/state");
			}

			return state.Replace("\"", "");
		}

		public bool IsApiConfigured() {
			if (string.IsNullOrEmpty(APIEndpoint) || string.IsNullOrEmpty(APIKey)) {
				return false;
			}

			return true;
		}

		public void SendWOL() {
			// Ensure the server is configured for WOL
			if(!IsConfiguredForWOL()) {
				throw new Exception("This server is not configured for WOL. The magic packet was not sent.");
			}

			NetworkService.SendWOL(this);
		}

		public bool IsConfiguredForWOL() {
			if(string.IsNullOrEmpty(BroadcastIP) || string.IsNullOrEmpty(MACAddress)) {
				return false;
			}

			if(!NetworkService.IsValidIPv4(BroadcastIP) || !NetworkService.IsValidMacAddress(MACAddress)) {
				return false;
			}

			return true;
		}

		public void SaveInstance() {
			// Get the current list of instances
			List<ServerInstance> instances = new List<ServerInstance>();
			try {
				instances = GetServerInstances();
			}
			catch (Exception ex) {
				Trace.WriteLine(ex.Message);
				throw new Exception($"Failed to get server list - Save failed. ({ex.Message})");
			}
			
			// Check if a server instance with this ID already exists - if so, replace it with this
			ServerInstance existingInstance = instances.FirstOrDefault(i => i.InstanceID == this.InstanceID);
			if (existingInstance != null) {
				instances.Remove(existingInstance);
			}
			instances.Add(this);


			// Update setting
			try {
				_updateInstancesSetting(instances);
			}
			catch (Exception ex) {
				Trace.WriteLine(ex.Message);
				throw new Exception($"Failed to update servers list - Save failed. ({ex.Message})");
			}
		}

		public static void DeleteInstanceByID(int instanceID) {
			List<ServerInstance> instances = new List<ServerInstance>();

			// Attempt to deserialise instances from the settings value
			try {
				instances = GetServerInstances();
			}
			catch (Exception ex) {
				Trace.WriteLine(ex.Message);
				throw new Exception($"Failed to get server list - Deletion failed. ({ex.Message})");
			}

			// Attempt to find this instance in the instances list
			ServerInstance foundInstance = instances.FirstOrDefault(i => i.InstanceID == instanceID) ?? null;

			// Verify that the instance was found
			if (foundInstance == null) {
				throw new Exception("Instance not found in instances list - Deletion failed.");
			}

			// Attempt to remove the found instance from the instances list
			instances.Remove(foundInstance);

			// Update setting
			try {
				_updateInstancesSetting(instances);
			}
			catch (Exception ex) {
				Trace.WriteLine(ex.Message);
				throw new Exception($"Failed to update servers list - Deletion failed. ({ex.Message})");
			}
		}

		public void DeleteInstance() {
			DeleteInstanceByID(this.InstanceID);
		}

	}
}
