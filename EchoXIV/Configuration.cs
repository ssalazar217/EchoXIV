using System.Numerics;
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
        public int Version { get; set; } = 0;
        
        /// <summary>
        /// Indica si es la primera vez que se ejecuta el plugin
        /// </summary>
        public bool FirstRun { get; set; } = true;
        
        /// <summary>
        /// Activa o desactiva la traducción automática
        /// </summary>
        public bool TranslationEnabled { get; set; } = true;
        
        /// <summary>
        /// Idioma origen (raramente cambia)
        /// </summary>
        public string SourceLanguage { get; set; } = "es"; // Español
        
        /// <summary>
        /// Idioma destino (cambio frecuente vía combobox)
        /// </summary>
        public string TargetLanguage { get; set; } = "en"; // English
        
        // Lista de mensajes que NO se traducen (expresiones universales, emoticonos, etc.)
        public HashSet<string> ExcludedMessages { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            "lol", "o/", "o7", "uwu", "gg", "ty", "thx", "xd", "omg", "wtf", 
            "afk", "brb", "gn", "gm", "\\o/", "\\(^o^)/", "^_^", "^^", 
            ":)", ":(", ":D", ";)", "<3", 
            "P1", "P2", "P3", "P4", "dc", "PLD", "MCH", "DNC",
            "m1s", "m2s", "m3s", "m4s", "m5s", "m6s", "m7s", "m8s", "m9s", "m10s", "m11s", "m12s",
            "PF", "ilvl", "lb", "mb", "tyfp", "c:"
        };
        

        



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
        /// Idioma destino para traducciones entrantes (el idioma AL QUE se traduce)
        /// El idioma origen siempre es auto-detectado
        /// Si está vacío, se usa SourceLanguage por defecto
        /// </summary>
        public string IncomingTargetLanguage { get; set; } = "";
        
        /// <summary>
        /// Canales habilitados para traducción entrante
        /// </summary>

        
        /// <summary>
        /// Opacidad de fondo de la ventana (0.0 a 1.0)
        /// </summary>
        public float WindowOpacity { get; set; } = 0.6f;



        // --- Geometría de la Ventana WPF (Nativa) ---
        public double WindowLeft { get; set; } = 100.0;
        public double WindowTop { get; set; } = 100.0;
        public double WindowWidth { get; set; } = 400.0;
        public double WindowHeight { get; set; } = 300.0;
        
        // --- Geometría de la Ventana ImGui (Interna) ---
        public Vector2 ImGuiPosition { get; set; } = new(100, 100);
        public Vector2 ImGuiSize { get; set; } = new(400, 300);
        
        // --- Visuales ---
        public int FontSize { get; set; } = 16;
        public int ChatMessageSpacing { get; set; } = 0;
        public string TimestampFormat { get; set; } = "HH:mm"; // "HH:mm:ss", "Short"

        /// <summary>
        /// Ocultar ventana automáticamente cuando el juego pierde el foco
        /// </summary>
        public bool SmartVisibility { get; set; } = true;

        public TranslationEngine SelectedEngine { get; set; } = TranslationEngine.Papago;
        public string PapagoVersionKey { get; set; } = Constants.DefaultPapagoVersionKey;
        public bool VerboseLogging { get; set; } = false; // Logs detallados (off por defecto)
        public bool OverlayVisible { get; set; } = true;

        /// <summary>
        /// Usar ventana nativa (WPF) en lugar de ventana interna de Dalamud
        /// True = Ventana externa (mejor para multipantalla)
        /// False = Ventana interna (mejor para overlay simple)
        /// </summary>
        public bool UseNativeWindow { get; set; } = true;

        /// <summary>
        /// Canales de chat entrantes a traducir
        /// </summary>
        public HashSet<int> IncomingChannels { get; set; } = new()
        {
            10,  // Say
            11,  // Shout
            30,  // Yell
            14,  // Party
            15,  // Alliance
            24,  // FreeCompany
        };

        /// <summary>
        /// Colores personalizados para cada canal de chat (ARGB)
        /// Guardamos como uint para serialización simple
        /// </summary>
        public Dictionary<int, uint> ChannelColors { get; set; } = new()
        {
            { 10, 0xFFF7F7F7 }, // Say
            { 11, 0xFFFFA666 }, // Shout
            { 30, 0xFFFFFF00 }, // Yell
            { 14, 0xFF66E5FF }, // Party
            { 15, 0xFFFF7F00 }, // Alliance
            { 24, 0xFFABDBE5 }, // FreeCompany
            { 13, 0xFFFFB8DE }, // TellIncoming
            { 12, 0xFFFFB8DE }, // TellOutgoing
            { 27, 0xFFD4FF7D }, // NoviceNetwork
            { 16, 0xFFD4FF7D }, // Ls1 (y el resto de LS suelen ser iguales)
        };


        [NonSerialized]
        private IDalamudPluginInterface? _pluginInterface;

        public void Initialize(IDalamudPluginInterface pluginInterface)
        {
            _pluginInterface = pluginInterface;
        }

        public void Save()
        {
            _pluginInterface!.SavePluginConfig(this);
        }
    }
}
