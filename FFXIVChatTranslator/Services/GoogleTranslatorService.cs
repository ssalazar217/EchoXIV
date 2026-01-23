using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FFXIVChatTranslator.Services
{
    /// <summary>
    /// Servicio de traducción usando Google Translate móvil sin API key.
    /// Adaptado del método de TataruHelper.
    /// </summary>
    public class GoogleTranslatorService : ITranslationService, IDisposable
    {
        public string Name => "Google";

        private readonly HttpClient _httpClient;
        private readonly CookieContainer _cookieContainer;
        
        // Regex para extraer la traducción del HTML (versión legacy y moderna)
        private static readonly Regex GoogleRxLegacy = new Regex(
            @"(?<=(<div dir=\""ltr\"" class=\""t0\"">)).*?(?=(</div>))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        private static readonly Regex GoogleRx = new Regex(
            @"(?<=(<div(.*)class=\""result-container\""(.*)\>)).*?(?=(</div>))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        private const string GoogleMobileUrl = "https://translate.google.com/m";
        private const string TranslateUrlTemplate = "https://translate.google.com/m?hl=ru&sl={0}&tl={1}&ie=UTF-8&prev=_m&q={2}";
        
        public GoogleTranslatorService()
        {
            _cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true
            };
            
            _httpClient = new HttpClient(handler);
            
            // Simular navegador móvil (Opera Mini)
            _httpClient.DefaultRequestHeaders.Add("User-Agent", 
                "Opera/9.80 (Android; Opera Mini/11.0.1912/37.7549; U; pl) Presto/2.12.423 Version/12.16");
            _httpClient.DefaultRequestHeaders.Add("Accept", 
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US;q=0.5,en;q=0.3");
            _httpClient.DefaultRequestHeaders.Add("DNT", "1");
            _httpClient.DefaultRequestHeaders.Add("Upgrade-Insecure-Requests", "1");
            _httpClient.DefaultRequestHeaders.Add("Pragma", "no-cache");
            _httpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            
            // Inicializar sesión
            InitializeSession();
        }
        
        /// <summary>
        /// Inicializa la sesión con Google Translate móvil para obtener cookies
        /// </summary>
        private void InitializeSession()
        {
            try
            {
                _httpClient.GetAsync(GoogleMobileUrl).Wait();
            }
            catch
            {
                // Ignorar errores de inicialización
            }
        }
        
        /// <summary>
        /// Traduce texto de forma asíncrona
        /// </summary>
        public async Task<string> TranslateAsync(string text, string fromLang, string toLang)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;
            
            try
            {
                var result = await TranslateInternalAsync(text, fromLang, toLang);
                
                // Si falla, reintentar con nueva sesión
                if (string.IsNullOrEmpty(result))
                {
                    InitializeSession();
                    result = await TranslateInternalAsync(text, fromLang, toLang);
                }
                
                return result;
            }
            catch
            {
                return text; // Retornar texto original en caso de error
            }
        }
        
        /// <summary>
        /// Traduce texto de forma síncrona
        /// </summary>
        public string Translate(string text, string fromLang, string toLang)
        {
            return TranslateAsync(text, fromLang, toLang).GetAwaiter().GetResult();
        }
        
        /// <summary>
        /// Implementación interna de traducción
        /// </summary>
        private async Task<string> TranslateInternalAsync(string text, string fromLang, string toLang)
        {
            try
            {
                // Construir URL con texto codificado
                var url = string.Format(TranslateUrlTemplate, fromLang, toLang, WebUtility.UrlEncode(text));
                
                // Hacer petición
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                    return string.Empty;
                
                var html = await response.Content.ReadAsStringAsync();
                
                // Parsear respuesta HTML
                return ParseTranslationFromHtml(html);
            }
            catch
            {
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Extrae la traducción del HTML usando regex
        /// </summary>
        private string ParseTranslationFromHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;
            
            // Intentar con regex legacy primero
            var match = GoogleRxLegacy.Match(html);
            
            if (match.Success)
            {
                return WebUtility.HtmlDecode(match.Value);
            }
            
            // Intentar con regex moderna
            match = GoogleRx.Match(html);
            
            if (match.Success)
            {
                return WebUtility.HtmlDecode(match.Value);
            }
            
            return string.Empty;
        }
        
        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
