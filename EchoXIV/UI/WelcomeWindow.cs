using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using EchoXIV.Resources;

namespace EchoXIV.UI;

public class WelcomeWindow : Window
{
    private Configuration _configuration;
    public event Action? OnConfigurationComplete;

    public WelcomeWindow(Configuration configuration) 
        : base("EchoXIV - Bienvenido / Welcome###WelcomeWindow")
    {
        _configuration = configuration;
        
        Size = new Vector2(450, 400);
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar;
    }

    public override void Draw()
    {
        ImGui.TextWrapped("¡Gracias por instalar EchoXIV! Por favor, configura tus idiomas iniciales para comenzar.");
        ImGui.TextWrapped("Thank you for installing EchoXIV! Please set your initial languages to get started.");
        
        ImGui.Separator();
        ImGui.Spacing();

        // Selector de idioma de origen (Lo que escribes)
        ImGui.Text("¿Qué idioma hablas?");
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "(Idioma que usarás para escribir en el chat)");
        ImGui.SetNextItemWidth(-1);
        
        var languages = new[] { "es", "en", "ja", "fr", "de", "pt", "ko", "zh-CN", "zh-TW", "ru", "it" };
        var languageNames = new[] { "Español", "English", "Japanese", "Français", "Deutsch", "Português", "Korean", "Chinese (Simp)", "Chinese (Trad)", "Russian", "Italiano" };
        
        var currentSourceIdx = Array.IndexOf(languages, _configuration.SourceLanguage);
        if (currentSourceIdx == -1) currentSourceIdx = 0;
        
        if (ImGui.Combo("##WelcomeSourceLang", ref currentSourceIdx, languageNames, languageNames.Length))
        {
            _configuration.SourceLanguage = languages[currentSourceIdx];
        }
        
        ImGui.Spacing();

        // Selector de idioma de destino (A qué traduces lo que escribes)
        ImGui.Text("¿A qué idioma quieres traducir tus mensajes?");
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "(Lo que verán los demás usuarios)");
        ImGui.SetNextItemWidth(-1);
        
        var currentTargetIdx = Array.IndexOf(languages, _configuration.TargetLanguage);
        if (currentTargetIdx == -1) currentTargetIdx = 1;
        
        if (ImGui.Combo("##WelcomeTargetLang", ref currentTargetIdx, languageNames, languageNames.Length))
        {
            _configuration.TargetLanguage = languages[currentTargetIdx];
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Idioma para los demás (Entrantes)
        ImGui.Text("¿En qué idioma quieres leer a los demás?");
        ImGui.SetNextItemWidth(-1);

        var incomingLangs = new[] { "", "es", "en", "ja", "fr", "de", "pt", "ko", "zh-CN", "zh-TW", "ru", "it" };
        var incomingLangNames = new[] { "Usar mi idioma de escritura", "Español", "English", "日本語", "Français", "Deutsch", "Português", "한국어", "简体中文", "繁體中文", "Русский", "Italiano" };

        var currentIncIdx = Array.IndexOf(incomingLangs, _configuration.IncomingTargetLanguage);
        if (currentIncIdx == -1) currentIncIdx = 0;

        if (ImGui.Combo("##WelcomeIncLang", ref currentIncIdx, incomingLangNames, incomingLangNames.Length))
        {
            _configuration.IncomingTargetLanguage = incomingLangs[currentIncIdx];
        }

        ImGui.Dummy(new Vector2(0, 20));
        
        if (ImGui.Button("Guardar y Comenzar / Save and Start", new Vector2(-1, 40)))
        {
            _configuration.FirstRun = false;
            _configuration.Save();
            IsOpen = false;
            OnConfigurationComplete?.Invoke();
        }
    }
}
