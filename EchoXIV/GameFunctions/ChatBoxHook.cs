using System;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using EchoXIV.Services;

namespace EchoXIV.GameFunctions
{
    /// <summary>
    /// Hook nativo para interceptar mensajes ANTES del envío.
    /// Permite traducción sin cancelar el mensaje original (sin error rojo).
    /// Usa siempre la dirección de UIModule.ProcessChatBoxEntry resuelta por Dalamud/FFXIVClientStructs;
    /// no hace falta mantener firmas manualmente al actualizar el juego.
    /// </summary>
    internal unsafe class ChatBoxHook : IDisposable
    {
        /// <summary>
        /// Delegate que coincide con ProcessChatBoxEntry(UIModule* this, Utf8String* message, nint a4, bool saveToHistory).
        /// </summary>
        public delegate void ProcessChatBoxDelegate(UIModule* uiModule, Utf8String* message, nint a4, bool saveToHistory);
        
        public delegate void OutgoingTranslationDelegate(string original, string translated);
        public event OutgoingTranslationDelegate? OnMessageTranslated;

        
        private readonly Configuration _configuration;
        private ITranslationService _translatorService;
        private readonly IPluginLog _pluginLog;
        private readonly IClientState _clientState;
        private readonly IGameInteropProvider _gameInteropProvider;
        
        // Hook de ProcessChatBoxEntry - función que procesa mensajes del chat box
        private Hook<ProcessChatBoxDelegate>? _processChatBoxHook;
        
        /// <summary>
        /// Evento emitido cuando se solicita un cambio de motor por fallo (failover)
        /// </summary>
        public event Action? OnRequestEngineFailover;
        
        private readonly GlossaryService _glossaryService;
        private readonly TranslationCache _translationCache;
        
        public ChatBoxHook(
            Configuration configuration,
            ITranslationService translatorService,
            GlossaryService glossaryService,
            TranslationCache translationCache,
            IPluginLog pluginLog,
            IClientState clientState,
            IGameInteropProvider gameInteropProvider)
        {
            _configuration = configuration;
            _translatorService = translatorService;
            _glossaryService = glossaryService;
            _translationCache = translationCache;
            _pluginLog = pluginLog;
            _clientState = clientState;
            _gameInteropProvider = gameInteropProvider;
        }

        public void UpdateTranslator(ITranslationService newService)
        {
            _translatorService = newService;
        }
        
        public void Enable()
        {
            try
            {
                nint address = (nint)UIModule.Addresses.ProcessChatBoxEntry.Value;
                _processChatBoxHook = _gameInteropProvider.HookFromAddress<ProcessChatBoxDelegate>(address, ProcessChatBoxDetour);
                _processChatBoxHook.Enable();
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "❌ Error al crear hook de ProcessChatBoxEntry");
                throw;
            }
        }
        
        /// <summary>
        /// Detour: intercepta mensajes ANTES de procesarlos. Parámetros = ProcessChatBoxEntry real.
        /// </summary>
        private void ProcessChatBoxDetour(UIModule* uiModule, Utf8String* message, nint a4, bool saveToHistory)
        {
            try
            {
                // Si la traducción está deshabilitada, pasar directamente
                if (!_configuration.TranslationEnabled)
                {
                    _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
                    return;
                }
                
                var originalText = message->ToString();
                
                // No traducir si está vacío, es comando o está en la lista de exclusión
                if (string.IsNullOrWhiteSpace(originalText) || originalText.StartsWith("/") || _configuration.ExcludedMessages.Contains(originalText))
                {
                    _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
                    return;
                }
                
                if (_configuration.VerboseLogging) _pluginLog.Info($"🎯 Hook interceptó: '{originalText}'");
                
                // 1. Verificar caché persistente
                var cached = _translationCache.Get(originalText, _configuration.SourceLanguage, _configuration.TargetLanguage);
                string translatedText;

                if (cached != null)
                {
                    translatedText = cached;
                }
                else
                {
                    // 2. Proteger términos con el Glosario
                    var protectedText = _glossaryService.Protect(originalText);

                    // 3. Traducir (sincrónico)
                    var rawTranslation = _translatorService.TranslateAsync(
                        protectedText,
                        _configuration.SourceLanguage,
                        _configuration.TargetLanguage
                    ).Result;

                    // 4. Restaurar términos y guardar en caché
                    translatedText = _glossaryService.Restore(rawTranslation);
                    
                    if (translatedText != originalText && !string.IsNullOrEmpty(translatedText))
                    {
                        _translationCache.Add(originalText, _configuration.SourceLanguage, _configuration.TargetLanguage, translatedText);
                    }
                }
                
                if (string.IsNullOrWhiteSpace(translatedText) || translatedText == originalText)
                {
                    // Si no se tradujo, enviar original
                    _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
                    return;
                }
                
                if (_configuration.VerboseLogging) _pluginLog.Info($"✅ Traducido: '{originalText}' → '{translatedText}'");
                
                // Notificar traducción para deduplicación en el historial
                OnMessageTranslated?.Invoke(originalText, translatedText);

                
                // SANEAMIENTO: Asegurar que el string sea válido y no exceda límites
                var sanitized = SanitizeText(translatedText);
                
                // Crear nuevo Utf8String con el texto traducido
                var translatedUtf8 = Utf8String.FromString(sanitized);
                
                try
                {
                    // Pasar mensaje TRADUCIDO a la función original
                    _processChatBoxHook!.Original(uiModule, translatedUtf8, a4, saveToHistory);
                }
                finally
                {
                    // Limpiar Utf8String creado
                    translatedUtf8->Dtor(true);
                }
            }
            catch (AggregateException aggEx) when (aggEx.InnerException is TranslationRateLimitException)
            {
                _pluginLog.Warning("⚠️ Límite de DeepL alcanzado durante interceptación. Activando conmutación...");
                OnRequestEngineFailover?.Invoke();
                // Enviar mensaje original
                _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "❌ Error en detour de ProcessChatBox");
                // En caso de error, enviar mensaje original para no perder el mensaje
                _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
            }
        }

        private string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            
            // Eliminar caracteres de control o nulos que puedan romper el chat
            var sanitized = text.Replace("\0", "").Replace("\r", "").Replace("\n", " ");
            
            // Limitar a ~450 para dejar margen al buffer de 500 bytes de FFXIV
            if (Encoding.UTF8.GetByteCount(sanitized) > 450)
            {
                while (Encoding.UTF8.GetByteCount(sanitized) > 447)
                {
                    sanitized = sanitized.Substring(0, sanitized.Length - 1);
                }
                sanitized += "...";
            }
            
            return sanitized;
        }
        
        public void Dispose()
        {
            _processChatBoxHook?.Dispose();
        }
    }
}
