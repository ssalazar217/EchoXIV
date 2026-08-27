using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EchoXIV.Services;

/// <summary>
/// LibreTranslate (Argos/NMT) translation engine.
/// </summary>
public class LibreTranslateTranslatorService : ITranslationService, IDisposable
{
    public string Name => string.IsNullOrWhiteSpace(_instance?.Name) ? "LibreTranslate" : _instance.Name;

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private readonly EngineInstance? _instance;

    private const string DefaultTranslateUrl = "https://libretranslate.com/translate";

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(EchoXIVClient.UserAgent);
        return httpClient;
    }

    public LibreTranslateTranslatorService() { }

    public LibreTranslateTranslatorService(EngineInstance instance)
    {
        _instance = instance;
    }

    private string TranslateUrl => string.IsNullOrWhiteSpace(_instance?.Endpoint) ? DefaultTranslateUrl : _instance.Endpoint.TrimEnd('/');

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        if (sourceLang == targetLang && sourceLang != "auto")
            return text;

        try
        {
            var payload = new JObject
            {
                ["q"] = text,
                ["source"] = "auto",
                ["target"] = MapLanguage(targetLang),
            };
            var apiKey = _instance?.ApiKey?.Trim();
            if (!string.IsNullOrWhiteSpace(apiKey))
                payload["api_key"] = apiKey;

            using var request = new HttpRequestMessage(HttpMethod.Post, TranslateUrl);
            request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

            var response = await SharedHttpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new TranslationRateLimitException(Name, "LibreTranslate rate limit exceeded");

            if (!response.IsSuccessStatusCode)
                return text;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var jObject = JObject.Parse(json);

            return (string?)jObject["translatedText"] ?? text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TranslationRateLimitException)
        {
            throw;
        }
        catch (Exception)
        {
            return text;
        }
    }

    /// <summary>
    /// Maps language codes to LibreTranslate server codes
    /// (nb=Norwegian, zh=Simplified Chinese, zt=Traditional Chinese).
    /// </summary>
    private static string MapLanguage(string lang)
    {
        return lang switch
        {
            "zh-CN" => "zh",
            "zh-TW" => "zt",
            "no" => "nb",
            _ => lang,
        };
    }

    public void Dispose()
    {
        // Intentionally left blank: SharedHttpClient is process-wide.
    }
}
