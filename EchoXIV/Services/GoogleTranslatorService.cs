using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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

        private readonly HttpClient _httpClient;
        
        private const string GtxUrlTemplate = "https://translate.googleapis.com/translate_a/single?client=gtx&sl={0}&tl={1}&dt=t&q={2}";
        
        public GoogleTranslatorService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }
        
        public async Task<string> TranslateAsync(string text, string fromLang, string toLang)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            if (fromLang == toLang && fromLang != "auto")
                return text;
            
            try
            {
                // Construir URL con el cliente gtx (Google Translate Extension)
                var url = string.Format(GtxUrlTemplate, fromLang, toLang, WebUtility.UrlEncode(text));
                
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                    return text; // Fallback
                
                var json = await response.Content.ReadAsStringAsync();
                
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
            catch (Exception)
            {
                return text; // Retornar texto original en caso de error
            }
        }
        
        public string Translate(string text, string fromLang, string toLang)
        {
            return TranslateAsync(text, fromLang, toLang).GetAwaiter().GetResult();
        }
        
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
