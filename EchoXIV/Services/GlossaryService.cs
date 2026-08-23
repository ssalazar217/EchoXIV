using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.IO;

namespace EchoXIV.Services
{
    /// <summary>
    /// Result of Protect(): protected text plus its own placeholder map,
    /// safe to use across concurrent translations.
    /// </summary>
    public sealed class GlossaryProtection
    {
        private readonly Dictionary<string, string> _placeholders;

        public string Text { get; }

        internal GlossaryProtection(string text, Dictionary<string, string> placeholders)
        {
            Text = text;
            _placeholders = placeholders;
        }

        public string Restore(string translatedText)
        {
            if (string.IsNullOrWhiteSpace(translatedText) || _placeholders.Count == 0)
                return translatedText;

            var result = translatedText;
            foreach (var kvp in _placeholders)
            {
                result = result.Replace(kvp.Key, kvp.Value);
            }
            return result;
        }
    }

    public class GlossaryService
    {
        // FFXIV term -> placeholder
        private static readonly Dictionary<string, string> FfxivTerms = new()
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
            { "RMT", "[[RMT]]" }
        };

        private static readonly Dictionary<string, Regex> FfxivRegexes = BuildRegexes(FfxivTerms);

        private readonly Dictionary<string, string> _customTerms = new();
        private readonly Dictionary<string, Regex> _customRegexes = new();

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
                if (!File.Exists(path)) return;

                var json = File.ReadAllText(path);
                var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (data == null) return;

                foreach (var kvp in data)
                {
                    _customTerms[kvp.Key] = kvp.Value;
                    _customRegexes[kvp.Key] = CompileTermRegex(kvp.Key);
                }
            }
            catch
            {
                // Corrupt glossary must not break translation.
            }
        }

        /// <summary>
        /// Protects game terms before translation. Thread-safe per call.
        /// </summary>
        public GlossaryProtection Protect(string text)
        {
            var placeholders = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(text))
                return new GlossaryProtection(text ?? string.Empty, placeholders);

            var result = text;

            // Custom terms first, then base FFXIV terms
            foreach (var term in _customTerms)
            {
                result = Apply(result, _customRegexes[term.Key], term.Key, term.Value, placeholders);
            }
            foreach (var term in FfxivTerms)
            {
                result = Apply(result, FfxivRegexes[term.Key], term.Key, term.Value, placeholders);
            }

            return new GlossaryProtection(result, placeholders);
        }

        private static string Apply(string text, Regex regex, string term, string placeholder, Dictionary<string, string> placeholders)
        {
            if (!regex.IsMatch(text))
                return text;

            placeholders[placeholder] = term;
            return regex.Replace(text, placeholder);
        }

        private static Dictionary<string, Regex> BuildRegexes(Dictionary<string, string> terms)
        {
            var map = new Dictionary<string, Regex>();
            foreach (var term in terms)
            {
                map[term.Key] = CompileTermRegex(term.Key);
            }
            return map;
        }

        private static Regex CompileTermRegex(string key) =>
            new(@"\b" + Regex.Escape(key) + @"\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
