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
using System.Globalization;
using System.Threading;

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
        [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
        [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
        [PluginService] internal static IGameConfig GameConfig { get; private set; } = null!;
        
        private Configuration _configuration = null!;
        private ITranslationService _translatorService = null!;
        private GoogleTranslatorService _googleTranslator = null!;
        private PapagoTranslatorService _papagoTranslator = null!;
        private GlossaryService _glossaryService = null!;
        private TranslationCache _translationCache = null!;
        private WindowSystem _windowSystem = null!;
        private ConfigWindow? _configWindow = null;
        private TranslatedChatWindow? _translatedChatWindow = null;
        private WelcomeWindow? _welcomeWindow = null;
        private WpfHost? _wpfHost = null;
        private GameFunctions.ChatBoxHook? _chatBoxHook = null;
        private IncomingMessageHandler? _incomingMessageHandler = null;
        private MessageHistoryManager _historyManager = null!;
        private static bool _chatVisible = false;
        private string _activeUiCulture = string.Empty;
        
        public Plugin()
        {
            try
            {
                // Cargar configuración
                _configuration = LoadConfiguration();
                _historyManager = new MessageHistoryManager(_configuration, PluginInterface.GetPluginConfigDirectory());

                // Initialize new services
                _glossaryService = new GlossaryService(PluginInterface.GetPluginConfigDirectory());
                _translationCache = new TranslationCache(PluginInterface.GetPluginConfigDirectory());

                // Borrar si existiera para evitar configs corruptas si el usuario borró el JSON
                if (_configuration.FirstRun)
                {
                    // Detect ScreenMode for initial UI recommendation
                    if (GameConfig.System.TryGetUInt("ScreenMode", out uint screenMode))
                    {
                        if (screenMode == 2) // Fullscreen
                            _configuration.UseNativeWindow = false;
                    }

                    var uiLang = PluginInterface.UiLanguage;
                    if (uiLang != "en")
                    {
                        _configuration.SourceLanguage = uiLang;
                        _configuration.TargetLanguage = "en";
                        _configuration.IncomingTargetLanguage = uiLang;
                        // Mantenemos FirstRun = true para que se muestre la ventana de bienvenida
                        _configuration.Save();
                    }
                }

                ApplyResourceCulture();
                
                // Inicializar motores de traducción
                _googleTranslator = new GoogleTranslatorService();
                _papagoTranslator = new PapagoTranslatorService(_configuration);

                // Inicializar servicio de traducción
                UpdateTranslationService();
                
                // Inicializar sistema de ventanas
                _windowSystem = new WindowSystem("EchoXIV");
                

                // Crear ventana de configuración (SIEMPRE)
                _configWindow = CreateConfigWindow();

                // Manejar pantalla de bienvenida si sigue siendo FirstRun (ej: Dalamud está en Inglés)
                if (_configuration.FirstRun)
                {
                    // Detect screen mode for welcome window recommendations
                    uint screenMode = 0; // Default to Windowed
                    if (GameConfig.System.TryGetUInt("ScreenMode", out uint detectedMode))
                    {
                        screenMode = detectedMode;
                    }
                    
                    _welcomeWindow = CreateWelcomeWindow(screenMode);
                    _welcomeWindow.IsOpen = true;
                }
                
                // Inicializar sistema de ventanas
                // NOTA: No iniciamos WpfHost aquí para evitar que aparezca en la pantalla de título.
                // Se iniciará en OnLogin solo si estamos realmente en el juego.

                
                // (Motores ya inicializados arriba)
                
                // (Motores ya inicializados arriba)

                // Crear manejador de mensajes entrantes
                var secondaryTranslator = _configuration.SelectedEngine == TranslationEngine.Google 
                    ? (ITranslationService)_papagoTranslator 
                    : _googleTranslator;

                _incomingMessageHandler = new IncomingMessageHandler(
                    _configuration,
                    _translatorService,
                    secondaryTranslator,
                    _glossaryService,
                    _translationCache,
                    ChatGui,
                    ClientState,
                    PlayerState,
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
                         _glossaryService,
                         _translationCache,
                         _incomingMessageHandler!,
                         PluginLog,
                         ClientState,
                         GameInteropProvider
                    );
                    _chatBoxHook.Enable();
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "❌ No se pudo habilitar el Hook de Chat (Traducción saliente deshabilitada).");
                    // No relanzamos para que el resto del plugin funcione
                }
                
                // Registrar WindowSystem
                PluginInterface.UiBuilder.Draw += DrawUi;
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
                    OnLogin();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Error fatal al inicializar el plugin. Limpiando para evitar zombies...");
                Dispose(); 
                throw;
            }
        }

        private ConfigWindow CreateConfigWindow()
        {
            var configWindow = new ConfigWindow(_configuration, _historyManager);
            configWindow.OnOpacityChanged += OnOpacityChangedHandler;
            configWindow.OnSmartVisibilityChanged += (enabled) => _wpfHost?.SetSmartVisibility(enabled);
            configWindow.OnVisualsChanged += () => _wpfHost?.UpdateVisuals();
            configWindow.OnUnlockNativeRequested += () => _wpfHost?.SetLock(false);
            configWindow.OnTranslationEngineChanged += (engine) => UpdateTranslationService();
            configWindow.OnWindowModeChanged += OnWindowModeChangedHandler;
            _windowSystem.AddWindow(configWindow);
            return configWindow;
        }

        private WelcomeWindow CreateWelcomeWindow(uint screenMode)
        {
            var welcomeWindow = new WelcomeWindow(_configuration, screenMode);
            welcomeWindow.OnConfigurationComplete += () => 
            {
                ApplyResourceCulture();
                RefreshLocalizedWindows();
                UpdateTranslationService();
            }
            ;
            _windowSystem.AddWindow(welcomeWindow);
            return welcomeWindow;
        }

        private void DrawUi()
        {
            if (ApplyResourceCulture())
            {
                RefreshLocalizedWindows();
            }

            _windowSystem.Draw();
        }

        private bool ApplyResourceCulture()
        {
            var cultureName = NormalizeUiLanguage(PluginInterface.UiLanguage);
            var changed = !string.Equals(_activeUiCulture, cultureName, StringComparison.OrdinalIgnoreCase);

            try
            {
                var culture = new CultureInfo(cultureName);
                Resources.Culture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                Thread.CurrentThread.CurrentCulture = culture;
                _activeUiCulture = culture.Name;
            }
            catch
            {
                var fallbackCulture = new CultureInfo("en");
                Resources.Culture = fallbackCulture;
                CultureInfo.DefaultThreadCurrentUICulture = fallbackCulture;
                CultureInfo.DefaultThreadCurrentCulture = fallbackCulture;
                CultureInfo.CurrentUICulture = fallbackCulture;
                CultureInfo.CurrentCulture = fallbackCulture;
                Thread.CurrentThread.CurrentUICulture = fallbackCulture;
                Thread.CurrentThread.CurrentCulture = fallbackCulture;
                changed = !string.Equals(_activeUiCulture, fallbackCulture.Name, StringComparison.OrdinalIgnoreCase);
                _activeUiCulture = fallbackCulture.Name;
            }

            return changed;
        }

        private static string NormalizeUiLanguage(string? uiLanguage)
        {
            if (string.IsNullOrWhiteSpace(uiLanguage))
            {
                return "en";
            }

            var normalized = uiLanguage.Trim();

            if (normalized.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es";
            if (normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
            if (normalized.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return "fr";
            if (normalized.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return "de";
            if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";

            return "en";
        }

        private void OnLogin()
        {
            ApplyResourceCulture();

            // Ya no es necesaria la validación aquí, el timer de WPF y DrawConditions de ImGui 
            // se encargarán de ocultar la ventana dinámicamente según el estado del juego.
            if (_configuration.UseNativeWindow)
            {
                // Modo WPF
                if (_translatedChatWindow != null)
                {
                    _windowSystem.RemoveWindow(_translatedChatWindow);
                    _translatedChatWindow.Dispose();
                    _translatedChatWindow = null;
                }

                if (_wpfHost == null)
                {
                    if (_configuration.VerboseLogging) PluginLog.Info("Jugador logueado. Iniciando host de ventana nativa...");
                    _wpfHost = new WpfHost(_configuration, PluginLog, _historyManager);
                    _wpfHost.OnRequestTranslation += m => _ = _incomingMessageHandler?.ProcessMessageAsync(m);
                    _wpfHost.Start();
                }
            }
            else
            {
                // Modo ImGui
                if (_wpfHost != null)
                {
                    _wpfHost.Dispose();
                    _wpfHost = null;
                }

                if (_translatedChatWindow == null)
                {
                    _translatedChatWindow = new TranslatedChatWindow(_configuration, _historyManager);
                    _translatedChatWindow.OnRequestTranslation += m => _ = _incomingMessageHandler?.ProcessMessageAsync(m);
                    _windowSystem.AddWindow(_translatedChatWindow);
                }
                
                _translatedChatWindow.IsOpen = _configuration.OverlayVisible;
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
            _incomingMessageHandler?.Dispose(); // Consolidated

            // Detener motores de traducción
            _googleTranslator?.Dispose();
            // _papagoTranslator no es IDisposable actualmente, pero lo manejamos por si acaso
            if (_papagoTranslator is IDisposable papagoDisp) papagoDisp.Dispose();

            // Detener hook nativo
            _chatBoxHook?.Dispose(); // Consolidated
            
            // Limpiar ventanas
            _windowSystem?.RemoveAllWindows();

            if (_configWindow != null)
            {
                _configWindow.OnOpacityChanged -= OnOpacityChangedHandler;
                // lambda: _configWindow.OnUnlockNativeRequested -= ... (Not possible for anon lambda)
                _configWindow.Dispose();
            }
            


            _wpfHost?.Dispose();
            _wpfHost = null;
            
            _translationCache.Save(); // Added
            
            // Limpiar comandos
            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler(CommandNameShort);
            CommandManager.RemoveHandler(ConfigCommandName);
            
            // Limpiar eventos
            Framework.Update -= OnFrameworkUpdate;
            if (_windowSystem != null)
            {
                PluginInterface.UiBuilder.Draw -= DrawUi;
            }
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
            PluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUI;
        }

        private void RefreshLocalizedWindows()
        {
            var configWasOpen = _configWindow?.IsOpen ?? false;
            if (_configWindow != null)
            {
                _windowSystem.RemoveWindow(_configWindow);
                _configWindow.Dispose();
            }

            _configWindow = CreateConfigWindow();
            _configWindow.IsOpen = configWasOpen;

            if (_welcomeWindow != null)
            {
                var welcomeWasOpen = _welcomeWindow.IsOpen;
                _windowSystem.RemoveWindow(_welcomeWindow);

                uint screenMode = 0;
                if (GameConfig.System.TryGetUInt("ScreenMode", out uint detectedMode))
                {
                    screenMode = detectedMode;
                }

                _welcomeWindow = CreateWelcomeWindow(screenMode);
                _welcomeWindow.IsOpen = welcomeWasOpen;
            }

            if (_translatedChatWindow != null)
            {
                var chatWasOpen = _translatedChatWindow.IsOpen;
                _windowSystem.RemoveWindow(_translatedChatWindow);
                _translatedChatWindow.Dispose();
                _translatedChatWindow = new TranslatedChatWindow(_configuration, _historyManager);
                _translatedChatWindow.OnRequestTranslation += m => _ = _incomingMessageHandler?.ProcessMessageAsync(m);
                _translatedChatWindow.IsOpen = chatWasOpen;
                _windowSystem.AddWindow(_translatedChatWindow);
            }

            _wpfHost?.UpdateLocalization();
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
                    ChatGui.Print($"Traducción activada. {_configuration.SourceLanguage.ToUpper()} → {_configuration.TargetLanguage.ToUpper()}");
                    break;
                
                case "off":
                    _configuration.TranslationEnabled = false;
                    _configuration.Save();
                    ChatGui.Print("Traducción desactivada");
                    break;
                
                case "lock":
                    if (_configuration.UseNativeWindow && _wpfHost != null)
                    {
                        _wpfHost.SetLock(true);
                        ChatGui.Print("Ventana nativa BLOQUEADA (Click-Through activado). Usa '/tl unlock' para desbloquear.");
                    }
                    else
                    {
                        ChatGui.Print("La ventana nativa no está activa.");
                    }
                    break;

                case "unlock":
                    if (_configuration.UseNativeWindow && _wpfHost != null)
                    {
                         _wpfHost.SetLock(false);
                         ChatGui.Print("Ventana nativa DESBLOQUEADA.");
                    }
                    else
                    {
                         ChatGui.Print("La ventana nativa no está activa.");
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
                    ChatGui.Print("Comandos disponibles:");
                    ChatGui.Print("/translate <mensaje> - Traduce al canal activo.");
                    ChatGui.Print("/translate on/off - Activa/desactiva traducción auto.");
                    ChatGui.Print("/translate config - Abre los ajustes.");
                    ChatGui.Print("/translate chat - Muestra/oculta ventana de chat.");
                    ChatGui.Print("/translate reset - Restaura posición de ventana.");
                    break;
                
                case "input":
                case "i":
                     // Legacy
                     ChatGui.Print("La ventana de input ha sido reemplazada. Usa /tl mensaje");
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
                _configWindow.Toggle();
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
                 ChatGui.Print("📍 Posición de ventana nativa reseteada a (100, 100).");
            }
            else if (_translatedChatWindow != null)
            {
                _translatedChatWindow.ResetPosition();
                ChatGui.Print("📍 Posición de ventana reseteada a (100, 100). Ya debería ser visible.");
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
                         _ = Framework.RunOnFrameworkThread(() => ChatGui.Print("⚠️ No hay mensaje para traducir"));
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
                                Sender = PlayerState.CharacterName.ToString(),
                                OriginalText = message,
                                TranslatedText = message,
                                IsTranslating = false
                            });
                        });
                        return;
                    }

                    var translated = await _translatorService.TranslateAsync(
                        message,
                        "auto",
                        _configuration.TargetLanguage
                    );
                    
                    // Enviar en main thread
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        // Registrar la traducción para que IncomingMessageHandler la pesque y la registre con el canal correcto
                        _incomingMessageHandler?.RegisterPendingOutgoing(translated, message);
                        
                        SendToChannel(translated, prefix);

                        // REGISTRO MANUAL en el historial para que aparezca en la ventana de traducción
                        _historyManager.AddMessage(new TranslatedChatMessage
                        {
                            Timestamp = DateTime.Now,
                            ChatType = type == XivChatType.Debug ? XivChatType.Debug : type,
                            Recipient = recipient,
                            Sender = PlayerState.CharacterName.ToString(),
                            OriginalText = message, // Tooltip
                            TranslatedText = translated, // Texto principal
                            IsTranslating = false
                        });
                        
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
                        ChatGui.PrintError("❌ Error al traducir");
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
                    case "/e": case "/echo":
                        return (command, remaining, XivChatType.Echo, string.Empty);
                    
                    case "/l1": case "/linkshell1": return (command, remaining, XivChatType.Ls1, string.Empty);
                    case "/l2": case "/linkshell2": return (command, remaining, XivChatType.Ls2, string.Empty);
                    case "/l3": case "/linkshell3": return (command, remaining, XivChatType.Ls3, string.Empty);
                    case "/l4": case "/linkshell4": return (command, remaining, XivChatType.Ls4, string.Empty);
                    case "/l5": case "/linkshell5": return (command, remaining, XivChatType.Ls5, string.Empty);
                    case "/l6": case "/linkshell6": return (command, remaining, XivChatType.Ls6, string.Empty);
                    case "/l7": case "/linkshell7": return (command, remaining, XivChatType.Ls7, string.Empty);
                    case "/l8": case "/linkshell8": return (command, remaining, XivChatType.Ls8, string.Empty);

                    case "/cwl1": case "/cwlinkshell1": return (command, remaining, XivChatType.CrossLinkShell1, string.Empty);
                    case "/cwl2": case "/cwlinkshell2": return (command, remaining, XivChatType.CrossLinkShell2, string.Empty);
                    case "/cwl3": case "/cwlinkshell3": return (command, remaining, XivChatType.CrossLinkShell3, string.Empty);
                    case "/cwl4": case "/cwlinkshell4": return (command, remaining, XivChatType.CrossLinkShell4, string.Empty);
                    case "/cwl5": case "/cwlinkshell5": return (command, remaining, XivChatType.CrossLinkShell5, string.Empty);
                    case "/cwl6": case "/cwlinkshell6": return (command, remaining, XivChatType.CrossLinkShell6, string.Empty);
                    case "/cwl7": case "/cwlinkshell7": return (command, remaining, XivChatType.CrossLinkShell7, string.Empty);
                    case "/cwl8": case "/cwlinkshell8": return (command, remaining, XivChatType.CrossLinkShell8, string.Empty);
                    
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
                /* SanitizeString a veces es demasiado restrictivo con alfabetos como el Cirílico
                mes->SanitizeString(
                    FFXIVClientStructs.FFXIV.Client.System.String.AllowedEntities.UppercaseLetters |
                    FFXIVClientStructs.FFXIV.Client.System.String.AllowedEntities.LowercaseLetters |
                    FFXIVClientStructs.FFXIV.Client.System.String.AllowedEntities.Numbers |
                    FFXIVClientStructs.FFXIV.Client.System.String.AllowedEntities.SpecialCharacters |
                    FFXIVClientStructs.FFXIV.Client.System.String.AllowedEntities.Payloads |
                    FFXIVClientStructs.FFXIV.Client.System.String.AllowedEntities.CJK
                );
                */
                FFXIVClientStructs.FFXIV.Client.UI.UIModule.Instance()->ProcessChatBoxEntry(mes);
                mes->Dtor(true);
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Error al enviar mensaje");
                ChatGui.PrintError("❌ Error al enviar mensaje");
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
                    _translatedChatWindow.OnRequestTranslation += m => _ = _incomingMessageHandler?.ProcessMessageAsync(m);
                    _windowSystem.AddWindow(_translatedChatWindow);
                }
                
                _translatedChatWindow.IsOpen = true;
                _configuration.OverlayVisible = true;
                _configuration.Save();
            }
        }

        public void ToggleTranslatedChatWindow()
        {
            if (_configuration.UseNativeWindow)
            {
                // Modo Nativo: Solo cambiamos la configuración. WpfHost lo detectará en su timer.
                _configuration.OverlayVisible = !_configuration.OverlayVisible;
                
                // Feedback visual inmediato (opcional, pero útil)
                var status = _configuration.OverlayVisible ? "VISIBLE" : "OCULTO";
                if (_configuration.VerboseLogging) PluginLog.Info($"ToggleNative: {status}");
            }
            else
            {
                // Modo ImGui: Manejo estándar de ventana Dalamud
                if (_translatedChatWindow == null)
                {
                    _translatedChatWindow = new TranslatedChatWindow(_configuration, _historyManager);
                    _translatedChatWindow.OnRequestTranslation += m => _ = _incomingMessageHandler?.ProcessMessageAsync(m);
                    _windowSystem.AddWindow(_translatedChatWindow);
                }

                _translatedChatWindow.IsOpen = !_translatedChatWindow.IsOpen;
                _configuration.OverlayVisible = _translatedChatWindow.IsOpen;
            }
            
            _configuration.Save();
            ChatGui.Print($"Chat traducido: {(_configuration.OverlayVisible ? "VISIBLE" : "OCULTO")}");
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
                    _translatorService = _papagoTranslator;
                    if (_configuration.VerboseLogging) PluginLog.Info("Motor de traducción cambiado a: Papago (Naver)");
                    break;
                case TranslationEngine.Google:
                default:
                    _translatorService = _googleTranslator;
                    if (_configuration.VerboseLogging) PluginLog.Info("Motor de traducción cambiado a: Google");
                    break;
            }
            
            // Actualizar referencia en IncomingMessageHandler si ya existe
            if (_incomingMessageHandler != null)
            {
                var secondaryTranslator = _configuration.SelectedEngine == TranslationEngine.Google 
                    ? (ITranslationService)_papagoTranslator 
                    : _googleTranslator;
                
                _incomingMessageHandler.UpdateTranslator(_translatorService);
                _incomingMessageHandler.UpdateSecondaryTranslator(secondaryTranslator);
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

            PluginLog.Warning("🔄 Cambiando automáticamente a Google Translate (Límite de Papago alcanzado).");
            _configuration.SelectedEngine = TranslationEngine.Google;
            _configuration.Save();
            
            // Actualizar todos los servicios
            UpdateTranslationService();

            // Notificar al usuario por el chat del juego
            _ = Framework.RunOnFrameworkThread(() => 
            {
                ChatGui.Print("🔄 Se ha alcanzado el límite de Papago. Cambiando automáticamente a Google Translate.");
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
            if (!ClientState.IsLoggedIn || !PlayerState.IsLoaded) 
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
