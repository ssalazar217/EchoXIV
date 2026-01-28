using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using System.Text.RegularExpressions;
using EchoXIV.Services;

namespace EchoXIV
{
    /// <summary>
    /// Modelo de mensaje traducido para la ventana de chat
    /// </summary>
    public class TranslatedChatMessage
    {
        public DateTime Timestamp { get; set; }
        public XivChatType ChatType { get; set; }
        public string Sender { get; set; } = string.Empty;
        public string OriginalText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty; // Destinatario (para Tells)
        public bool IsTranslating { get; set; }
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    /// <summary>
    /// Manejador de mensajes entrantes para traducción
    /// </summary>
    public class IncomingMessageHandler : IDisposable
    {
        private readonly Configuration _configuration;
        private ITranslationService _primaryTranslator;
        private ITranslationService? _secondaryTranslator;
        private readonly IChatGui _chatGui;
        private readonly IClientState _clientState;
        private readonly IObjectTable _objectTable;
        private readonly IPluginLog _pluginLog;
        private readonly Dictionary<string, string> _pendingOutgoingTranslations = new(); // Translated -> Original

        /// <summary>
        /// Evento emitido cuando un mensaje ha sido traducido
        /// </summary>
        public event Action<TranslatedChatMessage>? OnMessageTranslated;

        /// <summary>
        /// Evento emitido cuando se inicia la traducción de un mensaje
        /// </summary>
        public event Action<TranslatedChatMessage>? OnTranslationStarted;

        /// <summary>
        /// Evento emitido cuando se solicita un cambio de motor por fallo (failover)
        /// </summary>
        public event Action? OnRequestEngineFailover;

        private readonly GlossaryService _glossaryService;
        private readonly TranslationCache _translationCache;

        public IncomingMessageHandler(
            Configuration configuration,
            ITranslationService primaryTranslator,
            ITranslationService? secondaryTranslator,
            GlossaryService glossaryService,
            TranslationCache translationCache,
            IChatGui chatGui,
            IClientState clientState,
            IObjectTable objectTable,
            IPluginLog pluginLog)
        {
            _configuration = configuration;
            _primaryTranslator = primaryTranslator;
            _secondaryTranslator = secondaryTranslator;
            _glossaryService = glossaryService;
            _translationCache = translationCache;
            _chatGui = chatGui;
            _clientState = clientState;
            _objectTable = objectTable;
            _pluginLog = pluginLog;

            _chatGui.ChatMessage += OnChatMessage;
            _pluginLog.Info($"✅ IncomingMessageHandler inicializado con motor: {_primaryTranslator.Name}");
        }

        public void UpdateTranslator(ITranslationService newService)
        {
            _primaryTranslator = newService;
            _pluginLog.Info($"IncomingMessageHandler: Motor principal actualizado a {_primaryTranslator.Name}");
        }

        public void UpdateSecondaryTranslator(ITranslationService? newService)
        {
            _secondaryTranslator = newService;
            if (_secondaryTranslator != null)
                _pluginLog.Info($"IncomingMessageHandler: Motor secundario actualizado a {_secondaryTranslator.Name}");
        }

        public void RegisterPendingOutgoing(string translated, string original)
        {
            if (string.IsNullOrEmpty(translated)) return;
            lock (_pendingOutgoingTranslations)
            {
                _pendingOutgoingTranslations[translated] = original;
            }
        }

        private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            // Solo procesar si las traducciones entrantes están habilitadas.
            if (!_configuration.IncomingTranslationEnabled)
                return;

            // Verificar si el canal está en la lista de canales a traducir
            if (!_configuration.IncomingChannels.Contains((int)type))
                return;

            // Obtener texto del mensaje
            var messageText = message.TextValue;
            var senderName = sender.TextValue;

