using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace EchoXIV.Services
{
    public class TranslationCache
    {
        private readonly string _cacheFilePath;
        private readonly Dictionary<string, string> _cache = new();
        private bool _isDirty = false;

        public TranslationCache(string configDir)
        {
            _cacheFilePath = Path.Combine(configDir, "translation_cache.json");
            Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = File.ReadAllText(_cacheFilePath);
                    var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    if (data != null)
                    {
                        foreach (var kvp in data) _cache[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception)
            {
                // Silently fail if cache is corrupted
            }
        }

        public string? Get(string text, string sourceLang, string targetLang)
        {
            var key = GetKey(text, sourceLang, targetLang);
            return _cache.TryGetValue(key, out var translated) ? translated : null;
        }

        public void Add(string text, string sourceLang, string targetLang, string translated)
        {
            var key = GetKey(text, sourceLang, targetLang);
            if (!_cache.ContainsKey(key))
            {
                _cache[key] = translated;
                _isDirty = true;
            }
        }

        public void Save()
        {
            if (!_isDirty) return;

            try
            {
                var json = JsonConvert.SerializeObject(_cache, Formatting.Indented);
                File.WriteAllText(_cacheFilePath, json);
                _isDirty = false;
            }
            catch (Exception)
            {
                // Silently fail
            }
        }

        private string GetKey(string text, string sl, string tl) => $"{text}|{sl}|{tl}";
    }
}
