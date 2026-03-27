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
        private readonly IPlayerState _playerState;
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
            IPlayerState playerState,
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
            _playerState = playerState;
            _objectTable = objectTable;
            _pluginLog = pluginLog;

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

        public void RegisterPendingOutgoing(string translated, string original)
        {
            if (string.IsNullOrEmpty(translated)) return;
            lock (_pendingOutgoingTranslations)
            {
                _pendingOutgoingTranslations[translated] = original;
            }
        }

        public bool IsPendingOutgoing(string translated, bool remove)
        {
            if (string.IsNullOrEmpty(translated)) return false;
            lock (_pendingOutgoingTranslations)
            {
                if (_pendingOutgoingTranslations.ContainsKey(translated))
                {
                    if (remove) _pendingOutgoingTranslations.Remove(translated);
                    return true;
                }
            }
            return false;
        }

        public string? GetOriginalFromPending(string translated, bool remove)
        {
            if (string.IsNullOrEmpty(translated)) return null;
            lock (_pendingOutgoingTranslations)
            {
                if (_pendingOutgoingTranslations.TryGetValue(translated, out var original))
                {
                    if (remove) _pendingOutgoingTranslations.Remove(translated);
                    return original;
                }
            }
            return null;
        }

        private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            // Obtener texto del mensaje
            var messageText = message.TextValue;

            string? originalFromPending = GetOriginalFromPending(messageText, true);

            // Si NO es una traducción pendiente explícita, aplicar filtros de configuración
            if (originalFromPending == null)
            {
                // Solo procesar si las traducciones entrantes están habilitadas.
                if (!_configuration.IncomingTranslationEnabled)
                    return;

                // Verificar si el canal está en la lista de canales a traducir
                if (!_configuration.IncomingChannels.Contains((int)type))
                    return;
            }

            var senderName = sender.TextValue;

            // Formatear nombre: Nombre Apellido@Servidor
            var localPlayerName = _playerState.CharacterName.ToString();
            // Resolver nombre del mundo (string) para evitar problemas de tipos con Value/Nullable
            string? localPlayerWorldName = null;
            if (_playerState.HomeWorld.RowId > 0)
            {
               localPlayerWorldName = _playerState.HomeWorld.Value.Name.ToString();
            }

            // Separador (Arroba)
            const string worldIcon = "@";

            var nameParts = senderName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (nameParts.Length == 3)
            {
                // Formato Cross-World estándar: First Last World
                senderName = $"{nameParts[0]} {nameParts[1]}{worldIcon}{nameParts[2]}";
            }
            else if (nameParts.Length == 2)
            {
                 // Posible formato Cross-World pegado: First LastWorld
                 // O Jugador del mismo mundo: First Last
                 
                 string lastPart = nameParts[1];
                 int worldSplitIndex = -1;
                 
                 // Buscar una mayúscula que no sea la primera letra del apellido
                 // y comparar con el HomeWorld del jugador local si es posible
                 
                 // Si es el mismo mundo que nosotros, agregar nuestro mundo
                 if (!string.IsNullOrEmpty(localPlayerWorldName) && localPlayerName == senderName)
                 {
                     senderName = $"{senderName}{worldIcon}{localPlayerWorldName}";
                 }
                 else if (!string.IsNullOrEmpty(localPlayerWorldName))
                 {
                     // Lógica heurística simplificada
                     // Asumimos mismo mundo si no hay indicador de otro mundo.
                     senderName = $"{senderName}{worldIcon}{localPlayerWorldName}";
                 }

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
                     senderName = $"{nameParts[0]} {lastPart.Substring(0, worldSplitIndex)}{worldIcon}{lastPart.Substring(worldSplitIndex)}";
                 }
                 else if (!string.IsNullOrEmpty(localPlayerName) && senderName == localPlayerName && !string.IsNullOrEmpty(localPlayerWorldName))
                 {
                     // Jugador local: añadir mundo
                     senderName = $"{localPlayerName}{worldIcon}{localPlayerWorldName}";
                 }
                 else if (!string.IsNullOrEmpty(localPlayerName) && !string.IsNullOrEmpty(localPlayerWorldName))
                 {
                     // Otro jugador mismo mundo: añadir mundo local
                     senderName = $"{senderName}{worldIcon}{localPlayerWorldName}";
                 }
            }

            // Ignorar mensajes vacíos
            if (string.IsNullOrWhiteSpace(messageText))
                return;

            // Verificar si es del jugador local
            var isLocalPlayer = !string.IsNullOrEmpty(localPlayerName) && (senderName.StartsWith(localPlayerName));
            
            // Si es un mensaje del jugador local y no queremos mostrar nuestros propios mensajes,
            // LO DESCARTAMOS AQUÍ MISMO a menos que sea una traducción explícita
            if (isLocalPlayer && !_configuration.ShowOutgoingMessages && originalFromPending == null)
                return;

            // DEDUPLICACIÓN: Ya verificada al inicio del método
            // string? originalFromPending = null; (Ya declarado arriba)

            if (originalFromPending != null)
            {
                // Ya lo tenemos, no traducir otra vez y usar el original real
                // IMPORTANTE: Para mensajes salientes, invertimos: Texto principal = Traducción, Tooltip = Original
                var pendingMsg = new TranslatedChatMessage
                {
                    Timestamp = DateTime.Now,
                    ChatType = type,
                    Sender = senderName,
                    OriginalText = messageText, // La traducción que el juego captó
                    TranslatedText = originalFromPending, // El original que guardamos
                    IsTranslating = false
                };
                
                // Efectivamente, el UI debe saber que esto es saliente para mostrarlo al revés
                // o simplemente guardamos TranslatedText como el original para que el tooltip lo muestre.
                // En EchoXIV, TranslatedText se muestra en grande y OriginalText se muestra en el tooltip si no está vacío.
                // Así que para salientes:
                pendingMsg.TranslatedText = messageText; // La traducción (Texto grande)
                pendingMsg.OriginalText = originalFromPending; // El original (Tooltip)
                
                OnMessageTranslated?.Invoke(pendingMsg);
                return;
            }

            // FILTRO RMT/SPAM: Evitar gastar API en basura
            if (IsRmtSpam(messageText))
            {
                if (_configuration.VerboseLogging) _pluginLog.Info($"🚫 Spam RMT detectado y omitido: {messageText.Substring(0, Math.Min(20, messageText.Length))}...");
                return;
            }

            // Ignorar comandos
            if (messageText.StartsWith("/"))
                return;

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
            
            _pluginLog.Info($"[Retry] Iniciando reintento para: {message.OriginalText.Substring(0, Math.Min(20, message.OriginalText.Length))}...");
            message.IsTranslating = true;
            await TranslateAsync(message);
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
                var finalTranslation = SanitizeText(_glossaryService.Restore(translation));
                
                _translationCache.Add(message.OriginalText, "auto", targetLanguage, finalTranslation);

                message.TranslatedText = finalTranslation;
                message.IsTranslating = false;

                if (_configuration.VerboseLogging) _pluginLog.Info($"[{message.ChatType}] 📥 Traducido entrante: '{message.OriginalText}' → '{message.TranslatedText}'");

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
                        var finalTranslation = SanitizeText(_glossaryService.Restore(translation));
                        
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

        private string SanitizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            
            // Eliminar caracteres de control o nulos que puedan romper el chat o el overlay
            var sanitized = text.Replace("\0", "").Replace("\r", "").Replace("\n", " ");
            
            // Asegurar UTF-8 válido (aunque .NET strings son UTF-16, esto previene basura)
            // Dalamud maneja bien los strings, pero es mejor prevenir caracteres inválidos de las APIs
            return sanitized;
        }

        public void Dispose()
        {
            _chatGui.ChatMessage -= OnChatMessage;
            if (_configuration.VerboseLogging) _pluginLog.Info("🔌 IncomingMessageHandler desconectado");
        }
    }
}
