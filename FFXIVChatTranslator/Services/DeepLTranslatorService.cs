using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace FFXIVChatTranslator.Services
{
    public class DeepLTranslatorService : ITranslationService, IDisposable
    {
        public string Name => "DeepL";
        
        private readonly HttpClient _httpClient;
        private readonly Random _random;
        private long _idCounter;
        
        private const string DeepLApiUrl = "https://www2.deepl.com/jsonrpc?method=LMT_handle_jobs";

        public DeepLTranslatorService()
        {
            _random = new Random();
             // Base ID generation logic from TataruHelper (DeepLTranslator.cs:52)
            _idCounter = 10000 * (long)Math.Round(10000 * _random.NextDouble());

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.deepl.com/"); // Critical for DeepL
            _httpClient.DefaultRequestHeaders.Add("Origin", "https://www.deepl.com");
            _httpClient.DefaultRequestHeaders.Add("DNT", "1");
        }

        public async Task<string> TranslateAsync(string text, string fromLang, string toLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (fromLang == toLang) return text;
            
            // DeepL uses "auto" for auto-detection, and uppercase 2-letter codes mostly.
            // Ensure codes are uppercase (e.g. "EN", "ES", "JA").
            // "auto" should remain lowercase.
            string source = fromLang.ToLower() == "auto" ? "auto" : fromLang.ToUpper();
            string target = toLang.ToUpper();

            try
            {
                var requestPayload = CreateRequest(text, source, target);
                var json = JsonConvert.SerializeObject(requestPayload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // DeepL expects POST
                var response = await _httpClient.PostAsync(DeepLApiUrl, content);
                
                if (!response.IsSuccessStatusCode)
                {
                    // Optionally log error
                    // Console.WriteLine($"DeepL Error: {response.StatusCode}");
                    return text; // Fallback
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var result = ParseResponse(responseJson);
                
                return string.IsNullOrEmpty(result) ? text : result;

            }
            catch (Exception)
            {
                 // Log?
                 return text;
            }
        }

        private DeepLRequest CreateRequest(string text, string sourceLang, string targetLang)
        {
            // Update ID
            _idCounter++;
            
            // Handle splitting if needed (TataruHelper does splitting, but for chat messages usually simple sentences)
            // We follow the simple TataruHelper structure
            
            var job = new DeepLJob
            {
                 Kind = "default",
                 RawEnSentence = text,
                 RawEnContextBefore = new List<string>(),
                 RawEnContextAfter = new List<string>(),
                 PreferredNumBeams = 1 // Simplified
            };

            var lang = new DeepLLang
            {
                SourceLangComputed = sourceLang,
                TargetLang = targetLang,
                UserPreferredLangs = new List<string> { targetLang, sourceLang } // Often sent
            };

            var timestamp = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;

            return new DeepLRequest
            {
                JsonRpc = "2.0",
                Method = "LMT_handle_jobs",
                Id = _idCounter,
                Params = new DeepLParams
                {
                    Jobs = new List<DeepLJob> { job },
                    Lang = lang,
                    Priority = 1,
                    
                    // "commonJobParams": { "formality": null }, "timestamp": ...
                    CommonJobParams = new DeepLCommonParams(),
                    Timestamp = timestamp
                }
            };
        }

        private string ParseResponse(string json)
        {
            try 
            {
                var response = JsonConvert.DeserializeObject<DeepLResponse>(json);
                 if (response?.Result?.Translations != null && response.Result.Translations.Count > 0)
                 {
                     var firstTranslation = response.Result.Translations[0];
                     if (firstTranslation.Beams != null && firstTranslation.Beams.Count > 0)
                     {
                         return firstTranslation.Beams[0].PostprocessedSentence;
                     }
                 }
            }
            catch { }
            return string.Empty;
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

        // --- DTOs ---

        public class DeepLRequest
        {
            [JsonProperty("jsonrpc")] public string JsonRpc { get; set; }
            [JsonProperty("method")] public string Method { get; set; }
            [JsonProperty("id")] public long Id { get; set; }
            [JsonProperty("params")] public DeepLParams Params { get; set; }
        }

        public class DeepLParams
        {
            [JsonProperty("jobs")] public List<DeepLJob> Jobs { get; set; }
            [JsonProperty("lang")] public DeepLLang Lang { get; set; }
            [JsonProperty("priority")] public int Priority { get; set; }
            [JsonProperty("commonJobParams")] public DeepLCommonParams CommonJobParams { get; set; }
            [JsonProperty("timestamp")] public long Timestamp { get; set; }
        }

        public class DeepLJob
        {
            [JsonProperty("kind")] public string Kind { get; set; }
            [JsonProperty("raw_en_sentence")] public string RawEnSentence { get; set; }
            [JsonProperty("raw_en_context_before")] public List<string> RawEnContextBefore { get; set; }
            [JsonProperty("raw_en_context_after")] public List<string> RawEnContextAfter { get; set; }
            [JsonProperty("preferred_num_beams")] public int PreferredNumBeams { get; set; }
        }

        public class DeepLLang
        {
            [JsonProperty("source_lang_computed")] public string SourceLangComputed { get; set; }
            [JsonProperty("target_lang")] public string TargetLang { get; set; }
            [JsonProperty("user_preferred_langs")] public List<string> UserPreferredLangs { get; set; }
        }
        
        public class DeepLCommonParams
        {
            // Depending on version, typically empty object or null formality
            // [JsonProperty("formality")] public string Formality { get; set; } = null; 
        }

        public class DeepLResponse
        {
            [JsonProperty("result")] public DeepLResult Result { get; set; }
        }

        public class DeepLResult
        {
             [JsonProperty("translations")] public List<DeepLTranslation> Translations { get; set; }
        }

        public class DeepLTranslation
        {
            [JsonProperty("beams")] public List<DeepLBeam> Beams { get; set; }
        }

        public class DeepLBeam
        {
            [JsonProperty("postprocessed_sentence")] public string PostprocessedSentence { get; set; }
        }
    }
}
