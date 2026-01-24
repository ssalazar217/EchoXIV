using System;
using System.Numerics;
using System.Linq;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace FFXIVChatTranslator.UI;

public class ConfigWindow : Window, IDisposable
{
    private Configuration _configuration;
    private Integrations.Chat2Integration _chat2Integration;
    
    private string _newExcludedMessage = string.Empty;
    
    public ConfigWindow(Configuration configuration, Integrations.Chat2Integration chat2Integration) 
        : base("Chat2 Translator - Configuración###ConfigWindow")
    {
        _configuration = configuration;
        _chat2Integration = chat2Integration;
        
        Size = new Vector2(600, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
        
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("ConfigTabs"))
        {
            if (ImGui.BeginTabItem("⚙️ General"))
            {
                DrawGeneralTab();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("🚫 Mensajes Excluidos"))
            {
                DrawExcludedMessagesTab();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("📦 Caché"))
            {
                DrawCacheTab();
                ImGui.EndTabItem();
            }
            
            ImGui.EndTabBar();
        }
    }
    
    private void DrawGeneralTab()
    {
        ImGui.TextWrapped("Configuración general del traductor");
        ImGui.Separator();
        ImGui.Spacing();
        
        // Toggle de traducción
        var enabled = _configuration.TranslationEnabled;
        if (ImGui.Checkbox("✅ Activar traducción automática", ref enabled))
        {
            _configuration.TranslationEnabled = enabled;
            _configuration.Save();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Selector de idioma de origen
        ImGui.Text("📤 Idioma de origen:");
        ImGui.SetNextItemWidth(200);
        
        var languages = new[] { "es", "en", "ja", "fr", "de", "pt", "ko", "zh-CN", "zh-TW", "ru", "it" };
        var languageNames = new[] { "Español", "English", "日本語", "Français", "Deutsch", "Português", "한국어", "中文 (简)", "中文 (繁)", "Русский", "Italiano" };
        
        var currentSourceIdx = Array.IndexOf(languages, _configuration.SourceLanguage);
        if (currentSourceIdx == -1) currentSourceIdx = 0;
        
        if (ImGui.Combo("##SourceLang", ref currentSourceIdx, languageNames, languageNames.Length))
        {
            _configuration.SourceLanguage = languages[currentSourceIdx];
            _configuration.Save();
        }
        
        ImGui.Spacing();
        
        // Selector de idioma de destino
        ImGui.Text("📥 Idioma de destino:");
        ImGui.SetNextItemWidth(200);
        
        var currentTargetIdx = Array.IndexOf(languages, _configuration.TargetLanguage);
        if (currentTargetIdx == -1) currentTargetIdx = 1;
        
        if (ImGui.Combo("##TargetLang", ref currentTargetIdx, languageNames, languageNames.Length))
        {
            _configuration.TargetLanguage = languages[currentTargetIdx];
            _configuration.Save();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.TextWrapped("💡 Los cambios se aplican inmediatamente.");
    }
    
    private void DrawExcludedMessagesTab()
    {
        ImGui.TextWrapped("Mensajes que NO se traducirán (expresiones universales, emoticonos, etc.)");
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.Text("➕ Agregar mensaje:");
        ImGui.SetNextItemWidth(-100);
        if (ImGui.InputText("##NewExcluded", ref _newExcludedMessage, 100, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (!string.IsNullOrWhiteSpace(_newExcludedMessage))
            {
                _configuration.ExcludedMessages.Add(_newExcludedMessage.Trim());
                _configuration.Save();
                _newExcludedMessage = string.Empty;
            }
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Agregar##AddExcluded"))
        {
            if (!string.IsNullOrWhiteSpace(_newExcludedMessage))
            {
                _configuration.ExcludedMessages.Add(_newExcludedMessage.Trim());
                _configuration.Save();
                _newExcludedMessage = string.Empty;
            }
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.Text($"📋 Mensajes excluidos ({_configuration.ExcludedMessages.Count}):");
        
        ImGui.BeginChild("ExcludedList", new Vector2(0, 300), true);
        
        string? toRemove = null;
        foreach (var msg in _configuration.ExcludedMessages.OrderBy(x => x))
        {
            ImGui.Text($"⏭️ {msg}");
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 80);
            if (ImGui.SmallButton($"❌ Eliminar##{msg}"))
            {
                toRemove = msg;
            }
        }
        
        if (toRemove != null)
        {
            _configuration.ExcludedMessages.Remove(toRemove);
            _configuration.Save();
        }
        
        ImGui.EndChild();
        
        ImGui.Spacing();
        
        if (ImGui.Button("🔄 Restaurar lista por defecto"))
        {
            _configuration.ExcludedMessages = new()
            {
                "lol", "LOL",
                "o/", "o7", 
                "uwu", "UwU", 
                "gg", "GG",
                "ty", "TY", "thx", "THX",
                "xd", "XD", "xD",
                "omg", "OMG",
                "wtf", "WTF",
                "afk", "AFK",
                "brb", "BRB",
                "gn", "GN",
                "gm", "GM",
                "\\o/", "\\(^o^)/", "^_^", "^^",
                ":)", ":(", ":D", ";)",
                "<3"
            };
            _configuration.Save();
        }
    }
    
    private void DrawCacheTab()
    {
        ImGui.TextWrapped("Configuración del sistema de caché de traducciones");
        ImGui.Separator();
        ImGui.Spacing();
        
        var cacheEnabled = _configuration.CacheEnabled;
        if (ImGui.Checkbox("✅ Habilitar caché de traducciones", ref cacheEnabled))
        {
            _configuration.CacheEnabled = cacheEnabled;
            _configuration.Save();
        }
        
        if (!cacheEnabled)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0f, 1f), "⚠️ Caché deshabilitado - todas las traducciones serán nuevas");
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.Text("📏 Límite de caracteres para cachear:");
        ImGui.TextWrapped("Mensajes más largos que este límite NO se guardarán en caché (útil para mensajes con contexto).");
        
        var maxLength = _configuration.CacheMaxMessageLength;
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderInt("##CacheMaxLength", ref maxLength, 5, 50, $"{maxLength} caracteres"))
        {
            _configuration.CacheMaxMessageLength = maxLength;
            _configuration.Save();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        var cacheSize = _chat2Integration.GetCacheSize();
        ImGui.Text($"📊 Estadísticas:");
        ImGui.BulletText($"Traducciones en caché: {cacheSize}");
        ImGui.BulletText($"Límite máximo: 1000 entradas");
        
        if (cacheSize > 0)
        {
            var percentage = (cacheSize / 1000.0f) * 100f;
            ImGui.ProgressBar(percentage / 100f, new Vector2(-1, 0), $"{percentage:F1}% utilizado");
        }
        
        ImGui.Spacing();
        ImGui.Spacing();
        
        if (ImGui.Button("🗑️ Limpiar caché completamente", new Vector2(-1, 30)))
        {
            _chat2Integration.ClearCache();
        }
        
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Elimina todas las traducciones guardadas.\nÚtil si cambiaste el idioma destino o quieres refrescar traducciones.");
        }
        
        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), "💡 Tip: El caché se limpia automáticamente cuando está lleno.");
    }
}
