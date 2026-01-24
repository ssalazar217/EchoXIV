using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVChatTranslator.Services;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Newtonsoft.Json;

namespace FFXIVChatTranslator.Integrations
{
    /// <summary>
    /// Modelo de datos del estado del input de Chat2
    /// </summary>
    public record ChatInputState(
        bool InputVisible,
        bool InputFocused,
        bool HasText,
        bool IsTyping,
        int TextLength,
        int ChannelType
    );
    
    /// <summary>
    /// Solicitud de traducción en cola
    /// </summary>
    public class TranslationRequest
    {
        public string OriginalText { get; set; } = string.Empty;
        public XivChatType ChatType { get; set; }
        public TaskCompletionSource<string> CompletionSource { get; set; } = new();
        public DateTime RequestedAt { get; set; }
    }
    
    /// <summary>
    /// Integración con Chat2 mediante IPC y hooks
    /// </summary>
    public class Chat2Integration : IDisposable
    {
        private readonly Configuration _configuration;
        private readonly GoogleTranslatorService _translatorService;
        private readonly IPluginLog _pluginLog;
        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly IChatGui _chatGui;
        private readonly ICommandManager _commandManager;
        private readonly IFramework _framework;
        private readonly IClientState _clientState;
        
        // IPC Subscribers para Chat2
        private ICallGateSubscriber<object?>? _chat2Available;
        private ICallGateSubscriber<object>? _getChatInputState;
        private ICallGateSubscriber<object, object?>? _chatInputStateChanged;
        
        // Caché de traducciones para respuestas instantáneas
        private readonly Dictionary<string, string> _translationCache = new();
        private const int MaxCacheSize = 1000;
        
        // Contador de mensajes siendo reenviados (prevención de bucle)
        // Cuando > 0, significa que hay mensajes traducidos siendo enviados
        // En OnCheckMessageHandled, si > 0, decrementamos y NO procesamos (es nuestro reenvío)
        private int _pendingResends = 0;
        private readonly object _resendLock = new();
        
        // Cola de traducción asíncrona
        private readonly ConcurrentQueue<TranslationRequest> _translationQueue = new();
        private readonly SemaphoreSlim _queueSemaphore = new(0);
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _workerTask;
        
        public bool IsChat2Installed { get; private set; }
        
        // Estado actual del input (opcional, para features futuras)
        private dynamic? _currentInputState;
        private int _lastActiveChannelType = 0; // Cache del último canal activo conocido
        
        public Chat2Integration(
            Configuration configuration,
            GoogleTranslatorService translatorService,
            IPluginLog pluginLog,
            IDalamudPluginInterface pluginInterface,
            IChatGui chatGui,
            ICommandManager commandManager,
            IFramework framework,
            IClientState clientState)
        {
            _configuration = configuration;
            _translatorService = translatorService;
            _pluginLog = pluginLog;
            _pluginInterface = pluginInterface;
            _chatGui = chatGui;
            _commandManager = commandManager;
            _framework = framework;
            _clientState = clientState;
        }
        
        public object? GetChatInputState()
        {
            if (!IsChat2Installed || _getChatInputState == null)
                return null;
            
