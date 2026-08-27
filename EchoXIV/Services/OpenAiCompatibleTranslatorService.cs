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
    /// Shared helpers for OpenAI-compatible chat completions engines
    /// (ChatGPT, DeepSeek, OpenRouter).
    /// </summary>
    internal static class ChatCompletionsClient
    {
        public static async Task<string?> PostChatAsync(
            HttpClient http,
            string endpoint,
            string? apiKey,
            string model,
            string systemPrompt,
            string userText,
            float temperature,
            int maxTokens,
            CancellationToken ct,
            bool jsonMode = false)
        {
            var payload = new JObject
            {
                ["model"] = model,
                ["temperature"] = temperature,
                ["max_tokens"] = Math.Max(500, maxTokens),
                ["messages"] = new JArray
                {
                    new JObject { ["role"] = "system", ["content"] = systemPrompt },
                    new JObject { ["role"] = "user", ["content"] = userText },
                },
            };
            if (jsonMode)
                payload["response_format"] = new JObject { ["type"] = "json_object" };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey.Trim());
            }

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    throw new TranslationRateLimitException("Cloud LLM", "Rate limit reached");
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                var root = JToken.Parse(body);
                var content = root["choices"]?[0]?["message"]?["content"]?.ToString();
                if (!string.IsNullOrWhiteSpace(content))
                    return LocalAiTranslatorService.StripWrappingArtifacts(content);
            }
            catch
            {
                // fall through
            }
            return null;
        }
    }

    /// <summary>
    /// Generic OpenAI-compatible chat completions translator (ChatGPT, DeepSeek, OpenRouter).
    /// </summary>
    public class OpenAiCompatibleTranslatorService : ITranslationService, ICombinedTranslator
    {
        private static readonly HttpClient SharedHttpClient = CreateHttpClient();
        private readonly EngineInstance _instance;
        private readonly string _endpoint;

        public OpenAiCompatibleTranslatorService(Configuration configuration, EngineInstance instance)
        {
            _instance = instance;

            _endpoint = instance.Type switch
            {
                TranslationEngine.ChatGPT => "https://api.openai.com/v1/chat/completions",
                TranslationEngine.DeepSeek => "https://api.deepseek.com/v1/chat/completions",
                TranslationEngine.OpenRouter => "https://openrouter.ai/api/v1/chat/completions",
                _ => throw new ArgumentOutOfRangeException(nameof(instance)),
            };
        }

        public string Name => string.IsNullOrWhiteSpace(_instance.Name) ? _instance.Type.ToString() : _instance.Name;

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

            var system = LocalAiTranslatorService.FfxivTranslationPrompt.BuildSystemPrompt(sourceLang, targetLang, jsonOutput: false);
            var model = _instance.Model;

            var result = await ChatCompletionsClient.PostChatAsync(
                SharedHttpClient,
                _endpoint,
                _instance.ApiKey,
                model,
                system,
                text,
                _instance.Temperature,
                _instance.MaxTokens,
                cancellationToken);

            return string.IsNullOrWhiteSpace(result) ? text : result!;
        }

        public async Task<(string Translation, string? RomanizedText, string OriginalScript)> TranslateWithRomanizationAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default, bool romanizeTranslation = false)
        {
            if (string.IsNullOrWhiteSpace(text)) return (text, null, "Latin");
            if (sourceLang == targetLang && sourceLang != "auto") return (text, null, "Latin");
            var detectedForOriginal = NonLatinScriptDetector.Detect(text);
            var originalScript = detectedForOriginal == NonLatinScriptKind.Latin ? "Latin" : detectedForOriginal.ToString();
            var system = LocalAiTranslatorService.BuildSystemPromptWithRomanji(sourceLang, targetLang, romanizeTranslation);
            var result = await ChatCompletionsClient.PostChatAsync(SharedHttpClient, _endpoint, _instance.ApiKey, _instance.Model, system, text, _instance.Temperature, _instance.MaxTokens, cancellationToken, jsonMode: true);
            if (string.IsNullOrWhiteSpace(result)) return (text, null, originalScript);
            try
            {
                var parsed = JObject.Parse(result);
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
            // Fallback: try extract JSON object via regex
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(result, @"\{.*\}", System.Text.RegularExpressions.RegexOptions.Singleline);
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
            return (LocalAiTranslatorService.StripWrappingArtifacts(result), null, originalScript);
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
