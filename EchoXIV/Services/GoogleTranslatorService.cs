using System;
using System.Collections.Generic;
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
    /// Servicio de traducción usando Google Translate API (gtx) sin API key.
    /// Más estable que el scraping de HTML móvil.
    /// </summary>
    public class GoogleTranslatorService : ITranslationService, IDisposable
    {
        public string Name => "Google";

        private static readonly HttpClient SharedHttpClient = CreateHttpClient();
        
        private const string GtxUrlTemplate = "https://translate.googleapis.com/translate_a/single?client=gtx&sl={0}&tl={1}&dt=t&q={2}";

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
                // Construir URL con el cliente gtx (Google Translate Extension)
                var url = string.Format(GtxUrlTemplate, fromLang, toLang, WebUtility.UrlEncode(text));
                
                var response = await SharedHttpClient.GetAsync(url, cancellationToken);
                
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new TranslationRateLimitException(Name, "Google Translate rate limit exceeded");

                if (!response.IsSuccessStatusCode)
                    return text; // Fallback
                
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                
                // El formato de respuesta de gtx es un array anidado:
                // [[["traducción","original",...],[...]],...]
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
                return text; // Retornar texto original en caso de error
            }
        }
        
        public string Translate(string text, string fromLang, string toLang)
        {
            return TranslateAsync(text, fromLang, toLang, CancellationToken.None).GetAwaiter().GetResult();
        }
        
        public void Dispose()
        {
            // Intentionally left blank: SharedHttpClient is process-wide.
        }
    }
}