            try
            {
                object state = _getChatInputState.InvokeFunc();
                // DEBUG: Serializar para ver estructura real
                if (state != null)
                {
                     try {
                        string json = JsonConvert.SerializeObject(state);
                        _pluginLog.Info($"[DEBUG] Chat2 State RAW: {json}");
                     } catch {}
                }
                return state;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Intenta habilitar la integración con Chat2
        /// </summary>
        public void Enable()
        {
            try
            {
                // Inicializar IPC subscribers
                _chat2Available = _pluginInterface.GetIpcSubscriber<object?>("ChatTwo.Available");
                _getChatInputState = _pluginInterface.GetIpcSubscriber<object>("ChatTwo.GetChatInputState");
                _chatInputStateChanged = _pluginInterface.GetIpcSubscriber<object, object?>("ChatTwo.ChatInputStateChanged");
                
                // Intentar obtener el estado actual para verificar que Chat2 está instalado
                _currentInputState = _getChatInputState.InvokeFunc();
                
                IsChat2Installed = true;
                _pluginLog.Info("✅ Chat2 detectado, habilitando integración");
                
                // Suscribirse a cambios de estado (opcional, para features futuras)
                _chatInputStateChanged?.Subscribe(OnChatInputStateChanged);
            }
            catch (Exception ex)
            {
                IsChat2Installed = false;
                _pluginLog.Info($"ℹ️ Chat2 no detectado: {ex.Message}");
            }
            
            // Iniciar worker thread para procesar traducciones en background
            _workerTask = Task.Run(TranslationWorker, _cancellationTokenSource.Token);
            _pluginLog.Info("✅ Worker thread de traducción iniciado");
            
            // ⚠️ DESHABILITADO: Usamos TranslatorInputWindow custom (sin mensaje rojo)
            // _chatGui.CheckMessageHandled += OnCheckMessageHandled;
            _pluginLog.Info("ℹ️ Chat2Integration inicializado (traducción via input window custom)");
        }
        
        /// <summary>
        /// Callback cuando el estado del input de Chat2 cambia
        /// </summary>
        private void OnChatInputStateChanged(object stateObj)
        {
            _currentInputState = null; 
            
            try {
                dynamic state = stateObj;
                int channelType = (int)state.ChannelType;
                bool inputVisible = (bool)state.InputVisible;
                
                if (channelType != 0 && inputVisible)
                {
                   _lastActiveChannelType = channelType;
                   _pluginLog.Info($"[DEBUG] Cache updated via Event: {channelType}");
                }
            } catch {}
        }

        public int GetLastActiveChannel()
        {
            // Retorna el último canal conocido, útil si el input ya se cerró
            return _lastActiveChannelType;
        }
        
        /// <summary>
        /// Worker thread que procesa traducciones en background
        /// </summary>
        private async Task TranslationWorker()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    // Esperar a que haya algo en la cola
                    await _queueSemaphore.WaitAsync(_cancellationTokenSource.Token);
                    
                    if (_translationQueue.TryDequeue(out var request))
                    {
                        try
                        {
                            // Traducir en background
                            var translation = await _translatorService.TranslateAsync(
                                request.OriginalText,
                                _configuration.SourceLanguage,
                                _configuration.TargetLanguage
                            );
                            
                            // Completar la tarea con el resultado
                            request.CompletionSource.SetResult(translation);
                        }
                        catch (Exception ex)
                        {
                            _pluginLog.Error(ex, $"Error traduciendo '{request.OriginalText}'");
                            request.CompletionSource.SetResult(request.OriginalText); // Fallback: original
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _pluginLog.Error(ex, "Error en worker thread de traducción");
                }
            }
        }
        
        /// <summary>
        /// Intercepta TODOS los mensajes salientes mediante CheckMessageHandled
        /// </summary>
        private void OnCheckMessageHandled(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            try
            {
                // Solo procesar si la traducción está habilitada
                if (!_configuration.TranslationEnabled)
                    return;
                
                // Filtrar solo mensajes SALIENTES del jugador (excluir sistema, ecos, etc.)
                if (!IsPlayerOutgoingMessage(type))
                    return;
                
                // Obtener el texto del mensaje
                var messageText = message.TextValue;
                
                // No traducir mensajes vacíos o comandos
                if (string.IsNullOrWhiteSpace(messageText) || messageText.StartsWith("/"))
                    return;
                
                // No procesar si ya fue manejado por otro plugin
                if (isHandled)
                    return;
                
                // *** CRÍTICO: Verificar que el mensaje es del JUGADOR LOCAL ***
                // CheckMessageHandled intercepta TODOS los mensajes (de cualquier jugador)
                // Debemos verificar que el sender es el jugador local
                var localPlayerName = _clientState.LocalPlayer?.Name.TextValue;
                var senderName = sender.TextValue;
                
                if (string.IsNullOrEmpty(localPlayerName))
                {
                    _pluginLog.Warning("⚠️ No se pudo obtener nombre del jugador local");
                    return;
                }
                
                // Si el sender NO es el jugador local, ignorar (es mensaje de otro jugador)
                if (senderName != localPlayerName && !string.IsNullOrEmpty(senderName))
                {
                    _pluginLog.Info($"⏭️ Mensaje de otro jugador ignorado: '{senderName}'");
                    return;
                }
                
                // *** CRÍTICO: Evitar bucle infinito con contador ***
                // Si hay mensajes siendo reenviados, este es uno de ellos
                lock (_resendLock)
                {
                    if (_pendingResends > 0)
                    {
                        _pendingResends--;
                        _pluginLog.Info($"⏭️ Mensaje reenviado detectado (pendientes: {_pendingResends})");
                        return;
                    }
                }
                
                // *** VERIFICAR LISTA DE EXCLUSIÓN ***
                // No traducir expresiones universales/emoticonos (lol, o/, uwu, etc.)
                if (_configuration.ExcludedMessages.Contains(messageText))
                {
                    _pluginLog.Info($"⏭️ Excluido: '{messageText}' (no se traduce)");
                    return; // Enviar sin traducir
                }
                
                string translatedText = string.Empty;
                
                // Caché SOLO para mensajes cortos (límite configurable)
                // Perfecto para: o/, gg, ty, lol, uwu, etc.
                if (_configuration.CacheEnabled && messageText.Length <= _configuration.CacheMaxMessageLength)
                {
                    var cacheKey = $"{messageText}|{_configuration.SourceLanguage}|{_configuration.TargetLanguage}";
                    
                    // Verificar caché primero (traducción instantánea, NO bloquea)
                    if (_translationCache.TryGetValue(cacheKey, out var cachedTranslation))
                    {
                        _pluginLog.Info($"⚡ (caché) '{messageText}' → '{cachedTranslation}'");
                        message = new SeString(new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(cachedTranslation));
                        return;
                    }
                }
                
                // CANCELAR el mensaje original
                isHandled = true;
                
                // Traducir en background y reenviar cuando esté listo (SIN BLOQUEAR)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        _pluginLog.Info($"🔄 Traduciendo '{messageText}'...");
                        
                        var translatedText = await _translatorService.TranslateAsync(
                            messageText,
                            _configuration.SourceLanguage,
                            _configuration.TargetLanguage
                        );
                        
                        if (!string.IsNullOrWhiteSpace(translatedText) && translatedText != messageText)
                        {
                            _pluginLog.Info($"✅ '{messageText}' → '{translatedText}'");
                            
                            // Cachear si cumple con límite
                            if (_configuration.CacheEnabled && messageText.Length <= _configuration.CacheMaxMessageLength)
                            {
                                var cacheKey = $"{messageText}|{_configuration.SourceLanguage}|{_configuration.TargetLanguage}";
                                AddToCache(cacheKey, translatedText);
                            }
                            
                            // Re-enviar el mensaje traducido
                            ReSendMessage(translatedText, type);
                        }
                        else
                        {
                            // Si falla traducción, reenviar original
                            ReSendMessage(messageText, type);
                        }
                    }
                    catch (Exception ex)
                    {
                        _pluginLog.Error(ex, "❌ Error al traducir en background");
                        // Reenviar original si hay error
                        ReSendMessage(messageText, type);
                    }
                });
                
