using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace TrueNASRemoteSignaller {
	public class ApiKeyProtector {
		public static string Encrypt(string apiKey) {
			if(string.IsNullOrEmpty(apiKey)) { return ""; }

			byte[] data = Encoding.UTF8.GetBytes(apiKey);
			byte[] entropy = Encoding.UTF8.GetBytes("TrueNAS-Remote1234");
			byte[] encrypted = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
			return Convert.ToBase64String(encrypted);
		}

		public static string Decrypt(string encryptedApiKey) {
			if (string.IsNullOrEmpty(encryptedApiKey)) { return ""; }

			byte[] encryptedData = Convert.FromBase64String(encryptedApiKey);
			byte[] entropy = Encoding.UTF8.GetBytes("TrueNAS-Remote1234");
			byte[] decrypted = ProtectedData.Unprotect(encryptedData, entropy, DataProtectionScope.CurrentUser);
			return Encoding.UTF8.GetString(decrypted);
		}
	}
}
