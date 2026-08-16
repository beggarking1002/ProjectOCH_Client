using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Auth
{
	public sealed class GoogleOAuthResult
	{
		public string AuthorizationCode;
		public string CodeVerifier;
		public string RedirectUri;
	}

	public static class GoogleOAuthLogin
	{
		[Serializable]
		sealed class GoogleAuthConfig
		{
			public string client_id;
		}

		const string ConfigFileName = "GoogleAuth.json";
		const int TimeoutSeconds = 120;

		public static bool IsConfigured => TryLoadConfig(out _);

		public static async Task<GoogleOAuthResult> AuthorizeAsync()
		{
			if (!TryLoadConfig(out GoogleAuthConfig config))
				throw new InvalidOperationException($"Missing or invalid StreamingAssets/{ConfigFileName}.");

			string state = Base64Url(RandomBytes(32));
			string verifier = Base64Url(RandomBytes(64));
			string challenge;
			using (SHA256 sha256 = SHA256.Create())
				challenge = Base64Url(sha256.ComputeHash(Encoding.ASCII.GetBytes(verifier)));

			TcpListener callback = new TcpListener(IPAddress.Loopback, 0);
			callback.Start(1);
			int port = ((IPEndPoint)callback.LocalEndpoint).Port;
			string redirectUri = $"http://127.0.0.1:{port}/";
			string authorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth" +
				$"?client_id={Encode(config.client_id)}" +
				$"&redirect_uri={Encode(redirectUri)}" +
				"&response_type=code" +
				"&scope=openid%20email%20profile" +
				$"&state={Encode(state)}" +
				$"&code_challenge={Encode(challenge)}" +
				"&code_challenge_method=S256" +
				"&prompt=select_account";

			Application.OpenURL(authorizeUrl);
			try
			{
				Task<TcpClient> acceptTask = callback.AcceptTcpClientAsync();
				if (await Task.WhenAny(acceptTask, Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds))) != acceptTask)
					throw new TimeoutException("Google login timed out.");

				using (TcpClient client = await acceptTask)
				using (NetworkStream stream = client.GetStream())
				using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
				{
					string requestLine = await reader.ReadLineAsync();
					if (string.IsNullOrWhiteSpace(requestLine))
						throw new InvalidOperationException("Google callback was empty.");
					string[] requestParts = requestLine.Split(' ');
					if (requestParts.Length < 2)
						throw new InvalidOperationException("Google callback was malformed.");

					Uri callbackUri = new Uri(redirectUri.TrimEnd('/') + requestParts[1]);
					string returnedState = QueryValue(callbackUri.Query, "state");
					string code = QueryValue(callbackUri.Query, "code");
					string error = QueryValue(callbackUri.Query, "error");
					bool success = string.IsNullOrEmpty(error) && returnedState == state && !string.IsNullOrEmpty(code);
					string html = success
						? "<html><body><h2>로그인이 완료되었습니다.</h2><p>게임으로 돌아가세요.</p></body></html>"
						: "<html><body><h2>로그인에 실패했습니다.</h2><p>게임으로 돌아가 다시 시도하세요.</p></body></html>";
					byte[] htmlBytes = Encoding.UTF8.GetBytes(html);
					string headers = "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
						$"Content-Length: {htmlBytes.Length}\r\nConnection: close\r\n\r\n";
					byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
					await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
					await stream.WriteAsync(htmlBytes, 0, htmlBytes.Length);

					if (!success)
						throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "Google login state mismatch." : $"Google login failed: {error}");
					return new GoogleOAuthResult
					{
						AuthorizationCode = code,
						CodeVerifier = verifier,
						RedirectUri = redirectUri,
					};
				}
			}
			finally
			{
				callback.Stop();
			}
		}

		static bool TryLoadConfig(out GoogleAuthConfig config)
		{
			config = null;
			try
			{
				string path = Path.Combine(Application.streamingAssetsPath, ConfigFileName);
				if (!File.Exists(path))
					return false;
				config = JsonUtility.FromJson<GoogleAuthConfig>(File.ReadAllText(path));
				return config != null && !string.IsNullOrWhiteSpace(config.client_id) &&
					!config.client_id.StartsWith("REPLACE_", StringComparison.Ordinal);
			}
			catch (Exception exception)
			{
				UnityEngine.Debug.LogWarning($"Failed to read Google auth config: {exception.Message}");
				return false;
			}
		}

		static byte[] RandomBytes(int count)
		{
			byte[] bytes = new byte[count];
			using (RandomNumberGenerator random = RandomNumberGenerator.Create())
				random.GetBytes(bytes);
			return bytes;
		}

		static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
		static string Encode(string value) => Uri.EscapeDataString(value ?? string.Empty);

		static string QueryValue(string query, string key)
		{
			foreach (string pair in query.TrimStart('?').Split('&'))
			{
				int separator = pair.IndexOf('=');
				string pairKey = separator >= 0 ? pair.Substring(0, separator) : pair;
				if (Uri.UnescapeDataString(pairKey) != key)
					continue;
				return separator >= 0 ? Uri.UnescapeDataString(pair.Substring(separator + 1).Replace('+', ' ')) : string.Empty;
			}
			return string.Empty;
		}
	}
}
