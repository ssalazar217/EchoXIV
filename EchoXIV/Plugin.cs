using System;
using System.Collections.Generic;
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
    /// Main chat translation plugin
    /// </summary>
    public sealed class Plugin : IDalamudPlugin
    {
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
        private LocalAiTranslatorService _localAiTranslator = null!;
        private LibreTranslateTranslatorService _libreTranslateTranslator = null!;
        private YandexTranslatorService _yandexTranslator = null!;
        private GlossaryService _glossaryService = null!;
        private TranslationCache _translationCache = null!;
        private IRomanizationService _romanizationService = null!;
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
        private readonly CancellationTokenSource _shutdownCts = new();
        
        public Plugin()
        {
            try
            {
                // Load configuration
                _configuration = LoadConfiguration();
                _historyManager = new MessageHistoryManager(_configuration, PluginInterface.GetPluginConfigDirectory());

                // Initialize new services
                _glossaryService = new GlossaryService(PluginInterface.GetPluginConfigDirectory());
                _translationCache = new TranslationCache(PluginInterface.GetPluginConfigDirectory());

                _showWelcomeWindow = _configuration.FirstRun;
                if (_showWelcomeWindow)
                {
                    var uiLang = NormalizeTranslationLanguage(PluginInterface.UiLanguage);
                    if (!string.IsNullOrWhiteSpace(uiLang))
                    {
                        _configuration.SourceLanguage = uiLang;
                        _configuration.TargetLanguage = "en";
                        _configuration.IncomingTargetLanguage = string.Empty;
                        // Keep FirstRun=true so the welcome window shows
                        _configuration.Save();
                    }
                }

                ApplyResourceCulture();
                
                // Initialize translation engines
                _googleTranslator = new GoogleTranslatorService();
                _papagoTranslator = new PapagoTranslatorService();
                _localAiTranslator = new LocalAiTranslatorService(_configuration);
                _libreTranslateTranslator = new LibreTranslateTranslatorService();
                _yandexTranslator = new YandexTranslatorService();

                // Initialize translation service
                UpdateTranslationService();
                
                // Initialize window system
                _windowSystem = new WindowSystem("EchoXIV");
                

                // Create config window
                _configWindow = CreateConfigWindow();

                // Show welcome window while FirstRun
                if (_configuration.FirstRun)
                {
                    _welcomeWindow = CreateWelcomeWindow();
                    _welcomeWindow.IsOpen = true;
                }

                // Create incoming message handler
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
                    PluginLog,
                    _romanizationService,
                    BuildConversationContext
                );
                // Wire events
                _incomingMessageHandler.OnTranslationStarted += OnTranslationStarted;
                _incomingMessageHandler.OnMessageTranslated += OnMessageTranslated;
                _incomingMessageHandler.OnOutgoingMessage += OnOutgoingMessage;
                _incomingMessageHandler.OnRequestEngineFailover += SwitchToGoogleFailover;
                
                // Initialize native hook (outgoing translation)
                try
                {
_chatBoxHook = new GameFunctions.ChatBoxHook(
                         _configuration,
                         _translatorService,
                         secondaryTranslator,
                         _glossaryService,
                         _translationCache,
                         _incomingMessageHandler!,
                         _romanizationService,
                         PluginLog,
                         ClientState,
                         GameInteropProvider,
                         SwitchToGoogleFailover
                     );
                    _chatBoxHook.Enable();
                }
                catch (Exception ex)
                {
                    PluginLog.Error(ex, "Failed to enable Chat Hook (outgoing translation disabled).");
                    // Don't rethrow so the rest of the plugin keeps working
                }
                
                // Register WindowSystem
                PluginInterface.UiBuilder.Draw += DrawUi;
                PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
                PluginInterface.LanguageChanged += OnLanguageChanged;
                
                // Register commands
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
                
                // Register main UI callback ("Open" opens config)
                PluginInterface.UiBuilder.OpenMainUi += ToggleConfigUI;

                // Subscribe to Framework update for thread-safe visibility cache
                Framework.Update += OnFrameworkUpdate;

                ClientState.Login += OnClientLogin;
                ClientState.Logout += OnClientLogout;

                // Initialize immediately when already logged in.
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
            configWindow.OnTranslationEngineChanged += OnTranslationEngineChangedHandler;
            configWindow.OnUiLanguageChanged += OnUiLanguageChangedHandler;
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
            _windowSystem.Draw();
        }

        private void OnLanguageChanged(string langCode)
        {
            if (ApplyResourceCulture())
            {
                RefreshLocalizedWindows();
            }
        }

        private bool ApplyResourceCulture()
        {
            // Use the manually chosen language, otherwise follow Dalamud.
            var effectiveLanguage = !string.IsNullOrWhiteSpace(_configuration.UiLanguage)
                ? _configuration.UiLanguage
                : PluginInterface.UiLanguage;

            var cultureName = NormalizeUiLanguage(effectiveLanguage);
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
            if (normalized.StartsWith("tr", StringComparison.OrdinalIgnoreCase)) return "tr";
            if (normalized.StartsWith("pt-br", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
            if (normalized.StartsWith("pt", StringComparison.OrdinalIgnoreCase)) return "pt-BR";
            if (normalized.StartsWith("pl", StringComparison.OrdinalIgnoreCase)) return "pl";
            if (normalized.StartsWith("uk", StringComparison.OrdinalIgnoreCase)) return "uk";
            if (normalized.StartsWith("cs", StringComparison.OrdinalIgnoreCase)) return "cs";
            if (normalized.StartsWith("nl", StringComparison.OrdinalIgnoreCase)) return "nl";
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
            _shutdownCts.Cancel();

            // Stop incoming message handler
            _incomingMessageHandler?.Dispose();

            // Stop translation engines
            _googleTranslator?.Dispose();
            if (_papagoTranslator is IDisposable papagoDisp) papagoDisp.Dispose();
            _localAiTranslator?.Dispose();
            if (_libreTranslateTranslator is IDisposable libreDisp) libreDisp.Dispose();
            if (_yandexTranslator is IDisposable yandexDisp) yandexDisp.Dispose();

            // Stop native hook
            _chatBoxHook?.Dispose();
            
            // Remove windows
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
                _incomingMessageHandler.OnOutgoingMessage -= OnOutgoingMessage;
                _incomingMessageHandler.OnRequestEngineFailover -= SwitchToGoogleFailover;
            }
            
            _translationCache.Save();
            _historyManager?.Dispose();
            
            // Remove commands
            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler(CommandNameShort);
            CommandManager.RemoveHandler(ConfigCommandName);
            
            // Unhook events
            Framework.Update -= OnFrameworkUpdate;
            if (_windowSystem != null)
            {
                PluginInterface.UiBuilder.Draw -= DrawUi;
            }
            PluginInterface.LanguageChanged -= OnLanguageChanged;
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
            configWindow.OnTranslationEngineChanged -= OnTranslationEngineChangedHandler;
            configWindow.OnUiLanguageChanged -= OnUiLanguageChangedHandler;
        }

        private void OnTranslationEngineChangedHandler(TranslationEngine engine)
        {
            // A manual selection away from Local AI cancels automatic restoration.
            // The failover path sets the flag directly and does not come through here.
            if (engine != TranslationEngine.LocalAI && _configuration.LocalAiUnavailable)
            {
                _configuration.LocalAiUnavailable = false;
                _configuration.Save();
            }
            UpdateTranslationService();
        }

        private void OnUiLanguageChangedHandler()
        {
            if (ApplyResourceCulture())
            {
                RefreshLocalizedWindows();
            }
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

        private void OnOutgoingMessage(TranslatedChatMessage message)
        {
            // Dedicated path for sent messages to avoid duplicating incoming write logic
            _historyManager.AddMessage(message);
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
                    // Translate non-reserved commands.
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
        /// Resets the translated chat window position to center
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

        private void TranslateAndSend(string input, bool allowFailoverRetry = true)
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

                    // Exclusion list check before translating
                    if (_configuration.ExcludedMessages.Contains(message))
                    {
                        _ = Framework.RunOnFrameworkThread(() =>
                        {
                            if (_shutdownCts.IsCancellationRequested)
                                return;

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

                    using var timeout = TranslationDefaults.CreateTimeoutTokenSource(_shutdownCts.Token);
                    string translated;
                    string? romanizedText = null;
                    var combinedRomanization = false;

                    var canUseCombinedRomanization = _configuration.EnableRomanization &&
                        _configuration.SelectedEngine == TranslationEngine.LocalAI &&
                        _configuration.RomanizationLanguages.Contains(_configuration.TargetLanguage);

                    if (canUseCombinedRomanization)
                    {
                        var context = _configuration.LocalAiUseConversationContext
                            ? BuildConversationContext(type)
                            : null;
                        var combined = await _localAiTranslator.TranslateWithRomanizationAsync(
                            message,
                            "auto",
                            _configuration.TargetLanguage,
                            context,
                            timeout.Token,
                            romanizeTranslation: true);
                        translated = combined.Translation;
                        romanizedText = combined.RomanizedText;
                        combinedRomanization = true;
                    }
                    else if (_translatorService is LocalAiTranslatorService localAi &&
                             _configuration.LocalAiUseConversationContext)
                    {
                        // Build context from the target channel history.
                        var context = BuildConversationContext(type);
                        translated = await localAi.TranslateWithContextAsync(
                            message,
                            "auto",
                            _configuration.TargetLanguage,
                            context,
                            timeout.Token
                        );
                    }
                    else
                    {
                        translated = await _translatorService.TranslateAsync(
                            message,
                            "auto",
                            _configuration.TargetLanguage,
                            timeout.Token
                        );
                    }

                    var nativeTranslated = translated;
                    var textToSend = translated;
                    var translatedScript = NonLatinScriptDetector.Detect(nativeTranslated);
                    if (!combinedRomanization &&
                        _configuration.EnableRomanization &&
                        _configuration.RomanizationLanguages.Contains(_configuration.TargetLanguage) &&
                        translatedScript != NonLatinScriptKind.Latin)
                    {
                        using var romanizationTimeout = TranslationDefaults.CreateTimeoutTokenSource(_shutdownCts.Token);
                        romanizedText = await _romanizationService.RomanizeAsync(
                            nativeTranslated,
                            _configuration.TargetLanguage,
                            romanizationTimeout.Token);
                        if (!string.IsNullOrWhiteSpace(romanizedText))
                            textToSend = romanizedText;
                    }

                        // Send on main thread
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        if (_shutdownCts.IsCancellationRequested)
                            return;

                        // Register pending so IncomingMessageHandler matches it via dedicated outgoing path
                        _incomingMessageHandler?.RegisterPendingOutgoing(
                            textToSend,
                            message,
                            nativeTranslated,
                            romanizedText);
                        
                        SendToChannel(textToSend, prefix);
                        PluginLog.Info($"Translated and sent [{type}] using {_configuration.SelectedEngine} to {_configuration.TargetLanguage}: '{message}' -> '{textToSend}'");
                    });
                }
                catch (OperationCanceledException ex)
                {
                    if (_shutdownCts.IsCancellationRequested)
                        return;

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
                    if (!allowFailoverRetry)
                    {
                        PluginLog.Error(ex, "Translation failed after automatic failover. Sending the original message once.");
                        _ = Framework.RunOnFrameworkThread(() => SendOriginalAfterTranslationFailure(input));
                        return;
                    }

                    PluginLog.Warning($"{ex.Message}. Enabling automatic failover to Google (one retry only)...");
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        if (_shutdownCts.IsCancellationRequested)
                            return;
                        SwitchToGoogleFailover();
                        TranslateAndSend(input, allowFailoverRetry: false);
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
            }, _shutdownCts.Token);
        }

        private void SendOriginalAfterTranslationFailure(string input)
        {
            var (prefix, message, type, recipient) = ParseChannelAndMessage(input);
            if (string.IsNullOrWhiteSpace(message))
                return;

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
        }

        /// <summary>
        /// Builds conversation context from the target channel history.
        /// Returns null when context is disabled or insufficient.
        /// </summary>
        private List<ConversationLine>? BuildConversationContext(XivChatType channelType)
        {
            if (!_configuration.LocalAiUseConversationContext || _configuration.LocalAiContextMessages <= 0)
                return null;

            if (_configuration.LocalAiContextChannels == null ||
                !_configuration.LocalAiContextChannels.Contains((int)channelType))
            {
                return null;
            }

            if (_historyManager == null)
                return null;

            var count = _configuration.LocalAiContextMessages;

            var history = _historyManager.GetHistory();
            if (history == null || history.Count == 0)
                return null;

            // Filter by the target channel.
            var channelMessages = history
                .Where(m => m.ChatType == channelType)
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .Reverse()
                .Select(m => new ConversationLine(m.Sender, m.OriginalText))
                .ToList();

            return channelMessages.Count == 0 ? null : channelMessages;
        }

        private unsafe (string? prefix, string message, XivChatType type, string recipient) ParseChannelAndMessage(string input)
        {
            var trimmed = input.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return (null, string.Empty, XivChatType.Debug, string.Empty);

            // Detect channel command prefixes such as /p, /t, or /r.
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
                        return (command, remaining, XivChatType.TellOutgoing, GetResourceText("Tell_DefaultRecipient", "Recipient"));
                    }

                    case "/t": case "/tell":
                    {
                        // Handle /t "First Last@World" Message
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
                        
                        // Handle /t Name@World or /t First Last
                        var parts = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2)
                        {
                            // First token contains @: single-word name World@World
                            if (parts[0].Contains("@"))
                            {
                                var recipient = parts[0];
                                var message = string.Join(" ", parts.Skip(1));
                                return ($"{command} {recipient}", message, XivChatType.TellOutgoing, recipient);
                            }
                            
                            // Otherwise assume First Last (FFXIV names have two parts)
                            if (parts.Length >= 3)
                            {
                                var recipient = $"{parts[0]} {parts[1]}";
                                var message = string.Join(" ", parts.Skip(2));
                                return ($"{command} {recipient}", message, XivChatType.TellOutgoing, recipient);
                            }
                            
                            // Single-word name fallback
                            var fallbackRecipient = parts[0];
                            var fallbackMessage = string.Join(" ", parts.Skip(1));
                            return ($"{command} {fallbackRecipient}", fallbackMessage, XivChatType.TellOutgoing, fallbackRecipient);
                        }
                        break;
                    }
                }
                
                // Unknown /command: let the game handle it; mark as Debug for history
                return (null, trimmed, XivChatType.Debug, string.Empty);
            }

            // Implicit channel (no / prefix)
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
                var sanitized = message.Replace("\0", "").Replace("\r", "").Replace("\n", " ");
                var fullMessage = string.IsNullOrEmpty(channel) ? sanitized : $"{channel} {sanitized}";
                var maxBytes = TranslationDefaults.MaxChatBytes;
                
                if (System.Text.Encoding.UTF8.GetByteCount(fullMessage) > maxBytes)
                {
                    while (System.Text.Encoding.UTF8.GetByteCount(fullMessage) > maxBytes - 3)
                    {
                        fullMessage = fullMessage.Substring(0, fullMessage.Length - 1);
                    }
                    fullMessage += "...";
                }
                
                var mes = FFXIVClientStructs.FFXIV.Client.System.String.Utf8String.FromString(fullMessage);
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

            var engines = new Dictionary<TranslationEngine, ITranslationService>
            {
                [TranslationEngine.Google] = _googleTranslator,
                [TranslationEngine.Papago] = _papagoTranslator,
                [TranslationEngine.LibreTranslate] = _libreTranslateTranslator,
                [TranslationEngine.Yandex] = _yandexTranslator,
                [TranslationEngine.LocalAI] = _localAiTranslator,
            };

            _translatorService = new TranslationRouter(
                _configuration.SelectedEngine,
                _configuration.FallbackEngine,
                engines,
                _configuration.EngineRecoveryCheckMinutes);

