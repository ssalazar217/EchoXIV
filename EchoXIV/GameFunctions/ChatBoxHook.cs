using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using EchoXIV.Services;
using EchoXIV;

namespace EchoXIV.GameFunctions
{
    /// <summary>
    /// Hook nativo para interceptar mensajes ANTES del envío.
    /// Permite traducción sin cancelar el mensaje original (sin error rojo).
    /// Usa siempre el member function y delegate generados por FFXIVClientStructs para
    /// evitar duplicar firmas manuales al actualizar el juego.
    /// </summary>
    internal unsafe class ChatBoxHook : IDisposable
    {
        private readonly Configuration _configuration;
        private ITranslationService _translatorService;
        private readonly IPluginLog _pluginLog;
        private readonly IClientState _clientState;
        private readonly IGameInteropProvider _gameInteropProvider;
        
        // Hook de ProcessChatBoxEntry - función que procesa mensajes del chat box
        private Hook<UIModule.Delegates.ProcessChatBoxEntry>? _processChatBoxHook;
        
        
        private readonly IncomingMessageHandler _incomingMessageHandler;
        private readonly GlossaryService _glossaryService;
        private readonly TranslationCache _translationCache;
        
        public ChatBoxHook(
            Configuration configuration,
            ITranslationService translatorService,
            GlossaryService glossaryService,
            TranslationCache translationCache,
            IncomingMessageHandler incomingMessageHandler,
            IPluginLog pluginLog,
            IClientState clientState,
            IGameInteropProvider gameInteropProvider)
        {
            _configuration = configuration;
            _translatorService = translatorService;
            _glossaryService = glossaryService;
            _translationCache = translationCache;
            _incomingMessageHandler = incomingMessageHandler;
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
                _processChatBoxHook = _gameInteropProvider.HookFromAddress<UIModule.Delegates.ProcessChatBoxEntry>((nint)UIModule.MemberFunctionPointers.ProcessChatBoxEntry, ProcessChatBoxDetour);
                _processChatBoxHook.Enable();
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Failed to create ProcessChatBoxEntry hook.");
                throw;
            }
        }
        
        /// <summary>
        /// Detour: intercepta mensajes ANTES de procesarlos.
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
                
                // 1. BYPASS: Si es un comando, está vacío o es un mensaje que ya tradujimos nosotros (/tl)
                // Usamos remove:false porque IncomingMessageHandler lo eliminará cuando llegue el mensaje al chat real.
                if (string.IsNullOrWhiteSpace(originalText) || originalText.StartsWith("/") || 
                    _configuration.ExcludedMessages.Contains(originalText) ||
                    _incomingMessageHandler.IsPendingOutgoing(originalText, false))
                {
                    _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
                    return;
                }
                
                if (_configuration.VerboseLogging) _pluginLog.Info($"Hook intercepted message for translation: '{originalText}'");
                
                // 2. Verificar caché persistente (Rápido)
                var cached = _translationCache.Get(originalText, _configuration.SourceLanguage, _configuration.TargetLanguage);
                if (cached != null)
                {
                    _incomingMessageHandler.RegisterPendingOutgoing(cached, originalText);
                    SendTranslated(uiModule, cached, a4, saveToHistory);
                    return;
                }

                // 3. TRADUCCIÓN ASÍNCRONA (Sin congelar el juego)
                ProcessAsync(uiModule, originalText, a4, saveToHistory);
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Critical error in ProcessChatBox detour.");
                _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
            }
        }

        private void ProcessAsync(UIModule* uiModule, string originalText, nint a4, bool saveToHistory)
        {
            // Capturar lo necesario para el callback
            var context = new TranslationContext 
            { 
                UiModule = (nint)uiModule, 
                OriginalText = originalText, 
                A4 = a4, 
                SaveToHistory = saveToHistory 
            };

            TranslationHelper.TranslateAsync(
                _translatorService,
                _glossaryService,
                _translationCache,
                _incomingMessageHandler,
                _configuration,
                _pluginLog,
                context,
                (ctx, translated) => 
                {
                    _ = Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        if (translated != null)
                        {
                            SendTranslated((UIModule*)ctx.UiModule, translated, ctx.A4, ctx.SaveToHistory);
                        }
                        else
                        {
                            var originalUtf8 = Utf8String.FromString(ctx.OriginalText);
                            try {
                                _processChatBoxHook!.Original((UIModule*)ctx.UiModule, originalUtf8, ctx.A4, ctx.SaveToHistory);
                            } finally {
                                originalUtf8->Dtor(true);
                            }
                        }
                    });
                }
            );
        }

        private void SendTranslated(UIModule* uiModule, string text, nint a4, bool saveToHistory)
        {
            var sanitized = SanitizeText(text);
            var translatedUtf8 = Utf8String.FromString(sanitized);
            try
            {
                _processChatBoxHook!.Original(uiModule, translatedUtf8, a4, saveToHistory);
            }
            finally
            {
                translatedUtf8->Dtor(true);
            }
        }

        private string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sanitized = text.Replace("\0", "").Replace("\r", "").Replace("\n", " ");
            if (Encoding.UTF8.GetByteCount(sanitized) > 450)
            {
                _ = Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    Plugin.ChatGui.PrintError(GetResourceText("Command_MessageTruncated", "The translated message exceeded the chat limit and was truncated before sending."));
                });

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

        private static string GetResourceText(string key, string fallback)
        {
            return Properties.Resources.ResourceManager.GetString(key, Properties.Resources.Culture) ?? fallback;
        }
    }

    internal class TranslationContext
    {
        public nint UiModule { get; set; }
        public string OriginalText { get; set; } = string.Empty;
        public nint A4 { get; set; }
        public bool SaveToHistory { get; set; }
    }

    internal static class TranslationHelper
    {
        public static void TranslateAsync(
            ITranslationService translator,
            GlossaryService glossary,
            TranslationCache cache,
            IncomingMessageHandler handler,
            Configuration config,
            IPluginLog log,
            TranslationContext context,
            Action<TranslationContext, string?> callback)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var protectedText = glossary.Protect(context.OriginalText);
                    using var timeout = TranslationDefaults.CreateTimeoutTokenSource();
                    var rawTranslation = await translator.TranslateAsync(
                        protectedText,
                        "auto",
                        config.TargetLanguage,
                        timeout.Token
                    );

                    var translatedText = glossary.Restore(rawTranslation);
                    
                    if (!string.IsNullOrEmpty(translatedText) && translatedText != context.OriginalText)
                    {
                        cache.Add(context.OriginalText, config.SourceLanguage, config.TargetLanguage, translatedText);
                        handler.RegisterPendingOutgoing(translatedText, context.OriginalText);
                        callback(context, translatedText);
                    }
                    else
                    {
                        callback(context, null);
                    }
                }
                catch (OperationCanceledException ex)
                {
                    log.Warning(ex, "Outgoing translation timed out.");
                    callback(context, null);
                }
                catch (Exception ex)
                {
                    log.Error(ex, "❌ Error en TranslationHelper");
                    callback(context, null);
                }
            });
        }
    }
}
