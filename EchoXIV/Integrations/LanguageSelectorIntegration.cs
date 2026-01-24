using System;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using EchoXIV.Models;
using Dalamud.Bindings.ImGui;

namespace EchoXIV.Integrations
{
    /// <summary>
    /// Integra un selector de idioma en el menú contextual de Chat2
    /// </summary>
    public class LanguageSelectorIntegration : IDisposable
    {
        private readonly Configuration _configuration;
        private readonly IDalamudPluginInterface _pluginInterface;
        private readonly IPluginLog _pluginLog;
        private readonly Action _onLanguageChanged;
        
        // IPC para context menu de Chat2
        private ICallGateSubscriber<string>? _registerGate;
        private ICallGateSubscriber<string, object?>? _unregisterGate;
        private ICallGateSubscriber<object?>? _availableGate;
        private ICallGateSubscriber<string, PlayerPayload?, ulong, Payload?, SeString?, SeString?, object?>? _invokeGate;
        
        private string? _registrationId;
        
        public LanguageSelectorIntegration(
            Configuration configuration,
            IDalamudPluginInterface pluginInterface,
            IPluginLog pluginLog,
            Action onLanguageChanged)
        {
            _configuration = configuration;
            _pluginInterface = pluginInterface;
            _pluginLog = pluginLog;
            _onLanguageChanged = onLanguageChanged;
        }
        
        /// <summary>
        /// Habilita la integración con el context menu de Chat2
        /// </summary>
        public void Enable()
        {
            try
            {
                // Inicializar IPC gates
                _registerGate = _pluginInterface.GetIpcSubscriber<string>("ChatTwo.Register");
                _unregisterGate = _pluginInterface.GetIpcSubscriber<string, object?>("ChatTwo.Unregister");
                _availableGate = _pluginInterface.GetIpcSubscriber<object?>("ChatTwo.Available");
                _invokeGate = _pluginInterface.GetIpcSubscriber<string, PlayerPayload?, ulong, Payload?, SeString?, SeString?, object?>("ChatTwo.Invoke");
                
                // Registrar con Chat2
                Register();
                
                // Re-registrar cuando Chat2 se actualice
                _availableGate?.Subscribe(Register);
                
                // Suscribirse a eventos del context menu
                _invokeGate?.Subscribe(DrawLanguageSelector);
                
                _pluginLog.Info("✅ Selector de idioma integrado en Chat2 context menu");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "❌ Error al integrar selector de idioma con Chat2");
            }
        }
        
        private void Register()
        {
            try
            {
                _registrationId = _registerGate?.InvokeFunc();
                _pluginLog.Info($"📝 Registrado en Chat2 context menu: {_registrationId}");
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "❌ Error al registrarse en Chat2");
            }
        }
        
        /// <summary>
        /// Dibuja el selector de idioma en el context menu
        /// </summary>
        private void DrawLanguageSelector(
            string id,
            PlayerPayload? sender,
            ulong contentId,
            Payload? payload,
            SeString? senderString,
            SeString? content)
        {
            // Verificar que el ID coincida
            if (id != _registrationId)
                return;
            
            // Dibujar menú de traducción
            if (ImGui.BeginMenu("🌐 Traducir a..."))
            {
                ImGui.TextDisabled($"Idioma origen: {GetLanguageName(_configuration.SourceLanguage)}");
                ImGui.Separator();
                
                var languages = LanguageProvider.GetLanguages();
                foreach (var lang in languages)
                {
                    // Marcar el idioma actualmente seleccionado
                    bool isSelected = _configuration.TargetLanguage.Equals(lang.Code, StringComparison.OrdinalIgnoreCase);
                    
                    if (ImGui.MenuItem($"{lang.Flag} {lang.Name}", "", isSelected))
                    {
                        _configuration.TargetLanguage = lang.Code;
                        _onLanguageChanged?.Invoke();
                        _pluginLog.Info($"🌍 Idioma destino cambiado a: {lang.Name}");
                    }
                }
                
                ImGui.Separator();
                
                // Toggle de traducción
                var enabled = _configuration.TranslationEnabled;
                if (ImGui.MenuItem("✓ Traducción Habilitada", "", ref enabled))
                {
                    _configuration.TranslationEnabled = enabled;
                    _onLanguageChanged?.Invoke();
                    _pluginLog.Info($"🔄 Traducción: {(enabled ? "ON" : "OFF")}");
                }
                
                ImGui.EndMenu();
            }
        }
        
        private string GetLanguageName(string code)
        {
            var lang = LanguageProvider.GetLanguage(code);
            return lang != null ? $"{lang.Flag} {lang.Name}" : code.ToUpper();
        }
        
        public void Dispose()
        {
            if (_registrationId != null)
            {
                try
                {
                    _unregisterGate?.InvokeAction(_registrationId);
                    _pluginLog.Info("📝 Desregistrado de Chat2 context menu");
                }
                catch (Exception ex)
                {
                    _pluginLog.Error(ex, "Error al desregistrarse de Chat2");
                }
                
                _registrationId = null;
            }
            
            _invokeGate?.Unsubscribe(DrawLanguageSelector);
            _availableGate?.Unsubscribe(Register);
        }
    }
}
