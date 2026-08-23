using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace EchoXIV.Services
{
    /// <summary>
    /// Google Translate (gtx) service, no API key required.
    /// </summary>
    public class GoogleTranslatorService : ITranslationService, IRomanizationService, IDisposable
    {
        public string Name => "Google";

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();
        
        private const string GtxUrlTemplate = "https://translate.googleapis.com/translate_a/single?client=gtx&sl={0}&tl={1}&dt=t&q={2}";
        private const string GtxUrlTemplateWithRomanization = "https://translate.googleapis.com/translate_a/single?client=gtx&sl={0}&tl={1}&dt=t&q={2}&dt=rm";

        private static HttpClient CreateHttpClient()
        {
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            return httpClient;
        }
        
        public async Task<string> TranslateAsync(string text, string fromLang, string toLang, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (fromLang == toLang && fromLang != "auto")
                return text;
            
            try
            {
                // Build gtx URL
                var url = string.Format(GtxUrlTemplate, fromLang, toLang, WebUtility.UrlEncode(text));
                
                var response = await SharedHttpClient.GetAsync(url, cancellationToken);
                
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new TranslationRateLimitException(Name, "Google Translate rate limit exceeded");

                if (!response.IsSuccessStatusCode)
                    return text; // Fallback
                
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                // gtx response format: [[["translation","original",...],[...]],...]
                var jArray = JArray.Parse(json);
                var translatedParts = jArray[0];
                
                var sb = new StringBuilder();
                foreach (var part in translatedParts)
                {
                    var translatedText = part[0]?.ToString();
                    if (!string.IsNullOrEmpty(translatedText))
                    {
                        sb.Append(translatedText);
                    }
                }
                
                var result = sb.ToString();
                return string.IsNullOrEmpty(result) ? text : result;
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
                return text; // Return original on error
            }
        }
        
        public async Task<string?> RomanizeAsync(string text, string sourceLang, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Non-Latin scripts only
            if (NonLatinScriptDetector.Detect(text) == NonLatinScriptKind.Latin) return null;
            
            try
            {
                var url = string.Format(GtxUrlTemplateWithRomanization, sourceLang, "auto", Uri.EscapeDataString(text));
                var response = await SharedHttpClient.GetAsync(url, ct);

                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(ct);
                var jArray = JArray.Parse(json);

                var romajiParts = new StringBuilder();
                var units = jArray[0] as JArray;
                if (units == null) return null;

                foreach (var unit in units)
                {
                    var unitArray = unit as JArray;
                    if (unitArray != null && unitArray.Count > 2 && unitArray[2] != null)
                    {
                        romajiParts.Append(unitArray[2].ToString());
                    }
                }

                return romajiParts.Length > 0 ? romajiParts.ToString() : null;
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

        public void Dispose()
        {
            // Intentionally left blank: SharedHttpClient is process-wide.
        }
    }
}
