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
        
        // IPC Subscribers para Chat2
        private ICallGateSubscriber<object?>? _chat2Available;
        private ICallGateSubscriber<ChatInputState>? _getChatInputState;
        private ICallGateSubscriber<ChatInputState, object?>? _chatInputStateChanged;
        
        // Caché de traducciones para respuestas instantáneas
        private readonly Dictionary<string, string> _translationCache = new();
        private const int MaxCacheSize = 1000;
        
        // Cola de traducción asíncrona
        private readonly ConcurrentQueue<TranslationRequest> _translationQueue = new();
        private readonly SemaphoreSlim _queueSemaphore = new(0);
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task? _workerTask;
        
        public bool IsChat2Installed { get; private set; }
        
        // Estado actual del input (opcional, para features futuras)
        private ChatInputState? _currentInputState;
        
        public Chat2Integration(
            Configuration configuration,
            GoogleTranslatorService translatorService,
            IPluginLog pluginLog,
            IDalamudPluginInterface pluginInterface,
            IChatGui chatGui)
        {
            _configuration = configuration;
            _translatorService = translatorService;
            _pluginLog = pluginLog;
            _pluginInterface = pluginInterface;
            _chatGui = chatGui;
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
                _getChatInputState = _pluginInterface.GetIpcSubscriber<ChatInputState>("ChatTwo.GetChatInputState");
                _chatInputStateChanged = _pluginInterface.GetIpcSubscriber<ChatInputState, object?>("ChatTwo.ChatInputStateChanged");
                
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
            
            // Interceptar mensajes salientes usando ChatGui (funciona con o sin Chat2)
            _chatGui.CheckMessageHandled += OnCheckMessageHandled;
            _pluginLog.Info("✅ Interceptor de mensajes habilitado mediante ChatGui");
        }
        
        /// <summary>
        /// Callback cuando el estado del input de Chat2 cambia
        /// </summary>
        private void OnChatInputStateChanged(ChatInputState state)
        {
            _currentInputState = state;
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
                
                // Crear solicitud de traducción asíncrona
                var request = new TranslationRequest
                {
                    OriginalText = messageText,
                    ChatType = type,
                    CompletionSource = new TaskCompletionSource<string>(),
                    RequestedAt = DateTime.Now
                };
                
                // Encolar para procesamiento en background
                _translationQueue.Enqueue(request);
                _queueSemaphore.Release();
                
                // ESPERAR resultado con timeout de 3 SEGUNDOS
                // El mensaje NO se envía hasta que la traducción esté completa
                if (request.CompletionSource.Task.Wait(3000))
                {
                    translatedText = request.CompletionSource.Task.Result;
                }
                else
                {
                    // Si tarda más de 3 segundos, enviar mensaje original
                    // (la traducción seguirá procesándose en background para caché)
                    _pluginLog.Warning($"⚠️ Traducción de '{messageText}' tomó >3s, enviando original");
                    
                    // Continuar procesando en background para cachear
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var result = await request.CompletionSource.Task;
                            if (_configuration.CacheEnabled && messageText.Length <= _configuration.CacheMaxMessageLength)
                            {
                                var cacheKey = $"{messageText}|{_configuration.SourceLanguage}|{_configuration.TargetLanguage}";
                                AddToCache(cacheKey, result);
                                _pluginLog.Info($"📦 Cacheado '{messageText}' → '{result}' para próxima vez");
                            }
                        }
                        catch { }
                    });
                    
                    return; // Enviar original sin bloquear
                }
                
                if (!string.IsNullOrWhiteSpace(translatedText) && translatedText != messageText)
                {
                    _pluginLog.Info($"📝 '{messageText}' → '{translatedText}'");
                    
                    // Guardar en caché SOLO si está habilitado y el mensaje es corto
                    if (_configuration.CacheEnabled && messageText.Length <= _configuration.CacheMaxMessageLength)
                    {
                        var cacheKey = $"{messageText}|{_configuration.SourceLanguage}|{_configuration.TargetLanguage}";
                        AddToCache(cacheKey, translatedText);
                    }
                    
                    // Reemplazar el mensaje con la traducción
                    message = new SeString(new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(translatedText));
                }
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "❌ Error al traducir mensaje");
            }
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
