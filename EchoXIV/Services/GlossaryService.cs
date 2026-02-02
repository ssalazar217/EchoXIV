using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;

namespace EchoXIV.Services
{
    public class GlossaryService
    {
        // Mapa de términos: original -> protección (placeholder)
        private static readonly Dictionary<string, string> _ffxivTerms = new()
        {
            { "ilvl", "[[IL]]" },
            { "PF", "[[PF]]" },
            { "MB", "[[MB]]" },
            { "LB", "[[LB]]" },
            { "M1S", "[[M1S]]" },
            { "M2S", "[[M2S]]" },
            { "M3S", "[[M3S]]" },
            { "M4S", "[[M4S]]" },
            { "P1S", "[[P1S]]" },
            { "P2S", "[[P2S]]" },
            { "P3S", "[[P3S]]" },
            { "P4S", "[[P4S]]" },
            { "TOP", "[[TOP]]" },
            { "DSR", "[[DSR]]" },
            { "UWU", "[[UWU_ULT]]" },
            { "TEA", "[[TEA_ULT]]" },
            { "UCOB", "[[UCOB_ULT]]" },
            { "BiS", "[[BIS]]" },
            { "FC", "[[FC]]" },
            { "LS", "[[LS]]" },
            { "CWLS", "[[CWLS]]" },
            { "RMT", "[[RMT]]" },
            { "Wipe", "[[WIPE]]" },
            { "Pull", "[[PULL]]" },
            { "Macro", "[[MACRO]]" }
        };

        private readonly Dictionary<string, string> _activePlaceholders = new();
        private readonly Dictionary<string, string> _customTerms = new();

        public GlossaryService(string? configDir = null)
        {
            if (configDir != null)
            {
                LoadCustomGlossary(configDir);
            }
        }

        private void LoadCustomGlossary(string configDir)
        {
            try
            {
                var path = Path.Combine(configDir, "custom_glossary.json");
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (data != null)
                    {
                        foreach (var kvp in data) _customTerms[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Protege términos específicos del juego antes de la traducción
        /// </summary>
        public string Protect(string text)
        {
            _activePlaceholders.Clear();
            if (string.IsNullOrWhiteSpace(text)) return text;

            string result = text;

            // 1. Proteger términos personalizados del usuario primero
            foreach (var term in _customTerms)
            {
                result = ApplyProtection(result, term.Key, term.Value);
            }

            // 2. Proteger términos base de FFXIV
            foreach (var term in _ffxivTerms)
            {
                result = ApplyProtection(result, term.Key, term.Value);
            }

            return result;
        }

        private string ApplyProtection(string text, string key, string placeholder)
        {
            string pattern = @"\b" + Regex.Escape(key) + @"\b";
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                string result = Regex.Replace(text, pattern, placeholder, RegexOptions.IgnoreCase);
                _activePlaceholders[placeholder] = key;
                return result;
            }
            return text;
        }

        /// <summary>
        /// Restaura los términos originales después de la traducción
        /// </summary>
        public string Restore(string translatedText)
        {
            if (string.IsNullOrWhiteSpace(translatedText)) return translatedText;

            string result = translatedText;
            foreach (var placeholder in _activePlaceholders)
            {
                result = result.Replace(placeholder.Key, placeholder.Value);
            }

            return result;
        }
    }
}
