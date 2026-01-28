using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace EchoXIV.Models
{
    public class Language
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Flag { get; set; } = string.Empty;
        
        public string DisplayName => $"{Flag} {Code.ToUpper()}";
        public string FullDisplayName => $"{Flag} {Name}";
    }
    
    public class LanguageData
    {
        public List<Language> Languages { get; set; } = new();
    }
    
    /// <summary>
    /// Proveedor de idiomas disponibles
    /// </summary>
    public static class LanguageProvider
    {
        private static List<Language>? _languages;
        
        public static List<Language> GetLanguages()
        {
            if (_languages != null)
                return _languages;
            
            try
            {
                var jsonPath = Path.Combine(
                    Path.GetDirectoryName(typeof(LanguageProvider).Assembly.Location) ?? string.Empty,
                    "Properties",
                    "Languages.json"
                );
                
                if (File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    var data = JsonConvert.DeserializeObject<LanguageData>(json);
                    _languages = data?.Languages ?? GetDefaultLanguages();
                }
                else
                {
                    _languages = GetDefaultLanguages();
                }
            }
            catch
            {
                _languages = GetDefaultLanguages();
            }
            
            return _languages;
        }
        
        public static Language? GetLanguage(string code)
        {
            return GetLanguages().FirstOrDefault(l => l.Code.Equals(code, System.StringComparison.OrdinalIgnoreCase));
        }
        
        private static List<Language> GetDefaultLanguages()
        {
            return new List<Language>
            {
                new() { Code = "en", Name = "English", Flag = "🇺🇸" },
                new() { Code = "ja", Name = "日本語", Flag = "🇯🇵" },
                new() { Code = "de", Name = "Deutsch", Flag = "🇩🇪" },
                new() { Code = "fr", Name = "Français", Flag = "🇫🇷" },
                new() { Code = "es", Name = "Español", Flag = "🇪🇸" }
            };
        }
    }
}
