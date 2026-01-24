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
                
                // NOTA: Integración con Chat2 eliminada por redundancia.
                // Usamos ChatBoxHook para traducciones y DefaultChannel para comandos.

                // Crear ventana de configuración (SIEMPRE)
                _configWindow = new ConfigWindow(_configuration);
                _windowSystem.AddWindow(_configWindow);
                
                // Suscribirse a cambios de opacidad
                _configWindow.OnOpacityChanged += OnOpacityChangedHandler;
                _configWindow.OnSmartVisibilityChanged += (enabled) => _wpfHost?.SetSmartVisibility(enabled);
                _configWindow.OnVisualsChanged += () => _wpfHost?.UpdateVisuals();
                _configWindow.OnUnlockNativeRequested += () => _wpfHost?.SetLock(false);
                
                // Inicializar sistema de ventanas
                
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
            

            
            // Detener hook nativo
            _chatBoxHook?.Dispose();
            
            // Limpiar ventanas
            _windowSystem?.RemoveAllWindows();

            if (_configWindow != null)
            {
                _configWindow.OnOpacityChanged -= OnOpacityChangedHandler;
                // lambda: _configWindow.OnUnlockNativeRequested -= ... (Not possible for anon lambda)
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
                
                case "lock":
                    if (_configuration.UseNativeWindow && _wpfHost != null)
                    {
                        _wpfHost.SetLock(true);
                        ChatGui.Print("[Chat2 Translator] Ventana nativa BLOQUEADA (Click-Through activado). Usa '/tl unlock' para desbloquear.");
                    }
                    else
                    {
                        ChatGui.Print("[Chat2 Translator] La ventana nativa no está activa.");
                    }
                    break;

                case "unlock":
                    if (_configuration.UseNativeWindow && _wpfHost != null)
                    {
                         _wpfHost.SetLock(false);
                         ChatGui.Print("[Chat2 Translator] Ventana nativa DESBLOQUEADA.");
                    }
                    else
                    {
                         ChatGui.Print("[Chat2 Translator] La ventana nativa no está activa.");
                    }
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
            
            // Si no hay canal explícito, usar el Default Channel
            return (_configuration.DefaultChannel, input);
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
