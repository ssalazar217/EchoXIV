using System;
using System.Numerics;
using System.Linq;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using FFXIVChatTranslator.Resources;

namespace FFXIVChatTranslator.UI;

public class ConfigWindow : Window, IDisposable
{
    private Configuration _configuration;
    
    // Callback para actualizaciones en tiempo real
    public Action<float>? OnOpacityChanged;
    public Action<bool>? OnSmartVisibilityChanged;
    public Action? OnUnlockNativeRequested;
    public Action? OnVisualsChanged;
    public Action<TranslationEngine>? OnTranslationEngineChanged;
    
    private string _newExcludedMessage = string.Empty;
    
    public ConfigWindow(Configuration configuration) 
        : base("Chat2 Translator - Configuración###ConfigWindow")
    {
        _configuration = configuration;
        
        Size = new Vector2(600, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
        
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("ConfigTabs"))
        {
            if (ImGui.BeginTabItem(Loc.Tab_General))
            {
                DrawGeneralTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Visuales"))
            {
                DrawVisualsTab();
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem(Loc.Tab_ExcludedMessages))
            {
                DrawExcludedMessagesTab();
                ImGui.EndTabItem();
            }
            

            
            if (ImGui.BeginTabItem(Loc.Tab_IncomingChannels))
            {
                DrawIncomingChannelsTab();
                ImGui.EndTabItem();
            }
            
            ImGui.EndTabBar();
        }
    }
    
    private void DrawGeneralTab()
    {
        ImGui.TextWrapped(Loc.General_Description);
        ImGui.Separator();
        ImGui.Spacing();
        
        // Toggle de traducción
        var enabled = _configuration.TranslationEnabled;
        if (ImGui.Checkbox(Loc.General_EnableTranslation, ref enabled))
        {
            _configuration.TranslationEnabled = enabled;
            _configuration.Save();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Selector de Motor de Traducción
        ImGui.Text("Motor de Traducción");
        ImGui.SetNextItemWidth(200);
        
        var engines = Enum.GetValues<TranslationEngine>();
        var engineNames = engines.Select(e => e.ToString()).ToArray();
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
            ? "Motor clásico de Google Translate (Móvil)" 
            : "DeepL Web Translator (Sin API Key)");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Selector de idioma de origen
        ImGui.Text(Loc.General_SourceLanguage);
        ImGui.SetNextItemWidth(200);
        
        var languages = new[] { "es", "en", "ja", "fr", "de", "pt", "ko", "zh-CN", "zh-TW", "ru", "it" };
        var languageNames = new[] { "Espanol", "English", "Japanese", "Francais", "Deutsch", "Portugues", "Korean", "Chinese (Simp)", "Chinese (Trad)", "Russian", "Italiano" };
        
        var currentSourceIdx = Array.IndexOf(languages, _configuration.SourceLanguage);
        if (currentSourceIdx == -1) currentSourceIdx = 0;
        
        if (ImGui.Combo("##SourceLang", ref currentSourceIdx, languageNames, languageNames.Length))
        {
            _configuration.SourceLanguage = languages[currentSourceIdx];
            _configuration.Save();
        }
        
        ImGui.Spacing();
        
        // Selector de idioma de destino
        ImGui.Text(Loc.General_TargetLanguage);
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

        // Selector de Canal por Defecto
        ImGui.Text(Loc.General_DefaultChannel);
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1f), Loc.General_DefaultChannelTip);
        
        var channels = new[] { "/s", "/p", "/fc", "/sh", "/y", "/a", "/n", "/l1", "/cwl1" };
        var channelNames = new[] { "Say (/s)", "Party (/p)", "Free Company (/fc)", "Shout (/sh)", "Yell (/y)", "Alliance (/a)", "Novice (/n)", "Linkshell 1", "CWLS 1" };
        
