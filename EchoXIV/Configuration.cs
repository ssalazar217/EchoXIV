using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using Dalamud.Plugin;
using EchoXIV.Services;

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
        LocalAI
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

        /// <summary>
        /// Target language for incoming messages.
        /// Source language is always auto-detected.
        /// Falls back to the writing language when empty.
        /// </summary>
        public string IncomingTargetLanguage { get; set; } = "";

        // --- Visuals ---
        public int FontSize { get; set; } = 16;
        public int ChatMessageSpacing { get; set; } = 0;
        public string TimestampFormat { get; set; } = "HH:mm"; // "HH:mm:ss", "Short"
        public bool HideWhenInactive { get; set; } = false;
        public int InactivityHideTimeout { get; set; } = 60; // seconds

        [Flags]
        public enum ExtraGlyph { None = 0, Cyrillic = 1, Greek = 2, Vietnamese = 4, Thai = 8 }

        public ExtraGlyph ExtraGlyphs { get; set; } = ExtraGlyph.None;

        public TranslationEngine SelectedEngine { get; set; } = TranslationEngine.Papago;

        /// <summary>
        /// Backup engine used when the primary fails or hits its limit.
        /// null = rotate the remaining engines.
        /// The primary engine itself never changes on failover.
        /// </summary>
        public TranslationEngine? FallbackEngine { get; set; } = null;

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

        // === Conversation Context (Local AI) ===

        /// <summary>
        /// Uses recent channel history as AI context. Only applies to Local AI.
        /// </summary>
        public bool LocalAiUseConversationContext { get; set; } = true;

        /// <summary>
        /// Number of recent channel messages used as context.
        /// </summary>
        public int LocalAiContextMessages { get; set; } = 10;

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

        /// <summary>
        /// Channels where conversation context applies (Local AI only).
        /// </summary>
        public HashSet<int> LocalAiContextChannels { get; set; } = CreateDefaultContextChannels();

        /// <summary>
        /// Incoming chat channels to translate
        /// </summary>
        public HashSet<int> IncomingChannels { get; set; } = CreateDefaultIncomingChannels();

        /// <summary>
        /// Custom ARGB color per chat channel (uint for simple serialization)
        /// </summary>
        public Dictionary<int, uint> ChannelColors { get; set; } = CreateDefaultChannelColors();

        // === Romanization ===

        /// <summary>
        /// Enables romanization for non-Latin scripts (Japanese, Korean, Russian, Chinese).
        /// </summary>
        public bool EnableRomanization { get; set; } = false;

        /// <summary>
        /// Languages to romanize.
        /// </summary>
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

            if (IncomingTargetLanguage == null)
            {
                IncomingTargetLanguage = string.Empty;
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

            if (LocalAiContextMessages < 0 || LocalAiContextMessages > 50)
            {
                LocalAiContextMessages = 10;
                changed = true;
            }

            if (InactivityHideTimeout < 10 || InactivityHideTimeout > 600)
            {
                InactivityHideTimeout = 60;
                changed = true;
            }

            if (FallbackEngine != null && FallbackEngine == SelectedEngine)
            {
                FallbackEngine = null;
                changed = true;
            }

            if (EngineRecoveryCheckMinutes < 1 || EngineRecoveryCheckMinutes > 120)
            {
                EngineRecoveryCheckMinutes = 10;
                changed = true;
            }

            if (LocalAiContextChannels == null || LocalAiContextChannels.Count == 0)
            {
                LocalAiContextChannels = CreateDefaultContextChannels();
                changed = true;
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
            return string.IsNullOrWhiteSpace(IncomingTargetLanguage)
                ? SourceLanguage
                : IncomingTargetLanguage;
        }

        public void Save()
        {
            _pluginInterface!.SavePluginConfig(this);
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

        private static HashSet<int> CreateDefaultContextChannels()
        {
            // Group/raid channels only, to avoid context spam in noisy channels.
            return new HashSet<int>
            {
                14,
                15,
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

        private static HashSet<string> CreateDefaultRomanizationLanguages() => new(StringComparer.OrdinalIgnoreCase)
        {
            "ja", "ko", "ru", "zh-CN", "zh-TW"
        };
    }
}
