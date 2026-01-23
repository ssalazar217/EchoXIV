using System;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVChatTranslator.Services;
using FFXIVChatTranslator.UI;
using Newtonsoft.Json;

namespace FFXIVChatTranslator
{
    /// <summary>
    /// Plugin principal de traducción de chat
    /// </summary>
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "FFXIV Chat Translator";
        
        private const string CommandName = "/translate";
        private const string CommandNameShort = "/tl";
        
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IChatGui ChatGui { get;  private set; } = null!;
        [PluginService] internal static IFramework Framework { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
        [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
        
        private Configuration _configuration = null!;
        private GoogleTranslatorService _translatorService = null!;
        private WindowSystem _windowSystem = null!;
        private ConfigWindow? _configWindow = null;
        private TranslatedChatWindow? _translatedChatWindow = null;
        private WpfHost? _wpfHost = null; // Host para ventana WPF nativa
        private IncomingMessageHandler? _incomingMessageHandler = null;
        private Integrations.Chat2Integration? _chat2Integration = null;
        private GameFunctions.ChatBoxHook? _chatBoxHook = null;
        
        public Plugin()
        {
            try
            {
                // Cargar configuración
                _configuration = LoadConfiguration();
                
                // Inicializar localización basada en idioma del usuario
                Resources.Loc.SetCulture(_configuration.SourceLanguage);
                
                // Inicializar servicio de traducción
                _translatorService = new GoogleTranslatorService();
                
                // Inicializar sistema de ventanas
                _windowSystem = new WindowSystem("Chat2Translator");
                
                // Intentar integración con Chat2
                _chat2Integration = new Integrations.Chat2Integration(
                    _configuration, 
                    _translatorService, 
                    PluginLog,
                    PluginInterface,
                    ChatGui,
                    CommandManager,
                    Framework,
                    ClientState
                );
                _chat2Integration.Enable();
                
                if (_chat2Integration.IsChat2Installed && _configuration.PreferChat2Integration)
                {
                    // Modo: Integración con Chat2
                    PluginLog.Info("🔗 Modo: Integración con Chat2");
                }
                else
                {
                    // Modo fallback (sin Chat2) - para futuro
                    PluginLog.Warning("⚠️ Chat2 no detectado - Funcionalidad limitada pero operativa");
                }

                // Crear ventana de configuración (SIEMPRE)
                _configWindow = new ConfigWindow(_configuration, _chat2Integration);
                _windowSystem.AddWindow(_configWindow);
                
                // Suscribirse a cambios de opacidad
                _configWindow.OnOpacityChanged += OnOpacityChangedHandler;
                
                // Inicializar sistema de ventanas
                // Inicializar sistema de ventanas
                if (_configuration.UseNativeWindow)
                {
                    // Crear ventana nativa (WPF)
                    _wpfHost = new WpfHost(_configuration, PluginLog);
                    _wpfHost.Start();
                    
                    if (_wpfHost.IsInitialized)
                    {
                         PluginLog.Info("🖥️ Ventana WPF nativa iniciada correctamente");
                    }
                    else
                    {
                        PluginLog.Error("⚠️ Falló la inicialización de ventana nativa (posible conflicto de AppDomain o Zombie App). Usando fallback a ventana interna.");
                        ChatGui.PrintError("[Traductor] Error crítico en ventana nativa. Usando modo compatibilidad.");
                        _wpfHost.Dispose();
                        _wpfHost = null;
                        
                        // Fallback
                        _translatedChatWindow = new TranslatedChatWindow(_configuration);
                        _windowSystem.AddWindow(_translatedChatWindow);
                    }
                }
                else
                {
                    // Crear ventana interna (ImGui/Dalamud)
                    _translatedChatWindow = new TranslatedChatWindow(_configuration);
                    _windowSystem.AddWindow(_translatedChatWindow);
                    PluginLog.Info("🖥️ Ventana interna Dalamud iniciada");
                }
                
                // Crear manejador de mensajes entrantes
                _incomingMessageHandler = new IncomingMessageHandler(
                    _configuration,
                    _translatorService,
                    ChatGui,
                    ClientState,
                    PluginLog
                );
                
                // Conectar eventos
                _incomingMessageHandler.OnTranslationStarted += OnIncomingTranslationStarted;
                _incomingMessageHandler.OnMessageTranslated += OnIncomingMessageTranslated;
                
                // Registrar WindowSystem
                PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
                PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
                
                // Registrar comandos
                CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = "Traduce y envía mensajes al chat.\n" +
                                  "/translate Hola → Traduce al canal actual\n" +
                                  "/translate /p Hola → Traduce y envía a Party\n" +
                                  "/translate on → Activa traducción automática\n" +
                                  "/translate off → Desactiva traducción\n" +
                                  "/translate config → Abre configuración"
                });
                
                CommandManager.AddHandler(CommandNameShort, new CommandInfo(OnCommand)
                {
                    HelpMessage = "Atajo para /translate"
                });
                
                // Registrar UI callback principal (botón "Abrir" abre configuración)
                PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;
                
                PluginLog.Info($"{Name} cargado correctamente.");
                
                PluginLog.Info($"{Name} cargado correctamente.");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Error al inicializar el plugin");
                throw;
            }
        }
        
