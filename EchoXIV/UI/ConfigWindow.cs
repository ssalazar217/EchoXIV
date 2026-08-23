using System;
using System.Numerics;
using System.Linq;
using System.Text.Json;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using EchoXIV.Properties;
using Dalamud.Interface;
using EchoXIV.Services;

namespace EchoXIV.UI;

public class ConfigWindow : Window, IDisposable
{
    private Configuration _configuration;
    private string _lastSavedFingerprint;
    private MessageHistoryManager _historyManager;
    private string _historyFilter = string.Empty;
    
            // Real-time update callback
    public Action<TranslationEngine>? OnTranslationEngineChanged;
    public Action? OnUiLanguageChanged;
    
    private string _newExcludedMessage = string.Empty;
    
    private int _currentTab;

    public ConfigWindow(Configuration configuration, MessageHistoryManager historyManager) 
        : base($"{Resources.PluginName} - {Resources.ConfigWindow_Title}###ConfigWindow")
    {
        _configuration = configuration;
        _lastSavedFingerprint = GetFingerprint(_configuration);
        _historyManager = historyManager;
        
        Size = new Vector2(750, 550);
        SizeCondition = ImGuiCond.FirstUseEver;
        
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Sidebar layout: fixed left + stretch right (ChatTwo Settings.cs pattern)
        if (ImGui.BeginTable("##ConfigSidebarTable", 2, ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("##sidebar", ImGuiTableColumnFlags.WidthFixed, 150f);
            ImGui.TableSetupColumn("##content", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            // Left: sidebar
            ImGui.TableSetColumnIndex(0);
            using (var sidebarChild = ImRaii.Child("##ConfigSidebar", new Vector2(0, -1), false))
            {
                if (sidebarChild)
                {
                    var tabNames = new[]
                    {
                        Resources.Tab_General,
                        Resources.Tab_Visuals,
                        Resources.Tab_ExcludedMessages,
                        Resources.Tab_IncomingChannels,
                        Resources.Tab_History,
                        Resources.Tab_About
                    };
                    for (int i = 0; i < tabNames.Length; i++)
                    {
                        if (ImGui.Selectable(tabNames[i], _currentTab == i))
                            _currentTab = i;
                    }
                }
            }

            // Right: content
            ImGui.TableSetColumnIndex(1);
            using (var contentChild = ImRaii.Child("##ConfigContent", new Vector2(0, -1), false))
            {
                if (contentChild)
                {
                    switch (_currentTab)
                    {
                        case 0: DrawGeneralTab(); break;
                        case 1: DrawVisualsTab(); break;
                        case 2: DrawExcludedMessagesTab(); break;
                        case 3: DrawIncomingChannelsTab(); break;
                        case 4: DrawHistoryTab(); break;
                        case 5: DrawAboutTab(); break;
                    }
                }
            }

            ImGui.EndTable();
        }

        PersistLiveChanges();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.General_ChangesAppliedImmediately);
    }

    private void PersistLiveChanges()
    {
        var fingerprint = GetFingerprint(_configuration);
        if (fingerprint == _lastSavedFingerprint)
            return;

        try
        {
            _configuration.Save();
            // Only acknowledge the fingerprint after SavePluginConfig succeeds.
            _lastSavedFingerprint = fingerprint;
        }
        catch (Exception ex)
        {
            Plugin.PluginLog.Error(ex, "Failed to persist EchoXIV configuration. It will be retried.");
        }
    }

    private static string GetFingerprint(Configuration configuration) =>
        JsonSerializer.Serialize(configuration);

    private void DrawAboutTab()
    {
        using var child = ImRaii.Child("AboutScroll");
        if (!child) return;

        // Title - centered and colored
        var title = Resources.About_Title;
        var titleSize = ImGui.CalcTextSize(title);
        var availX = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX((availX - titleSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.9f, 0.7f, 0.2f, 1f), title);

        ImGui.Spacing();

        // Description
        ImGui.TextWrapped(Resources.About_Description);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Version and Author
        var version = typeof(ConfigWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.2.1";
        ImGui.Text(string.Format(Resources.About_Version, version));
        ImGui.Text(Resources.About_Author);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Discord
        ImGui.TextColored(new Vector4(0.4f, 0.6f, 1f, 1f), Resources.About_Discord);
        ImGui.TextWrapped(Resources.About_DiscordDesc);
        ImGui.Spacing();
        if (ImGui.Button($"{Resources.About_OpenDiscord}##OpenDiscord"))
        {
            Dalamud.Utility.Util.OpenLink("https://discord.gg/B3qnvuhN9G");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("https://discord.gg/B3qnvuhN9G");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // GitHub
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.About_GitHub);
        ImGui.TextWrapped(Resources.About_GitHubDesc);
        ImGui.Spacing();
        if (ImGui.Button($"{Resources.About_OpenGitHub}##OpenGitHub"))
        {
            Dalamud.Utility.Util.OpenLink("https://github.com/ssalazar217/EchoXIV");
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("https://github.com/ssalazar217/EchoXIV");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Disclaimer
        ImGui.TextWrapped(Resources.About_Disclaimer);
    }
    
    private void DrawGeneralTab()
    {
        using var child = ImRaii.Child("GeneralScroll");
        if (child)
        {
            ImGui.TextWrapped(Resources.General_Description);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            DrawInterfaceLanguageSelector();

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text(Resources.General_Engine);
            ImGui.SetNextItemWidth(200);
            
            var engines = Enum.GetValues<TranslationEngine>();
            var engineNames = new[]
            {
                Resources.General_EngineNameGoogle,        // Google
                Resources.General_EngineNamePapago,        // Papago
                Resources.General_EngineNameLibreTranslate, // LibreTranslate
                Resources.General_EngineNameYandex,        // Yandex
                "LocalAI",                                 // Local AI
            };
            var currentEngineIdx = Array.IndexOf(engines, _configuration.SelectedEngine);
            
            if (ImGui.Combo("##Engine", ref currentEngineIdx, engineNames, engineNames.Length))
            {
                var newEngine = engines[currentEngineIdx];
                if (_configuration.SelectedEngine != newEngine)
                {
                    _configuration.SelectedEngine = newEngine;
                    _configuration.Save();
                    OnTranslationEngineChanged?.Invoke(newEngine);
                }
            }

            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), 
                _configuration.SelectedEngine == TranslationEngine.Google
                ? Resources.General_EngineGoogleDesc 
                : _configuration.SelectedEngine == TranslationEngine.LocalAI
                ? Resources.General_EngineLocalAIDesc
                : _configuration.SelectedEngine == TranslationEngine.LibreTranslate
                ? Resources.General_EngineLibreTranslateDesc
                : _configuration.SelectedEngine == TranslationEngine.Yandex
                ? Resources.General_EngineYandexDesc
                : Resources.General_EnginePapagoDesc);

            ImGui.Spacing();

            // Fallback engine
            ImGui.Text(Resources.General_EngineFallback);
            ImGui.SetNextItemWidth(200);

            var fallbackOptions = new TranslationEngine?[] { null, TranslationEngine.Google, TranslationEngine.Papago, TranslationEngine.LibreTranslate, TranslationEngine.Yandex, TranslationEngine.LocalAI };
            var fallbackNames = new[]
            {
                Resources.General_EngineFallbackAuto,  // null -> auto
                Resources.General_EngineNameGoogle,
                Resources.General_EngineNamePapago,
                Resources.General_EngineNameLibreTranslate,
                Resources.General_EngineNameYandex,
                "LocalAI",
            };
            var currentFallbackIdx = Array.IndexOf(fallbackOptions, _configuration.FallbackEngine);
            if (currentFallbackIdx == -1) currentFallbackIdx = 0;

            if (ImGui.Combo("##FallbackEngine", ref currentFallbackIdx, fallbackNames, fallbackNames.Length))
            {
                var newFallback = fallbackOptions[currentFallbackIdx];
                if (_configuration.FallbackEngine != newFallback)
                {
                    _configuration.FallbackEngine = newFallback;
                    _configuration.Save();
                    OnTranslationEngineChanged?.Invoke(_configuration.SelectedEngine);
                }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.General_EngineFallbackTip);

            // Primary recheck interval (minutes)
            var recoveryMinutes = _configuration.EngineRecoveryCheckMinutes;
            ImGui.Text(Resources.General_EngineRecoveryMinutes);
            ImGui.SetNextItemWidth(200);
            if (ImGui.SliderInt("##EngineRecoveryMinutes", ref recoveryMinutes, 1, 60))
            {
                _configuration.EngineRecoveryCheckMinutes = recoveryMinutes;
                _configuration.Save();
                OnTranslationEngineChanged?.Invoke(_configuration.SelectedEngine);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.General_EngineRecoveryMinutesTip);

            if (_configuration.SelectedEngine == TranslationEngine.LocalAI)
            {
                DrawLocalAiSettings();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            // Source language selector
            ImGui.Text(Resources.General_SourceLanguage);
            ImGui.SetNextItemWidth(200);
            
            var languages = new[] { "es", "en", "ja", "fr", "de", "it", "ko", "no", "pt", "ru", "zh-CN", "zh-TW" };
            var languageNames = new[] { 
                Resources.Lang_ES, Resources.Lang_EN, Resources.Lang_JA, Resources.Lang_FR, 
                Resources.Lang_DE, Resources.Lang_IT, Resources.Lang_KO, Resources.Lang_NO,
                Resources.Lang_PT, Resources.Lang_RU, Resources.Lang_ZH_Simp, Resources.Lang_ZH_Trad
            };
            
            var currentSourceIdx = Array.IndexOf(languages, _configuration.SourceLanguage);
            if (currentSourceIdx == -1) currentSourceIdx = 0;
            
            if (ImGui.Combo("##SourceLang", ref currentSourceIdx, languageNames, languageNames.Length))
            {
                _configuration.SourceLanguage = languages[currentSourceIdx];
            }
            
            ImGui.Spacing();
            
            // Target language selector
            ImGui.Text(Resources.General_TargetLanguage);
            ImGui.SetNextItemWidth(200);
            
            var currentTargetIdx = Array.IndexOf(languages, _configuration.TargetLanguage);
            if (currentTargetIdx == -1) currentTargetIdx = 1;
            
            if (ImGui.Combo("##TargetLang", ref currentTargetIdx, languageNames, languageNames.Length))
            {
                _configuration.TargetLanguage = languages[currentTargetIdx];
            }

            // Verbose logging option
            var verboseLogs = _configuration.VerboseLogging;
            if (ImGui.Checkbox(Resources.General_VerboseLogs, ref verboseLogs))
            {
                _configuration.VerboseLogging = verboseLogs;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.General_VerboseLogsTip);
        }
    }

    /// <summary>
    /// UI language selector. Empty follows Dalamud.
    /// </summary>
    private void DrawInterfaceLanguageSelector()
    {
        ImGui.Text(Resources.General_InterfaceLanguage);
        ImGui.SetNextItemWidth(200);

        // Options: empty (follow Dalamud) plus supported languages.
        var codes = new[] { "", "en", "es", "de", "fr", "ja", "it", "ko", "no", "ru", "pt-BR", "tr", "pl", "uk", "cs", "nl", "zh-CN", "zh-TW" };
        var names = new[]
        {
            Resources.General_InterfaceLanguageDefault,
            Resources.Lang_EN, Resources.Lang_ES, Resources.Lang_DE, Resources.Lang_FR, Resources.Lang_JA,
            Resources.Lang_IT, Resources.Lang_KO, Resources.Lang_NO, Resources.Lang_RU, Resources.Lang_PT,
            Resources.Lang_TR, Resources.Lang_PL, Resources.Lang_UK, Resources.Lang_CS, Resources.Lang_NL,
            Resources.Lang_ZH_Simp, Resources.Lang_ZH_Trad
        };

        var currentIdx = Array.IndexOf(codes, _configuration.UiLanguage);
        if (currentIdx == -1) currentIdx = 0;

        if (ImGui.Combo("##UiLanguage", ref currentIdx, names, names.Length))
        {
            _configuration.UiLanguage = codes[currentIdx];
            _configuration.Save();
            OnUiLanguageChanged?.Invoke();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.General_InterfaceLanguageTip);
    }

    private void DrawExcludedMessagesTab()
    {
        using var child = ImRaii.Child("ExcludedScroll");
        if (child)
        {
            ImGui.TextWrapped(Resources.Excluded_Description);
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.Text(Resources.Excluded_InputLabel);
            ImGui.SetNextItemWidth(-100);
            if (ImGui.InputText("##NewExcluded", ref _newExcludedMessage, 100, ImGuiInputTextFlags.EnterReturnsTrue))
            {
                var trimmed = _newExcludedMessage.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    if (!_configuration.ExcludedMessages.Contains(trimmed))
                    {
                        _configuration.ExcludedMessages.Add(trimmed);
                        _newExcludedMessage = string.Empty;
                    }
                }
            }
            
            ImGui.SameLine();
            if (ImGui.Button(Resources.Excluded_Add + "##AddExcluded"))
            {
                var trimmed = _newExcludedMessage.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    if (!_configuration.ExcludedMessages.Contains(trimmed))
                    {
                        _configuration.ExcludedMessages.Add(trimmed);
                        _newExcludedMessage = string.Empty;
                    }
                }
            }
            
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            ImGui.Text($"{Resources.Excluded_CurrentList} ({_configuration.ExcludedMessages.Count}):");
            
            using (var innerChild = ImRaii.Child("ExcludedList", new Vector2(0, 200), true))
            {
                if (innerChild)
                {
                    string? toRemove = null;
            foreach (var msg in _configuration.ExcludedMessages.OrderBy(x => x))
            {
                ImGui.Text(msg);
                ImGui.SameLine();
                ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 80);
                if (ImGui.SmallButton($"{Resources.Excluded_Remove}##{msg}"))
                {
                    toRemove = msg;
                }
            }
                if (toRemove != null)
                {
                    _configuration.ExcludedMessages.Remove(toRemove);
                }
            }
        }
            
            ImGui.Spacing();
            
            if (ImGui.Button(Resources.Excluded_RestoreDefault))
            {
                _configuration.ExcludedMessages = new(StringComparer.OrdinalIgnoreCase)
                {
                    "lol", "o/", "o7", "uwu", "gg", "ty", "thx", "xd", "omg", "wtf", 
                    "afk", "brb", "gn", "gm", "\\o/", "\\(^o^)/", "^_^", "^^", 
                    ":)", ":(", ":D", ";)", "<3", 
                    "P1", "P2", "P3", "P4", "dc", "PLD", "MCH", "DNC",
                    "m1s", "m2s", "m3s", "m4s", "m5s", "m6s", "m7s", "m8s", "m9s", "m10s", "m11s", "m12s",
                    "PF", "ilvl", "lb", "mb", "tyfp", "c:"
                };
            }
        }
    }
    



    private void DrawVisualsTab()
    {
        using var child = ImRaii.Child("VisualsScroll");
        if (child)
        {
            ImGui.TextWrapped(Resources.Visuals_Description);
            ImGui.Separator();
            ImGui.Spacing();

            int fontSize = _configuration.FontSize;
            if (ImGui.SliderInt(Resources.Visuals_FontSize, ref fontSize, 10, 32))
            {
                _configuration.FontSize = fontSize;
            }
            
            int spacing = _configuration.ChatMessageSpacing;
            if (ImGui.SliderInt(Resources.Visuals_Spacing, ref spacing, 0, 20))
            {
                _configuration.ChatMessageSpacing = spacing;
            }

            ImGui.Spacing();
            
            ImGui.Text(Resources.Visuals_TimeFormat);
            string[] formats = { "HH:mm", "HH:mm:ss", "h:mm tt" };
            string[] formatNames = { Resources.Visuals_TimeFormatShort, Resources.Visuals_TimeFormatLong, Resources.Visuals_TimeFormatAMPM };
            
            int currentFmt = Array.IndexOf(formats, _configuration.TimestampFormat);
            if (currentFmt == -1) currentFmt = 0;
            
            if (ImGui.Combo("##TimestampFmt", ref currentFmt, formatNames, formatNames.Length))
            {
                _configuration.TimestampFormat = formats[currentFmt];
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            // HideWhenInactive (ChatTwo pattern)
            var hideInactive = _configuration.HideWhenInactive;
            if (ImGui.Checkbox("Hide when inactive", ref hideInactive))
            {
                _configuration.HideWhenInactive = hideInactive;
            }
            HelpText("Hides the translated window after inactivity.");
            if (hideInactive)
            {
                int timeout = _configuration.InactivityHideTimeout;
                if (ImGui.SliderInt("Inactivity timeout (seconds)", ref timeout, 10, 600))
                {
                    _configuration.InactivityHideTimeout = timeout;
                }
            }
            ImGui.Spacing();
            if (ImGui.CollapsingHeader("Extra Glyphs"))
            {
                int extra = (int)_configuration.ExtraGlyphs;
                bool changed = false;
                if (ImGui.CheckboxFlags("Cyrillic", ref extra, (int)Configuration.ExtraGlyph.Cyrillic)) changed = true;
                ImGui.SameLine();
                if (ImGui.CheckboxFlags("Greek", ref extra, (int)Configuration.ExtraGlyph.Greek)) changed = true;
                ImGui.SameLine();
                if (ImGui.CheckboxFlags("Vietnamese", ref extra, (int)Configuration.ExtraGlyph.Vietnamese)) changed = true;
                if (ImGui.CheckboxFlags("Thai", ref extra, (int)Configuration.ExtraGlyph.Thai)) changed = true;
                if (changed) _configuration.ExtraGlyphs = (Configuration.ExtraGlyph)extra;
                HelpText("Adds glyph ranges for better language support. Requires restart.");
            }
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            
            DrawChannelColors();
        }
    }
    
    private void DrawChannelColors()
    {
        ImGui.Text(Resources.Visuals_Colors);
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), Resources.Visuals_ChannelColorsDesc);
        
        ImGui.Spacing();

        var channels = new Dictionary<int, string>
        {
            { 10, Resources.Channel_Say },
            { 11, Resources.Channel_Shout },
            { 30, Resources.Channel_Yell },
            { 14, Resources.Channel_Party },
            { 15, Resources.Channel_Alliance },
            { 24, Resources.Channel_FC },
            { 13, Resources.Channel_Tell },
            { 27, Resources.Channel_NN },
            { 16, Resources.Channel_Linkshell }
        };

        foreach (var channel in channels)
        {
            if (!_configuration.ChannelColors.TryGetValue(channel.Key, out var colorValue))
                continue;

            var colorVector = ColorToVector4(colorValue);
            if (ImGui.ColorEdit4($"{channel.Value}##Color{channel.Key}", ref colorVector, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha))
            {
                _configuration.ChannelColors[channel.Key] = Vector4ToColor(colorVector);
            }
            if (channel.Key == 13 || channel.Key == 15 || channel.Key == 24) ImGui.SameLine();
        }
    }

    private static Vector4 ColorToVector4(uint color)
    {
        float a = ((color >> 24) & 0xFF) / 255f;
        float r = ((color >> 16) & 0xFF) / 255f;
        float g = ((color >> 8) & 0xFF) / 255f;
        float b = (color & 0xFF) / 255f;
        return new Vector4(r, g, b, a);
    }

    private static uint Vector4ToColor(Vector4 vector)
    {
        uint a = (uint)(vector.W * 255) << 24;
        uint r = (uint)(vector.X * 255) << 16;
        uint g = (uint)(vector.Y * 255) << 8;
        uint b = (uint)(vector.Z * 255);
        return a | r | g | b;
    }

    private void DrawHistoryTab()
    {
        ImGui.Text(Resources.History_Search);
        ImGui.SetNextItemWidth(-120);
        ImGui.InputText("##HistorySearch", ref _historyFilter, 100);
        
        ImGui.SameLine();
        if (ImGui.Button(Resources.History_Clear))
        {
            _historyManager.Clear();
        }

        ImGui.Separator();
        ImGui.Spacing();

        using var child = ImRaii.Child("HistoryList", new Vector2(0, -1), true);
        if (child)
        {
            var messages = _historyManager.GetHistory();
            if (!string.IsNullOrWhiteSpace(_historyFilter))
            {
                messages = messages.Where(m =>
                    m.OriginalText.Contains(_historyFilter, StringComparison.OrdinalIgnoreCase) ||
                    m.TranslatedText.Contains(_historyFilter, StringComparison.OrdinalIgnoreCase) ||
                    (m.RomanizedText?.Contains(_historyFilter, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    m.Sender.Contains(_historyFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (messages.Count == 0)
            {
                ImGui.TextWrapped(Resources.History_NoMessages);
            }
            else
            {
                foreach (var msg in messages.AsEnumerable().Reverse())
                {
                    var color = GetChannelColor(msg.ChatType);
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"[{msg.Timestamp.ToString(_configuration.TimestampFormat)}]");
                    ImGui.SameLine();
                    ImGui.TextColored(color, $"[{msg.ChatType}] {msg.Sender}:");
                    ImGui.TextWrapped(msg.TranslatedText);
                    if (!string.IsNullOrWhiteSpace(msg.RomanizedText))
                    {
                        ImGui.TextColored(new Vector4(0.55f, 0.75f, 0.9f, 1f), $"{Resources.ChatWindow_RomanizationLabel} {msg.RomanizedText}");
                    }
                    if (_configuration.ShowOriginalText)
                    {
                        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 0.8f), msg.OriginalText);
                    }
                    ImGui.Separator();
                }
            }
        }
    }

    private Vector4 GetChannelColor(XivChatType type)
    {
        int typeId = (int)type;
        if (type == XivChatType.TellIncoming || type == XivChatType.TellOutgoing) typeId = 13;
        if (typeId >= 16 && typeId <= 23) typeId = 16;
        if (typeId >= 101 && typeId <= 108) typeId = 16;

        if (_configuration.ChannelColors.TryGetValue(typeId, out var colorValue))
        {
            return ColorToVector4(colorValue);
        }
        return new Vector4(0.8f, 0.8f, 0.8f, 1f);
    }
    
    private void DrawIncomingChannelsTab()
    {
            // Scrollable region to avoid clipping
        using var child = ImRaii.Child("IncomingScroll");
        if (child)
        {
            ImGui.TextWrapped(Resources.Incoming_Description);
        ImGui.Separator();
        ImGui.Spacing();
        
        // Toggle general
        var incomingEnabled = _configuration.IncomingTranslationEnabled;
        if (ImGui.Checkbox(Resources.Incoming_Enable, ref incomingEnabled))
        {
            _configuration.IncomingTranslationEnabled = incomingEnabled;
        }
        
        if (!incomingEnabled)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0f, 1f), Resources.Incoming_Disabled);
            return;
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
            // Incoming target language selector
        ImGui.Text(Resources.Incoming_TranslateTo);
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), Resources.Incoming_AutoDetectNote);
        ImGui.SetNextItemWidth(200);
        
        var languages = new[] { "", "es", "en", "ja", "fr", "de", "it", "ko", "no", "pt", "ru", "zh-CN", "zh-TW" };
        var languageNames = new[] { 
            Resources.Incoming_UseWritingLanguage, Resources.Lang_ES, Resources.Lang_EN, Resources.Lang_JA, 
            Resources.Lang_FR, Resources.Lang_DE, Resources.Lang_IT, Resources.Lang_KO,
            Resources.Lang_NO, Resources.Lang_PT, Resources.Lang_RU, Resources.Lang_ZH_Simp,
            Resources.Lang_ZH_Trad
        };
        
        var currentIdx = Array.IndexOf(languages, _configuration.IncomingTargetLanguage);
        if (currentIdx == -1) currentIdx = 0;
        
        if (ImGui.Combo("##IncomingTargetLang", ref currentIdx, languageNames, languageNames.Length))
        {
            _configuration.IncomingTargetLanguage = languages[currentIdx];
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
            // Display options
        ImGui.Text(Resources.Incoming_DisplayOptions);
        
        var showOriginal = _configuration.ShowOriginalText;
        if (ImGui.Checkbox(Resources.Incoming_ShowOriginal, ref showOriginal))
        {
            _configuration.ShowOriginalText = showOriginal;
        }
        
        var showTimestamps = _configuration.ShowTimestamps;
        if (ImGui.Checkbox(Resources.Incoming_ShowTimestamps, ref showTimestamps))
        {
            _configuration.ShowTimestamps = showTimestamps;
        }
        
        var maxMessages = _configuration.MaxDisplayedMessages;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt(Resources.Incoming_MaxMessages, ref maxMessages))
        {
            _configuration.MaxDisplayedMessages = Math.Clamp(maxMessages, 10, 200);
        }
        
        var showOutgoing = _configuration.ShowOutgoingMessages;
        if (ImGui.Checkbox(Resources.Incoming_ShowOutgoing, ref showOutgoing))
        {
            _configuration.ShowOutgoingMessages = showOutgoing;
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.Text(Resources.Incoming_ChannelsToTranslate);
        ImGui.Spacing();
        
        // Main channels
        ImGui.Columns(3, "ChannelColumns", false);
        
        DrawChannelCheckbox("Say", XivChatType.Say);
        DrawChannelCheckbox("Yell", XivChatType.Yell);
        DrawChannelCheckbox("Shout", XivChatType.Shout);
        
        ImGui.NextColumn();
        
        DrawChannelCheckbox("Party", XivChatType.Party);
        DrawChannelCheckbox("Alliance", XivChatType.Alliance);
        DrawChannelCheckbox("Free Company", XivChatType.FreeCompany);
        
        ImGui.NextColumn();
        
        DrawChannelCheckbox("Novice Network", XivChatType.NoviceNetwork);
        
        ImGui.Columns(1);
        
        ImGui.Spacing();
        ImGui.Text(Resources.Incoming_Linkshells);
        ImGui.Columns(4, "LSColumns", false);
        
        DrawChannelCheckbox("LS1", XivChatType.Ls1);
        DrawChannelCheckbox("LS2", XivChatType.Ls2);
        ImGui.NextColumn();
        DrawChannelCheckbox("LS3", XivChatType.Ls3);
        DrawChannelCheckbox("LS4", XivChatType.Ls4);
        ImGui.NextColumn();
        DrawChannelCheckbox("LS5", XivChatType.Ls5);
        DrawChannelCheckbox("LS6", XivChatType.Ls6);
        ImGui.NextColumn();
        DrawChannelCheckbox("LS7", XivChatType.Ls7);
        DrawChannelCheckbox("LS8", XivChatType.Ls8);
        
        ImGui.Columns(1);
        
        ImGui.Spacing();
        ImGui.Text(Resources.Incoming_CrossWorldLS);
        ImGui.Columns(4, "CWLSColumns", false);
        
        DrawChannelCheckbox("CWLS1", XivChatType.CrossLinkShell1);
        DrawChannelCheckbox("CWLS2", XivChatType.CrossLinkShell2);
        ImGui.NextColumn();
        DrawChannelCheckbox("CWLS3", XivChatType.CrossLinkShell3);
        DrawChannelCheckbox("CWLS4", XivChatType.CrossLinkShell4);
        ImGui.NextColumn();
        DrawChannelCheckbox("CWLS5", XivChatType.CrossLinkShell5);
        DrawChannelCheckbox("CWLS6", XivChatType.CrossLinkShell6);
        ImGui.NextColumn();
        DrawChannelCheckbox("CWLS7", XivChatType.CrossLinkShell7);
        DrawChannelCheckbox("CWLS8", XivChatType.CrossLinkShell8);
        
        ImGui.Columns(1);
        
        if (_configuration.SelectedEngine == TranslationEngine.LocalAI)
        {
            DrawIncomingContextSettings();
        }
        }
    }
    
    /// <summary>
    /// Conversation context options for incoming messages.
    /// Visible only when Local AI is selected.
    /// </summary>
    private void DrawIncomingContextSettings()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.4f, 1f), Resources.LocalAI_ContextTitle);
        ImGui.TextWrapped(Resources.LocalAI_ContextDesc);
        ImGui.Spacing();

        var useContext = _configuration.LocalAiUseConversationContext;
        if (ImGui.Checkbox(Resources.LocalAI_ContextEnable, ref useContext))
        {
            _configuration.LocalAiUseConversationContext = useContext;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.LocalAI_ContextEnableTip);

        if (!useContext)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), Resources.LocalAI_ContextDisabled);
            return;
        }

        ImGui.Spacing();
        ImGui.Text(Resources.LocalAI_ContextMessages);
        ImGui.SetNextItemWidth(150);
        var contextMessages = _configuration.LocalAiContextMessages;
        if (ImGui.SliderInt("##LocalAiContextMessages", ref contextMessages, 0, 20))
        {
            _configuration.LocalAiContextMessages = contextMessages;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.LocalAI_ContextMessagesTip);

        ImGui.Spacing();
        ImGui.Text(Resources.LocalAI_ContextChannels);
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), Resources.LocalAI_ContextChannelsTip);
        ImGui.Spacing();

        ImGui.Columns(3, "ContextColumns", false);
        DrawContextChannelCheckbox("Say", XivChatType.Say);
        DrawContextChannelCheckbox("Yell", XivChatType.Yell);
        DrawContextChannelCheckbox("Shout", XivChatType.Shout);
        ImGui.NextColumn();
        DrawContextChannelCheckbox("Party", XivChatType.Party);
        DrawContextChannelCheckbox("Alliance", XivChatType.Alliance);
        DrawContextChannelCheckbox("Free Company", XivChatType.FreeCompany);
        ImGui.NextColumn();
        DrawContextChannelCheckbox("Novice Network", XivChatType.NoviceNetwork);
        DrawContextChannelCheckbox("Linkshell", XivChatType.Ls1);
        DrawContextChannelCheckbox("Tell", XivChatType.TellIncoming);
        ImGui.Columns(1);

        ImGui.Spacing();
        ImGui.TextWrapped(Resources.LocalAI_ContextDefaultNote);
    }

    private void DrawContextChannelCheckbox(string label, XivChatType chatType)
    {
        var enabled = _configuration.LocalAiContextChannels.Contains((int)chatType);
        if (ImGui.Checkbox($"{label}##ctx{(int)chatType}", ref enabled))
        {
            if (enabled)
                _configuration.LocalAiContextChannels.Add((int)chatType);
            else
                _configuration.LocalAiContextChannels.Remove((int)chatType);
        }
    }
    
    private void DrawChannelCheckbox(string label, XivChatType chatType)
    {
        var enabled = _configuration.IncomingChannels.Contains((int)chatType);
        if (ImGui.Checkbox(label, ref enabled))
        {
            if (enabled)
                _configuration.IncomingChannels.Add((int)chatType);
            else
                _configuration.IncomingChannels.Remove((int)chatType);
        }
    }

    private void DrawLocalAiSettings()
    {
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.4f, 1f), Resources.LocalAI_SectionTitle);
        ImGui.Spacing();

        // Endpoint
        var endpoint = _configuration.LocalAiEndpoint ?? string.Empty;
        ImGui.Text(Resources.LocalAI_Endpoint);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##LocalAiEndpoint", ref endpoint, 200))
        {
            _configuration.LocalAiEndpoint = endpoint.Trim();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.LocalAI_EndpointTip);

        // Model.
        var model = _configuration.LocalAiModel ?? string.Empty;
        ImGui.Text(Resources.LocalAI_Model);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##LocalAiModel", ref model, 100))
        {
            _configuration.LocalAiModel = model.Trim();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.LocalAI_ModelTip);

        // API Key
        var apiKey = _configuration.LocalAiApiKey ?? string.Empty;
        ImGui.Text(Resources.LocalAI_ApiKey);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##LocalAiApiKey", ref apiKey, 200, ImGuiInputTextFlags.Password))
        {
            _configuration.LocalAiApiKey = apiKey.Trim();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.LocalAI_ApiKeyTip);

        // Temperature.
        var temperature = _configuration.LocalAiTemperature;
        ImGui.Text(Resources.LocalAI_Temperature);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##LocalAiTemp", ref temperature, 0f, 1f))
        {
            _configuration.LocalAiTemperature = temperature;
        }

        // Timeout
        var timeout = _configuration.LocalAiTimeoutSeconds;
        ImGui.Text(Resources.LocalAI_Timeout);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderInt("##LocalAiTimeout", ref timeout, 5, 120))
        {
            _configuration.LocalAiTimeoutSeconds = timeout;
        }

        // Disable "think" mode.
        var disableThinking = _configuration.LocalAiDisableThinking;
        if (ImGui.Checkbox(Resources.LocalAI_DisableThinking, ref disableThinking))
        {
            _configuration.LocalAiDisableThinking = disableThinking;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.LocalAI_DisableThinkingTip);

        // Max response tokens.
        var maxTokens = _configuration.LocalAiMaxTokens;
        ImGui.Text(Resources.LocalAI_MaxTokens);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt("##LocalAiMaxTokens", ref maxTokens))
        {
            maxTokens = Math.Clamp(maxTokens, 0, 1000);
            _configuration.LocalAiMaxTokens = maxTokens;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.LocalAI_MaxTokensTip);
    }

    // Helpers (ChatTwo ImGuiUtil pattern)
    private bool OptionCheckbox(string label, ref bool value, string? description = null)
    {
        bool changed = ImGui.Checkbox(label, ref value);
        if (description != null)
        {
            ImGui.SameLine();
            HelpText(description);
        }
        return changed;
    }

    private void HelpText(string text)
    {
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), text);
        ImGui.Spacing();
    }

    private void WarningText(string text)
    {
        ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), text);
    }

    private void ScaledDummy(float size)
    {
        ImGui.Dummy(new Vector2(0, size));
    }
}
