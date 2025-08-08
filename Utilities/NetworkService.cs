using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TrueNASRemoteSignaller.Models;

namespace TrueNASRemoteSignaller.Utilities {
	public class NetworkService {
		public static string ConvertToWebSocketUrl(string url) {
			if (string.IsNullOrWhiteSpace(url))
				return string.Empty;

			if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				return "wss://" + url.Substring("https://".Length);

			if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
				return "ws://" + url.Substring("http://".Length);

			return url; // Return unchanged if already ws:// or wss://
		}

		public static HttpClient GetHttpClient(string baseUri, string key) {
			var handler = new HttpClientHandler { // Force the client to accept the default self-signed ssl cert
				ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) => true
			};

			HttpClient client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
			client.BaseAddress = new Uri(baseUri ?? "https://truenas.local");
			client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

			return client;
		}

		public static async Task<string> SendRestApiRequestAsync(ServerInstance server, string req, string jsonBody = "", string reqType = "GET") {
			reqType = reqType.ToUpper();
			HttpResponseMessage response;
			using (HttpClient client = GetHttpClient(server.APIEndpoint, server.GetUnencryptedKey())) {

				// Check request type
				try {
					switch (reqType) {
						case "GET":
							response = await client.GetAsync(req);
							break;
						case "POST":
							response = await client.PostAsync(req, new StringContent(jsonBody, Encoding.UTF8, "application/json"));
							break;
						default:
							throw new Exception($"Unknown request type {reqType}");
					}
				}
				catch (TaskCanceledException) { // Handle timeout
					throw new TimeoutException($"Request timed out after {client.Timeout.TotalSeconds} seconds.");
				}
			}


			// Handle status
			switch (response.StatusCode) {
				case HttpStatusCode.NotFound:
					throw new Exception("API error: 404 - The requested endpoint was not found.");
					break;
				case HttpStatusCode.InternalServerError: // Truenas sends wrong error code it seems
				case HttpStatusCode.Unauthorized:
					throw new UnauthorizedAccessException("API error: 401 - Check your API key or server permissions.");
					break;
				case HttpStatusCode.OK:
					return await response.Content.ReadAsStringAsync();
					break;
				default:
					throw new Exception($"API error: {(int)response.StatusCode} - {response.ReasonPhrase}: {response.RequestMessage}");

			}

		}

		public static void SendWOL(ServerInstance server) {
			string cleanedMac = server.MACAddress.Replace(":", "").Replace("-", "");
			byte[] macBytes = Enumerable.Range(0, 6)
				.Select(i => Convert.ToByte(cleanedMac.Substring(i * 2, 2), 16))
				.ToArray();

			byte[] packet = new byte[102];
			for (int i = 0; i < 6; i++) packet[i] = 0xFF;
			for (int i = 6; i < packet.Length; i += macBytes.Length)
				Array.Copy(macBytes, 0, packet, i, macBytes.Length);

			using var client = new UdpClient();
			client.EnableBroadcast = true;

			var broadcastIP = IPAddress.Parse(server.BroadcastIP);
			var endpoint = new IPEndPoint(broadcastIP, 9);

			client.Send(packet, packet.Length, endpoint);
		}

		public static bool IsValidIPv4(string input) {
			return IPAddress.TryParse(input, out IPAddress ip) &&
				   ip.AddressFamily == AddressFamily.InterNetwork;
		}

		public static bool IsValidMacAddress(string input) {
			var regex = new Regex(@"^([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})$");
			return regex.IsMatch(input);
		}

		public static ClientWebSocket GetWebSocketClient() {
			ClientWebSocket client = new ClientWebSocket();
			client.Options.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true; // Force accept self-signed certs
			return client;
		}

		public static async Task<string> SendWebsocketAsync(ClientWebSocket client, object message) {
			string json = JsonSerializer.Serialize(message);
			byte[] buffer = Encoding.UTF8.GetBytes(json);
			await client.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
			return await _receiveWebsocketResponseAsync(client);
		}

		private static async Task<string> _receiveWebsocketResponseAsync(ClientWebSocket client) {
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			var buffer = new byte[4096];

			try {
				var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
				string response = Encoding.UTF8.GetString(buffer, 0, result.Count);
				//Debug.WriteLine(response);

				using var doc = JsonDocument.Parse(response);
				var root = doc.RootElement;

				// Check if it's a result message with an error
				if (root.TryGetProperty("msg", out var msgProp) && msgProp.GetString() == "result" &&
					root.TryGetProperty("error", out var errorObj)) {

					int errorCode = errorObj.TryGetProperty("error", out var codeProp) ? codeProp.GetInt32() : -1;
					string reason = errorObj.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() ?? "No reason provided" : "No reason provided";

					// Handle auth errors
					if(errorCode == 207) {
						throw new UnauthorizedAccessException("API auth error.");
					}

					throw new Exception(reason);
				}

				return response;
			}
			catch (OperationCanceledException) {
				throw new TimeoutException("Timed out.");
			}
			catch (JsonException ex) {
				throw new Exception($"Unknown error. - Error parsing json response.");
			}
		}


		public static async Task LogWebSocketResponsesAsync(ClientWebSocket client) {
			var buffer = new byte[4096];

			while (client.State == WebSocketState.Open) {
				var result = await client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
				if (result.MessageType == WebSocketMessageType.Close) {
					Debug.WriteLine("Connection closed by server.");
					break;
				}

				string response = Encoding.UTF8.GetString(buffer, 0, result.Count);
				Debug.WriteLine($"Received: {response}");
			}
		}
	}
}
