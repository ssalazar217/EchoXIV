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
    private Guid _engineConfigTargetId = Guid.Empty;
    private TranslationEngine _newEngineType = TranslationEngine.ChatGPT;
    private int _newEngineTypeIdx;
    private string _newEngineName = string.Empty;
    private List<GeminiModelInfo>? _geminiModels;
    private bool _geminiModelsLoading;
    private string _geminiModelsError = "";
    private string _geminiModelsFilter = "";

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
                        Resources.Tab_Engines,
                        Resources.Tab_ExcludedMessages,
                        Resources.Tab_Chat,
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
                        case 1: DrawEnginesTab(); break;
                        case 2: DrawExcludedMessagesTab(); break;
                        case 3: DrawChatTab(); break;
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

    private static string Text(string key)
    {
        return Resources.ResourceManager.GetString(key, Resources.Culture) ?? key;
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
        var version = typeof(ConfigWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
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

            // Warning if target needs romaji but engine not compatible
            {
                bool isTargetNonLatin = Configuration.SupportedRomanizationLanguages.Contains(_configuration.TargetLanguage);
                bool isEngineCompatible = IsEngineRomanizationCompatible(_configuration.SelectedEngine);
                if (isTargetNonLatin && _configuration.EnableRomanization && !isEngineCompatible)
                {
                    ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), string.Format(Resources.Engines_RomanjiNotCompatibleFor, _configuration.TargetLanguage));
                }
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
    



    private void DrawEnginesTab()
    {
        using var child = ImRaii.Child("EnginesScroll");
        if (!child) return;

        ImGui.TextWrapped(Resources.Engines_Description);
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.Engines_PriorityNote);
        ImGui.Spacing();

        // Add new instance.
        ImGui.Text(Resources.Engines_Add);
        ImGui.SetNextItemWidth(180);
        var addTypeIdx = _newEngineTypeIdx;
        if (ImGui.Combo("##NewEngineType", ref addTypeIdx, EngineTypeNames, EngineTypeNames.Length))
        {
            _newEngineTypeIdx = addTypeIdx;
            if (addTypeIdx > 0)
                _newEngineType = EngineTypes[addTypeIdx - 1];
        }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180);
        if (ImGui.InputText("##NewEngineName", ref _newEngineName, 100))
        {
        }
        ImGui.SameLine();
        if (ImGui.Button(Resources.Engines_AddButton + "##AddEngine") && _newEngineTypeIdx > 0)
        {
            var name = string.IsNullOrWhiteSpace(_newEngineName) ? GetEngineDisplayName(_newEngineType) : _newEngineName.Trim();
            _configuration.Engines.Add(new EngineInstance
            {
                Type = _newEngineType,
                Name = name,
                Enabled = false,
                Model = GetDefaultModel(_newEngineType),
                Temperature = 0.2f
            });
            _newEngineName = string.Empty;
            _engineConfigTargetId = _configuration.Engines[^1].Id;
            _configuration.Save();
            OnTranslationEngineChanged?.Invoke(_configuration.SelectedEngine);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (_configuration.Engines.Count == 0)
        {
            ImGui.TextWrapped(Resources.Engines_NoneEnabled);
            return;
        }

        // Two-pane layout: instance list (left) + configuration (right).
        if (ImGui.BeginTable("##EngineSplit", 2, ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("##engineList", ImGuiTableColumnFlags.WidthFixed, 250f);
            ImGui.TableSetupColumn("##engineConfig", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();

            // --- LEFT: ordered instance list. Right-click for options. ---
            ImGui.TableSetColumnIndex(0);
            using (var listChild = ImRaii.Child("##EngineList", new Vector2(0, -1), true))
            {
                if (listChild)
                {
                    ImGui.TextDisabled(Text("Engines_ContextMenuHelp"));
                    ImGui.Spacing();

                    for (int i = 0; i < _configuration.Engines.Count; i++)
                    {
                        var instance = _configuration.Engines[i];
                        var isPrimary = instance.Id == _configuration.PrimaryEngineId;
                        var label = instanceName(instance);
                        var primaryTag = isPrimary ? $"  [{Resources.Engines_Primary}]" : "";

                        if (ImGui.Selectable($"   {i + 1}. {label}{primaryTag}", _engineConfigTargetId == instance.Id))
                        {
                            _engineConfigTargetId = instance.Id;
                        }

                        var itemMin = ImGui.GetItemRectMin();
                        var itemMax = ImGui.GetItemRectMax();
                        var markerCenter = new Vector2(itemMin.X + 8f, (itemMin.Y + itemMax.Y) * 0.5f);
                        var markerColor = ImGui.ColorConvertFloat4ToU32(instance.Enabled
                            ? new Vector4(0.25f, 0.85f, 0.4f, 1f)
                            : new Vector4(0.45f, 0.45f, 0.45f, 1f));
                        if (instance.Enabled)
                            ImGui.GetWindowDrawList().AddCircleFilled(markerCenter, 4f, markerColor);
                        else
                            ImGui.GetWindowDrawList().AddCircle(markerCenter, 4f, markerColor, 12, 1.5f);
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{label} - {GetEngineDisplayName(instance.Type)}");

                        if (ImGui.BeginPopupContextItem($"##ctx{i}"))
                        {
                            if (ImGui.MenuItem(Text("Engines_MoveUp"), false, i > 0))
                            {
                                SwapEngines(i, i - 1);
                            }
                            if (ImGui.MenuItem(Text("Engines_MoveDown"), false, i < _configuration.Engines.Count - 1))
                            {
                                SwapEngines(i, i + 1);
                            }
                            ImGui.Separator();
                            if (ImGui.MenuItem(Text("Engines_ToggleEnabled"), instance.Enabled))
                            {
                                instance.Enabled = !instance.Enabled;
                                if (!instance.Enabled && instance.Id == _configuration.PrimaryEngineId)
                                {
                                    var fallback = _configuration.Engines.FirstOrDefault(e => e.Enabled && e.Id != instance.Id);
                                    _configuration.PrimaryEngineId = fallback?.Id ?? Guid.Empty;
                                }
                                SaveEnginesChanged();
                            }
                            if (ImGui.MenuItem(Text("Engines_SetPrimaryMenu"), isPrimary, instance.Enabled))
                            {
                                _configuration.PrimaryEngineId = instance.Id;
                                SaveEnginesChanged();
                            }
                            ImGui.Separator();
                            if (ImGui.MenuItem(Text("Engines_Delete"), false, _configuration.Engines.Count > 1))
                            {
                                var wasPrimary = isPrimary;
                                _configuration.Engines.RemoveAt(i);
                                if (wasPrimary || _engineConfigTargetId == instance.Id)
                                {
                                    _engineConfigTargetId = _configuration.Engines.Count > 0
                                        ? _configuration.Engines[0].Id
                                        : Guid.Empty;
                                }
                                SaveEnginesChanged();
                            }
                            ImGui.EndPopup();
                        }
                    }
                }
            }

            // --- RIGHT: selected instance configuration ---
            ImGui.TableSetColumnIndex(1);
            using (var configChild = ImRaii.Child("##EngineConfig", new Vector2(0, -1), false))
            {
                if (configChild)
                {
                    var target = _configuration.Engines.FirstOrDefault(e => e.Id == _engineConfigTargetId);
                    if (target != null)
                        DrawEngineConfigPanel(target);
                }
            }

            ImGui.EndTable();
        }
    }

    private static string instanceName(EngineInstance instance) =>
        string.IsNullOrWhiteSpace(instance.Name) ? GetEngineDisplayName(instance.Type) : instance.Name;

    private void SwapEngines(int a, int b)
    {
        (_configuration.Engines[a], _configuration.Engines[b]) = (_configuration.Engines[b], _configuration.Engines[a]);
        SaveEnginesChanged();
    }

    private void SaveEnginesChanged()
    {
        _configuration.Save();
        OnTranslationEngineChanged?.Invoke(_configuration.SelectedEngine);
    }

    private void DrawEngineConfigPanel(EngineInstance instance)
    {
        // Activation toggle at the top.
        var enabled = instance.Enabled;
        if (ImGui.Checkbox($"{Resources.Engines_Enabled}##enable{instance.Id}", ref enabled))
        {
            instance.Enabled = enabled;
            // Keep the primary valid when disabling it.
            if (!enabled && instance.Id == _configuration.PrimaryEngineId)
            {
                var fallback = _configuration.Engines.FirstOrDefault(e => e.Enabled && e.Id != instance.Id);
                _configuration.PrimaryEngineId = fallback?.Id ?? Guid.Empty;
            }
            _configuration.Save();
            OnTranslationEngineChanged?.Invoke(_configuration.SelectedEngine);
        }

        ImGui.SameLine();
        var isPrimary = instance.Id == _configuration.PrimaryEngineId;
        if (ImGui.Button(isPrimary ? Resources.Engines_PrimarySetActive : Resources.Engines_SetPrimary + "##prim" + instance.Id))
        {
            _configuration.PrimaryEngineId = instance.Id;
            _configuration.Save();
            OnTranslationEngineChanged?.Invoke(_configuration.SelectedEngine);
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.4f, 1f), $"{Resources.Engines_Configure}: {instanceName(instance)}");
        ImGui.Spacing();

        // Editable name.
        var name = instance.Name ?? string.Empty;
        ImGui.Text(Resources.Engines_InstanceName);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##InstanceName", ref name, 100))
        {
            instance.Name = name.Trim();
            _configuration.Save();
        }

        ImGui.Spacing();

        // Romanji compatibility note
        bool isRomanjiCompatible = instance.Type == TranslationEngine.Google
            || instance.Type == TranslationEngine.Papago
            || instance.Type == TranslationEngine.LocalAI
            || instance.Type == TranslationEngine.Gemini
            || instance.Type == TranslationEngine.Claude
            || instance.Type == TranslationEngine.ChatGPT
            || instance.Type == TranslationEngine.DeepSeek
            || instance.Type == TranslationEngine.OpenRouter;
        if (!isRomanjiCompatible)
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), Resources.Engines_RomanjiNotCompatible);
        }

        switch (instance.Type)
        {
            case TranslationEngine.Google:
            case TranslationEngine.Papago:
            case TranslationEngine.Yandex:
                ImGui.TextWrapped(Resources.Engines_NoConfigNeeded);
                break;
            case TranslationEngine.LibreTranslate:
                DrawLibreTranslateSettings(instance);
                break;
            case TranslationEngine.LocalAI:
                DrawLocalAiSettings();
                break;
            case TranslationEngine.ChatGPT:
                DrawCloudLlmSettings(instance, Resources.Engines_ChatGptEndpointTip);
                break;
            case TranslationEngine.Gemini:
                DrawCloudLlmSettings(instance, Resources.Engines_GeminiEndpointTip);
                break;
            case TranslationEngine.Claude:
                DrawCloudLlmSettings(instance, Resources.Engines_ClaudeEndpointTip);
                break;
            case TranslationEngine.DeepSeek:
                DrawCloudLlmSettings(instance, Resources.Engines_DeepSeekEndpointTip);
                break;
            case TranslationEngine.OpenRouter:
                DrawCloudLlmSettings(instance, Resources.Engines_OpenRouterEndpointTip);
                break;
        }
    }

    private void DrawCloudLlmSettings(EngineInstance instance, string tip)
    {
        var apiKey = instance.ApiKey ?? string.Empty;
        ImGui.Text(Resources.Engines_ApiKey);
        ImGui.SetNextItemWidth(-30);
        if (ImGui.InputText("##CloudApiKey", ref apiKey, 200, ImGuiInputTextFlags.Password))
        {
            instance.ApiKey = apiKey.Trim();
            _configuration.Save();
        }
        ImGui.SameLine();
        if (Dalamud.Interface.Components.ImGuiComponents.IconButton($"##CopyCloudApiKey{instance.Id}", Dalamud.Interface.FontAwesomeIcon.Copy))
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                ImGui.SetClipboardText(apiKey);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.Engines_CopyApiKeyTooltip);

        var model = instance.Model ?? string.Empty;
        ImGui.Text(Resources.Engines_Model);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##CloudModel", ref model, 100))
        {
            instance.Model = model.Trim();
            _configuration.Save();
        }

        var temperature = instance.Temperature;
        ImGui.Text(Resources.Engines_Temperature);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##CloudTemp", ref temperature, 0f, 1f))
        {
            instance.Temperature = temperature;
            _configuration.Save();
        }

        var maxTokens = instance.MaxTokens;
        ImGui.Text(Resources.LocalAI_MaxTokens);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt($"##CloudMaxTokens{instance.Id}", ref maxTokens))
        {
            instance.MaxTokens = Math.Clamp(maxTokens, 500, 16384);
            _configuration.Save();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.LocalAI_MaxTokensTip);

        ImGui.TextWrapped(tip);

        if (instance.Type == TranslationEngine.Gemini)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text(Text("Engines_ModelBrowser"));
            ImGui.SameLine();
            if (ImGui.Button($"{Text("Engines_LoadModels")}##fetch{instance.Id}"))
            {
                _geminiModels = null;
                _geminiModelsError = "";
                _geminiModelsFilter = "";
                _geminiModelsLoading = true;
                _ = LoadGeminiModelsAsync(instance);
            }
            if (_geminiModelsLoading)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted(Text("Engines_Loading"));
            }
            if (!string.IsNullOrEmpty(_geminiModelsError))
            {
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), _geminiModelsError);
            }

            if (_geminiModels != null && _geminiModels.Count > 0)
            {
                ImGui.Text(Text("Engines_TranslatableModels"));
                ImGui.InputTextWithHint("##geminiModelsFilter", Text("Engines_Filter"), ref _geminiModelsFilter, 128);
                using var list = ImRaii.Child("##geminiModels", new Vector2(0, 220), true);
                if (list)
                {
                    foreach (var m in _geminiModels.Where(x => x.Translatable)
                                 .Where(x => _geminiModelsFilter.Length == 0 ||
                                             x.DisplayName.Contains(_geminiModelsFilter, StringComparison.OrdinalIgnoreCase) ||
                                             x.Id.Contains(_geminiModelsFilter, StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
                    {
                        if (ImGui.Selectable(m.DisplayName, m.Id == instance.Model))
                        {
                            instance.Model = m.Id;
                            _configuration.Save();
                        }
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(m.Id);
                    }
                }
            }
        }
    }

    private void DrawLibreTranslateSettings(EngineInstance instance)
    {
        var endpoint = instance.Endpoint ?? string.Empty;
        ImGui.Text(Resources.Engines_Endpoint);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##LibreEndpoint", ref endpoint, 200))
        {
            instance.Endpoint = endpoint.Trim();
            _configuration.Save();
        }
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.Engines_LibreTranslateExample);

        var apiKey = instance.ApiKey ?? string.Empty;
        ImGui.Text(Resources.Engines_ApiKey + " (opcional)");
        ImGui.SetNextItemWidth(-30);
        if (ImGui.InputText("##LibreApiKey", ref apiKey, 200, ImGuiInputTextFlags.Password))
        {
            instance.ApiKey = apiKey.Trim();
            _configuration.Save();
        }
        ImGui.SameLine();
        if (Dalamud.Interface.Components.ImGuiComponents.IconButton($"##CopyLibreApiKey{instance.Id}", Dalamud.Interface.FontAwesomeIcon.Copy))
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                ImGui.SetClipboardText(apiKey);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.Engines_CopyApiKeyTooltip);
    }

    private async Task LoadGeminiModelsAsync(EngineInstance instance)
    {
        var models = await GeminiModelCatalog.ListModelsAsync(instance.ApiKey ?? string.Empty);
        _geminiModels = models;
        _geminiModelsLoading = false;
        _geminiModelsError = models.Count == 0
            ? Text("Engines_NoModelsReturned")
            : "";
    }

    private static readonly TranslationEngine[] EngineTypes =
    {
        TranslationEngine.Google,
        TranslationEngine.Papago,
        TranslationEngine.Yandex,
        TranslationEngine.LibreTranslate,
        TranslationEngine.LocalAI,
        TranslationEngine.ChatGPT,
        TranslationEngine.Gemini,
        TranslationEngine.Claude,
        TranslationEngine.DeepSeek,
        TranslationEngine.OpenRouter,
    };

    private static string[] EngineTypeNames => new[]
        { Resources.Engines_SelectProvider }.Concat(EngineTypes.Select(GetEngineDisplayName)).ToArray();

    private static string GetEngineDisplayName(TranslationEngine engine) => engine switch
    {
        TranslationEngine.Google => Resources.General_EngineNameGoogle,
        TranslationEngine.Papago => Resources.General_EngineNamePapago,
        TranslationEngine.LibreTranslate => Resources.General_EngineNameLibreTranslate,
        TranslationEngine.Yandex => Resources.General_EngineNameYandex,
        TranslationEngine.LocalAI => "Local AI",
        TranslationEngine.ChatGPT => "ChatGPT (OpenAI)",
        TranslationEngine.Gemini => "Gemini",
        TranslationEngine.Claude => "Claude",
        TranslationEngine.DeepSeek => "DeepSeek",
        TranslationEngine.OpenRouter => "OpenRouter",
        _ => engine.ToString(),
    };

    // Only traditional engines need explicit check; AI generates via system prompt
    private static bool IsEngineRomanizationCompatible(TranslationEngine engine) => engine switch
    {
        TranslationEngine.Google => true,
        TranslationEngine.Papago => true,
        TranslationEngine.Yandex => false,
        TranslationEngine.LibreTranslate => true,
        _ => true,
    };

    private static string GetDefaultModel(TranslationEngine engine) => engine switch
    {
        TranslationEngine.ChatGPT => "gpt-4o-mini",
        TranslationEngine.Gemini => "gemini-2.0-flash",
        TranslationEngine.Claude => "claude-3-5-sonnet",
        TranslationEngine.DeepSeek => "deepseek-chat",
        TranslationEngine.OpenRouter => "openai/gpt-4o-mini",
        TranslationEngine.LocalAI => "",
        _ => "",
    };

    private void DrawChatTab()
    {
        using var child = ImRaii.Child("ChatScroll");
        if (!child) return;

        // Chat appearance.
        ImGui.TextWrapped(Resources.Visuals_Description);
        ImGui.Separator();
        ImGui.Spacing();

        int fontSize = _configuration.FontSize;
        if (ImGui.SliderInt(Resources.Visuals_FontSize, ref fontSize, 10, 32))
        {
            _configuration.FontSize = fontSize;
            _configuration.Save();
        }
        int spacing = _configuration.ChatMessageSpacing;
        if (ImGui.SliderInt(Resources.Visuals_Spacing, ref spacing, 0, 20))
        {
            _configuration.ChatMessageSpacing = spacing;
            _configuration.Save();
        }
        ImGui.Spacing();
        ImGui.Text(Resources.Visuals_TimeFormat);
        string[] formats = { "HH:mm", "HH:mm:ss", "h:mm tt" };
        string[] formatNames = { Resources.Visuals_TimeFormatShort, Resources.Visuals_TimeFormatLong, Resources.Visuals_TimeFormatAMPM };
        int currentFmt = Array.IndexOf(formats, _configuration.TimestampFormat);
        if (currentFmt == -1) currentFmt = 0;
        if (ImGui.Combo("##TimestampFmtChat", ref currentFmt, formatNames, formatNames.Length))
        {
            _configuration.TimestampFormat = formats[currentFmt];
            _configuration.Save();
        }
        ImGui.Spacing();
        var hideInactive = _configuration.HideWhenInactive;
        if (ImGui.Checkbox(Text("Visuals_HideWhenInactive"), ref hideInactive))
        {
            _configuration.HideWhenInactive = hideInactive;
            _configuration.Save();
        }
        HelpText(Text("Visuals_HideWhenInactiveTip"));
        if (hideInactive)
        {
            int timeout = _configuration.InactivityHideTimeout;
            if (ImGui.SliderInt(Resources.Visuals_InactivityTimeout, ref timeout, 10, 600))
            {
                _configuration.InactivityHideTimeout = timeout;
                _configuration.Save();
            }
        }

        ImGui.Spacing();
        var maxDisplayedMessages = _configuration.MaxDisplayedMessages;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt(Resources.Incoming_MaxMessages, ref maxDisplayedMessages))
        {
            _configuration.MaxDisplayedMessages = Math.Clamp(maxDisplayedMessages, 10, 200);
            _configuration.Save();
        }

        ImGui.Spacing();
        var showChatToggleLog = _configuration.ShowChatToggleLog;
        if (ImGui.Checkbox(Resources.General_ShowChatToggleLog, ref showChatToggleLog))
        {
            _configuration.ShowChatToggleLog = showChatToggleLog;
            _configuration.Save();
        }
        HelpText(Resources.General_ShowChatToggleLogTip);

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        // Translation and channel colors.
        ImGui.TextWrapped(Resources.Incoming_Description);
        ImGui.Separator();
        ImGui.Spacing();
        var incomingEnabled = _configuration.IncomingTranslationEnabled;
        if (ImGui.Checkbox(Resources.Incoming_Enable, ref incomingEnabled))
        {
            _configuration.IncomingTranslationEnabled = incomingEnabled;
            _configuration.Save();
        }
        if (!incomingEnabled)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0f, 1f), Resources.Incoming_Disabled);
            return;
        }
        ImGui.Spacing();
        ImGui.Text(Resources.Incoming_LanguageDetection);
        var skipSameLanguage = _configuration.SkipSameLanguageTranslation;
        if (ImGui.Checkbox(Resources.Incoming_SkipSameLanguage, ref skipSameLanguage))
        {
            _configuration.SkipSameLanguageTranslation = skipSameLanguage;
            _configuration.Save();
        }
        HelpText(Resources.Incoming_SkipSameLanguageTip);
        var confidence = Math.Clamp(_configuration.LanguageDetectionConfidence * 100f, 70f, 100f);
        ImGui.SetNextItemWidth(200);
        if (ImGui.SliderFloat(Resources.Incoming_DetectionConfidence, ref confidence, 70f, 100f, "%.0f%%"))
        {
            _configuration.LanguageDetectionConfidence = Math.Clamp(confidence / 100f, 0.70f, 1f);
            _configuration.Save();
        }
        HelpText(Resources.Incoming_DetectionConfidenceTip);

        ImGui.Spacing();
        ImGui.Text(Resources.Incoming_DisplayOptions);
        var showOriginal = _configuration.ShowOriginalText;
        if (ImGui.Checkbox(Resources.Incoming_ShowOriginal, ref showOriginal))
        {
            _configuration.ShowOriginalText = showOriginal;
            _configuration.Save();
        }
        var showTimestamps = _configuration.ShowTimestamps;
        if (ImGui.Checkbox(Resources.Incoming_ShowTimestamps, ref showTimestamps))
        {
            _configuration.ShowTimestamps = showTimestamps;
            _configuration.Save();
        }
        var showOutgoing = _configuration.ShowOutgoingMessages;
        if (ImGui.Checkbox(Resources.Incoming_ShowOutgoing, ref showOutgoing))
        {
            _configuration.ShowOutgoingMessages = showOutgoing;
            _configuration.Save();
        }

        ImGui.Text(Resources.Incoming_ChannelsToTranslate);
        // Combined channel toggle and color.
        void DrawFused(string label, XivChatType type)
        {
            var enabled = _configuration.IncomingChannels.Contains((int)type);
            if (ImGui.Checkbox($"##chk{(int)type}", ref enabled))
            {
                if (enabled) _configuration.IncomingChannels.Add((int)type);
                else _configuration.IncomingChannels.Remove((int)type);
                _configuration.Save();
            }
            ImGui.SameLine();
            if (_configuration.ChannelColors.TryGetValue((int)type, out var colVal) || _configuration.ChannelColors.TryGetValue(GetColorKeyForChannel(type), out colVal))
            {
                var colVec = ColorToVector4(colVal);
                if (ImGui.ColorEdit4($"##col{(int)type}", ref colVec, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoAlpha | ImGuiColorEditFlags.NoTooltip))
                {
                    _configuration.ChannelColors[GetColorKeyForChannel(type)] = Vector4ToColor(colVec);
                    _configuration.Save();
                }
            }
            else
            {
                ImGui.Dummy(new Vector2(20, 0));
                ImGui.SameLine();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted(label);
        }
        ImGui.Columns(3, "ChannelColumnsChat", false);
        DrawFused(Resources.Channel_Say, XivChatType.Say);
        DrawFused(Resources.Channel_Yell, XivChatType.Yell);
        DrawFused(Resources.Channel_Shout, XivChatType.Shout);
        ImGui.NextColumn();
        DrawFused(Resources.Channel_Party, XivChatType.Party);
        DrawFused(Resources.Channel_Alliance, XivChatType.Alliance);
        DrawFused(Resources.Channel_FC, XivChatType.FreeCompany);
        ImGui.NextColumn();
        DrawFused(Resources.Channel_NN, XivChatType.NoviceNetwork);
        DrawFused(Resources.Channel_Tell, XivChatType.TellIncoming);
        ImGui.Columns(1);
        ImGui.Spacing();
        ImGui.Text(Resources.Incoming_Linkshells);
        ImGui.Columns(4, "LSColumnsChat", false);
        DrawFused("LS1", XivChatType.Ls1);
        DrawFused("LS2", XivChatType.Ls2);
        ImGui.NextColumn();
        DrawFused("LS3", XivChatType.Ls3);
        DrawFused("LS4", XivChatType.Ls4);
        ImGui.NextColumn();
        DrawFused("LS5", XivChatType.Ls5);
        DrawFused("LS6", XivChatType.Ls6);
        ImGui.NextColumn();
        DrawFused("LS7", XivChatType.Ls7);
        DrawFused("LS8", XivChatType.Ls8);
        ImGui.Columns(1);
        ImGui.Spacing();
        ImGui.Text(Resources.Incoming_CrossWorldLS);
        ImGui.Columns(4, "CWLSColumnsChat", false);
        DrawFused("CWLS1", XivChatType.CrossLinkShell1);
        DrawFused("CWLS2", XivChatType.CrossLinkShell2);
        ImGui.NextColumn();
        DrawFused("CWLS3", XivChatType.CrossLinkShell3);
        DrawFused("CWLS4", XivChatType.CrossLinkShell4);
        ImGui.NextColumn();
        DrawFused("CWLS5", XivChatType.CrossLinkShell5);
        DrawFused("CWLS6", XivChatType.CrossLinkShell6);
        ImGui.NextColumn();
        DrawFused("CWLS7", XivChatType.CrossLinkShell7);
        DrawFused("CWLS8", XivChatType.CrossLinkShell8);
        ImGui.Columns(1);
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

        ImGui.Spacing();
        var maxHistorySizeMb = _configuration.MaxHistorySizeMb;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt(Resources.History_MaxSizeMb, ref maxHistorySizeMb))
        {
            _configuration.MaxHistorySizeMb = Math.Clamp(maxHistorySizeMb, 1, 1024);
            _historyManager.TrimToConfiguredSize();
            _configuration.Save();
        }
        HelpText(Resources.History_MaxSizeMbTip);

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
    

    private static int GetColorKeyForChannel(XivChatType type)
    {
        var id = (int)type;
        if (id >= (int)XivChatType.Ls1 && id <= (int)XivChatType.Ls8) return 16;
        if (id >= (int)XivChatType.CrossLinkShell1 && id <= (int)XivChatType.CrossLinkShell8) return 27;
        return id;
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
        ImGui.SetNextItemWidth(-30);
        if (ImGui.InputText("##LocalAiApiKey", ref apiKey, 200, ImGuiInputTextFlags.Password))
        {
            _configuration.LocalAiApiKey = apiKey.Trim();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.LocalAI_ApiKeyTip);
        ImGui.SameLine();
        if (Dalamud.Interface.Components.ImGuiComponents.IconButton("##CopyLocalAiApiKey", Dalamud.Interface.FontAwesomeIcon.Copy))
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
                ImGui.SetClipboardText(apiKey);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.Engines_CopyApiKeyTooltip);

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
            maxTokens = Math.Clamp(maxTokens, 0, 16384);
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