// Configure romanization engines (direct dictionary lookup)
            var romanizationEngines = new Dictionary<TranslationEngine, IRomanizationService>
            {
                [TranslationEngine.Google] = _googleTranslator,
                [TranslationEngine.Papago] = _papagoTranslator,
                [TranslationEngine.LocalAI] = _localAiTranslator,
                // Yandex/LibreTranslate lack native romanization; skipped
            };

            // Use the primary engine directly per configuration
            _romanizationService = romanizationEngines.TryGetValue(_configuration.SelectedEngine, out var service)
                ? service
                : _googleTranslator;

            if (_configuration.VerboseLogging)
                PluginLog.Info($"Romanization engine active: {_configuration.SelectedEngine}");

            var secondaryTranslator = _configuration.SelectedEngine == TranslationEngine.Google 
                ? (ITranslationService)_papagoTranslator 
                : _googleTranslator;
            
            // Update reference in IncomingMessageHandler if present.
            if (_incomingMessageHandler != null)
            {
                _incomingMessageHandler.UpdateTranslator(_translatorService);
                _incomingMessageHandler.UpdateSecondaryTranslator(secondaryTranslator);
                _incomingMessageHandler.UpdateRomanizationService(_romanizationService);
            }
            
            // Actualizar referencia en ChatBoxHook
            if (_chatBoxHook != null)
            {
                _chatBoxHook.UpdateTranslators(_translatorService, secondaryTranslator);
                _chatBoxHook.UpdateRomanizationService(_romanizationService);
                if (_configuration.VerboseLogging) PluginLog.Info("ChatBoxHook: translator updated.");
            }

            // Router update no longer needed (direct engine use)
        }

        private void SwitchToGoogleFailover()
        {
            // TranslationRouter handles failover internally without changing the primary
            // engine. This method only notifies and remains as legacy backup for LocalAI.
            var wasLocalAi = _configuration.SelectedEngine == TranslationEngine.LocalAI;
            PluginLog.Warning(wasLocalAi
                ? "Local AI server is not responding. Switching automatically to Google Translate."
                : "Primary engine reached its limit. Switching automatically to Google Translate.");

            // Failover must actually switch engines or retries hit the same dead LocalAI.
            if (_configuration.SelectedEngine != TranslationEngine.Google)
            {
                _configuration.SelectedEngine = TranslationEngine.Google;
                _configuration.LocalAiUnavailable = wasLocalAi;
                _configuration.Save();
                UpdateTranslationService();
            }

            // Notify the user via game chat
            var messageKey = wasLocalAi ? "Command_LocalAiFailoverToGoogle" : "Command_FailoverToGoogle";
            var fallbackMessage = wasLocalAi
                ? "Local AI server is not responding. Switching automatically to Google Translate."
                : "Primary engine limit reached. Switching automatically to Google Translate.";
            _ = Framework.RunOnFrameworkThread(() => 
            {
                ChatGui.Print(GetResourceText(messageKey, fallbackMessage));
            });
        }

        private static string GetResourceText(string key, string fallback)
        {
            return Resources.ResourceManager.GetString(key, Resources.Culture) ?? fallback;
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            _chatVisible = UpdateChatVisibilityInternal();

            // Periodically re-check Local AI availability for auto-recovery
            if (_configuration.LocalAiUnavailable)
            {
                var now = Environment.TickCount64;
                var recoveryIntervalMs = Math.Max(1, _configuration.EngineRecoveryCheckMinutes) * 60_000L;
                if (now - _lastLocalAiCheckMs >= recoveryIntervalMs)
                {
                    _lastLocalAiCheckMs = now;
                    _ = CheckLocalAiRecoveryAsync();
                }
            }

            // Periodic translation-cache flush (no-op when clean)
            var nowMs = Environment.TickCount64;
            if (nowMs - _lastCacheSaveMs >= 30_000)
            {
                _lastCacheSaveMs = nowMs;
                _translationCache.Save();
            }
        }

        private long _lastLocalAiCheckMs = 0;
        private long _lastCacheSaveMs = 0;
        private int _localAiRecoveryCheckInProgress;

        private async Task CheckLocalAiRecoveryAsync()
        {
            if (Interlocked.Exchange(ref _localAiRecoveryCheckInProgress, 1) != 0)
                return;

            try
            {
                var available = await _localAiTranslator.IsAvailableAsync(_shutdownCts.Token);
                if (available)
                {
                    _configuration.SelectedEngine = TranslationEngine.LocalAI;
                    _configuration.LocalAiUnavailable = false;
                    _configuration.Save();
                    UpdateTranslationService();
                    PluginLog.Info("Local AI server is back online. Restoring Local AI engine.");
                    _ = Framework.RunOnFrameworkThread(() =>
                    {
                        ChatGui.Print(GetResourceText("Command_LocalAiRestored", "Local AI server is back online. Restored Local AI engine."));
                    });
                }
            }
            catch (Exception ex)
            {
                if (_shutdownCts.IsCancellationRequested)
                    return;
                PluginLog.Error(ex, "Error while checking Local AI recovery.");
            }
            finally
            {
                Volatile.Write(ref _localAiRecoveryCheckInProgress, 0);
            }
        }

        /// <summary>
        /// Returns the cached chat visibility state.
        /// </summary>
        public static bool IsChatVisible() => _chatVisible;

        /// <summary>
        /// Chat visibility logic (main thread only)
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
