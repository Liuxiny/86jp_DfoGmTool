using System.Net.Http.Headers;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jp86.GmClient;

public sealed class SecureApiClient : IDisposable
{
    // Entry point is merely obfuscated to keep it out of UI/string scans. It is not a secret.
    private static readonly byte[] EncodedEndpoint = { 50, 46, 46, 42, 41, 96, 117, 117, 107, 104, 110, 116, 104, 104, 107, 116, 107, 105, 111, 116, 107, 109, 96, 98, 106, 106, 107 };
    private const byte EndpointKey = 90;
    private const string CertificateSha256 = "413DACE62AB617DB56A88E2CD65F85216AB8E12D1F0D50A487E561929AA24505";
    private readonly HttpClient _http;

    public SecureApiClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate == null || errors == SslPolicyErrors.RemoteCertificateNotAvailable) return false;
                var hash = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
                return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(CertificateSha256));
            },
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri(DecodeEndpoint()), Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("86JPGM/1.0");
    }

    public void SetToken(string token)
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token) ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<JsonDocument> GetAsync(string path)
    {
        using var response = await _http.GetAsync(path);
        return await ReadAsync(response);
    }

    public async Task<JsonDocument> PostAsync(string path, object? body = null)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body ?? new { }), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(path, content);
        return await ReadAsync(response);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException($"服务返回空响应（{(int)response.StatusCode}）。");
        var document = JsonDocument.Parse(text);
        if (!response.IsSuccessStatusCode)
        {
            var message = document.RootElement.TryGetProperty("error", out var error) ? error.GetString() : "请求失败。";
            document.Dispose();
            throw new InvalidOperationException(message);
        }
        return document;
    }

    private static string DecodeEndpoint()
    {
        var bytes = EncodedEndpoint.Select(value => (byte)(value ^ EndpointKey)).ToArray();
        return Encoding.ASCII.GetString(bytes);
    }

    public void Dispose() => _http.Dispose();
}
