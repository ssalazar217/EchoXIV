using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Configuration;
using Dalamud.Plugin;
using EchoXIV.Services;
using Newtonsoft.Json;

namespace EchoXIV
{
    /// <summary>
    /// Plugin configuration
    /// </summary>
    public enum TranslationEngine
    {
        Google,
        Papago,
        LibreTranslate,
        Yandex,
        LocalAI,
        ChatGPT,
        Gemini,
        Claude,
        DeepSeek,
        OpenRouter
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        private const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        
        /// <summary>
        /// True on first plugin run
        /// </summary>
        public bool FirstRun { get; set; } = true;
        
        /// <summary>
        /// Enables automatic translation
        /// </summary>
        public bool TranslationEnabled { get; set; } = true;
        
        /// <summary>
        /// Language you write your own messages in.
        /// </summary>
        public string SourceLanguage { get; set; } = "en";

        /// <summary>
        /// EchoXIV UI language. If empty, follows Dalamud.
        /// </summary>
        public string UiLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// Target language for outgoing messages.
        /// </summary>
        public string TargetLanguage { get; set; } = "en";
        
        // Messages never translated (universal phrases, emoticons, etc.)
        // Replace: prevents the deserializer from merging saved items on top of the
        // initializer defaults (which re-added removed engines/channels on every reload).
        [JsonProperty("ExcludedMessages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> ExcludedMessages { get; set; } = CreateDefaultExcludedMessages();

        // === Incoming translation settings ===

        /// <summary>
        /// Enable incoming message translation
        /// </summary>
        public bool IncomingTranslationEnabled { get; set; } = true;
        
        /// <summary>
        /// Max messages shown in the translation window
        /// </summary>
        public int MaxDisplayedMessages { get; set; } = 50;

        /// <summary>
        /// Maximum size of the persisted message history in megabytes.
        /// </summary>
        public int MaxHistorySizeMb { get; set; } = 10;
        
        /// <summary>
        /// Show original text next to translation
        /// </summary>
        public bool ShowOriginalText { get; set; } = true;
        
        /// <summary>
        /// Show message timestamps
        /// </summary>
        public bool ShowTimestamps { get; set; } = true;
        
        /// <summary>
        /// Show own messages in the chat window
        /// </summary>
        public bool ShowOutgoingMessages { get; set; } = true;

        /// <summary>Skip translation when the detected language already matches the target.</summary>
        public bool SkipSameLanguageTranslation { get; set; } = true;

        /// <summary>Minimum confidence required before skipping a translation.</summary>
        public float LanguageDetectionConfidence { get; set; } = 0.85f;

        // --- Visuals ---
        public int FontSize { get; set; } = 16;
        public int ChatMessageSpacing { get; set; } = 0;
        public string TimestampFormat { get; set; } = "HH:mm"; // "HH:mm:ss", "Short"
        public bool HideWhenInactive { get; set; } = false;
        public int InactivityHideTimeout { get; set; } = 60; // seconds

        /// <summary>Show a confirmation in game chat when /tl chat toggles the translated chat window.</summary>
        public bool ShowChatToggleLog { get; set; } = true;

        /// <summary>
        /// Ordered list of engine instances. List order defines fallback priority:
        /// the primary instance is tried first, then the rest in list order.
        /// Multiple instances of the same engine type (e.g. two Gemini models)
        /// are allowed.
        /// </summary>
        [JsonProperty("Engines", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<EngineInstance> Engines { get; set; } = new();

        /// <summary>
        /// True once the initial engine list has been created. Prevents the
        /// default engines from being re-added on every plugin reload, so the
        /// user's curated list is preserved.
        /// </summary>
        public bool EnginesInitialized { get; set; } = false;

        /// <summary>
        /// Id of the instance used as the primary engine.
        /// </summary>
        public Guid PrimaryEngineId { get; set; } = Guid.Empty;

        /// <summary>
        /// The primary engine instance, or the first enabled one when unset.
        /// </summary>
        public EngineInstance? PrimaryInstance =>
            Engines.FirstOrDefault(e => e.Id == PrimaryEngineId)
            ?? Engines.FirstOrDefault(e => e.Enabled);

        /// <summary>
        /// Engine type of the active primary instance (compatibility shim).
        /// </summary>
        public TranslationEngine SelectedEngine =>
            PrimaryInstance?.Type ?? (Engines.Count > 0 ? Engines[0].Type : TranslationEngine.Google);

        /// <summary>
        /// Display name of the primary instance (custom name, else the engine type).
        /// </summary>
        public string PrimaryInstanceName =>
            PrimaryInstance == null ? "?" : GetInstanceName(PrimaryInstance.Id);

        /// <summary>
        /// Display name of an instance by id (custom name, else the engine type).
        /// </summary>
        public string GetInstanceName(Guid id)
        {
            var inst = Engines.FirstOrDefault(e => e.Id == id);
            if (inst == null) return "?";
            return string.IsNullOrWhiteSpace(inst.Name) ? inst.Type.ToString() : inst.Name;
        }

        /// <summary>
        /// Minutes between primary-engine health checks for auto-recovery.
        /// </summary>
        public int EngineRecoveryCheckMinutes { get; set; } = 10;

        public bool VerboseLogging { get; set; } = false;
        public bool OverlayVisible { get; set; } = true;

        // === Local AI ===

        /// <summary>
        /// OpenAI-compatible endpoint URL (base, without /v1/chat/completions).
        /// </summary>
        public string LocalAiEndpoint { get; set; } = "http://localhost:1234";

        /// <summary>
        /// Model name. Empty uses the server-loaded model.
        /// </summary>
        public string LocalAiModel { get; set; } = "";

        /// <summary>
        /// Optional API key.
        /// </summary>
        public string LocalAiApiKey { get; set; } = "";

        /// <summary>
        /// Model temperature.
        /// </summary>
        public float LocalAiTemperature { get; set; } = 0.2f;

        /// <summary>
        /// Request timeout in seconds.
        /// </summary>
        public int LocalAiTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Disables reasoning ("think") mode where supported.
        /// Sent as chat_template_kwargs.think=false.
        /// </summary>
        public bool LocalAiDisableThinking { get; set; } = true;

        /// <summary>
        /// Maximum output tokens. Zero uses the server default.
        /// </summary>
        public int LocalAiMaxTokens { get; set; } = 500;

        /// <summary>
        /// Set when the Local AI server is unreachable and the engine
        /// has automatically switched to Google. Reset when Local AI
        /// is selected again in settings.
        /// </summary>
        public bool LocalAiUnavailable { get; set; } = false;

        // === Cloud LLM profiles (API key based) ===

        public string ChatGptApiKey { get; set; } = "";
        public string ChatGptModel { get; set; } = "gpt-4o-mini";

        public string GeminiApiKey { get; set; } = "";
        public string GeminiModel { get; set; } = "gemini-2.0-flash";

        public string ClaudeApiKey { get; set; } = "";
        public string ClaudeModel { get; set; } = "claude-3-5-sonnet";

        public string DeepSeekApiKey { get; set; } = "";
        public string DeepSeekModel { get; set; } = "deepseek-chat";

        public string OpenRouterApiKey { get; set; } = "";
        public string OpenRouterModel { get; set; } = "openai/gpt-4o-mini";

        public float LlmTemperature { get; set; } = 0.2f;

        [JsonProperty("IncomingChannels", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<int> IncomingChannels { get; set; } = CreateDefaultIncomingChannels();

        [JsonProperty("ChannelColors", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<int, uint> ChannelColors { get; set; } = CreateDefaultChannelColors();

        // === Romanization ===

        /// <summary>
        /// Enables romanization for non-Latin scripts (Japanese, Korean, Russian, Chinese).
        /// </summary>
        public bool EnableRomanization { get; set; } = false;

        [JsonProperty("RomanizationLanguages", ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public HashSet<string> RomanizationLanguages { get; set; } = CreateDefaultRomanizationLanguages();


        [NonSerialized]
        private IDalamudPluginInterface? _pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            _pluginInterface = pluginInterface;
        }

        public bool NormalizeDefaults()
        {
            var changed = false;

            if (Version < CurrentVersion)
            {
                Version = CurrentVersion;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(SourceLanguage))
            {
                SourceLanguage = "en";
                changed = true;
            }

            if (UiLanguage == null)
            {
                UiLanguage = string.Empty;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(TargetLanguage))
            {
                TargetLanguage = "en";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(LocalAiEndpoint))
            {
                LocalAiEndpoint = "http://localhost:1234";
                changed = true;
            }

            if (LocalAiModel == null)
            {
                LocalAiModel = string.Empty;
                changed = true;
            }

            if (LocalAiApiKey == null)
            {
                LocalAiApiKey = string.Empty;
                changed = true;
            }

            if (LocalAiTemperature is < 0f or > 1f)
            {
                LocalAiTemperature = 0.2f;
                changed = true;
            }

            if (LocalAiTimeoutSeconds <= 0)
            {
                LocalAiTimeoutSeconds = 30;
                changed = true;
            }

            if (InactivityHideTimeout < 10 || InactivityHideTimeout > 600)
            {
                InactivityHideTimeout = 60;
                changed = true;
            }

            if (EngineRecoveryCheckMinutes < 1 || EngineRecoveryCheckMinutes > 120)
            {
                EngineRecoveryCheckMinutes = 10;
                changed = true;
            }

            if (LanguageDetectionConfidence > 1f && LanguageDetectionConfidence <= 100f)
            {
                LanguageDetectionConfidence /= 100f;
                changed = true;
            }

            if (LanguageDetectionConfidence < 0.70f || LanguageDetectionConfidence > 1f)
            {
                LanguageDetectionConfidence = 0.85f;
                changed = true;
            }

            // Migrate/backfill the ordered engine list. Only on FIRST run: once
            // initialized, the user's curated list is never touched again.
            if (!EnginesInitialized)
            {
                if (Engines == null || Engines.Count == 0)
                    Engines = CreateDefaultEngines();
                EnginesInitialized = true;
                changed = true;
            }

            Engines ??= new List<EngineInstance>();

            // Ensure a valid primary instance exists when engines are available.
            var primary = Engines.FirstOrDefault(e => e.Id == PrimaryEngineId && e.Enabled);
            if (primary == null)
            {
                primary = Engines.FirstOrDefault(e => e.Enabled);
                if (primary != null)
                {
                    PrimaryEngineId = primary.Id;
                    changed = true;
                }
                else if (Engines.Count > 0)
                {
                    PrimaryEngineId = Engines[0].Id;
                    Engines[0].Enabled = true;
                    changed = true;
                }
            }

            if (ChatGptApiKey == null) { ChatGptApiKey = ""; changed = true; }
            if (GeminiApiKey == null) { GeminiApiKey = ""; changed = true; }
            if (ClaudeApiKey == null) { ClaudeApiKey = ""; changed = true; }
            if (DeepSeekApiKey == null) { DeepSeekApiKey = ""; changed = true; }
            if (OpenRouterApiKey == null) { OpenRouterApiKey = ""; changed = true; }
            if (LlmTemperature is < 0f or > 1f) { LlmTemperature = 0.2f; changed = true; }

            foreach (var engine in Engines)
            {
                if (engine.MaxTokens > 0 && engine.MaxTokens < 500)
                {
                    engine.MaxTokens = 500;
                    changed = true;
                }
            }

            if (ExcludedMessages == null)
            {
                ExcludedMessages = CreateDefaultExcludedMessages();
                changed = true;
            }
            else if (!ReferenceEquals(ExcludedMessages.Comparer, StringComparer.OrdinalIgnoreCase))
            {
                ExcludedMessages = new HashSet<string>(ExcludedMessages, StringComparer.OrdinalIgnoreCase);
                changed = true;
            }

            if (IncomingChannels == null || IncomingChannels.Count == 0)
            {
                IncomingChannels = CreateDefaultIncomingChannels();
                changed = true;
            }

            if (MaxHistorySizeMb < 1 || MaxHistorySizeMb > 1024)
            {
                MaxHistorySizeMb = Math.Clamp(MaxHistorySizeMb, 1, 1024);
                changed = true;
            }

            if (ChannelColors == null || ChannelColors.Count == 0)
            {
                ChannelColors = CreateDefaultChannelColors();
                changed = true;
            }

            if (RomanizationLanguages == null || RomanizationLanguages.Count == 0)
            {
                RomanizationLanguages = CreateDefaultRomanizationLanguages();
                changed = true;
            }
            else if (!ReferenceEquals(RomanizationLanguages.Comparer, StringComparer.OrdinalIgnoreCase))
            {
                RomanizationLanguages = new HashSet<string>(RomanizationLanguages, StringComparer.OrdinalIgnoreCase);
                changed = true;
            }

            // Ensure enough tokens for translation
            if (LocalAiMaxTokens > 0 && LocalAiMaxTokens < 500)
            {
                LocalAiMaxTokens = 500;
                changed = true;
            }

            return changed;
        }

        public string GetReadingLanguage()
        {
            return SourceLanguage;
        }

        private const int SaveDebounceMs = 400;
        private readonly object _saveGate = new();
        private bool _saveScheduled;

        public void Save()
        {
            // Debounce: many UI triggers collapse into one background write.
            lock (_saveGate)
            {
                if (_saveScheduled) return;
                _saveScheduled = true;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(SaveDebounceMs);
                }
                catch
                {
                }

                lock (_saveGate)
                {
                    _saveScheduled = false;
                }

                try
                {
                    _pluginInterface?.SavePluginConfig(this);
                }
                catch
                {
                    // Ignore; the next change re-schedules the save.
                }
            });
        }

        /// <summary>
        /// Synchronous save for plugin unload, so pending debounced changes are not lost.
        /// </summary>
        public void SaveNow()
        {
            lock (_saveGate)
            {
                _saveScheduled = false;
            }
            try
            {
                _pluginInterface?.SavePluginConfig(this);
            }
            catch
            {
                // Ignore on shutdown.
            }
        }

        private static HashSet<string> CreateDefaultExcludedMessages()
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "lol", "o/", "o7", "uwu", "gg", "ty", "thx", "xd", "omg", "wtf",
                "afk", "brb", "gn", "gm", "\\o/", "\\(^o^)/", "^_^", "^^",
                ":)", ":(", ":D", ";)", "<3",
                "P1", "P2", "P3", "P4", "dc", "PLD", "MCH", "DNC",
                "m1s", "m2s", "m3s", "m4s", "m5s", "m6s", "m7s", "m8s", "m9s", "m10s", "m11s", "m12s",
                "PF", "ilvl", "lb", "mb", "tyfp", "c:"
            };
        }

        private static HashSet<int> CreateDefaultIncomingChannels()
        {
            return new HashSet<int>
            {
                10,
                11,
                30,
                14,
                15,
                24,
                12,
                13,
            };
        }

        private static List<EngineInstance> CreateDefaultEngines()
        {
            return new List<EngineInstance>
            {
                new() { Type = TranslationEngine.Google, Name = "Google", Enabled = true },
                new() { Type = TranslationEngine.Papago, Name = "Papago", Enabled = true },
                new() { Type = TranslationEngine.Yandex, Name = "Yandex", Enabled = true },
            };
        }

        private static Dictionary<int, uint> CreateDefaultChannelColors()
        {
            return new Dictionary<int, uint>
            {
                { 10, 0xFFF7F7F7 },
                { 11, 0xFFFFA666 },
                { 30, 0xFFFFFF00 },
                { 14, 0xFF66E5FF },
                { 15, 0xFFFF7F00 },
                { 24, 0xFFABDBE5 },
                { 13, 0xFFFFB8DE },
                { 12, 0xFFFFB8DE },
                { 27, 0xFFD4FF7D },
                { 16, 0xFFD4FF7D },
            };
        }

        public static readonly HashSet<string> SupportedRomanizationLanguages = new(StringComparer.OrdinalIgnoreCase)
        {
            "ja", "ko", "ru", "zh-CN", "zh-TW", "uk"
        };

        private static HashSet<string> CreateDefaultRomanizationLanguages() => new(SupportedRomanizationLanguages, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One configured engine instance in the ordered list. The list order is the
    /// fallback priority; the instance marked as primary is tried first.
    /// </summary>
    [Serializable]
    public class EngineInstance
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public TranslationEngine Type { get; set; }
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;

        // Per-instance credentials/model (cloud LLM engines).
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public float Temperature { get; set; } = 0.2f;
        public int MaxTokens { get; set; } = 500;
    }
}