                return; // Salir inmediatamente sin bloquear
            }
            catch (Exception ex)
            {
            _pluginLog.Error(ex, "❌ Error al procesar mensaje");
            }
        }
        
        /// <summary>
        /// Re-envía un mensaje traducido al chat usando UIModule directamente
        /// IMPORTANTE: Ejecuta en el main thread del juego para evitar crashes
        /// </summary>
        private unsafe void ReSendMessage(string text, XivChatType chatType)
        {
            try
            {
                // Obtener el comando del canal según el tipo
                var channelCommand = GetChannelCommand(chatType);
                var fullMessage = $"{channelCommand}{text}";
                
                _pluginLog.Info($"📤 Reenviando: {fullMessage}");
                
                // Convertir a bytes UTF-8
                var bytes = System.Text.Encoding.UTF8.GetBytes(fullMessage);
                
                if (bytes.Length == 0)
                {
                    _pluginLog.Warning("⚠️ Mensaje vacío, no se envía");
                    return;
                }
                
                if (bytes.Length > 500)
                {
                    _pluginLog.Warning("⚠️ Mensaje muy largo (>500 bytes), truncando");
                    Array.Resize(ref bytes, 500);
                }
                
                // CRÍTICO: Incrementar contador ANTES de reenviar
                // Esto previene que el callback intercepte el reenvío como nuevo mensaje
                lock (_resendLock)
                {
                    _pendingResends++;
                    _pluginLog.Info($"🔖 Incrementando contador de reenvíos: {_pendingResends}");
                }
                
                // CRÍTICO: Ejecutar en el MAIN THREAD del juego
                // UIModule NO es thread-safe y crashea si se llama desde background
                _framework.RunOnFrameworkThread(() =>
                {
                    try
                    {
                        // Usar el método directo de FFXIV para enviar mensaje
                        // Exactamente como ChatTwo lo hace
                        var mes = FFXIVClientStructs.FFXIV.Client.System.String.Utf8String.FromSequence(bytes);
                        FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance()->ProcessChatBoxEntry(mes);
                        mes->Dtor(true);
                        
                        _pluginLog.Info("✅ Mensaje enviado correctamente");
                    }
                    catch (Exception ex)
                    {
                        _pluginLog.Error(ex, "❌ Error al enviar mensaje en main thread");
                    }
                });
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, $"❌ Error al reenviar mensaje: {text}");
            }
        }
        
        /// <summary>
        /// Obtiene el comando del canal según el tipo de chat
        /// </summary>
        private string GetChannelCommand(XivChatType chatType)
        {
            return chatType switch
            {
                XivChatType.Say => "/s ",
                XivChatType.Yell => "/y ",
                XivChatType.Shout => "/sh ",
                XivChatType.Party => "/p ",
                XivChatType.Alliance => "/a ",
                XivChatType.FreeCompany => "/fc ",
                XivChatType.TellOutgoing => "", // Los tells requieren manejo especial  
                XivChatType.Ls1 => "/l1 ",
                XivChatType.Ls2 => "/l2 ",
                XivChatType.Ls3 => "/l3 ",
                XivChatType.Ls4 => "/l4 ",
                XivChatType.Ls5 => "/l5 ",
                XivChatType.Ls6 => "/l6 ",
                XivChatType.Ls7 => "/l7 ",
                XivChatType.Ls8 => "/l8 ",
                XivChatType.CrossLinkShell1 => "/cwl1 ",
                XivChatType.CrossLinkShell2 => "/cwl2 ",
                XivChatType.CrossLinkShell3 => "/cwl3 ",
                XivChatType.CrossLinkShell4 => "/cwl4 ",
                XivChatType.CrossLinkShell5 => "/cwl5 ",
                XivChatType.CrossLinkShell6 => "/cwl6 ",
                XivChatType.CrossLinkShell7 => "/cwl7 ",
                XivChatType.CrossLinkShell8 => "/cwl8 ",
                XivChatType.NoviceNetwork => "/n ",
                _ => "/s "
            };
        }
        
        /// <summary>
        /// Agrega traducción al caché con límite de tamaño
        /// </summary>
        private void AddToCache(string key, string value)
        {
            if (_translationCache.Count >= MaxCacheSize)
            {
                // Limpiar la mitad más antigua del caché
                var toRemove = _translationCache.Take(MaxCacheSize / 2).Select(x => x.Key).ToList();
                foreach (var k in toRemove)
                {
                    _translationCache.Remove(k);
                }
            }
            
            _translationCache[key] = value;
        }
        
        /// <summary>
        /// Limpia completamente el caché de traducciones
        /// </summary>
        public void ClearCache()
        {
            _translationCache.Clear();
            _pluginLog.Info("🗑️ Caché de traducciones limpiado");
        }
        
        /// <summary>
        /// Obtiene el tamaño actual del caché
        /// </summary>
        public int GetCacheSize() => _translationCache.Count;
        
        /// <summary>
        /// Determina si un tipo de mensaje es saliente del jugador
        /// </summary>
        private bool IsPlayerOutgoingMessage(XivChatType type)
        {
            // Solo traducir mensajes que el jugador envía
            return type switch
            {
                XivChatType.Say => true,
                XivChatType.Shout => true,
                XivChatType.Yell => true,
                XivChatType.Party => true,
                XivChatType.Alliance => true,
                XivChatType.FreeCompany => true,
                XivChatType.Ls1 => true,
                XivChatType.Ls2 => true,
                XivChatType.Ls3 => true,
                XivChatType.Ls4 => true,
                XivChatType.Ls5 => true,
                XivChatType.Ls6 => true,
                XivChatType.Ls7 => true,
                XivChatType.Ls8 => true,
                XivChatType.CrossLinkShell1 => true,
                XivChatType.CrossLinkShell2 => true,
                XivChatType.CrossLinkShell3 => true,
                XivChatType.CrossLinkShell4 => true,
                XivChatType.CrossLinkShell5 => true,
                XivChatType.CrossLinkShell6 => true,
                XivChatType.CrossLinkShell7 => true,
                XivChatType.CrossLinkShell8 => true,
                XivChatType.NoviceNetwork => true,
                XivChatType.CustomEmote => true,
                XivChatType.StandardEmote => true,
                XivChatType.TellOutgoing => true, // Tells enviados por ti
                _ => false // Todos los demás (System, Echo, Debug, etc.) NO se traducen
            };
        }
        
        public void Dispose()
        {
            // Detener worker thread
            _cancellationTokenSource.Cancel();
            _queueSemaphore.Release(); // Liberar si está esperando
            
            try
            {
                _workerTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
            
            _chatInputStateChanged?.Unsubscribe(OnChatInputStateChanged);
            _chatGui.CheckMessageHandled -= OnCheckMessageHandled;
            
            _cancellationTokenSource.Dispose();
            _queueSemaphore.Dispose();
        }
    }
}
