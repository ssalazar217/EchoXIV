using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace EchoXIV.Services;

/// <summary>
/// Papago (Naver) engine using the current papago.naver.com web endpoint (no API key).
/// </summary>
public class PapagoTranslatorService : ITranslationService, IRomanizationService
{
    private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

    private const string TranslateUrl = "https://papago.naver.com/api/text/translation";

    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    public PapagoTranslatorService()
    {
    }

    public string Name => "Papago";

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        if (sourceLang == targetLang && sourceLang != "auto")
            return text;

        try
        {
            var formFields = new Dictionary<string, string>
            {
                ["source"] = sourceLang,
                ["target"] = targetLang,
                ["text"] = text,
                ["dict"] = "false",
                ["useGlossary"] = "false",
                ["honorific"] = "false"
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, TranslateUrl);
            request.Content = new FormUrlEncodedContent(formFields);

            request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
            request.Headers.Add("device-type", "pc");
            request.Headers.Add("Origin", "https://papago.naver.com");
            request.Headers.Add("Referer", "https://papago.naver.com/");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Sec-Fetch-Mode", "cors");
            request.Headers.Add("Sec-Fetch-Dest", "empty");

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == (HttpStatusCode)429)
                throw new TranslationRateLimitException(Name, "Papago rate limit reached");

            if (!response.IsSuccessStatusCode)
                return text;

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var papagoResponse = JsonConvert.DeserializeObject<PapagoResponse>(body);

            return papagoResponse?.translatedText ?? text;
        }
        catch (TranslationRateLimitException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            return text;
        }
    }

    public async Task<string?> RomanizeAsync(string text, string sourceLang, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // Non-Latin scripts only
        if (NonLatinScriptDetector.Detect(text) == NonLatinScriptKind.Latin) return null;

        try
        {
            var formFields = new Dictionary<string, string>
            {
                ["source"] = sourceLang,
                ["target"] = "auto",
                ["text"] = text,
                ["dict"] = "false",
                ["useGlossary"] = "false",
                ["honorific"] = "false"
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, TranslateUrl);
            request.Content = new FormUrlEncodedContent(formFields);

            request.Headers.UserAgent.ParseAdd(BrowserUserAgent);
            request.Headers.Add("device-type", "pc");
            request.Headers.Add("Origin", "https://papago.naver.com");
            request.Headers.Add("Referer", "https://papago.naver.com/");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Sec-Fetch-Mode", "cors");
            request.Headers.Add("Sec-Fetch-Dest", "empty");

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            var papagoResponse = JsonConvert.DeserializeObject<PapagoResponse>(body);

            if (papagoResponse == null || string.IsNullOrEmpty(papagoResponse.translatedText)) return null;

            // Extract tlitSrc phoneme data for romanization
            if (papagoResponse.tlitSrc?.message?.tlitResult != null)
            {
                var phonemes = new List<string>();
                foreach (var item in papagoResponse.tlitSrc.message.tlitResult)
                {
                    if (item.phoneme != null) phonemes.Add(item.phoneme);
                }
                if (phonemes.Count > 0) return string.Join(" ", phonemes);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public bool CanRomanize(string sourceLang, string text)
    {
        return NonLatinScriptDetector.Detect(text) != NonLatinScriptKind.Latin;
    }

    private class PapagoResponse
    {
        [JsonProperty("srcLangType")]
        public string? srcLangType { get; set; }
        
        [JsonProperty("tarLangType")]
        public string? tarLangType { get; set; }
        
        [JsonProperty("translatedText")]
        public string? translatedText { get; set; }
        
        [JsonProperty("delay")]
        public int delay { get; set; }
        
        [JsonProperty("delaySmt")]
        public int delaySmt { get; set; }
        
        [JsonProperty("tlitSrc")]
        public TlitSource? tlitSrc { get; set; }
    }

    private class TlitSource
    {
        [JsonProperty("message")]
        public TlitMessage? message { get; set; }
    }

    private class TlitMessage
    {
        [JsonProperty("tlitResult")]
        public List<TlitResultItem>? tlitResult { get; set; }
    }

    private class TlitResultItem
    {
        [JsonProperty("phoneme")]
        public string? phoneme { get; set; }
    }

    public void Dispose()
    {
        // Intentionally left blank: shared HttpClient is process-wide.
    }
}
