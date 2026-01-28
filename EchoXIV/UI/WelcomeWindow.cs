using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using EchoXIV.Properties;

namespace EchoXIV.UI;

public class WelcomeWindow : Window
{
    private Configuration _configuration;
    public event Action? OnConfigurationComplete;

    public WelcomeWindow(Configuration configuration) 
        : base($"{Resources.PluginName} - {Resources.WelcomeWindow_Title}###WelcomeWindow")
    {
        _configuration = configuration;
        
        Size = new Vector2(450, 400);
        Flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar;
    }

    public override void Draw()
    {
        ImGui.TextWrapped(Resources.Welcome_Intro);
        
        ImGui.Separator();
        ImGui.Spacing();

        // Selector de idioma de origen (Lo que escribes)
        ImGui.Text(Resources.Welcome_SourceQuestion);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.Welcome_SourceTip);
        ImGui.SetNextItemWidth(-1);
        
        var languages = new[] { "es", "en", "ja", "fr", "de", "pt", "ko", "zh-CN", "zh-TW", "ru", "it" };
        var languageNames = new[] { 
            Resources.Lang_ES, Resources.Lang_EN, Resources.Lang_JA, Resources.Lang_FR, 
            Resources.Lang_DE, Resources.Lang_PT, Resources.Lang_KO, Resources.Lang_ZH_Simp, 
            Resources.Lang_ZH_Trad, Resources.Lang_RU, Resources.Lang_IT 
        };
        
        var currentSourceIdx = Array.IndexOf(languages, _configuration.SourceLanguage);
        if (currentSourceIdx == -1) currentSourceIdx = 0;
        
        if (ImGui.Combo("##WelcomeSourceLang", ref currentSourceIdx, languageNames, languageNames.Length))
        {
            _configuration.SourceLanguage = languages[currentSourceIdx];
        }
        
        ImGui.Spacing();

        // Selector de idioma de destino (A qué traduces lo que escribes)
        ImGui.Text(Resources.Welcome_TargetQuestion);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.Welcome_TargetTip);
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
        ImGui.Text(Resources.Welcome_ReadQuestion);
        ImGui.SetNextItemWidth(-1);

        var incomingLangs = new[] { "", "es", "en", "ja", "fr", "de", "pt", "ko", "zh-CN", "zh-TW", "ru", "it" };
        var incomingLangNames = new[] { 
            Resources.Incoming_UseWritingLanguage, Resources.Lang_ES, Resources.Lang_EN, Resources.Lang_JA, 
            Resources.Lang_FR, Resources.Lang_DE, Resources.Lang_PT, Resources.Lang_KO, 
            Resources.Lang_ZH_Simp, Resources.Lang_ZH_Trad, Resources.Lang_RU, Resources.Lang_IT 
        };

        var currentIncIdx = Array.IndexOf(incomingLangs, _configuration.IncomingTargetLanguage);
        if (currentIncIdx == -1) currentIncIdx = 0;

        if (ImGui.Combo("##WelcomeIncLang", ref currentIncIdx, incomingLangNames, incomingLangNames.Length))
        {
            _configuration.IncomingTargetLanguage = incomingLangs[currentIncIdx];
        }

        ImGui.Dummy(new Vector2(0, 20));
        
        if (ImGui.Button(Resources.Welcome_SaveStart, new Vector2(-1, 40)))
        {
            _configuration.FirstRun = false;
            _configuration.Save();
            IsOpen = false;
            OnConfigurationComplete?.Invoke();
        }
    }
}