        public void Dispose()
        {
            // Detener manejador de mensajes entrantes
            if (_incomingMessageHandler != null)
            {
                _incomingMessageHandler.OnTranslationStarted -= OnIncomingTranslationStarted;
                _incomingMessageHandler.OnMessageTranslated -= OnIncomingMessageTranslated;
                _incomingMessageHandler.Dispose();
            }
            
            // Detener integración
            _chat2Integration?.Dispose();
            
            // Detener hook nativo
            _chatBoxHook?.Dispose();
            
            // Limpiar ventanas
            _windowSystem?.RemoveAllWindows();

            if (_configWindow != null)
            {
                _configWindow.OnOpacityChanged -= OnOpacityChangedHandler;
                _configWindow.Dispose();
            }
            // _translatedChatWindow?.Dispose();
            _wpfHost?.Dispose();
            
            // Limpiar comandos
            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler(CommandNameShort);
            
            // Limpiar eventos
            PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUI;
        }
        
        private void OnCommand(string command, string args)
        {
            var lowerArgs = args.ToLowerInvariant().Trim();
            
            if (string.IsNullOrWhiteSpace(args))
            {
                ToggleConfigUI();
                return;
            }

            switch (lowerArgs)
            {
                case "on":
                    _configuration.TranslationEnabled = true;
                    _configuration.Save();
                    ChatGui.Print($"[Chat2 Translator] Traducción activada. {_configuration.SourceLanguage.ToUpper()} → {_configuration.TargetLanguage.ToUpper()}");
                    break;
                
                case "off":
                    _configuration.TranslationEnabled = false;
                    _configuration.Save();
                    ChatGui.Print("[Chat2 Translator] Traducción desactivada");
                    break;
                
                case "config":
                case "settings":
                    ToggleConfigUI();
                    break;
                
                case "chat":
                case "c":
                    ToggleTranslatedChatWindow();
                    break;
                
                case "reset":
                    ResetTranslatedChatWindowPosition();
                    break;
                
                case "input":
                case "i":
                     // Legacy, no hace nada o abre config
                     ChatGui.Print("[Traductor] La ventana de input ha sido reemplazada. Usa /tl mensaje");
                     break;
                
                default:
                    // Si no es un comando reservado, intentar traducir
                    TranslateAndSend(args);
                    break;
            }
        }
        
        private void ToggleConfigUI()
        {
            if (_configWindow != null)
            {
                _configWindow.IsOpen = !_configWindow.IsOpen;
            }
        }
        
        /// <summary>
        /// Muestra/oculta la ventana de chat traducido
        /// </summary>
        private void ToggleTranslatedChatWindow()
        {
            if (_configuration.UseNativeWindow)
            {
                if (_wpfHost == null)
                {
                     ChatGui.PrintError("[Traductor] ⚠️ La ventana nativa está activada pero no iniciada. Por favor reinicia el plugin.");
                     return;
                }

                _wpfHost.ToggleWindow();
                ChatGui.Print("[Traductor] 👁️ Alternando visibilidad de ventana nativa.");
            }
            else if (_translatedChatWindow != null)
            {
                _translatedChatWindow.IsOpen = !_translatedChatWindow.IsOpen;
                 if (_translatedChatWindow.IsOpen)
                    ChatGui.Print("[Traductor] 💬 Ventana de traducciones abierta. Usa /tl reset si no la ves.");
                else
                    ChatGui.Print("[Traductor] 💬 Ventana de traducciones cerrada.");
            }
        }
        
