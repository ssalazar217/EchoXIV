using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EchoXIV.Services;

/// <summary>
/// Free Yandex Translate engine (mobile endpoint, no API key).
/// Session id is a dash-less UUID.
/// </summary>
public class YandexTranslatorService : ITranslationService, IRomanizationService
{
    private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

    private const string ApiUrl = "https://translate.yandex.net/api/v1/tr.json/translate?id={0}-0-0&srv=android";

    private const string AppUserAgent = "Yandex Translate/21.11.2";

    public string Name => "Yandex";

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var source = string.IsNullOrWhiteSpace(sourceLang) ? "auto" : MapLanguage(sourceLang);
        var target = string.IsNullOrWhiteSpace(targetLang) ? "en" : MapLanguage(targetLang);

        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            return text;

        try
        {
            // Yandex requires lowercase; target-only pair enables auto-detect.
            var langPair = string.Equals(source, "auto", StringComparison.OrdinalIgnoreCase)
                ? target.ToLowerInvariant()
                : source.ToLowerInvariant() + "-" + target.ToLowerInvariant();

            var sessionId = Guid.NewGuid().ToString("N");
            var url = string.Format(ApiUrl, sessionId);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.UserAgent.ParseAdd(AppUserAgent);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("text", text),
                new System.Collections.Generic.KeyValuePair<string, string>("lang", langPair),
                new System.Collections.Generic.KeyValuePair<string, string>("options", "4"),
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == (HttpStatusCode)429)
                throw new TranslationRateLimitException(Name, "Yandex rate limit reached");

            if (!response.IsSuccessStatusCode)
                return text;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseResponse(body) ?? text;
        }
        catch (TranslationRateLimitException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return text;
        }
    }

    /// <summary>Maps EchoXIV codes to Yandex codes (zh, pt, no).</summary>
    private static string MapLanguage(string lang)
    {
        return lang switch
        {
            "zh-CN" => "zh",
            "zh-TW" => "zh",
            "pt-BR" => "pt",
            "no" => "no",
            _ => lang,
        };
    }

    /// <summary>Parses {"code":200,"lang":"en-es","text":["..."]}.</summary>
    private static string? ParseResponse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            var root = JObject.Parse(body);
            if ((int?)root["code"] != 200) return null;

            if (!(root["text"] is JArray parts)) return null;

            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                if (part == null || part.Type == JTokenType.Null) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(part.ToString());
            }

            return sb.ToString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public Task<string?> RomanizeAsync(string text, string sourceLang, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public bool CanRomanize(string sourceLang, string text) => false;

    public void Dispose()
    {
        // Intentionally left blank: shared HttpClient is process-wide.
    }
}