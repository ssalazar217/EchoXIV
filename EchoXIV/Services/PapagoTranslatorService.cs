using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Net;
using System.Collections.Generic;

namespace EchoXIV.Services;

public class PapagoTranslatorService : ITranslationService
{
    private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler 
    { 
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate 
    });
    
    // Inyectamos configuración para acceder a la clave dinámica
    private readonly Configuration _configuration;

    public PapagoTranslatorService(Configuration configuration)
    {
        _configuration = configuration;
    }

    public string Name => "Papago";

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        try
        {
            var uuid = Guid.NewGuid().ToString();
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            
            // Usar clave desde config
            var hmacKey = _configuration.PapagoVersionKey; 
            if (string.IsNullOrEmpty(hmacKey)) hmacKey = Constants.DefaultPapagoVersionKey;

            var signature = GenerateSignature(uuid, timestamp, hmacKey);
            var authHeader = $"PPG {uuid}:{signature}";

            using var request = new HttpRequestMessage(HttpMethod.Post, Constants.PapagoApiUrl);
            
            // Headers obligatorios
            request.Headers.Add("Authorization", authHeader);
            request.Headers.Add("Timestamp", timestamp);
            request.Headers.Add("device-type", "pc");
            request.Headers.Add("x-apigw-partnerid", "papago");
            request.Headers.Add("Origin", Constants.PapagoOrigin);
            request.Headers.Add("Referer", Constants.PapagoReferer);
            request.Headers.Add("User-Agent", Constants.UserAgent);

            // Payload en formato x-www-form-urlencoded
            var dict = new Dictionary<string, string>
            {
                { "deviceId", uuid },
                { "locale", "en-US" },
                { "dict", "false" },
                { "dictDisplay", "0" },
                { "honorific", "false" },
                { "instant", "false" },
                { "paging", "false" },
                { "source", sourceLang },
                { "target", targetLang },
                { "text", text },
                { "authroization", authHeader },
                { "timestamp", timestamp }
            };

            request.Content = new FormUrlEncodedContent(dict);

            var response = await _httpClient.SendAsync(request);
            
            if (response.StatusCode == (HttpStatusCode)429)
                throw new TranslationRateLimitException("Papago", "Papago rate limit reached");

            if (!response.IsSuccessStatusCode)
                return text;

            var responseJson = await response.Content.ReadAsStringAsync();
            var papagoResponse = JsonConvert.DeserializeObject<dynamic>(responseJson);
            
            if (papagoResponse == null) return text;

            return (string?)papagoResponse.translatedText ?? text;
        }
        catch (TranslationRateLimitException) { throw; }
        catch (Exception)
        {
            return text;
        }
    }

    private string GenerateSignature(string uuid, string timestamp, string key)
    {
        var data = $"{uuid}\n{timestamp}";
        using var hmac = new HMACMD5(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }
}