            // Formatear nombre: Nombre Apellido@Servidor
            var localPlayer = _objectTable.LocalPlayer;
            var nameParts = senderName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (nameParts.Length == 3)
            {
                // Formato Cross-World estándar: First Last World
                senderName = $"{nameParts[0]} {nameParts[1]}@{nameParts[2]}";
            }
            else if (nameParts.Length == 2)
            {
                 // Posible formato Cross-World pegado: First LastWorld
                 // O Jugador del mismo mundo: First Last
                 
                 string lastPart = nameParts[1];
                 int worldSplitIndex = -1;
                 
                 // Buscar una mayúscula que no sea la primera letra del apellido
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
                     // Es First LastWorld -> First Last@World
                     senderName = $"{nameParts[0]} {lastPart.Substring(0, worldSplitIndex)}@{lastPart.Substring(worldSplitIndex)}";
                 }
                 else if (localPlayer != null && senderName == localPlayer.Name.TextValue)
                 {
                     // Jugador local: añadir mundo
                     senderName = $"{localPlayer.Name.TextValue}@{localPlayer.HomeWorld.Value.Name}";
                 }
                 else if (localPlayer != null)
                 {
                     // Otro jugador mismo mundo: añadir mundo local
                     senderName = $"{senderName}@{localPlayer.HomeWorld.Value.Name}";
                 }
            }

            // Ignorar mensajes vacíos
            if (string.IsNullOrWhiteSpace(messageText))
                return;

            // FILTRO RMT/SPAM: Evitar gastar API en basura
            if (IsRmtSpam(messageText))
            {
                if (_configuration.VerboseLogging) _pluginLog.Info($"🚫 Spam RMT detectado y omitido: {messageText.Substring(0, Math.Min(20, messageText.Length))}...");
                return;
            }

            // Ignorar comandos
            if (messageText.StartsWith("/"))
                return;

            // Verificar si es del jugador local
            var isLocalPlayer = localPlayer != null && (senderName.StartsWith(localPlayer.Name.TextValue));
            
            if (isLocalPlayer && !_configuration.ShowOutgoingMessages)
                return;

            // DEDUPLICACIÓN: Verificar si es una traducción que nosotros mismos enviamos
            string? originalFromPending = null;
            lock (_pendingOutgoingTranslations)
            {
                if (_pendingOutgoingTranslations.TryGetValue(messageText, out originalFromPending))
                {
                    _pendingOutgoingTranslations.Remove(messageText);
                }
            }

            if (originalFromPending != null)
            {
                // Ya lo tenemos, no traducir otra vez y usar el original real
                var pendingMsg = new TranslatedChatMessage
                {
                    Timestamp = DateTime.Now,
                    ChatType = type,
                    Sender = senderName,
                    OriginalText = originalFromPending,
                    TranslatedText = messageText,
                    IsTranslating = false
                };
                OnMessageTranslated?.Invoke(pendingMsg);
                return;
            }

            // Verificar lista de exclusión (insensible a mayúsculas gracias al HashSet configurado)
            if (_configuration.ExcludedMessages.Contains(messageText))
            {
                // Mensaje excluido: se muestra en el historial pero NO se traduce
                var excludedMsg = new TranslatedChatMessage
                {
                    Timestamp = DateTime.Now,
                    ChatType = type,
                    Sender = senderName,
                    OriginalText = messageText,
                    TranslatedText = messageText, 
                    IsTranslating = false
                };
                OnMessageTranslated?.Invoke(excludedMsg);
                return;
            }

            // Crear mensaje inicial (mostrando "traduciendo...")
            var translatedMessage = new TranslatedChatMessage
            {
                Timestamp = DateTime.Now,
                ChatType = type,
                Sender = senderName,
                OriginalText = messageText,
                TranslatedText = string.Empty,
                IsTranslating = true
            };

            // Notificar que se inició la traducción
            OnTranslationStarted?.Invoke(translatedMessage);

            // Traducir async
            _ = TranslateAsync(translatedMessage);
        }

