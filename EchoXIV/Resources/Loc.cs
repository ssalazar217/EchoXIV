using System.Globalization;
using System.Resources;

namespace EchoXIV.Resources
{
    /// <summary>
    /// Clase de localización para acceder a strings traducidos
    /// </summary>
    public static class Loc
    {
        private static readonly ResourceManager ResourceManager = 
            new("EchoXIV.Resources.Strings", typeof(Loc).Assembly);
        
        private static CultureInfo _culture = CultureInfo.CurrentUICulture;
        
        /// <summary>
        /// Establece la cultura para la localización
        /// </summary>
        public static void SetCulture(string cultureCode)
        {
            try
            {
                _culture = new CultureInfo(cultureCode);
            }
            catch
            {
                _culture = CultureInfo.CurrentUICulture;
            }
        }
        
        /// <summary>
        /// Obtiene un string localizado por su clave
        /// </summary>
        public static string Get(string key)
        {
            try
            {
                return ResourceManager.GetString(key, _culture) ?? key;
            }
            catch
            {
                return key;
            }
        }
        
        // === Plugin Info ===
        public static string PluginName => Get("PluginName");
        
        // === Tabs ===
        public static string Tab_General => Get("Tab_General");
        public static string Tab_ExcludedMessages => Get("Tab_ExcludedMessages");
        public static string Tab_Cache => Get("Tab_Cache");
        public static string Tab_IncomingChannels => Get("Tab_IncomingChannels");
        
        // === General Tab ===
        public static string General_Description => Get("General_Description");
        public static string General_EnableTranslation => Get("General_EnableTranslation");
        public static string General_SourceLanguage => Get("General_SourceLanguage");
        public static string General_TargetLanguage => Get("General_TargetLanguage");
        public static string General_DefaultChannel => Get("General_DefaultChannel");
        public static string General_DefaultChannelTip => Get("General_DefaultChannelTip");
        
        // === Incoming Channels Tab ===
        public static string Incoming_Description => Get("Incoming_Description");
        public static string Incoming_Enable => Get("Incoming_Enable");
        public static string Incoming_Disabled => Get("Incoming_Disabled");
        public static string Incoming_TranslateTo => Get("Incoming_TranslateTo");
        public static string Incoming_AutoDetectNote => Get("Incoming_AutoDetectNote");
        public static string Incoming_UseWritingLanguage => Get("Incoming_UseWritingLanguage");
        public static string Incoming_DisplayOptions => Get("Incoming_DisplayOptions");
        public static string Incoming_ShowOriginal => Get("Incoming_ShowOriginal");
        public static string Incoming_ShowTimestamps => Get("Incoming_ShowTimestamps");
        public static string Incoming_MaxMessages => Get("Incoming_MaxMessages");
        public static string Incoming_ChannelsToTranslate => Get("Incoming_ChannelsToTranslate");
        public static string Incoming_Linkshells => Get("Incoming_Linkshells");
        public static string Incoming_CrossWorldLS => Get("Incoming_CrossWorldLS");
        
        // === Excluded Messages Tab ===
        public static string Excluded_Description => Get("Excluded_Description");
        public static string Excluded_CurrentList => Get("Excluded_CurrentList");
        public static string Excluded_AddNew => Get("Excluded_AddNew");
        public static string Excluded_Add => Get("Excluded_Add");
        
        // === Cache Tab ===
        public static string Cache_Description => Get("Cache_Description");
        public static string Cache_Enable => Get("Cache_Enable");
        public static string Cache_Description_Long => Get("Cache_Description_Long");
        public static string Cache_MaxLength => Get("Cache_MaxLength");
        public static string Cache_Stats => Get("Cache_Stats");
        public static string Cache_Entries => Get("Cache_Entries");
        public static string Cache_Clear => Get("Cache_Clear");
        public static string Cache_ClearTooltip => Get("Cache_ClearTooltip");
        public static string Cache_Tip => Get("Cache_Tip");
        
        // === Translated Chat Window ===
        public static string ChatWindow_Title => Get("ChatWindow_Title");
        public static string ChatWindow_Active => Get("ChatWindow_Active");
        public static string ChatWindow_AutoScroll => Get("ChatWindow_AutoScroll");
        public static string ChatWindow_Clear => Get("ChatWindow_Clear");
        public static string ChatWindow_Translating => Get("ChatWindow_Translating");
        public static string ChatWindow_Original => Get("ChatWindow_Original");
        
        // === Commands ===
        public static string Command_TranslationEnabled => Get("Command_TranslationEnabled");
        public static string Command_TranslationDisabled => Get("Command_TranslationDisabled");
        public static string Command_WindowOpened => Get("Command_WindowOpened");
        public static string Command_WindowClosed => Get("Command_WindowClosed");
        public static string Command_PositionReset => Get("Command_PositionReset");
        public static string Command_NoMessage => Get("Command_NoMessage");
        public static string Command_MessageTooLong => Get("Command_MessageTooLong");
        public static string Command_TranslateError => Get("Command_TranslateError");
        public static string Command_SendError => Get("Command_SendError");
            public static string General_ChangesAppliedImmediately => Get("General_ChangesAppliedImmediately");
        
        public static string Excluded_InputLabel => Get("Excluded_InputLabel");
        public static string Excluded_Remove => Get("Excluded_Remove");
        public static string Excluded_RestoreDefault => Get("Excluded_RestoreDefault");
        
        public static string Cache_CharLimit => Get("Cache_CharLimit");
        public static string Cache_PercentUsed => Get("Cache_PercentUsed");
        
        public static string Incoming_WindowOpacity => Get("Incoming_WindowOpacity");
        public static string Incoming_NativeWindow => Get("Incoming_NativeWindow");
        public static string Incoming_NativeWindowTooltip => Get("Incoming_NativeWindowTooltip");
        public static string Incoming_RestartNote => Get("Incoming_RestartNote");
            public static string Cache_DisabledWarning => Get("Cache_DisabledWarning");
    }
}