        /// <summary>
        /// Resetea la posición de la ventana de chat traducido al centro
        /// </summary>
        private void ResetTranslatedChatWindowPosition()
        {
            if (_configuration.UseNativeWindow)
            {
                 _wpfHost?.ResetWindow();
                 ChatGui.Print("[Traductor] 📍 Posición de ventana nativa reseteada a (100, 100).");
            }
            else if (_translatedChatWindow != null)
            {
                _translatedChatWindow.Position = new System.Numerics.Vector2(100, 100);
                _translatedChatWindow.IsOpen = true;
                ChatGui.Print("[Traductor] 📍 Posición de ventana reseteada a (100, 100). Ya debería ser visible.");
            }
        }

        private void TranslateAndSend(string input)
        {
            // Parsear canal explícito o usar actual
            var (channel, message) = ParseChannelAndMessage(input);
            
            if (string.IsNullOrWhiteSpace(message))
            {
                ChatGui.Print("[Traductor] ⚠️ No hay mensaje para traducir");
                return;
            }
            
            // Mostrar indicador (SILENT: Eliminado por solicitud)
            // ChatGui.Print("[Traductor] 🔄 Traduciendo...");
            
            // Traducir async
            Task.Run(async () =>
            {
                try
                {
                    var translated = await _translatorService.TranslateAsync(
                        message,
                        _configuration.SourceLanguage,
                        _configuration.TargetLanguage
                    );
                    
                    // Enviar en main thread
                    Framework.RunOnFrameworkThread(() =>
                    {
                        SendToChannel(translated, channel);
                        PluginLog.Info($"✅ Traducido y enviado: '{message}' → '{translated}' al canal {channel}");
                    });
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Error al traducir mensaje");
                    Framework.RunOnFrameworkThread(() =>
                    {
                        ChatGui.PrintError("[Traductor] ❌ Error al traducir");
                    });
                }
            });
        }

        private (string channel, string message) ParseChannelAndMessage(string input)
        {
            input = input.Trim();
            
            // Detectar canal explícito al inicio
            if (input.StartsWith("/"))
            {
                var parts = input.Split(new[] { ' ' }, 2);
                if (parts.Length == 2)
                {
                    var explicitChannel = parts[0]; // ej: /p, /fc, /s
                    // Validar si parece un canal válido (empieza con /)
                    if (explicitChannel.Length > 1) 
                    {
                         var message = parts[1];
                         
                         // STICKY CHANNEL: Si el usuario especifica un canal, actualizar el default
                         // para que los siguientes mensajes vayan al mismo lugar automáticamente.
                         if (_configuration.DefaultChannel != explicitChannel)
                         {
                             _configuration.DefaultChannel = explicitChannel;
                             _configuration.Save();
                             // Opcional: Notificar visualmente o por log
                             PluginLog.Info($"[Sticky] Canal por defecto actualizado a: {explicitChannel}");
                         }
                         
                         return (explicitChannel, message);
                    }
                }
            }
            
            // Si no hay canal explícito, obtener el actual de Chat2
            var currentChannel = GetCurrentChannelFromChat2();
            return (currentChannel, input);
        }

