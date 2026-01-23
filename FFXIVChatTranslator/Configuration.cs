using System.Numerics;
using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace FFXIVChatTranslator
{
    /// <summary>
    /// Configuración del plugin
    /// </summary>
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;
        
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
        public HashSet<string> ExcludedMessages { get; set; } = new()
        {
            "lol", "LOL",
            "o/", "o7", 
            "uwu", "UwU", 
            "gg", "GG",
            "ty", "TY", "thx", "THX",
            "xd", "XD", "xD",
            "omg", "OMG",
            "wtf", "WTF",
            "afk", "AFK",
            "brb", "BRB",
            "gn", "GN",
            "gm", "GM",
            "\\o/", "\\(^o^)/", "^_^", "^^",
            ":)", ":(", ":D", ";)",
            "<3"
        };
        
        // Límite de caracteres para cachear (mensajes más largos no se cachean)
        public int CacheMaxMessageLength { get; set; } = 10;
        
        // Habilitar/deshabilitar caché completamente
        public bool CacheEnabled { get; set; } = true;
        
        /// <summary>
        /// Preferir integración con Chat2 si está instalado
        /// </summary>
        public bool PreferChat2Integration { get; set; } = true;

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
