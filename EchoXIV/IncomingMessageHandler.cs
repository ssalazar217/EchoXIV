using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using EchoXIV.Services;

namespace EchoXIV
{
    /// <summary>
    /// Translated message model for the chat window
    /// </summary>
public class TranslatedChatMessage
    {
        public DateTime Timestamp { get; set; }
        public XivChatType ChatType { get; set; }
        public string Sender { get; set; } = string.Empty;
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public bool IsTranslating { get; set; }
        public bool IsOutgoing { get; set; }
        public Guid Id { get; set; } = Guid.NewGuid();

        // Romanization fields
        public string? RomanizedText { get; set; }
        public string OriginalScript { get; set; } = "Latin";
    }

    /// <summary>
    /// Incoming message translation handler
    /// </summary>
    public class IncomingMessageHandler : IDisposable
    {
        private sealed class PendingOutgoingEntry
        {
            public required string SourceText { get; init; }
            public required string NativeText { get; init; }
            public string? RomanizedText { get; init; }
            public required DateTime CreatedAtUtc { get; init; }
        }

        private static readonly TimeSpan PendingOutgoingTtl = TimeSpan.FromMinutes(2);
        private const int MaxPendingOutgoingEntries = 256;
        private static readonly Regex[] RmtSpamPatterns =
        {
            new(@"g\s*u\s*l\s*d\s*2\s*v\s*i\s*p", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"f\s*f\s*1\s*4\s*c\s*o\s*i\s*n", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"p\s*v\s*p\s*b\s*a\s*n\s*k", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"m\s*m\s*o\s*g\s*a\s*h", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"\$\d+\s*=\s*\d+M", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"FAST\s*DELIVERY", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            new(@"CHEAP\s*GIL", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };

        private readonly Configuration _configuration;
        private ITranslationService _primaryTranslator;
        private ITranslationService? _secondaryTranslator;
        private readonly IChatGui _chatGui;
        private readonly IClientState _clientState;
        private readonly IPlayerState _playerState;
        private readonly IObjectTable _objectTable;
        private readonly IPluginLog _pluginLog;
        private readonly Dictionary<string, PendingOutgoingEntry> _pendingOutgoingTranslations = new(); // Translated -> Original

        /// <summary>
        /// Raised when a message finishes translating
        /// </summary>
        public event Action<TranslatedChatMessage>? OnMessageTranslated;

        /// <summary>
        /// Raised when message translation starts
        /// </summary>
        public event Action<TranslatedChatMessage>? OnTranslationStarted;

        /// <summary>
        /// Raised for outgoing messages detected via pending (separate from incoming flow)
        /// </summary>
        public event Action<TranslatedChatMessage>? OnOutgoingMessage;

        /// <summary>
        /// Raised when an engine failover is requested
        /// </summary>
        public event Action? OnRequestEngineFailover;

        private readonly GlossaryService _glossaryService;
        private readonly TranslationCache _translationCache;
        private readonly Func<XivChatType, List<ConversationLine>?>? _conversationContextProvider;
        private IRomanizationService _romanizationService;
        private readonly CancellationTokenSource _lifetimeCts = new();

        public IncomingMessageHandler(
            Configuration configuration,
            ITranslationService primaryTranslator,
            ITranslationService? secondaryTranslator,
            GlossaryService glossaryService,
            TranslationCache translationCache,
            IChatGui chatGui,
            IClientState clientState,
            IPlayerState playerState,
            IObjectTable objectTable,
            IPluginLog pluginLog,
            IRomanizationService romanizationService,
            Func<XivChatType, List<ConversationLine>?>? conversationContextProvider = null)
        {
            _configuration = configuration;
            _primaryTranslator = primaryTranslator;
            _secondaryTranslator = secondaryTranslator;
            _glossaryService = glossaryService;
            _translationCache = translationCache;
            _chatGui = chatGui;
            _clientState = clientState;
            _playerState = playerState;
            _objectTable = objectTable;
            _pluginLog = pluginLog;
            _romanizationService = romanizationService;
            _conversationContextProvider = conversationContextProvider;

            _chatGui.ChatMessage += OnChatMessage;
        }

        public void UpdateTranslator(ITranslationService newService)
        {
            _primaryTranslator = newService;
        }

        public void UpdateSecondaryTranslator(ITranslationService? newService)
        {
            _secondaryTranslator = newService;
        }

        public void UpdateRomanizationService(IRomanizationService newService)
        {
            _romanizationService = newService;
        }

        public void RegisterPendingOutgoing(string sentText, string sourceText, string? nativeText = null, string? romanizedText = null)
        {
            if (string.IsNullOrEmpty(sentText)) return;
            lock (_pendingOutgoingTranslations)
            {
                PruneExpiredPendingOutgoingEntries();
                PruneOverflowPendingOutgoingEntries();
                _pendingOutgoingTranslations[sentText] = new PendingOutgoingEntry
                {
                    SourceText = sourceText,
                    NativeText = nativeText ?? sentText,
                    RomanizedText = romanizedText,
                    CreatedAtUtc = DateTime.UtcNow
                };
            }
        }

        public bool IsPendingOutgoing(string translated, bool remove)
        {
            if (string.IsNullOrEmpty(translated)) return false;
            lock (_pendingOutgoingTranslations)
            {
                PruneExpiredPendingOutgoingEntries();
                if (_pendingOutgoingTranslations.ContainsKey(translated))
                {
                    if (remove) _pendingOutgoingTranslations.Remove(translated);
                    return true;
                }
            }
            return false;
        }

        private PendingOutgoingEntry? TakePendingOutgoing(string sentText)
        {
            if (string.IsNullOrEmpty(sentText)) return null;
            lock (_pendingOutgoingTranslations)
            {
                PruneExpiredPendingOutgoingEntries();
                if (!_pendingOutgoingTranslations.TryGetValue(sentText, out var pendingEntry))
                    return null;

                _pendingOutgoingTranslations.Remove(sentText);
                return pendingEntry;
            }
        }

        private void OnChatMessage(IHandleableChatMessage message)
        {
            var type = message.LogKind;
            // Get message text
            var messageText = message.Message.TextValue;

            var pendingOutgoing = TakePendingOutgoing(messageText);
            string? originalFromPending = pendingOutgoing?.SourceText;

            // Not an explicit pending translation: apply config filters
            if (originalFromPending == null)
            {
                // Require incoming translations enabled
                if (!_configuration.IncomingTranslationEnabled)
                    return;

                // Channel must be enabled for translation
                if (!_configuration.IncomingChannels.Contains((int)type))
                    return;
            }

            var senderName = message.Sender.TextValue;

            // Format name: First Last@World - idempotent: keep if already contains @, only append for local player
            var localPlayerName = _playerState.CharacterName.ToString();
            string? localPlayerWorldName = null;
            if (_playerState.HomeWorld.RowId > 0)
            {
               localPlayerWorldName = _playerState.HomeWorld.Value.Name.ToString();
            }

            const string worldIcon = "@";

            if (!senderName.Contains("@"))
            {
                if (senderName == localPlayerName && !string.IsNullOrEmpty(localPlayerWorldName))
                {
                    senderName = $"{senderName}{worldIcon}{localPlayerWorldName}";
                }
                else if (senderName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length == 3)
                {
                    // Standard cross-world format: First Last World -> First Last@World
                    var parts = senderName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    senderName = $"{parts[0]} {parts[1]}{worldIcon}{parts[2]}";
                }
                else
                {
                    // Check glued LastWorld pattern
                    var nameParts = senderName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (nameParts.Length == 2)
                    {
                        var lastPart = nameParts[1];
                        int worldSplitIndex = -1;
                        for (int i = 1; i < lastPart.Length; i++)
                        {
                            if (char.IsUpper(lastPart[i]))
                            {
                                worldSplitIndex = i;
                                break;
                            }
                        }
                        if (worldSplitIndex != -1)
                        {
                            senderName = $"{nameParts[0]} {lastPart.Substring(0, worldSplitIndex)}{worldIcon}{lastPart.Substring(worldSplitIndex)}";
                        }
                    }
                }
            }

            // Ignore empty messages
            if (string.IsNullOrWhiteSpace(messageText))
                return;

            // Check local player
            var isLocalPlayer = !string.IsNullOrEmpty(localPlayerName) && (senderName.StartsWith(localPlayerName));
            
            // Drop own messages when ShowOutgoingMessages is off, unless explicit pending
            if (isLocalPlayer && !_configuration.ShowOutgoingMessages && originalFromPending == null)
                return;

            if (pendingOutgoing != null)
            {
                // Outgoing: use dedicated event to avoid mixing with incoming write path
                var pendingMsg = new TranslatedChatMessage
                {
                    Timestamp = DateTime.Now,
                    ChatType = type,
                    Sender = senderName,
                    OriginalText = pendingOutgoing.SourceText,
                    TranslatedText = pendingOutgoing.NativeText,
                    RomanizedText = pendingOutgoing.RomanizedText,
                    OriginalScript = NonLatinScriptDetector.Detect(pendingOutgoing.NativeText).ToString(),
                    IsOutgoing = true,
                    IsTranslating = false
                };

                OnOutgoingMessage?.Invoke(pendingMsg);
                return;
            }

            // RMT/spam filter: avoid wasting API calls
            if (IsRmtSpam(messageText))
            {
                if (_configuration.VerboseLogging) _pluginLog.Info($"RMT spam detected and skipped: {messageText.Substring(0, Math.Min(20, messageText.Length))}...");
                return;
            }

            // Ignore commands
            if (messageText.StartsWith("/"))
                return;

            // Exclusion list check (case-insensitive)
            if (_configuration.ExcludedMessages.Contains(messageText))
            {
                // Excluded: shown in history but not translated
                var excludedMsg = new TranslatedChatMessage
                {
                    Timestamp = DateTime.Now,
                    ChatType = type,
                    Sender = senderName,
                    OriginalText = messageText,
                    TranslatedText = messageText, 
                    IsTranslating = false
                };
                OnTranslationStarted?.Invoke(excludedMsg);
                return;
            }

            var translatedMessage = new TranslatedChatMessage
            {
                Timestamp = DateTime.Now,
                ChatType = type,
                Sender = senderName,
                OriginalText = messageText,
                TranslatedText = string.Empty,
                IsTranslating = true
            };

            OnTranslationStarted?.Invoke(translatedMessage);

            _ = TranslateAsync(translatedMessage);
        }

        public async Task ProcessMessageAsync(TranslatedChatMessage message)
        {
            if (message == null) return;
            
            _pluginLog.Info($"[Retry] Starting retry for: {message.OriginalText.Substring(0, Math.Min(20, message.OriginalText.Length))}...");
            message.IsTranslating = true;
            await TranslateAsync(message);
        }

        private async Task TranslateAsync(TranslatedChatMessage message)
        {
            try
            {
                // Check cache
                var targetLanguage = _configuration.GetReadingLanguage();

                if (_translationCache.TryGetWithRomanization(
                        message.OriginalText,
                        "auto",
                        targetLanguage,
                        out var cached,
                        out var cachedRomanized))
                {
                    message.TranslatedText = cached!;
                    message.RomanizedText = cachedRomanized;
                    await ApplyRomanizationAsync(
                        message,
                        CancellationToken.None,
                        requestIfMissing: string.IsNullOrWhiteSpace(message.RomanizedText));
                    if (!string.IsNullOrWhiteSpace(message.RomanizedText))
                    {
                        _translationCache.Add(
                            message.OriginalText,
                            "auto",
                            targetLanguage,
                            message.TranslatedText,
                            message.RomanizedText);
                    }
                    message.IsTranslating = false;
                    OnMessageTranslated?.Invoke(message);
                    return;
                }

                // 2. Protect glossary terms
                var glossary = _glossaryService.Protect(message.OriginalText);
                var protectedText = glossary.Text;
                
                // 3. Translate
                using var primaryTimeout = TranslationDefaults.CreateTimeoutTokenSource(_lifetimeCts.Token);
                string translation;
                var combinedRomanization = false;

                var sourceScript = NonLatinScriptDetector.Detect(message.OriginalText);
                var sourceLanguageCode = sourceScript.ToLanguageCode();
                var canUseCombinedRomanization = _configuration.EnableRomanization &&
                    _configuration.SelectedEngine == TranslationEngine.LocalAI &&
                    _romanizationService is LocalAiTranslatorService &&
                    !string.IsNullOrEmpty(sourceLanguageCode) &&
                    _configuration.RomanizationLanguages.Contains(sourceLanguageCode);

                if (canUseCombinedRomanization)
                {
                    var localAi = (LocalAiTranslatorService)_romanizationService;
                    var context = _configuration.LocalAiUseConversationContext && _conversationContextProvider != null
                        ? _conversationContextProvider(message.ChatType)
                        : null;
                    var combined = await localAi.TranslateWithRomanizationAsync(
                        protectedText,
                        "auto",
                        targetLanguage,
                        context,
                        primaryTimeout.Token,
                        romanizeTranslation: false);
                    translation = combined.Translation;
                    message.RomanizedText = combined.RomanizedText;
                    message.OriginalScript = combined.OriginalScript;
                    combinedRomanization = true;
                }
                else if (_primaryTranslator is LocalAiTranslatorService localAi &&
                         _configuration.LocalAiUseConversationContext &&
                         _conversationContextProvider != null)
                {
                    var context = _conversationContextProvider(message.ChatType);
                    translation = await localAi.TranslateWithContextAsync(
                        protectedText,
                        "auto",
                        targetLanguage,
                        context,
                        primaryTimeout.Token
                    );
                }
                else
                {
                    translation = await _primaryTranslator.TranslateAsync(
                        protectedText,
                        "auto",
                        targetLanguage,
                        primaryTimeout.Token
                    );
                }

                // 4. Restore glossary terms and cache
                var finalTranslation = SanitizeText(glossary.Restore(translation));
                
                message.TranslatedText = finalTranslation;
                
                await ApplyRomanizationAsync(message, primaryTimeout.Token, !combinedRomanization);

                _translationCache.Add(
                    message.OriginalText,
                    "auto",
                    targetLanguage,
                    finalTranslation,
                    message.RomanizedText);
                
                message.IsTranslating = false;

                if (_configuration.VerboseLogging) _pluginLog.Info($"[{message.ChatType}] Incoming translated: '{message.OriginalText}' -> '{message.TranslatedText}'");

                // Notify translation ready
                OnMessageTranslated?.Invoke(message);
            }
            catch (OperationCanceledException ex)
            {
                if (_lifetimeCts.IsCancellationRequested)
                    return;

                _pluginLog.Warning(ex, "Translation timed out for incoming message.");
                message.TranslatedText = message.OriginalText;
                message.IsTranslating = false;
                OnMessageTranslated?.Invoke(message);
            }
            catch (TranslationRateLimitException ex)
            {
                _pluginLog.Warning($"{ex.Message}. Attempting immediate failover...");
                
                try
                {
                    if (_secondaryTranslator != null)
                    {
                        var secondaryGlossary = _glossaryService.Protect(message.OriginalText);
                        var targetLanguage = _configuration.GetReadingLanguage();

                        using var secondaryTimeout = TranslationDefaults.CreateTimeoutTokenSource(_lifetimeCts.Token);
                        var translation = await _secondaryTranslator.TranslateAsync(secondaryGlossary.Text, "auto", targetLanguage, secondaryTimeout.Token);
                        var finalTranslation = SanitizeText(secondaryGlossary.Restore(translation));
                        
                        message.TranslatedText = finalTranslation;
                        
                        await ApplyRomanizationAsync(message, secondaryTimeout.Token);

                        _translationCache.Add(
                            message.OriginalText,
                            "auto",
                            targetLanguage,
                            finalTranslation,
                            message.RomanizedText);
                        
                        message.IsTranslating = false;
                        OnMessageTranslated?.Invoke(message);
                        
                        // Request permanent engine switch on success
                        OnRequestEngineFailover?.Invoke();
                        return;
                    }
                }
                catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
                {
                    return;
                }
                catch (OperationCanceledException secondaryTimeoutEx)
                {
                    _pluginLog.Warning(secondaryTimeoutEx, "Secondary translator timed out during failover.");
                }
                catch (Exception secondaryEx)
                {
                    _pluginLog.Error(secondaryEx, "Error in secondary translator during failover.");
                }

                message.TranslatedText = message.OriginalText; // Final fallback
                
                await ApplyRomanizationAsync(message, CancellationToken.None);
                
                message.IsTranslating = false;
                OnMessageTranslated?.Invoke(message);
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Error while translating incoming message.");
                message.TranslatedText = message.OriginalText; // Fallback
                message.IsTranslating = false;                OnMessageTranslated?.Invoke(message);
            }
        }

        private async Task ApplyRomanizationAsync(
            TranslatedChatMessage message,
            CancellationToken cancellationToken,
            bool requestIfMissing = true)
        {
            var script = NonLatinScriptDetector.Detect(message.OriginalText);
            if (message.OriginalScript == "Latin" || string.IsNullOrEmpty(message.OriginalScript))
                message.OriginalScript = script == NonLatinScriptKind.Latin ? "Latin" : script.ToString();

            if (!requestIfMissing)
                return;

            var languageCode = script.ToLanguageCode();
            if (!_configuration.EnableRomanization || string.IsNullOrEmpty(languageCode) ||
                !_configuration.RomanizationLanguages.Contains(languageCode))
            {
                return;
            }

            try
            {
                message.RomanizedText = await _romanizationService.RomanizeAsync(
                    message.OriginalText, languageCode, cancellationToken);
            }
            catch (Exception ex)
            {
                _pluginLog.Warning(ex, "Romanization failed for incoming message");
                message.RomanizedText = null;
            }
        }

        private bool IsRmtSpam(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            foreach (var pattern in RmtSpamPatterns)
            {
                if (pattern.IsMatch(text))
                    return true;
            }

            return false;
        }

        private void PruneExpiredPendingOutgoingEntries()
        {
            var cutoff = DateTime.UtcNow - PendingOutgoingTtl;
            var expiredKeys = new List<string>();

            foreach (var entry in _pendingOutgoingTranslations)
            {
                if (entry.Value.CreatedAtUtc < cutoff)
                {
                    expiredKeys.Add(entry.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                _pendingOutgoingTranslations.Remove(key);
            }
        }

        private void PruneOverflowPendingOutgoingEntries()
        {
            if (_pendingOutgoingTranslations.Count < MaxPendingOutgoingEntries)
            {
                return;
            }

            var orderedKeys = _pendingOutgoingTranslations
                .OrderBy(entry => entry.Value.CreatedAtUtc)
                .Take(_pendingOutgoingTranslations.Count - MaxPendingOutgoingEntries + 1)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var key in orderedKeys)
            {
                _pendingOutgoingTranslations.Remove(key);
            }
        }

        private string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            
            // Strip control/null chars that could break chat or overlay
            var sanitized = text.Replace("\0", "").Replace("\r", "").Replace("\n", " ");
            
            // Guard against invalid characters from APIs
            return sanitized;
        }

        public void Dispose()
        {
            _lifetimeCts.Cancel();
            _chatGui.ChatMessage -= OnChatMessage;
            if (_configuration.VerboseLogging) _pluginLog.Info("IncomingMessageHandler disconnected.");
        }
    }
}