        private string GetCurrentChannelFromChat2()
        {
            try
            {
                PluginLog.Info($"[DEBUG] Intentando obtener canal. Chat2 instalado: {_chat2Integration?.IsChat2Installed}");
                
                if (_chat2Integration?.IsChat2Installed == true)
                {
                    // Usar IPC para obtener canal actual
                    object stateObj = _chat2Integration.GetChatInputState();
                    dynamic state = stateObj;
                    int channelId = 0;
                    
                    // Solo usar el estado actual si la caja de chat está VISIBLE
                    // Si presionaste Enter, probablemente InputVisible ya es false, así que ignoramos
                    // el ChannelType actual (que podría haberse reseteado a Say) y vamos al else.
                    try 
                    {
                        if (state != null && (int)state.ChannelType != 0 && (bool)state.InputVisible)
                        {
                            channelId = (int)state.ChannelType;
                        }
                    }
                    catch { }

                    if (channelId == 0)
                    {
                        // Si el estado actual es nulo, vacío O CERRADO, usar el último conocido
                        channelId = _chat2Integration.GetLastActiveChannel();
                        PluginLog.Info($"[DEBUG] Input cerrado o inválido. Usando último canal conocido: {channelId}");
                    }

                    if (channelId != 0)
                    {
                        var command = ConvertChatTypeToCommand(channelId);
                        PluginLog.Info($"[DEBUG] Chat2 ChannelType ID: {channelId} -> Mapped to: {command}");
                        return command;
                    }
                    else
                    {
                         PluginLog.Warning("[DEBUG] Chat2 state is null or channel 0");
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "No se pudo obtener canal de Chat2");
            }
            
            // Fallback: Canal por defecto configurado por el usuario
            return _configuration.DefaultChannel;
        }

        private string ConvertChatTypeToCommand(int channelType)
        {
            // Mapear ChatType de Chat2 a comando de canal
            // Valores basados en XivChatType estándar
            return channelType switch
            {
                10 => "/s",    // Say (0xA)
                11 => "/sh",   // Shout (0xB)
                12 => "/y",    // Yell (0xC)
                13 => "/t",    // Tell (0xD) - Requiere manejo especial de target
                14 => "/p",    // Party (0xE)
                15 => "/a",    // Alliance (0xF)
                
                // Linkshells
                16 => "/l1",
                17 => "/l2",
                18 => "/l3",
                19 => "/l4",
                20 => "/l5",
                21 => "/l6",
                22 => "/l7",
                23 => "/l8",
                
                24 => "/fc",   // FreeCompany (0x18)
                
                27 => "/n",    // Novice Network (0x1B)
                
                // CrossWorld Linkshells
                37 => "/cwl1",
                38 => "/cwl2",
                39 => "/cwl3",
                40 => "/cwl4",
                41 => "/cwl5",
                42 => "/cwl6",
                43 => "/cwl7",
                44 => "/cwl8",
                
                _ => "/s"      // Default fallback
            };
        }

        private unsafe void SendToChannel(string message, string channel)
        {
            var fullMessage = $"{channel} {message}";
            
            try
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(fullMessage);
                
                if (bytes.Length > 500)
                {
                    ChatGui.PrintError("[Traductor] ⚠️ Mensaje muy largo");
                    return;
                }
                
                var mes = FFXIVClientStructs.FFXIV.Client.System.String.Utf8String.FromSequence(bytes);
                FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance()->ProcessChatBoxEntry(mes);
                mes->Dtor(true);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Error al enviar mensaje");
                ChatGui.PrintError("[Traductor] ❌ Error al enviar mensaje");
            }
        }
        
        private void OnConfigurationChanged()
        {
            _configuration.Save();
            PluginLog.Info($"Idioma destino cambiado a: {_configuration.TargetLanguage}");
        }
        
        private Configuration LoadConfiguration()
        {
            var config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            config.Initialize(PluginInterface);
            return config;
        }
        
        /// <summary>
        /// Callback cuando se inicia traducción de un mensaje entrante
        /// </summary>
        private void OnIncomingTranslationStarted(TranslatedChatMessage message)
        {
            _translatedChatWindow?.AddPendingMessage(message);
            _wpfHost?.AddMessage(message);
        }
        
        /// <summary>
        /// Callback cuando se completa la traducción de un mensaje entrante
        /// </summary>
        private void OnIncomingMessageTranslated(TranslatedChatMessage message)
        {
            _translatedChatWindow?.UpdateMessage(message);
            _wpfHost?.UpdateMessage(message);
        }


        private void OnOpacityChangedHandler(float opacity)
        {
            _wpfHost?.SetOpacity(opacity);
        }
    }
}
