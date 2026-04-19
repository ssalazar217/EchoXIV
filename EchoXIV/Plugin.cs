using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using EchoXIV.Services;
using EchoXIV.UI;
using EchoXIV.Properties;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using FFXIVClientStructs.FFXIV.Client.System.String;
using System.Text.RegularExpressions;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

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
        private GameFunctions.ChatBoxHook? _chatBoxHook = null;
        private IncomingMessageHandler? _incomingMessageHandler = null;
        private MessageHistoryManager _historyManager = null!;
        private static bool _chatVisible = false;
        private string _activeUiCulture = string.Empty;
        private bool _showWelcomeWindow;
        
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
                _showWelcomeWindow = _configuration.FirstRun;
                if (_showWelcomeWindow)
                {
                    var uiLang = NormalizeTranslationLanguage(PluginInterface.UiLanguage);
                    if (!string.IsNullOrWhiteSpace(uiLang))
                    {
                        _configuration.SourceLanguage = uiLang;
                        _configuration.TargetLanguage = "en";
                        _configuration.IncomingTargetLanguage = string.Empty;
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
                    _welcomeWindow = CreateWelcomeWindow();
                    _welcomeWindow.IsOpen = true;
                }

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
                _incomingMessageHandler.OnTranslationStarted += OnTranslationStarted;
                _incomingMessageHandler.OnMessageTranslated += OnMessageTranslated;
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
                    PluginLog.Error(ex, "Failed to enable Chat Hook (outgoing translation disabled).");
                    // No relanzamos para que el resto del plugin funcione
                }
                
                // Registrar WindowSystem
                PluginInterface.UiBuilder.Draw += DrawUi;
                PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
                
                // Registrar comandos
                CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = GetResourceText("Command_HelpMessageMain", "Translate and send: /translate [message/on/off/config/help]")
                });

                CommandManager.AddHandler(CommandNameShort, new CommandInfo(OnCommand)
                {
                    HelpMessage = GetResourceText("Command_HelpMessageShort", "Short alias for /translate")
                });

                CommandManager.AddHandler(ConfigCommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = GetResourceText("Command_HelpMessageConfig", "Open EchoXIV configuration")
                });
                
                // Registrar UI callback principal (botón "Abrir" abre configuración)
                PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;

                // Suscribirse al Update del Framework para caché de visibilidad seguro entre hilos
                Framework.Update += OnFrameworkUpdate;

                // Suscribirse a eventos de login/logout para manejar la ventana nativa
                ClientState.Login += OnClientLogin;
                ClientState.Logout += OnClientLogout;

                // Si ya estamos logueados (ej: recarga de plugin), iniciar ahora
                if (ClientState.IsLoggedIn)
                    OnLogin();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Fatal error while initializing the plugin. Cleaning up to avoid dangling state...");
                Dispose(); 
                throw;
            }
        }

        private ConfigWindow CreateConfigWindow()
        {
            var configWindow = new ConfigWindow(_configuration, _historyManager);
            configWindow.OnVisualsChanged += OnVisualsChangedHandler;
            configWindow.OnTranslationEngineChanged += OnTranslationEngineChangedHandler;
            _windowSystem.AddWindow(configWindow);
            return configWindow;
        }

        private WelcomeWindow CreateWelcomeWindow()
        {
            var welcomeWindow = new WelcomeWindow(_configuration);
            welcomeWindow.OnConfigurationComplete += OnWelcomeConfigurationComplete;
            _windowSystem.AddWindow(welcomeWindow);
            return welcomeWindow;
        }

        private TranslatedChatWindow CreateTranslatedChatWindow()
        {
            var translatedChatWindow = new TranslatedChatWindow(_configuration, _historyManager);
            translatedChatWindow.OnRequestTranslation += OnTranslatedChatRequestTranslation;
            _windowSystem.AddWindow(translatedChatWindow);
            return translatedChatWindow;
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

            if (normalized.StartsWith("zh-hant", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
            if (normalized.StartsWith("zh-hans", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
            if (normalized.StartsWith("tw", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
            if (normalized.StartsWith("zh-tw", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
            if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
            if (normalized.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es";
            if (normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
            if (normalized.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return "fr";
            if (normalized.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return "de";
            if (normalized.StartsWith("it", StringComparison.OrdinalIgnoreCase)) return "it";
            if (normalized.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
            if (normalized.StartsWith("no", StringComparison.OrdinalIgnoreCase)) return "no";
            if (normalized.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) return "ru";
            if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";

            return "en";
        }

        private static string NormalizeTranslationLanguage(string? uiLanguage)
        {
            if (string.IsNullOrWhiteSpace(uiLanguage))
            {
                return "en";
            }

            var normalized = uiLanguage.Trim();

            if (normalized.StartsWith("zh-hant", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
            if (normalized.StartsWith("zh-hans", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            if (normalized.StartsWith("zh-tw", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
            if (normalized.StartsWith("tw", StringComparison.OrdinalIgnoreCase)) return "zh-TW";
            if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) return "zh-CN";
            if (normalized.StartsWith("es", StringComparison.OrdinalIgnoreCase)) return "es";
            if (normalized.StartsWith("ja", StringComparison.OrdinalIgnoreCase)) return "ja";
            if (normalized.StartsWith("fr", StringComparison.OrdinalIgnoreCase)) return "fr";
            if (normalized.StartsWith("de", StringComparison.OrdinalIgnoreCase)) return "de";
            if (normalized.StartsWith("it", StringComparison.OrdinalIgnoreCase)) return "it";
            if (normalized.StartsWith("ko", StringComparison.OrdinalIgnoreCase)) return "ko";
            if (normalized.StartsWith("ru", StringComparison.OrdinalIgnoreCase)) return "ru";
            if (normalized.StartsWith("no", StringComparison.OrdinalIgnoreCase)) return "no";
            if (normalized.StartsWith("en", StringComparison.OrdinalIgnoreCase)) return "en";

            return "en";
        }

        private void OnLogin()
        {
            ApplyResourceCulture();

            if (_translatedChatWindow == null)
            {
                _translatedChatWindow = CreateTranslatedChatWindow();
            }

            _translatedChatWindow.IsOpen = _configuration.OverlayVisible;
        }

        private void OnLogout()
        {
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
                DetachConfigWindowHandlers(_configWindow);
                _configWindow.Dispose();
            }

            if (_welcomeWindow != null)
            {
                _welcomeWindow.OnConfigurationComplete -= OnWelcomeConfigurationComplete;
            }

            if (_translatedChatWindow != null)
            {
                _translatedChatWindow.OnRequestTranslation -= OnTranslatedChatRequestTranslation;
            }

            if (_incomingMessageHandler != null)
            {
                _incomingMessageHandler.OnTranslationStarted -= OnTranslationStarted;
                _incomingMessageHandler.OnMessageTranslated -= OnMessageTranslated;
                _incomingMessageHandler.OnRequestEngineFailover -= SwitchToGoogleFailover;
            }
            
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
            ClientState.Login -= OnClientLogin;
            ClientState.Logout -= OnClientLogout;
        }

        private void RefreshLocalizedWindows()
        {
            var configWasOpen = _configWindow?.IsOpen ?? false;
            if (_configWindow != null)
            {
                _windowSystem.RemoveWindow(_configWindow);
                DetachConfigWindowHandlers(_configWindow);
                _configWindow.Dispose();
            }

            _configWindow = CreateConfigWindow();
            _configWindow.IsOpen = configWasOpen;

            if (_welcomeWindow != null)
            {
                var welcomeWasOpen = _welcomeWindow.IsOpen;
                _windowSystem.RemoveWindow(_welcomeWindow);
                _welcomeWindow.OnConfigurationComplete -= OnWelcomeConfigurationComplete;

                _welcomeWindow = CreateWelcomeWindow();
                _welcomeWindow.IsOpen = welcomeWasOpen;
            }
            else if (_showWelcomeWindow)
            {
                _welcomeWindow = CreateWelcomeWindow();
                _welcomeWindow.IsOpen = false;
            }

            if (_translatedChatWindow != null)
            {
                var chatWasOpen = _translatedChatWindow.IsOpen;
                _windowSystem.RemoveWindow(_translatedChatWindow);
                _translatedChatWindow.OnRequestTranslation -= OnTranslatedChatRequestTranslation;
                _translatedChatWindow.Dispose();
                _translatedChatWindow = CreateTranslatedChatWindow();
                _translatedChatWindow.IsOpen = chatWasOpen;
            }
        }

        private void DetachConfigWindowHandlers(ConfigWindow configWindow)
        {
            configWindow.OnVisualsChanged -= OnVisualsChangedHandler;
            configWindow.OnTranslationEngineChanged -= OnTranslationEngineChangedHandler;
        }

        private void OnVisualsChangedHandler()
        {
        }

        private void OnTranslationEngineChangedHandler(TranslationEngine engine)
        {
            UpdateTranslationService();
        }

        private void OnWelcomeConfigurationComplete()
        {
            _showWelcomeWindow = false;
            ApplyResourceCulture();
            RefreshLocalizedWindows();
            UpdateTranslationService();
        }

        private void OnTranslatedChatRequestTranslation(TranslatedChatMessage message)
        {
            _ = _incomingMessageHandler?.ProcessMessageAsync(message);
        }

        private void OnTranslationStarted(TranslatedChatMessage message)
        {
            _historyManager.AddMessage(message);
        }

        private void OnMessageTranslated(TranslatedChatMessage message)
        {
            _historyManager.UpdateMessage(message);
        }

        private void OnClientLogin()
        {
            OnLogin();
        }

        private void OnClientLogout(int type, int code)
        {
            OnLogout();
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
                    ChatGui.Print(GetResourceText("Command_TranslationEnabledWithLanguages", "Translation enabled. {0} -> {1}")
                        .Replace("{0}", _configuration.SourceLanguage.ToUpperInvariant())
                        .Replace("{1}", _configuration.TargetLanguage.ToUpperInvariant()));
                    break;
                
                case "off":
                    _configuration.TranslationEnabled = false;
                    _configuration.Save();
                    ChatGui.Print(GetResourceText("Command_TranslationDisabled", "Translation disabled"));
                    break;
                
                case "lock":
                    ChatGui.Print(GetResourceText("Command_DalamudWindowOnly", "Only the Dalamud window is available."));
                    break;

                case "unlock":
                    ChatGui.Print(GetResourceText("Command_DalamudWindowOnly", "Only the Dalamud window is available."));
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
                    ChatGui.Print(GetResourceText("Command_HelpHeader", "Available commands:"));
                    ChatGui.Print(GetResourceText("Command_HelpTranslateLine", "/translate <message> - Translate to the active channel."));
                    ChatGui.Print(GetResourceText("Command_HelpToggleLine", "/translate on/off - Toggle automatic translation."));
                    ChatGui.Print(GetResourceText("Command_HelpConfigLine", "/translate config - Open settings."));
                    ChatGui.Print(GetResourceText("Command_HelpChatLine", "/translate chat - Show or hide the translated chat window."));
                    ChatGui.Print(GetResourceText("Command_HelpResetLine", "/translate reset - Reset the translated chat window position."));
                    break;
                
                case "input":
                case "i":
                     // Legacy
                     ChatGui.Print(GetResourceText("Command_InputReplaced", "The input window was replaced. Use /tl <message>"));
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
            if (_translatedChatWindow == null)
            {
                _translatedChatWindow = CreateTranslatedChatWindow();
            }

            if (_translatedChatWindow != null)
            {
                _translatedChatWindow.ResetPosition();
                ChatGui.Print(GetResourceText("Command_PositionReset", "Window position reset to (100, 100). It should be visible now."));
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
                        _ = Framework.RunOnFrameworkThread(() => ChatGui.Print(GetResourceText("Command_NoMessage", "No message to translate")));
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

                    using var timeout = TranslationDefaults.CreateTimeoutTokenSource();
                    var translated = await _translatorService.TranslateAsync(
                        message,
                        "auto",
                        _configuration.TargetLanguage,
                        timeout.Token
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
                        
                        PluginLog.Info($"Translated and sent [{type}]: '{message}' -> '{translated}'");
                    });
                }
                catch (OperationCanceledException ex)
                {
                    PluginLog.Warning(ex, "Manual translation timed out. Sending original message instead.");
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        var (prefix, message, type, recipient) = ParseChannelAndMessage(input);
                        if (string.IsNullOrWhiteSpace(message))
                        {
                            return;
                        }

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
                }
                catch (TranslationRateLimitException ex)
                {
                    PluginLog.Warning($"{ex.Message}. Enabling automatic failover to Google...");
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        SwitchToGoogleFailover();
                        // Re-intentar la traducción una vez con el nuevo motor para el comando actual
                        TranslateAndSend(input);
                    });
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Error while translating message.");
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        ChatGui.PrintError(GetResourceText("Command_TranslateError", "Translation error"));
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
            
            var agentChat = AgentChatLog.Instance();
            var shell = RaptureShellModule.Instance();

            if (agentChat != null)
            {
                switch (agentChat->CurrentChannel)
                {
                    case ChatChannel.Say:
                        activeType = XivChatType.Say;
                        break;
                    case ChatChannel.Party:
                        activeType = XivChatType.Party;
                        break;
                    case ChatChannel.Alliance:
                        activeType = XivChatType.Alliance;
                        break;
                }
            }
            
            if (shell != null)
            {
                var gameChatType = (uint)shell->ChatType;
                if (activeType == XivChatType.Debug)
                {
                    activeType = MapActiveInputChannelToChatType(gameChatType);
                }

                if (activeType == XivChatType.TellOutgoing && agentChat != null)
                {
                    activeRecipient = agentChat->TellPlayerName.ToString();
                }
            }

            return (null, trimmed, activeType, activeRecipient);
        }

        private static XivChatType MapActiveInputChannelToChatType(uint inputChannel)
        {
            return inputChannel switch
            {
                0 => XivChatType.TellOutgoing,
                1 => XivChatType.Say,
                2 => XivChatType.Party,
                3 => XivChatType.Alliance,
                4 => XivChatType.Yell,
                5 => XivChatType.Shout,
                6 => XivChatType.FreeCompany,
                7 => XivChatType.PvPTeam,
                8 => XivChatType.NoviceNetwork,
                9 => XivChatType.CrossLinkShell1,
                10 => XivChatType.CrossLinkShell2,
                11 => XivChatType.CrossLinkShell3,
                12 => XivChatType.CrossLinkShell4,
                13 => XivChatType.CrossLinkShell5,
                14 => XivChatType.CrossLinkShell6,
                15 => XivChatType.CrossLinkShell7,
                16 => XivChatType.CrossLinkShell8,
                17 => XivChatType.TellOutgoing,
                18 => XivChatType.TellOutgoing,
                19 => XivChatType.Ls1,
                20 => XivChatType.Ls2,
                21 => XivChatType.Ls3,
                22 => XivChatType.Ls4,
                23 => XivChatType.Ls5,
                24 => XivChatType.Ls6,
                25 => XivChatType.Ls7,
                26 => XivChatType.Ls8,
                _ => XivChatType.Debug,
            };
        }

        private unsafe void SendToChannel(string message, string? channel)
        {
            try
            {
               // SANEAMIENTO
               var sanitized = message.Replace("\0", "").Replace("\r", "").Replace("\n", " ");
               var fullMessage = string.IsNullOrEmpty(channel) ? sanitized : $"{channel} {sanitized}";
                
                if (System.Text.Encoding.UTF8.GetByteCount(fullMessage) > 500)
                {
                    // Truncar si es necesario
                    while (System.Text.Encoding.UTF8.GetByteCount(fullMessage) > 497)
                    {
                        fullMessage = fullMessage.Substring(0, fullMessage.Length - 1);
                    }
                    fullMessage += "...";
                }
                
                var mes = FFXIVClientStructs.FFXIV.Client.System.String.Utf8String.FromString(fullMessage);
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
                PluginLog.Error(ex, "Error while sending message.");
                ChatGui.PrintError(GetResourceText("Command_SendError", "Error sending message"));
            }
        }
        
        private Configuration LoadConfiguration()
        {
            var config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            config.Initialize(PluginInterface);
            var hasChanges = config.NormalizeDefaults();
            if (hasChanges)
            {
                config.Save();
            }
            return config;
        }

        public void ToggleTranslatedChatWindow()
        {
            if (_translatedChatWindow == null)
            {
                _translatedChatWindow = CreateTranslatedChatWindow();
            }

            _translatedChatWindow.IsOpen = !_translatedChatWindow.IsOpen;
            _configuration.OverlayVisible = _translatedChatWindow.IsOpen;
            
            _configuration.Save();
            ChatGui.Print(GetResourceText(
                _configuration.OverlayVisible ? "Command_ChatVisible" : "Command_ChatHidden",
                _configuration.OverlayVisible ? "Translated chat: visible" : "Translated chat: hidden"));
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
                    if (_configuration.VerboseLogging) PluginLog.Info("Translation engine changed to: Papago (Naver)");
                    break;
                case TranslationEngine.Google:
                default:
                    _translatorService = _googleTranslator;
                    if (_configuration.VerboseLogging) PluginLog.Info("Translation engine changed to: Google");
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
                if (_configuration.VerboseLogging) PluginLog.Info("ChatBoxHook: translator updated.");
            }
        }

        private void SwitchToGoogleFailover()
        {
            // Solo cambiar si no estamos ya en Google
            if (_configuration.SelectedEngine == TranslationEngine.Google) return;

            PluginLog.Warning("Automatically switching to Google Translate (Papago limit reached).");
            _configuration.SelectedEngine = TranslationEngine.Google;
            _configuration.Save();
            
            // Actualizar todos los servicios
            UpdateTranslationService();

            // Notificar al usuario por el chat del juego
            _ = Framework.RunOnFrameworkThread(() => 
            {
                ChatGui.Print(GetResourceText("Command_FailoverToGoogle", "Papago limit reached. Switching automatically to Google Translate."));
            });
        }

        private static string GetResourceText(string key, string fallback)
        {
            return Resources.ResourceManager.GetString(key, Resources.Culture) ?? fallback;
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
