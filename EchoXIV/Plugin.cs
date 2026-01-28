using System;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using EchoXIV.Services;
using EchoXIV.UI;
using EchoXIV.Properties;
using Newtonsoft.Json;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Client.System.String;
using System.Text.RegularExpressions;
using System.Linq;

namespace EchoXIV
{
    /// <summary>
    /// Plugin principal de traducción de chat
    /// </summary>
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "EchoXIV";
        
        private const string CommandName = "/translate";
        private const string CommandNameShort = "/tl";
        private const string ConfigCommandName = "/echoxiv";
        
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IChatGui ChatGui { get;  private set; } = null!;
        [PluginService] internal static IFramework Framework { get; private set; } = null!;
        [PluginService] internal static IClientState ClientState { get; private set; } = null!;
        [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
        [PluginService] internal static ICondition Condition { get; private set; } = null!;
        [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
        [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
        [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
        [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
        
        private Configuration _configuration = null!;
        private ITranslationService _translatorService = null!;
        private WindowSystem _windowSystem = null!;
        private ConfigWindow? _configWindow = null;
        private TranslatedChatWindow? _translatedChatWindow = null;
        private WelcomeWindow? _welcomeWindow = null;
        private WpfHost? _wpfHost = null; // Host para ventana WPF nativa
        private IncomingMessageHandler? _incomingMessageHandler = null;
        private readonly MessageHistoryManager _historyManager;
        private GameFunctions.ChatBoxHook? _chatBoxHook = null!;
        private static bool _chatVisible = false;
        
        public Plugin()
        {
            try
            {
                // Cargar configuración
                _configuration = LoadConfiguration();
                _historyManager = new MessageHistoryManager(_configuration);

                // Borrar si existiera para evitar configs corruptas si el usuario borró el JSON
                if (_configuration.FirstRun)
                {
                    var uiLang = PluginInterface.UiLanguage;
                    if (uiLang != "en")
                    {
                        PluginLog.Info($"[EchoXIV] Primera ejecución detectada. Auto-configurando idioma base desde Dalamud: {uiLang}");
                        _configuration.SourceLanguage = uiLang;
                        _configuration.TargetLanguage = "en";
                        _configuration.IncomingTargetLanguage = uiLang;
                        _configuration.FirstRun = false;
                        _configuration.Save();
                    }
                }

                // Inicializar localización basada en idioma del usuario
                try 
                {
                    Resources.Culture = new System.Globalization.CultureInfo(_configuration.SourceLanguage ?? "en");
                }
                catch { }
                
                // Inicializar servicio de traducción
                UpdateTranslationService();
                
                // Inicializar sistema de ventanas
                _windowSystem = new WindowSystem("EchoXIV");
                

                // Crear ventana de configuración (SIEMPRE)
                _configWindow = new ConfigWindow(_configuration);
                _windowSystem.AddWindow(_configWindow);

                // Manejar pantalla de bienvenida si sigue siendo FirstRun (ej: Dalamud está en Inglés)
                if (_configuration.FirstRun)
                {
                    _welcomeWindow = new WelcomeWindow(_configuration);
                    _welcomeWindow.OnConfigurationComplete += () => 
                    {
                        try 
                        {
                            Resources.Culture = new System.Globalization.CultureInfo(_configuration.SourceLanguage ?? "en");
                        }
                        catch { }
                        UpdateTranslationService();
                    };
                    _windowSystem.AddWindow(_welcomeWindow);
                    _welcomeWindow.IsOpen = true;
                }
                
                // Suscribirse a cambios de opacidad
                _configWindow.OnOpacityChanged += OnOpacityChangedHandler;
                _configWindow.OnSmartVisibilityChanged += (enabled) => _wpfHost?.SetSmartVisibility(enabled);
                _configWindow.OnVisualsChanged += () => _wpfHost?.UpdateVisuals();
                _configWindow.OnUnlockNativeRequested += () => _wpfHost?.SetLock(false);
                _configWindow.OnTranslationEngineChanged += (engine) => UpdateTranslationService();
                _configWindow.OnWindowModeChanged += OnWindowModeChangedHandler;
                
                // Inicializar sistema de ventanas
                // NOTA: No iniciamos WpfHost aquí para evitar que aparezca en la pantalla de título.
                // Se iniciará en OnLogin solo si estamos realmente en el juego.
                /*
                if (_configuration.UseNativeWindow)
                {
                    // Crear ventana nativa (WPF) - ELIMINADO DE AQUÍ
                }
                */
                
                if (!_configuration.UseNativeWindow)
                {
                    // Crear ventana interna (ImGui/Dalamud)
                    _translatedChatWindow = new TranslatedChatWindow(_configuration, _historyManager);
                    _windowSystem.AddWindow(_translatedChatWindow);
                    if (_configuration.VerboseLogging) PluginLog.Info("🖥️ Ventana interna Dalamud iniciada");
                }
                
                // Crear manejador de mensajes entrantes
                _incomingMessageHandler = new IncomingMessageHandler(
                    _configuration,
                    _translatorService,
                    ChatGui,
                    ClientState,
                    ObjectTable,
                    PluginLog
                );
                // Conectar eventos
                _incomingMessageHandler.OnTranslationStarted += m => _historyManager.AddMessage(m);
                _incomingMessageHandler.OnMessageTranslated += m => _historyManager.UpdateMessage(m);
                _incomingMessageHandler.OnRequestEngineFailover += SwitchToGoogleFailover;
                
                // Inicializar Hook Nativo (para traducción saliente segura)
                try
                {
                    _chatBoxHook = new GameFunctions.ChatBoxHook(
                         _configuration,
                         _translatorService,
                         PluginLog,
                         ClientState,
                         GameInteropProvider
                    );
                    _chatBoxHook.OnMessageTranslated += (orig, trans) => _incomingMessageHandler?.RegisterPendingOutgoing(trans, orig);
                    _chatBoxHook.OnRequestEngineFailover += SwitchToGoogleFailover;
                    _chatBoxHook.Enable();
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "❌ No se pudo habilitar el Hook de Chat (Traducción saliente deshabilitada).");
                    // No relanzamos para que el resto del plugin funcione
                }
                
                // Registrar WindowSystem
                PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
                PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
                
                // Registrar comandos
                CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = "Traduce y envía: /translate [mensaje/on/off/config/help]"
                });

                CommandManager.AddHandler(CommandNameShort, new CommandInfo(OnCommand)
                {
                    HelpMessage = "Atajo corto de /translate"
                });

                CommandManager.AddHandler(ConfigCommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = "Configuración de EchoXIV"
                });
                
                // Registrar UI callback principal (botón "Abrir" abre configuración)
                PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;

                // Suscribirse al Update del Framework para caché de visibilidad seguro entre hilos
                Framework.Update += OnFrameworkUpdate;

                // Suscribirse a eventos de login/logout para manejar la ventana nativa
                ClientState.Login += delegate { OnLogin(); };
                ClientState.Logout += delegate { OnLogout(); };

                // Si ya estamos logueados (ej: recarga de plugin), iniciar ahora
                if (ClientState.IsLoggedIn)
                {
                    OnLogin();
                }

                PluginLog.Info($"{Name} cargado correctamente.");
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Error fatal al inicializar el plugin. Limpiando para evitar zombies...");
                Dispose(); 
                throw;
            }
        }

        private void OnLogin()
        {
            // Ya no es necesaria la validación aquí, el timer de WPF y DrawConditions de ImGui 
            // se encargarán de ocultar la ventana dinámicamente según el estado del juego.
            if (_configuration.UseNativeWindow && _wpfHost == null)
            {
                if (_configuration.VerboseLogging) PluginLog.Info("Jugador logueado. Iniciando host de ventana nativa...");
                _wpfHost = new WpfHost(_configuration, PluginLog, _historyManager);
                _wpfHost.Start();
            }
        }

        private void OnLogout()
        {
            if (_wpfHost != null)
            {
                if (_configuration.VerboseLogging) PluginLog.Info("Jugador deslogueado. Cerrando ventana nativa...");
                _wpfHost.Dispose();
                _wpfHost = null;
            }
        }
    
        
        public void Dispose()
        {
            // Detener manejador de mensajes entrantes
            if (_incomingMessageHandler != null)
            {
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
            
            // Limpiar eventos de login (desuscripción manual no es posible con delegados anónimos, 
            // pero Dalamud maneja la limpieza al descargar el plugin en la mayoría de casos)

            _wpfHost?.Dispose();
            _wpfHost = null;
            
            // Limpiar comandos
            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler(CommandNameShort);
            CommandManager.RemoveHandler(ConfigCommandName);
            
            // Limpiar eventos
            Framework.Update -= OnFrameworkUpdate;
            if (_windowSystem != null)
            {
                PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
            }
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
                    ChatGui.Print($"[EchoXIV] Traducción activada. {_configuration.SourceLanguage.ToUpper()} → {_configuration.TargetLanguage.ToUpper()}");
                    break;
                
                case "off":
                    _configuration.TranslationEnabled = false;
                    _configuration.Save();
                    ChatGui.Print("[EchoXIV] Traducción desactivada");
                    break;
                
                case "lock":
                    if (_configuration.UseNativeWindow && _wpfHost != null)
                    {
                        _wpfHost.SetLock(true);
                        ChatGui.Print("[EchoXIV] Ventana nativa BLOQUEADA (Click-Through activado). Usa '/tl unlock' para desbloquear.");
                    }
                    else
                    {
                        ChatGui.Print("[EchoXIV] La ventana nativa no está activa.");
                    }
                    break;

                case "unlock":
                    if (_configuration.UseNativeWindow && _wpfHost != null)
                    {
                         _wpfHost.SetLock(false);
                         ChatGui.Print("[EchoXIV] Ventana nativa DESBLOQUEADA.");
                    }
                    else
                    {
                         ChatGui.Print("[EchoXIV] La ventana nativa no está activa.");
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
                
                case "help":
                case "?":
                    ChatGui.Print("[EchoXIV] Comandos disponibles:");
                    ChatGui.Print("/translate <mensaje> - Traduce al canal activo.");
                    ChatGui.Print("/translate on/off - Activa/desactiva traducción auto.");
                    ChatGui.Print("/translate config - Abre los ajustes.");
                    ChatGui.Print("/translate chat - Muestra/oculta ventana de chat.");
                    ChatGui.Print("/translate reset - Restaura posición de ventana.");
                    break;
                
                case "input":
                case "i":
                     // Legacy
                     ChatGui.Print("[EchoXIV] La ventana de input ha sido reemplazada. Usa /tl mensaje");
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
                     ChatGui.PrintError("[EchoXIV] ⚠️ La ventana nativa está activada pero no iniciada. Por favor reinicia el plugin.");
                     return;
                }

                _wpfHost.ToggleWindow();
                ChatGui.Print("[EchoXIV] 👁️ Alternando visibilidad de ventana nativa.");
            }
            else if (_translatedChatWindow != null)
            {
                _translatedChatWindow.IsOpen = !_translatedChatWindow.IsOpen;
                 if (_translatedChatWindow.IsOpen)
                    ChatGui.Print("[EchoXIV] 💬 Ventana de traducciones abierta. Usa /tl reset si no la ves.");
                else
                    ChatGui.Print("[EchoXIV] 💬 Ventana de traducciones cerrada.");
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
                 ChatGui.Print("[EchoXIV] 📍 Posición de ventana nativa reseteada a (100, 100).");
            }
            else if (_translatedChatWindow != null)
            {
                _translatedChatWindow.ResetPosition();
                ChatGui.Print("[EchoXIV] 📍 Posición de ventana reseteada a (100, 100). Ya debería ser visible.");
            }
        }

        private void TranslateAndSend(string input)
        {
            // Traducir async
            _ = Task.Run(async () =>
            {
                try
                {
                    var (prefix, message, type, recipient) = ParseChannelAndMessage(input);

                    if (string.IsNullOrWhiteSpace(message))
                    {
                         _ = Framework.RunOnFrameworkThread(() => ChatGui.Print("[EchoXIV] ⚠️ No hay mensaje para traducir"));
                         return;
                    }

                    // Verificar lista de exclusión antes de traducir
                    if (_configuration.ExcludedMessages.Contains(message))
                    {
                        _ = Framework.RunOnFrameworkThread(() =>
                        {
                            SendToChannel(message, prefix);
                            _historyManager.AddMessage(new TranslatedChatMessage
                            {
                                Timestamp = DateTime.Now,
                                ChatType = type == XivChatType.Debug ? XivChatType.Debug : type,
                                Recipient = recipient,
                                Sender = ObjectTable.LocalPlayer?.Name.TextValue ?? "Yo",
                                OriginalText = message,
                                TranslatedText = message,
                                IsTranslating = false
                            });
                        });
                        return;
                    }

                    var translated = await _translatorService.TranslateAsync(
                        message,
                        _configuration.SourceLanguage,
                        _configuration.TargetLanguage
                    );
                    
                    // Enviar en main thread
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        // Registrar la traducción para que IncomingMessageHandler la pesque y la registre con el canal correcto
                        _incomingMessageHandler?.RegisterPendingOutgoing(translated, message);
                        
                        SendToChannel(translated, prefix);
                        
                        if (_configuration.VerboseLogging) PluginLog.Info($"✅ Traducido y enviado: '{message}' → '{translated}'");
                    });
                }
                catch (TranslationRateLimitException ex)
                {
                    PluginLog.Warning($"⚠️ {ex.Message}. Activando conmutación automática a Google...");
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        SwitchToGoogleFailover();
                        // Re-intentar la traducción una vez con el nuevo motor para el comando actual
                        TranslateAndSend(input);
                    });
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Error al traducir mensaje");
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        ChatGui.PrintError("[EchoXIV] ❌ Error al traducir");
                    });
                }
            });
        }

        private unsafe (string? prefix, string message, XivChatType type, string recipient) ParseChannelAndMessage(string input)
        {
            var trimmed = input.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return (null, string.Empty, XivChatType.Debug, string.Empty);

            // Intentar detectar prefijo de comando (ej: /p, /t, /r)
            var match = Regex.Match(trimmed, @"^(/[a-z]+)\s*(.*)$", RegexOptions.IgnoreCase);
            
            if (match.Success)
            {
                var command = match.Groups[1].Value.ToLower();
                var remaining = match.Groups[2].Value.Trim();
                
                switch (command)
                {
                    case "/p": case "/party": 
                        return (command, remaining, XivChatType.Party, string.Empty);
                    case "/fc": case "/freecompany": 
                        return (command, remaining, XivChatType.FreeCompany, string.Empty);
                    case "/sh": case "/shout": 
                        return (command, remaining, XivChatType.Shout, string.Empty);
                    case "/y": case "/yell": 
                        return (command, remaining, XivChatType.Yell, string.Empty);
                    case "/s": case "/say": 
                        return (command, remaining, XivChatType.Say, string.Empty);
                    case "/a": case "/alliance": 
                        return (command, remaining, XivChatType.Alliance, string.Empty);
                    
                    case "/r": case "/reply":
                    {
                        var agent = AgentChatLog.Instance();
                        if (agent != null)
                        {
                            var recipient = agent->TellPlayerName.ToString();
                            if (!string.IsNullOrEmpty(recipient))
                            {
                                return (command, remaining, XivChatType.TellOutgoing, recipient);
                            }
                        }
                        return (command, remaining, XivChatType.TellOutgoing, "Destinatario");
                    }

                    case "/t": case "/tell":
                    {
                        // Manejar /t "Nombre Apellido@Mundo" Mensaje
                        if (remaining.StartsWith("\""))
                        {
                            var endQuoteIndex = remaining.IndexOf("\"", 1);
                            if (endQuoteIndex != -1)
                            {
                                var recipient = remaining.Substring(1, endQuoteIndex - 1);
                                var message = remaining.Substring(endQuoteIndex + 1).Trim();
                                return ($"{command} \"{recipient}\"", message, XivChatType.TellOutgoing, recipient);
                            }
                        }
                        
                        // Manejar /t Nombre@Mundo Mensaje o /t Nombre Apellido Mensaje
                        var parts = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            // Si la primera palabra contiene @, es un nombre simple Nombre@Mundo
                            if (parts[0].Contains("@"))
                            {
                                var recipient = parts[0];
                                var message = string.Join(" ", parts.Skip(1));
                                return ($"{command} {recipient}", message, XivChatType.TellOutgoing, recipient);
                            }
                            
                            // Si no, asumimos que puede ser Nombre Apellido (formato FFXIV)
                            // En FFXIV, los nombres siempre tienen dos partes.
                            if (parts.Length >= 3)
                            {
                                var recipient = $"{parts[0]} {parts[1]}";
                                var message = string.Join(" ", parts.Skip(2));
                                return ($"{command} {recipient}", message, XivChatType.TellOutgoing, recipient);
                            }
                            
                            // Fallback para nombres de una sola palabra
                            var fallbackRecipient = parts[0];
                            var fallbackMessage = string.Join(" ", parts.Skip(1));
                            return ($"{command} {fallbackRecipient}", fallbackMessage, XivChatType.TellOutgoing, fallbackRecipient);
                        }
                        break;
                    }
                }
                
                // Si es un comando no reconocido por nosotros, pero es un comando (/...),
                // dejamos que el juego lo maneje, pero lo marcamos como Debug para el historial.
                return (null, trimmed, XivChatType.Debug, string.Empty);
            }

            // --- Canal Implícito (Sin prefijo /) ---
            var activeType = XivChatType.Debug;
            var activeRecipient = string.Empty;
            
            var shell = RaptureShellModule.Instance();
            var agentChat = AgentChatLog.Instance();
            
            if (shell != null)
            {
                // Mapear el tipo de chat actual del shell del juego
                var gameChatType = (uint)shell->ChatType;
                
                // 17 y 18 suelen ser Tell (Incoming/Outgoing)
                if (gameChatType == 17 || gameChatType == 18)
                {
                    activeType = XivChatType.TellOutgoing;
                    if (agentChat != null)
                    {
                        activeRecipient = agentChat->TellPlayerName.ToString();
                    }
                }
                else
                {
                    activeType = (XivChatType)gameChatType;
                }
            }

            return (null, trimmed, activeType, activeRecipient);
        }

        private unsafe void SendToChannel(string message, string? channel)
        {
            try
            {
               // SANEAMIENTO
               var sanitized = message.Replace("\0", "").Replace("\r", "").Replace("\n", " ");
               var fullMessage = string.IsNullOrEmpty(channel) ? sanitized : $"{channel} {sanitized}";
               
               var bytes = System.Text.Encoding.UTF8.GetBytes(fullMessage);
                
                if (bytes.Length > 500)
                {
                    // Truncar si es necesario
                    while (System.Text.Encoding.UTF8.GetByteCount(fullMessage) > 497)
                    {
                        fullMessage = fullMessage.Substring(0, fullMessage.Length - 1);
                    }
                    fullMessage += "...";
                    bytes = System.Text.Encoding.UTF8.GetBytes(fullMessage);
                }
                
                var mes = FFXIVClientStructs.FFXIV.Client.System.String.Utf8String.FromSequence(bytes);
                FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance()->ProcessChatBoxEntry(mes);
                mes->Dtor(true);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Error al enviar mensaje");
                ChatGui.PrintError("[EchoXIV] ❌ Error al enviar mensaje");
            }
        }
        
        private void OnConfigurationChanged()
        {
            _configuration.Save();
            if (_configuration.VerboseLogging) PluginLog.Info($"Idioma destino cambiado a: {_configuration.TargetLanguage}");
        }
        
        private Configuration LoadConfiguration()
        {
            var config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            config.Initialize(PluginInterface);
            return config;
        }
        



        private void OnOpacityChangedHandler(float opacity)
        {
            _wpfHost?.SetOpacity(opacity);
        }

        private void OnWindowModeChangedHandler(bool useNative)
        {
            if (useNative)
            {
                // Destruir ventana ImGui
                if (_translatedChatWindow != null)
                {
                    _windowSystem.RemoveWindow(_translatedChatWindow);
                    _translatedChatWindow.Dispose();
                    _translatedChatWindow = null;
                }

                // Iniciar WPF si estamos logueados
                if (ClientState.IsLoggedIn && _wpfHost == null)
                {
                    OnLogin();
                }
            }
            else
            {
                // Destruir WPF
                OnLogout();

                // Crear ImGui
                if (_translatedChatWindow == null)
                {
                    _translatedChatWindow = new TranslatedChatWindow(_configuration, _historyManager);
                    _windowSystem.AddWindow(_translatedChatWindow);
                }
            }
        }

        private void UpdateTranslationService()
        {
            // Dispose previous service if disposable
            if (_translatorService is IDisposable disposable)
            {
                disposable.Dispose();
            }

            switch (_configuration.SelectedEngine)
            {
                case TranslationEngine.Papago:
                    _translatorService = new PapagoTranslatorService(_configuration);
                    if (_configuration.VerboseLogging) PluginLog.Info("Motor de traducción cambiado a: Papago (Naver)");
                    break;
                case TranslationEngine.Google:
                default:
                    _translatorService = new GoogleTranslatorService();
                    if (_configuration.VerboseLogging) PluginLog.Info("Motor de traducción cambiado a: Google");
                    break;
            }
            
            // Actualizar referencia en IncomingMessageHandler si ya existe
            if (_incomingMessageHandler != null)
            {
                _incomingMessageHandler.UpdateTranslator(_translatorService);
            }
            
            // Actualizar referencia en ChatBoxHook
            if (_chatBoxHook != null)
            {
                _chatBoxHook.UpdateTranslator(_translatorService);
                if (_configuration.VerboseLogging) PluginLog.Info("ChatBoxHook: Motor actualizado.");
            }
        }

        private void SwitchToGoogleFailover()
        {
            // Solo cambiar si no estamos ya en Google
            if (_configuration.SelectedEngine == TranslationEngine.Google) return;

            PluginLog.Warning("🔄 [EchoXIV] Cambiando automáticamente a Google Translate (Límite de Papago alcanzado).");
            _configuration.SelectedEngine = TranslationEngine.Google;
            _configuration.Save();
            
            // Actualizar todos los servicios
            UpdateTranslationService();

            // Notificar al usuario por el chat del juego
            _ = Framework.RunOnFrameworkThread(() => 
            {
                ChatGui.Print("[EchoXIV] 🔄 Se ha alcanzado el límite de Papago. Cambiando automáticamente a Google Translate.");
            });
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            _chatVisible = UpdateChatVisibilityInternal();
        }

        /// <summary>
        /// Determina si el chat es actualmente visible (retorna valor cacheado de forma segura)
        /// </summary>
        public static bool IsChatVisible() => _chatVisible;

        /// <summary>
        /// Lógica interna para determinar la visibilidad del chat (solo llamar desde hilo principal)
        /// </summary>
        private static unsafe bool UpdateChatVisibilityInternal()
        {
            var localPlayer = ObjectTable.LocalPlayer;
            if (!ClientState.IsLoggedIn || localPlayer == null) 
            {
                return false;
            }

            if (Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BetweenAreas] || 
                Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene] || 
                Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInCutSceneEvent] || 
                Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WatchingCutscene78])
            {
                return false;
            }

            var chatLogAddon = (FFXIVClientStructs.FFXIV.Component.GUI.AtkUnitBase*)(nint)GameGui.GetAddonByName("ChatLog");
            bool nativeChatVisible = chatLogAddon != null && chatLogAddon->IsVisible && chatLogAddon->RootNode != null;

            if (nativeChatVisible) return true;

            return true;
        }
    }
}
