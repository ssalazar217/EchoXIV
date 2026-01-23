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
        [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
        [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
        
        private Configuration _configuration = null!;
        private GoogleTranslatorService _translatorService = null!;
        private WindowSystem _windowSystem = null!;
        private ConfigWindow? _configWindow = null;
        private Integrations.Chat2Integration? _chat2Integration = null;
        
        public Plugin()
        {
            try
            {
                // Cargar configuración
                _configuration = LoadConfiguration();
                
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
                    ChatGui
                );
                _chat2Integration.Enable();
                
                if (_chat2Integration.IsChat2Installed && _configuration.PreferChat2Integration)
                {
                    // Modo: Integración con Chat2
                    PluginLog.Info("🔗 Modo: Integración con Chat2");
                    
                    // Integrar selector de idioma en context menu de Chat2
                    var languageSelectorIntegration = new Integrations.LanguageSelectorIntegration(
                        _configuration,
                        PluginInterface,
                        PluginLog,
                        OnConfigurationChanged
                    );
                    languageSelectorIntegration.Enable();
                    
                    // Crear ventana de configuración
                    _configWindow = new ConfigWindow(_configuration, _chat2Integration);
                    _windowSystem.AddWindow(_configWindow);
                }
                else
                {
                    // Modo fallback (sin Chat2) - para futuro
                    PluginLog.Warning("⚠️ Chat2 no detectado - Funcionalidad limitada");
                }
                
                // Registrar WindowSystem
                PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
                PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
                
                // Registrar comandos
                CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
                {
                    HelpMessage = "Abre la configuración del traductor de chat.\n" +
                                  "/translate on → Activa la traducción\n" +
                                  "/translate off → Desactiva la traducción\n" +
                                  "/translate config → Abre configuración"
                });
                
                CommandManager.AddHandler(CommandNameShort, new CommandInfo(OnCommand)
                {
                    HelpMessage = "Atajo para /translate"
                });
                
                
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
            // Detener integración
            _chat2Integration?.Dispose();
            
            // Limpiar ventanas
            _windowSystem?.RemoveAllWindows();
            _configWindow?.Dispose();
            
            // Limpiar comandos
            CommandManager.RemoveHandler(CommandName);
            CommandManager.RemoveHandler(CommandNameShort);
            
            // Limpiar eventos
            PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUI;
        }
        
        private void OnCommand(string command, string args)
        {
            var lowerArgs = args.ToLowerInvariant().Trim();
            
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
                
                default:
                    ToggleConfigUI();
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
    }
}
