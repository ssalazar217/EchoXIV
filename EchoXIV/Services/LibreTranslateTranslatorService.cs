using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EchoXIV.Services;

/// <summary>
/// LibreTranslate (Argos/NMT) hosted on the public EchoXIV server; no API key.
/// The server identifies clients via User-Agent "EchoXIV/&lt;version&gt;".
/// </summary>
public class LibreTranslateTranslatorService : ITranslationService, IDisposable
{
    public string Name => "LibreTranslate";

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private const string TranslateUrl = "https://echoxiv.ddns.net/translate";

    private static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient();
        // Server-side validator requires "User-Agent: EchoXIV/<version>".
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(EchoXIVClient.UserAgent);
        return httpClient;
    }

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
    /// Maps EchoXIV codes to the LibreTranslate server codes
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