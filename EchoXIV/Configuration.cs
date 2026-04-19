using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace EchoXIV
{
    /// <summary>
    /// Configuración del plugin
    /// </summary>
    public enum TranslationEngine
    {
        Google,
        Papago
    }

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        private const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        
        /// <summary>
        /// Indica si es la primera vez que se ejecuta el plugin
        /// </summary>
        public bool FirstRun { get; set; } = true;
        
        /// <summary>
        /// Activa o desactiva la traducción automática
        /// </summary>
        public bool TranslationEnabled { get; set; } = true;
        
        /// <summary>
        /// Idioma en el que escribes tus propios mensajes.
        /// </summary>
        public string SourceLanguage { get; set; } = "en";
        
        /// <summary>
        /// Idioma al que se traducen tus mensajes salientes.
        /// </summary>
        public string TargetLanguage { get; set; } = "en";
        
        // Lista de mensajes que NO se traducen (expresiones universales, emoticonos, etc.)
        public HashSet<string> ExcludedMessages { get; set; } = CreateDefaultExcludedMessages();
        

        



        // === Configuración de Traducciones Entrantes ===
        
        /// <summary>
        /// Habilitar traducción de mensajes entrantes
        /// </summary>
        public bool IncomingTranslationEnabled { get; set; } = true;
        
        /// <summary>
        /// Máximo de mensajes a mostrar en la ventana de traducciones
        /// </summary>
        public int MaxDisplayedMessages { get; set; } = 50;
        
        /// <summary>
        /// Mostrar texto original junto con la traducción
        /// </summary>
        public bool ShowOriginalText { get; set; } = true;
        
        /// <summary>
        /// Mostrar timestamps en los mensajes
        /// </summary>
        public bool ShowTimestamps { get; set; } = true;
        
        /// <summary>
        /// Mostrar mis propios mensajes en la ventana de chat
        /// </summary>
        public bool ShowOutgoingMessages { get; set; } = true;

        /// <summary>
        /// Idioma al que se traducen los mensajes entrantes.
        /// El idioma origen siempre se detecta automáticamente.
        /// Si está vacío, se reutiliza el idioma en el que escribes.
        /// </summary>
        public string IncomingTargetLanguage { get; set; } = "";
        // --- Visuales ---
        public int FontSize { get; set; } = 16;
        public int ChatMessageSpacing { get; set; } = 0;
        public string TimestampFormat { get; set; } = "HH:mm"; // "HH:mm:ss", "Short"

        public TranslationEngine SelectedEngine { get; set; } = TranslationEngine.Papago;
        public string PapagoVersionKey { get; set; } = Constants.DefaultPapagoVersionKey;
        public bool VerboseLogging { get; set; } = false; // Logs detallados (off por defecto)
        public bool OverlayVisible { get; set; } = true;

        /// <summary>
        /// Canales de chat entrantes a traducir
        /// </summary>
        public HashSet<int> IncomingChannels { get; set; } = CreateDefaultIncomingChannels();

        /// <summary>
        /// Colores personalizados para cada canal de chat (ARGB)
        /// Guardamos como uint para serialización simple
        /// </summary>
        public Dictionary<int, uint> ChannelColors { get; set; } = CreateDefaultChannelColors();


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

            if (string.IsNullOrWhiteSpace(PapagoVersionKey))
            {
                PapagoVersionKey = Constants.DefaultPapagoVersionKey;
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
    }
}