        var currentChannelIdx = Array.IndexOf(channels, _configuration.DefaultChannel);
        if (currentChannelIdx == -1) currentChannelIdx = 0; // Default Say
        
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("##DefaultChannel", ref currentChannelIdx, channelNames, channelNames.Length))
        {
            _configuration.DefaultChannel = channels[currentChannelIdx];
            _configuration.Save();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.TextWrapped(Loc.General_ChangesAppliedImmediately);
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Selector de modo de ventana (Movido a General para visibilidad)
        ImGui.Text("Modo de Ventana");
        var useNative = _configuration.UseNativeWindow;
        if (ImGui.Checkbox(Loc.Incoming_NativeWindow, ref useNative))
        {
            _configuration.UseNativeWindow = useNative;
            _configuration.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.Incoming_NativeWindowTooltip);
        }
        
        if (useNative)
        {
             // Smart Visibility Option
             var smartVis = _configuration.SmartVisibility;
             if (ImGui.Checkbox("Smart Visibility (Ocultar al perder foco)", ref smartVis))
             {
                 _configuration.SmartVisibility = smartVis;
                 _configuration.Save();
                 OnSmartVisibilityChanged?.Invoke(smartVis);
             }
             if (ImGui.IsItemHovered()) ImGui.SetTooltip("Oculta automáticamente la ventana cuando el juego no está en primer plano.");

             var windowOpacity = _configuration.WindowOpacity;
            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderFloat(Loc.Incoming_WindowOpacity, ref windowOpacity, 0.0f, 1.0f, "%.2f"))
            {
                _configuration.WindowOpacity = windowOpacity;
                _configuration.Save();
                
                // Notificar cambio
                OnOpacityChanged?.Invoke(windowOpacity);
            }

            ImGui.SameLine();
            if (ImGui.Button("Restaurar/Desbloquear Ventana"))
            {
                OnUnlockNativeRequested?.Invoke();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Úsalo si la ventana nativa está bloqueada o perdida.");
            }
        }
        
        if (useNative != _configuration.UseNativeWindow) 
        {
             ImGui.TextColored(new Vector4(1f, 0.5f, 0f, 1f), Loc.Incoming_RestartNote);
        }
    }
    
    private void DrawExcludedMessagesTab()
    {
        ImGui.TextWrapped(Loc.Excluded_Description);
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.Text(Loc.Excluded_InputLabel);
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
        if (ImGui.Button(Loc.Excluded_Add + "##AddExcluded"))
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
        
        ImGui.Text($"{Loc.Excluded_CurrentList} ({_configuration.ExcludedMessages.Count}):");
        
        ImGui.BeginChild("ExcludedList", new Vector2(0, 300), true);
        
        string? toRemove = null;
        foreach (var msg in _configuration.ExcludedMessages.OrderBy(x => x))
        {
            ImGui.Text(msg);
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - 80);
            if (ImGui.SmallButton($"{Loc.Excluded_Remove}##{msg}"))
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
        
        if (ImGui.Button(Loc.Excluded_RestoreDefault))
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
    



    private void DrawVisualsTab()
    {
        ImGui.TextWrapped("Personaliza la apariencia de la ventana de chat traducido.");
        ImGui.Separator();
        ImGui.Spacing();

        // Font Size
        int fontSize = _configuration.FontSize;
        if (ImGui.SliderInt("Tamaño de Fuente", ref fontSize, 10, 32))
        {
            _configuration.FontSize = fontSize;
            _configuration.Save();
            OnVisualsChanged?.Invoke();
        }
        
        // Spacing
        int spacing = _configuration.ChatMessageSpacing;
        if (ImGui.SliderInt("Espaciado entre Mensajes", ref spacing, 0, 20))
        {
            _configuration.ChatMessageSpacing = spacing;
            _configuration.Save();
            OnVisualsChanged?.Invoke();
        }

        ImGui.Spacing();
        
        // Timestamp Format
        ImGui.Text("Formato de Hora");
        string[] formats = { "HH:mm", "HH:mm:ss", "h:mm tt" };
        string[] formatNames = { "Corto (13:30)", "Largo (13:30:45)", "AM/PM (1:30 PM)" };
        
        int currentFmt = Array.IndexOf(formats, _configuration.TimestampFormat);
        if (currentFmt == -1) currentFmt = 0;
        
        if (ImGui.Combo("##TimestampFmt", ref currentFmt, formatNames, formatNames.Length))
        {
            _configuration.TimestampFormat = formats[currentFmt];
            _configuration.Save();
            OnVisualsChanged?.Invoke();
        }
    }
    
    private void DrawIncomingChannelsTab()
    {
        // Añadir región desplazable para evitar corte de contenido
        if (ImGui.BeginChild("IncomingScroll", new Vector2(0, 0), false, ImGuiWindowFlags.None))
        {
            ImGui.TextWrapped(Loc.Incoming_Description);
        ImGui.Separator();
        ImGui.Spacing();
        
        // Toggle general
        var incomingEnabled = _configuration.IncomingTranslationEnabled;
        if (ImGui.Checkbox(Loc.Incoming_Enable, ref incomingEnabled))
        {
            _configuration.IncomingTranslationEnabled = incomingEnabled;
            _configuration.Save();
        }
        
        if (!incomingEnabled)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0f, 1f), Loc.Incoming_Disabled);
            return;
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Selector de idioma destino para entrantes
        ImGui.Text(Loc.Incoming_TranslateTo);
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), Loc.Incoming_AutoDetectNote);
        ImGui.SetNextItemWidth(200);
        
        var languages = new[] { "", "es", "en", "ja", "fr", "de", "pt", "ko", "zh-CN", "zh-TW", "ru", "it" };
        var languageNames = new[] { Loc.Incoming_UseWritingLanguage, "Español", "English", "日本語", "Français", "Deutsch", "Português", "한국어", "简体中文", "繁體中文", "Русский", "Italiano" };
        
        var currentIdx = Array.IndexOf(languages, _configuration.IncomingTargetLanguage);
        if (currentIdx == -1) currentIdx = 0;
        
        if (ImGui.Combo("##IncomingTargetLang", ref currentIdx, languageNames, languageNames.Length))
        {
            _configuration.IncomingTargetLanguage = languages[currentIdx];
            _configuration.Save();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        // Opciones de visualización
        ImGui.Text(Loc.Incoming_DisplayOptions);
        
        var showOriginal = _configuration.ShowOriginalText;
        if (ImGui.Checkbox(Loc.Incoming_ShowOriginal, ref showOriginal))
        {
            _configuration.ShowOriginalText = showOriginal;
            _configuration.Save();
        }
        
        var showTimestamps = _configuration.ShowTimestamps;
        if (ImGui.Checkbox(Loc.Incoming_ShowTimestamps, ref showTimestamps))
        {
            _configuration.ShowTimestamps = showTimestamps;
            _configuration.Save();
        }
        
        var maxMessages = _configuration.MaxDisplayedMessages;
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt(Loc.Incoming_MaxMessages, ref maxMessages))
        {
            _configuration.MaxDisplayedMessages = Math.Clamp(maxMessages, 10, 200);
            _configuration.Save();
        }
        
        var showOutgoing = _configuration.ShowOutgoingMessages;
        if (ImGui.Checkbox("Mostrar mis mensajes", ref showOutgoing))
        {
            _configuration.ShowOutgoingMessages = showOutgoing;
            _configuration.Save();
        }
        
        
        // Settings moved to General tab
        /*
        var windowOpacity = _configuration.WindowOpacity;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat(Loc.Incoming_WindowOpacity, ref windowOpacity, 0.0f, 1.0f, "%.2f"))
        {
            _configuration.WindowOpacity = windowOpacity;
            _configuration.Save();
            
            // Notificar cambio
            OnOpacityChanged?.Invoke(windowOpacity);
        }
        
        ImGui.Spacing();
        
        // Selector de modo de ventana
        var useNative = _configuration.UseNativeWindow;
        if (ImGui.Checkbox(Loc.Incoming_NativeWindow, ref useNative))
        {
            _configuration.UseNativeWindow = useNative;
            _configuration.Save();
            
            // Advertencia de reinicio necesario (simple o intentar swap hot reload?)
            // Por ahora solo guardamos. El usuario necesita reiniciar plugin o usar comando para recargar.
            // Una opción mejor es mostrar un tooltip o texto de aviso.
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.Incoming_NativeWindowTooltip);
        }
        if (useNative != _configuration.UseNativeWindow) // Si acaba de cambiar (pero esto es post-render loop logic, no buena idea)
        {
             // Omitir lógica compleja aquí
        }
        
        // Mostrar aviso si se cambió y difiere del estado actual (requiere tracking de estado en plugin)
        // Simplificación: texto explicativo estático.
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), Loc.Incoming_RestartNote);
        */
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.Text(Loc.Incoming_ChannelsToTranslate);
        ImGui.Spacing();
        
        // Canales principales
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
        ImGui.Text(Loc.Incoming_Linkshells);
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
        ImGui.Text(Loc.Incoming_CrossWorldLS);
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
        
            ImGui.EndChild();
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
            _configuration.Save();
        }
    }
}
