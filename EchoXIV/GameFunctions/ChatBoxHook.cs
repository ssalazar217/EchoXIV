using System;
using System.Text;
using System.Diagnostics;
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
    /// Hooks chat box messages before sending so they can be translated
    /// without canceling the original message.
    /// Uses FFXIVClientStructs member functions/delegates to survive game updates.
    /// </summary>
    internal unsafe class ChatBoxHook : IDisposable
    {
        private readonly Configuration _configuration;
        private ITranslationService _translatorService;
        private ITranslationService? _fallbackTranslator;
        private readonly Action _onEngineFailover;
        private readonly IPluginLog _pluginLog;
        private readonly IClientState _clientState;
        private readonly IGameInteropProvider _gameInteropProvider;
        
        // ProcessChatBoxEntry hook
        private Hook<UIModule.Delegates.ProcessChatBoxEntry>? _processChatBoxHook;
        
        
        private readonly IncomingMessageHandler _incomingMessageHandler;
        private readonly GlossaryService _glossaryService;
        private readonly TranslationCache _translationCache;
        private readonly CancellationTokenSource _lifetimeCts = new();

        private IRomanizationService? RomanizationService => _translatorService as IRomanizationService;
        
        public ChatBoxHook(
            Configuration configuration,
            ITranslationService translatorService,
            ITranslationService? fallbackTranslator,
            GlossaryService glossaryService,
            TranslationCache translationCache,
            IncomingMessageHandler incomingMessageHandler,
            IPluginLog pluginLog,
            IClientState clientState,
            IGameInteropProvider gameInteropProvider,
            Action onEngineFailover)
        {
            _configuration = configuration;
            _translatorService = translatorService;
            _fallbackTranslator = fallbackTranslator;
            _glossaryService = glossaryService;
            _translationCache = translationCache;
            _incomingMessageHandler = incomingMessageHandler;
            _pluginLog = pluginLog;
            _clientState = clientState;
            _gameInteropProvider = gameInteropProvider;
            _onEngineFailover = onEngineFailover;
        }

        public void UpdateTranslator(ITranslationService newService)
        {
            _translatorService = newService;
        }

        public void UpdateTranslators(ITranslationService newService, ITranslationService? fallbackTranslator)
        {
            _translatorService = newService;
            _fallbackTranslator = fallbackTranslator;
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
        /// Detour: intercepts messages before processing.
        /// </summary>
        private void ProcessChatBoxDetour(UIModule* uiModule, Utf8String* message, nint a4, bool saveToHistory)
        {
            try
            {
                // Pass through when translation is disabled
                if (!_configuration.TranslationEnabled)
                {
                    _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
                    return;
                }
                
                var originalText = message->ToString();
                
                // Bypass commands, empty input, excluded texts and our own pending translations
                if (string.IsNullOrWhiteSpace(originalText) || originalText.StartsWith("/") || 
                    _configuration.ExcludedMessages.Contains(originalText) ||
                    _incomingMessageHandler.IsPendingOutgoing(originalText, false))
                {
                    _processChatBoxHook!.Original(uiModule, message, a4, saveToHistory);
                    return;
                }
                
                if (_configuration.VerboseLogging) _pluginLog.Info($"Hook intercepted message for translation: '{originalText}'");
                
                // Check persistent cache
                if (_translationCache.TryGetWithRomanization(originalText, _configuration.SourceLanguage, _configuration.TargetLanguage, out var cachedNative, out var cachedRomaji))
                {
                    // Send cached romaji when still enabled
                    var textToSendCached = cachedNative!;
                    if (_configuration.EnableRomanization &&
                        !string.IsNullOrEmpty(cachedRomaji) &&
                        _configuration.RomanizationLanguages.Contains(_configuration.TargetLanguage))
                    {
                        textToSendCached = cachedRomaji!;
                    }

                    // Sanitize before registering so the pending key matches what the game shows
                    var sentTextCached = SanitizeText(textToSendCached);
                    _incomingMessageHandler.RegisterPendingOutgoing(
                        sentTextCached,
                        originalText,
                        cachedNative,
                        sentTextCached != cachedNative ? sentTextCached : null);
                    if (_configuration.VerboseLogging)
                    {
                        LogOutgoingTranslation(
                            _pluginLog,
                            _translatorService.Name + " (cache)",
                            _configuration.TargetLanguage,
                            0,
                            originalText,
                            cachedNative!,
                            sentTextCached != cachedNative ? sentTextCached : null);
                    }
                    SendTranslated(uiModule, sentTextCached, a4, saveToHistory);
                    return;
                }

                // Async translation (non-blocking)
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
            // Capture context for the callback
            var context = new TranslationContext 
            { 
                UiModule = (nint)uiModule, 
                OriginalText = originalText, 
                A4 = a4, 
                SaveToHistory = saveToHistory 
            };

            TranslationHelper.TranslateAsync(
                _translatorService,
                _fallbackTranslator,
                _glossaryService,
                _translationCache,
                _incomingMessageHandler,
                _configuration,
                _pluginLog,
                _onEngineFailover,
                _lifetimeCts.Token,
                context,
                (ctx, translated) => 
                {
                    _ = Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        if (_lifetimeCts.IsCancellationRequested)
                            return;

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

        internal static string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sanitized = text.Replace("\0", "").Replace("\r", "").Replace("\n", " ");
            var maxBytes = TranslationDefaults.MaxChatBytes;
            if (Encoding.UTF8.GetByteCount(sanitized) > maxBytes)
            {
                _ = Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    Plugin.ChatGui.PrintError(GetResourceText("Command_MessageTruncated", "The translated message exceeded the chat limit and was truncated before sending."));
                });

                while (Encoding.UTF8.GetByteCount(sanitized) > maxBytes - 3)
                {
                    sanitized = sanitized.Substring(0, sanitized.Length - 1);
                }
                sanitized += "...";
            }
            return sanitized;
        }

        internal static void LogOutgoingTranslation(
            IPluginLog log,
            string engineName,
            string targetLanguage,
            long elapsedMilliseconds,
            string sourceText,
            string nativeTranslation,
            string? romanizedText = null)
        {
            var romanization = string.IsNullOrWhiteSpace(romanizedText)
                ? string.Empty
                : $" (romaji: '{romanizedText}')";
            log.Info($"Translated and sent [Echo] using {engineName} to {targetLanguage} ({elapsedMilliseconds}ms): '{sourceText}' -> '{nativeTranslation}'{romanization}");
        }
        
        public void Dispose()
        {
            _lifetimeCts.Cancel();
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
                ITranslationService? fallbackTranslator,
                GlossaryService glossary,
                TranslationCache cache,
                IncomingMessageHandler handler,
                Configuration config,
                IPluginLog log,
                Action onEngineFailover,
                CancellationToken lifetimeToken,
                TranslationContext context,
                Action<TranslationContext, string?> callback)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var stopwatch = Stopwatch.StartNew();
                        var glossaryProtection = glossary.Protect(context.OriginalText);
                        using var timeout = TranslationDefaults.CreateTimeoutTokenSource(lifetimeToken);
                        string rawTranslation;
                        string? romanizedFromTranslation = null;
                        var combinedRomanization = false;
                        var engineName = translator.Name;

                        try
                        {
                            if (config.EnableRomanization &&
                                config.RomanizationLanguages.Contains(config.TargetLanguage) &&
                                translator is ICombinedTranslator combinedTranslator)
                            {
                                var combined = await combinedTranslator.TranslateWithRomanizationAsync(
                                    glossaryProtection.Text,
                                    "auto",
                                    config.TargetLanguage,
                                    timeout.Token,
                                    romanizeTranslation: true);
                                rawTranslation = combined.Translation;
                                romanizedFromTranslation = combined.RomanizedText;
                                combinedRomanization = true;
                            }
                            else
                            {
                                rawTranslation = await translator.TranslateAsync(
                                    glossaryProtection.Text,
                                    "auto",
                                    config.TargetLanguage,
                                    timeout.Token
                                );
                            }
                        }
                        catch (TranslationRateLimitException)
                        {
                            // Primary engine unavailable: try the fallback and switch permanently.
                            log.Warning("Primary engine unavailable for outgoing message. Attempting failover...");
                            if (fallbackTranslator != null)
                            {
                                using var fallbackTimeout = TranslationDefaults.CreateTimeoutTokenSource(lifetimeToken);
                                rawTranslation = await fallbackTranslator.TranslateAsync(
                                    glossaryProtection.Text,
                                    "auto",
                                    config.TargetLanguage,
                                    fallbackTimeout.Token
                                );
                                engineName = fallbackTranslator.Name;
                                onEngineFailover?.Invoke();
                            }
                            else
                            {
                                throw;
                            }
                        }

                        var translatedText = glossaryProtection.Restore(rawTranslation);
                        
                        // Outgoing romanization: non-Latin results only
                        var targetScript = NonLatinScriptDetector.Detect(translatedText);
                        string textToSend = translatedText;
                        string textForTooltip = translatedText;
                        
                        if (!combinedRomanization &&
                            config.EnableRomanization && 
                            config.RomanizationLanguages.Contains(config.TargetLanguage) &&
                            targetScript != NonLatinScriptKind.Latin &&
                            translator is IRomanizationService romanizer)
                        {
                            try
                            {
                                var romanized = await romanizer.RomanizeAsync(
                                    translatedText, config.TargetLanguage, timeout.Token);
                                if (!string.IsNullOrEmpty(romanized))
                                {
                                    textToSend = romanized;           // Sent to game
                                textForTooltip = translatedText;  // Native script for tooltip
                                }
                            }
                            catch (Exception ex)
                            {
                                log.Warning(ex, "Romanization failed for outgoing message");
                            }
                        }
                        else if (combinedRomanization && !string.IsNullOrWhiteSpace(romanizedFromTranslation))
                        {
                            textToSend = romanizedFromTranslation;
                            textForTooltip = translatedText;
                        }

                        if (translator is TranslationRouter router &&
                            !string.IsNullOrWhiteSpace(router.LastUsedInstanceName) &&
                            router.LastUsedInstanceName != "?")
                        {
                            engineName = router.LastUsedInstanceName;
                        }
                        
                        if (!string.IsNullOrEmpty(textToSend) && textToSend != context.OriginalText)
                        {
                            // Cache native + romaji pair
                            var romanjiForCache = textToSend != textForTooltip ? textToSend : null;
                            cache.Add(context.OriginalText, config.SourceLanguage, config.TargetLanguage, translatedText, romanjiForCache);

                            if (config.VerboseLogging)
                            {
                                ChatBoxHook.LogOutgoingTranslation(
                                    log,
                                    engineName,
                                    config.TargetLanguage,
                                    stopwatch.ElapsedMilliseconds,
                                    context.OriginalText,
                                    translatedText,
                                    romanjiForCache);
                            }

                            // Sanitize before registering so the pending key matches what the game shows
                            var sentText = ChatBoxHook.SanitizeText(textToSend);

                            // Inverted: main=romaji, tooltip=native
                            handler.RegisterPendingOutgoing(
                                sentText,
                                context.OriginalText,
                                textForTooltip,
                                sentText != textForTooltip ? sentText : null);
                            callback(context, sentText);
                        }
                        else
                        {
                            callback(context, null);
                        }
                    }
                    catch (OperationCanceledException ex)
                    {
                        if (lifetimeToken.IsCancellationRequested)
                            return;
                        log.Warning(ex, "Outgoing translation timed out.");
                        callback(context, null);
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex, "âŒ Error en TranslationHelper");
                        callback(context, null);
                    }
                }, lifetimeToken);
            }
    }
}