        private async Task TranslateAsync(TranslatedChatMessage message)
        {
            try
            {
                // 1. Verificar Caché
                var targetLanguage = string.IsNullOrEmpty(_configuration.IncomingTargetLanguage)
                    ? _configuration.SourceLanguage
                    : _configuration.IncomingTargetLanguage;

                var cached = _translationCache.Get(message.OriginalText, "auto", targetLanguage);
                if (cached != null)
                {
                    message.TranslatedText = cached;
                    message.IsTranslating = false;
                    OnMessageTranslated?.Invoke(message);
                    return;
                }

                // 2. Proteger términos con Glossary
                var protectedText = _glossaryService.Protect(message.OriginalText);
                
                // 3. Traducir
                var translation = await _primaryTranslator.TranslateAsync(
                    protectedText,
                    "auto",
                    targetLanguage
                );

                // 4. Restaurar términos y guardar en caché
                var finalTranslation = _glossaryService.Restore(translation);
                
                _translationCache.Add(message.OriginalText, "auto", targetLanguage, finalTranslation);

                message.TranslatedText = finalTranslation;
                message.IsTranslating = false;

                if (_configuration.VerboseLogging) _pluginLog.Info($"📥 Traducido entrante: '{message.OriginalText}' → '{message.TranslatedText}'");

                // Notificar que la traducción está lista
                OnMessageTranslated?.Invoke(message);
            }
            catch (TranslationRateLimitException ex)
            {
                _pluginLog.Warning($"⚠️ {ex.Message}. Intentando failover inmediato...");
                
                try
                {
                    if (_secondaryTranslator != null)
                    {
                        var protectedText = _glossaryService.Protect(message.OriginalText);
                        var targetLanguage = string.IsNullOrEmpty(_configuration.IncomingTargetLanguage)
                            ? _configuration.SourceLanguage
                            : _configuration.IncomingTargetLanguage;

                        var translation = await _secondaryTranslator.TranslateAsync(protectedText, "auto", targetLanguage);
                        var finalTranslation = _glossaryService.Restore(translation);
                        
                        _translationCache.Add(message.OriginalText, "auto", targetLanguage, finalTranslation);
                        message.TranslatedText = finalTranslation;
                        message.IsTranslating = false;
                        OnMessageTranslated?.Invoke(message);
                        
                        // Si funcionó, pedir cambio permanente de motor
                        OnRequestEngineFailover?.Invoke();
                        return;
                    }
                }
                catch (Exception secondaryEx)
                {
                    _pluginLog.Error(secondaryEx, "Error en el motor secundario durante failover");
                }

                message.TranslatedText = message.OriginalText; // Fallback final
                message.IsTranslating = false;
                OnMessageTranslated?.Invoke(message);
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Error traduciendo mensaje entrante");
                message.TranslatedText = message.OriginalText; // Fallback
                message.IsTranslating = false;
                OnMessageTranslated?.Invoke(message);
            }
        }

        public void InjectMessage(TranslatedChatMessage message)
        {
            OnMessageTranslated?.Invoke(message);
        }

        private bool IsRmtSpam(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            
            // Patrones comunes de RMT
            var spamPatterns = new[] {
                @"g\s*u\s*l\s*d\s*2\s*v\s*i\s*p", // guld2vip
                @"f\s*f\s*1\s*4\s*c\s*o\s*i\s*n", // ff14coin
                @"p\s*v\s*p\s*b\s*a\s*n\s*k",     // pvpbank
                @"m\s*m\s*o\s*g\s*a\s*h",         // mmogah
                @"\$\d+\s*=\s*\d+M",               // $10=100M (precios comunes)
                @"FAST\s*DELIVERY",
                @"CHEAP\s*GIL"
            };

            foreach (var pattern in spamPatterns)
            {
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                    return true;
            }

            return false;
        }

        public void Dispose()
        {
            _chatGui.ChatMessage -= OnChatMessage;
            if (_configuration.VerboseLogging) _pluginLog.Info("🔌 IncomingMessageHandler desconectado");
        }
    }
}
