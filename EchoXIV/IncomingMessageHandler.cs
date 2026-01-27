using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
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
        private ITranslationService _translatorService;
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

        public IncomingMessageHandler(
            Configuration configuration,
            ITranslationService translatorService,
            IChatGui chatGui,
            IClientState clientState,
            IObjectTable objectTable,
            IPluginLog pluginLog)
        {
            _configuration = configuration;
            _translatorService = translatorService;
            _chatGui = chatGui;
            _clientState = clientState;
            _objectTable = objectTable;
            _pluginLog = pluginLog;

            _chatGui.ChatMessage += OnChatMessage;
            _pluginLog.Info($"✅ IncomingMessageHandler inicializado con motor: {_translatorService.Name}");
        }

        public void UpdateTranslator(ITranslationService newService)
        {
            _translatorService = newService;
            _pluginLog.Info($"IncomingMessageHandler: Motor actualizado a {_translatorService.Name}");
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
                // Determinar idioma destino: usar IncomingTargetLanguage si está configurado,
                // sino usar SourceLanguage (idioma nativo del usuario)
                var targetLanguage = string.IsNullOrEmpty(_configuration.IncomingTargetLanguage)
                    ? _configuration.SourceLanguage
                    : _configuration.IncomingTargetLanguage;
                
                // Traducir con auto-detect de origen → idioma destino configurado
                var translation = await _translatorService.TranslateAsync(
                    message.OriginalText,
                    "auto",          // Siempre auto-detectar idioma de origen
                    targetLanguage   // Idioma destino (configurable)
                );

                message.TranslatedText = translation;
                message.IsTranslating = false;

                if (_configuration.VerboseLogging) _pluginLog.Info($"📥 Traducido entrante: '{message.OriginalText}' → '{message.TranslatedText}'");

                // Notificar que la traducción está lista
                OnMessageTranslated?.Invoke(message);
            }
            catch (TranslationRateLimitException ex)
            {
                _pluginLog.Warning($"⚠️ {ex.Message}. Activando conmutación automática a Google...");
                message.TranslatedText = message.OriginalText; // Fallback inmediato para este mensaje
                message.IsTranslating = false;
                OnMessageTranslated?.Invoke(message);

                // Activar failover (cambiar motor globalmente)
                OnRequestEngineFailover?.Invoke();
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

        public void Dispose()
        {
            _chatGui.ChatMessage -= OnChatMessage;
            if (_configuration.VerboseLogging) _pluginLog.Info("🔌 IncomingMessageHandler desconectado");
        }
    }
}
