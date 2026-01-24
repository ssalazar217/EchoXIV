using System;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVChatTranslator.Services;

namespace FFXIVChatTranslator.GameFunctions
{
    /// <summary>
    /// Hook nativo para interceptar mensajes ANTES del envío
    /// Permite traducción sin cancelar el mensaje original (sin error rojo)
    /// </summary>
    internal unsafe class ChatBoxHook : IDisposable
    {
        private readonly Configuration _configuration;
        private readonly GoogleTranslatorService _translatorService;
        private readonly IPluginLog _pluginLog;
        private readonly IClientState _clientState;
        private readonly IGameInteropProvider _gameInteropProvider;
        
        // Hook de ProcessChatBoxEntry - función que procesa mensajes del chat box
        // Signature de ChatTwo como referencia
        private Hook<ProcessChatBoxDelegate>? _processChatBoxHook;
        private delegate void ProcessChatBoxDelegate(UIModule* uiModule, Utf8String* message, IntPtr unused, byte a4);
        
        public ChatBoxHook(
            Configuration configuration,
            GoogleTranslatorService translatorService,
            IPluginLog pluginLog,
            IClientState clientState,
            IGameInteropProvider gameInteropProvider)
        {
            _configuration = configuration;
            _translatorService = translatorService;
            _pluginLog = pluginLog;
            _clientState = clientState;
            _gameInteropProvider = gameInteropProvider;
        }
        
        public void Enable()
        {
            try
            {
                // Intentar hookear ProcessChatBoxEntry
                // Signature puede necesitar ajustes según versión del juego
                _processChatBoxHook = _gameInteropProvider.HookFromSignature<ProcessChatBoxDelegate>(
                    "48 89 5C 24 ?? 57 48 83 EC 20 48 8B FA 48 8B D9 45 84 C9",
                    ProcessChatBoxDetour
                );
                
                _processChatBoxHook?.Enable();
                _pluginLog.Info("✅ Hook nativo de ProcessChatBoxEntry habilitado");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "❌ Error al crear hook de ProcessChatBoxEntry");
                throw;
            }
        }
        
        /// <summary>
        /// Detour function: intercepta mensajes ANTES de procesarlos
        /// </summary>
        private void ProcessChatBoxDetour(UIModule* uiModule, Utf8String* message, IntPtr unused, byte a4)
        {
            try
            {
                // Si la traducción está deshabilitada, pasar directamente
                if (!_configuration.TranslationEnabled)
                {
                    _processChatBoxHook!.Original(uiModule, message, unused, a4);
                    return;
                }
                
                var originalText = message->ToString();
                
                // No traducir si está vacío o es comando
                if (string.IsNullOrWhiteSpace(originalText) || originalText.StartsWith("/"))
                {
                    _processChatBoxHook!.Original(uiModule, message, unused, a4);
                    return;
                }
                
                _pluginLog.Info($"🎯 Hook interceptó: '{originalText}'");
                
                // Verificar si ya está en caché
                var cacheKey = $"{originalText}|{_configuration.SourceLanguage}|{_configuration.TargetLanguage}";
                
                // TRADUCIR (sincrónico usando .Result para mantener thread)
                // En caché será instantáneo, fuera de caché tomará tiempo
                var translatedText = _translatorService.TranslateAsync(
                    originalText,
                    _configuration.SourceLanguage,
                    _configuration.TargetLanguage
                ).Result;
                
                if (string.IsNullOrWhiteSpace(translatedText) || translatedText == originalText)
                {
                    // Si no se tradujo, enviar original
                    _processChatBoxHook!.Original(uiModule, message, unused, a4);
                    return;
                }
                
                _pluginLog.Info($"✅ Traducido: '{originalText}' → '{translatedText}'");
                
                // Crear nuevo Utf8String con el texto traducido
                var translatedUtf8 = Utf8String.FromString(translatedText);
                
                try
                {
                    // Pasar mensaje TRADUCIDO a la función original
                    _processChatBoxHook!.Original(uiModule, translatedUtf8, unused, a4);
                }
                finally
                {
                    // Limpiar Utf8String creado
                    translatedUtf8->Dtor(true);
                }
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "❌ Error en detour de ProcessChatBox");
                // En caso de error, enviar mensaje original para no perder el mensaje
                _processChatBoxHook!.Original(uiModule, message, unused, a4);
            }
        }
        
        public void Dispose()
        {
            _processChatBoxHook?.Dispose();
            _pluginLog.Info("🔌 Hook nativo de ProcessChatBoxEntry deshabilitado");
        }
    }
}
