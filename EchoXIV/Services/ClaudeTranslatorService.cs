using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EchoXIV.Services
{
    /// <summary>
    /// Anthropic Claude Messages API translator using an API key.
    /// </summary>
    public class ClaudeTranslatorService : ITranslationService, ICombinedTranslator
    {
        private static readonly HttpClient SharedHttpClient = CreateHttpClient();
        private readonly EngineInstance _instance;

        public ClaudeTranslatorService(Configuration configuration, EngineInstance instance)
        {
            _instance = instance;
        }

        public string Name => string.IsNullOrWhiteSpace(_instance.Name) ? "Claude" : _instance.Name;

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd(EchoXIVClient.UserAgent);
            return http;
        }

        public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (sourceLang == targetLang && sourceLang != "auto") return text;

            var apiKey = _instance.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey)) return text;

            var model = _instance.Model;
            var system = LocalAiTranslatorService.FfxivTranslationPrompt.BuildSystemPrompt(sourceLang, targetLang, jsonOutput: false);

            var payload = new JObject
            {
                ["model"] = model,
                ["max_tokens"] = Math.Max(500, _instance.MaxTokens),
                ["temperature"] = _instance.Temperature,
                ["system"] = system,
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "user", ["content"] = text },
                },
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

            try
            {
                using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        throw new TranslationRateLimitException(Name, "Claude rate limit reached");
                    return text;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var root = JToken.Parse(body);
                var sb = new StringBuilder();
                foreach (var block in root["content"] ?? new JArray())
                {
                    if (block["type"]?.ToString() == "text")
                        sb.Append(block["text"]?.ToString());
                }

                var translated = sb.ToString();
                return string.IsNullOrWhiteSpace(translated)
                    ? text
                    : LocalAiTranslatorService.StripWrappingArtifacts(translated);
            }
            catch (TranslationRateLimitException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return text;
            }
        }

        public async Task<(string Translation, string? RomanizedText, string OriginalScript)> TranslateWithRomanizationAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default, bool romanizeTranslation = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return (text, null, "Latin");
            if (sourceLang == targetLang && sourceLang != "auto") return (text, null, "Latin");
            var detectedForOriginal = NonLatinScriptDetector.Detect(text);
            var originalScript = detectedForOriginal == NonLatinScriptKind.Latin ? "Latin" : detectedForOriginal.ToString();
            var apiKey = _instance.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey)) return (text, null, originalScript);
            var model = _instance.Model;
            if (string.IsNullOrWhiteSpace(model)) return (text, null, originalScript);
            var system = LocalAiTranslatorService.BuildSystemPromptWithRomanji(sourceLang, targetLang, romanizeTranslation);
            var payload = new JObject
            {
                ["model"] = model,
                ["max_tokens"] = Math.Max(500, _instance.MaxTokens),
                ["temperature"] = _instance.Temperature,
                ["system"] = system,
                ["messages"] = new JArray { new JObject { ["role"] = "user", ["content"] = text } },
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
            request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            try
            {
                using var response = await SharedHttpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                        throw new TranslationRateLimitException(Name, "Claude rate limit reached");
                    return (text, null, originalScript);
                }
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var root = JToken.Parse(body);
                var sb = new StringBuilder();
                foreach (var block in root["content"] ?? new JArray())
                {
                    if (block["type"]?.ToString() == "text")
                        sb.Append(block["text"]?.ToString());
                }
                var fullText = sb.ToString();
                if (string.IsNullOrWhiteSpace(fullText)) return (text, null, originalScript);
                try
                {
                    var parsed = JObject.Parse(fullText);
                    var translation = parsed["translation"]?.ToString() ?? parsed["Translation"]?.ToString();
                    var romanji = parsed["romanji"]?.ToString()
                                  ?? parsed["romaji"]?.ToString()
                                  ?? parsed["romanized"]?.ToString()
                                  ?? parsed["romanization"]?.ToString()
                                  ?? parsed["latin"]?.ToString()
                                  ?? parsed["transliteration"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(translation))
                    {
                        var cleanTrans = LocalAiTranslatorService.StripWrappingArtifacts(translation);
                        var cleanRoman = string.IsNullOrWhiteSpace(romanji) ? null : LocalAiTranslatorService.StripWrappingArtifacts(romanji);
                        if (romanizeTranslation && cleanRoman != null)
                        {
                            var transScript = NonLatinScriptDetector.Detect(cleanTrans);
                            originalScript = transScript == NonLatinScriptKind.Latin ? "Latin" : transScript.ToString();
                        }
                        return (cleanTrans, cleanRoman, originalScript);
                    }
                }
                catch { }
                try
                {
                    var match = System.Text.RegularExpressions.Regex.Match(fullText, @"\{.*\}", System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (match.Success)
                    {
                        var parsed = JObject.Parse(match.Value);
                        var translation = parsed["translation"]?.ToString() ?? parsed["Translation"]?.ToString();
                        var romanji = parsed["romanji"]?.ToString()
                                      ?? parsed["romaji"]?.ToString()
                                      ?? parsed["romanized"]?.ToString()
                                      ?? parsed["romanization"]?.ToString()
                                      ?? parsed["transliteration"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(translation))
                        {
                            var cleanTrans = LocalAiTranslatorService.StripWrappingArtifacts(translation);
                            var cleanRoman = string.IsNullOrWhiteSpace(romanji) ? null : LocalAiTranslatorService.StripWrappingArtifacts(romanji);
                            return (cleanTrans, cleanRoman, originalScript);
                        }
                    }
                }
                catch { }
                return (LocalAiTranslatorService.StripWrappingArtifacts(fullText), null, originalScript);
            }
            catch (TranslationRateLimitException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch { return (text, null, originalScript); }
        }

        public async Task<string?> RomanizeAsync(string text, string sourceLang, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var result = await TranslateWithRomanizationAsync(text, sourceLang, "en", ct);
            return result.RomanizedText;
        }

        public bool CanRomanize(string sourceLang, string text) => NonLatinScriptDetector.Detect(text) != NonLatinScriptKind.Latin;

        public void Dispose()
        {
            // Shared HttpClient is process-wide.
        }
    }
}
