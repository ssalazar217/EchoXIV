using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EchoXIV.Services
{
public sealed class TranslationCache : IDisposable
{
    private const string CacheKeyVersion = "v2";
    private const int MaxEntries = 5000;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
        private readonly string _cacheFilePath;
        private readonly object _gate = new();
        private readonly Dictionary<string, string> _cache = new();
        private readonly Dictionary<string, string> _romanizationCache = new();
        private bool _isDirty = false;
        private long _changeVersion;

        public TranslationCache(string configDir)
        {
            _cacheFilePath = Path.Combine(configDir, "translation_cache.json");
            Load();
        }

    private void Load()
    {
        try
        {
            if (!File.Exists(_cacheFilePath)) return;
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheFilePath) > MaxAge) return;

                var json = File.ReadAllText(_cacheFilePath);
                var root = JObject.Parse(json);

                // New format: { "translations": {...}, "romanizations": {...} }
                var translations = root["translations"]?.ToObject<Dictionary<string, string>>();
                if (translations != null)
                {
                    foreach (var kvp in translations) _cache[kvp.Key] = kvp.Value;

                    var romanizations = root["romanizations"]?.ToObject<Dictionary<string, string>>();
                    if (romanizations != null)
                    {
                        foreach (var kvp in romanizations) _romanizationCache[kvp.Key] = kvp.Value;
                    }
                }
                else
                {
                    // Legacy format: flat translation dictionary
                    var legacy = root.ToObject<Dictionary<string, string>>();
                    if (legacy != null)
                    {
                        foreach (var kvp in legacy) _cache[kvp.Key] = kvp.Value;
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
            lock (_gate)
            {
                return _cache.TryGetValue(key, out var translated) ? translated : null;
            }
        }

        public bool TryGetWithRomanization(
            string text,
            string sourceLang,
            string targetLang,
            out string? translated,
            out string? romanized)
        {
            translated = null;
            romanized = null;

            var key = GetKey(text, sourceLang, targetLang);
            lock (_gate)
            {
                if (!_cache.TryGetValue(key, out var cachedTranslation))
                    return false;

                translated = cachedTranslation;
                _romanizationCache.TryGetValue(key, out romanized);
                return true;
            }
        }

        public void Add(string text, string sourceLang, string targetLang, string translated)
        {
            Add(text, sourceLang, targetLang, translated, null);
        }

    public void Add(string text, string sourceLang, string targetLang, string translated, string? romanized)
        {
            var key = GetKey(text, sourceLang, targetLang);
            lock (_gate)
            {
                if (!_cache.TryGetValue(key, out var existingTranslation) || existingTranslation != translated)
                {
                    _cache[key] = translated;
                    _isDirty = true;
                    _changeVersion++;
                }

                if (!string.IsNullOrEmpty(romanized) &&
                    (!_romanizationCache.TryGetValue(key, out var existingRomanized) || existingRomanized != romanized))
                {
                    _romanizationCache[key] = romanized!;
                    _isDirty = true;
                    _changeVersion++;
                }

                TrimToLimit(_cache);
                TrimToLimit(_romanizationCache);
            }
        }

    public void Save()
        {
            JObject root;
            long snapshotVersion;
            lock (_gate)
            {
                if (!_isDirty) return;
                root = BuildSnapshot();
                snapshotVersion = _changeVersion;
            }

            try
            {
                var temporaryPath = _cacheFilePath + ".tmp";
                File.WriteAllText(temporaryPath, root.ToString(Formatting.Indented));
                File.Move(temporaryPath, _cacheFilePath, true);
                lock (_gate)
                {
                    // Keep dirty state when a write raced with a new cache entry.
                    if (_changeVersion == snapshotVersion)
                        _isDirty = false;
                }
            }
            catch (Exception)
            {
                // Keep the dirty flag so the next scheduled flush retries.
            }
        }

        private static void TrimToLimit(Dictionary<string, string> cache)
        {
            while (cache.Count > MaxEntries)
                cache.Remove(cache.Keys.First());
        }

        private JObject BuildSnapshot()
        {
            return new JObject
            {
                ["translations"] = JObject.FromObject(_cache),
                ["romanizations"] = _romanizationCache.Count > 0
                    ? JObject.FromObject(_romanizationCache)
                    : new JObject()
            };
        }

        private static string GetKey(string text, string sl, string tl) =>
            $"{CacheKeyVersion}|{text.Length}:{text}{sl.Length}:{sl}{tl.Length}:{tl}";

        public void Dispose() => Save();
    }
}
