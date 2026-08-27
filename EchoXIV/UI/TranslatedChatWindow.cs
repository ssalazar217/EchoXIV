using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using EchoXIV.Properties;
using EchoXIV.Services;

namespace EchoXIV.UI
{
    public class TranslatedChatWindow : Window, IDisposable
    {
        private readonly Configuration _configuration;
        public event Action<TranslatedChatMessage>? OnRequestTranslation;
        public event Action? OnRequestSettings;
        private readonly MessageHistoryManager _historyManager;
        private readonly List<TranslatedChatMessage> _messages = new();
        private readonly object _messagesLock = new();
        private bool _autoScroll = true;
        private bool _resetPending = false;
        private bool _isOnMainScreen = true;
        private bool _stickToBottom = true;
        private DateTime _lastActivity = DateTime.UtcNow;

        public bool IsOnMainScreen => _isOnMainScreen;

        public TranslatedChatWindow(Configuration configuration, MessageHistoryManager historyManager)
            : base(Resources.ChatWindow_Title + "###TranslatedChatWindow")
        {
            _configuration = configuration;
            _historyManager = historyManager;

            lock (_messagesLock)
            {
                _messages.AddRange(_historyManager.GetHistory());
            }

            _historyManager.OnMessageAdded += AddPendingMessage;
            _historyManager.OnMessageUpdated += UpdateMessage;
            _historyManager.OnHistoryCleared += ClearMessages;

            Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
            PositionCondition = ImGuiCond.FirstUseEver;
            SizeCondition = ImGuiCond.FirstUseEver;
            IsOpen = _configuration.OverlayVisible;
        }

        public void AddPendingMessage(TranslatedChatMessage message)
        {
            _lastActivity = DateTime.UtcNow;
            if (_configuration.HideWhenInactive && !IsOpen)
                IsOpen = true;
            lock (_messagesLock)
            {
                _messages.Add(message);
                while (_messages.Count > _configuration.MaxDisplayedMessages)
                {
                    _messages.RemoveAt(0);
                }
            }
        }

        public void UpdateMessage(TranslatedChatMessage message)
        {
            _lastActivity = DateTime.UtcNow;
            lock (_messagesLock)
            {
                var existing = _messages.FirstOrDefault(m => m.Id == message.Id);
                if (existing != null)
                {
                    existing.TranslatedText = message.TranslatedText;
                    existing.RomanizedText = message.RomanizedText;
                    existing.OriginalScript = message.OriginalScript;
                    existing.IsTranslating = false;
                }
                else
                {
                    // New message (e.g., outgoing with fresh Id)
                    _messages.Add(message);
                    while (_messages.Count > _configuration.MaxDisplayedMessages)
                    {
                        _messages.RemoveAt(0);
                    }
                }
            }
        }

        public void ClearMessages()
        {
            lock (_messagesLock)
            {
                _messages.Clear();
            }
        }

        public override void Draw()
        {
            // Detect if running on the game's primary monitor
            var viewport = ImGui.GetWindowViewport();
            var mainViewport = ImGui.GetMainViewport();
            _isOnMainScreen = (viewport.ID == mainViewport.ID);

            // HideWhenInactive (ChatTwo pattern)
            if (_configuration.HideWhenInactive && (DateTime.UtcNow - _lastActivity).TotalSeconds > _configuration.InactivityHideTimeout)
            {
                IsOpen = false;
                return;
            }

            // Apply configured font size scale
            ImGui.SetWindowFontScale(_configuration.FontSize / 16.0f);
            try
            {
                DrawToolbar();
                ImGui.Separator();

                // Fixed banner when Local AI is unavailable
                if (_configuration.LocalAiUnavailable)
                {
                    using var red = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.8f, 0.1f, 0.1f, 1f));
                    using var banner = ImRaii.Child("LocalAiOfflineBanner", new Vector2(0, 26), false);
                    ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), Resources.ChatWindow_IAOfflineBanner);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(Resources.ChatWindow_IAOfflineTooltip);
                    }
                    ImGui.Spacing();
                }

                DrawMessageList();
            }
            finally
            {
                ImGui.SetWindowFontScale(1.0f);
            }

            if (_resetPending)
            {
                _resetPending = false;
                PositionCondition = ImGuiCond.Appearing;
                SizeCondition = ImGuiCond.Appearing;
            }
        }

        public override void OnOpen()
        {
            // On open, re-anchor to the bottom so recent messages are visible.
            _stickToBottom = true;

            if (!_configuration.OverlayVisible)
            {
                _configuration.OverlayVisible = true;
                _configuration.Save();
            }
        }

        public override void OnClose()
        {
            if (_configuration.OverlayVisible)
            {
                _configuration.OverlayVisible = false;
                _configuration.Save();
            }
        }

        public void ResetPosition()
        {
            Position = new Vector2(100, 100);
            Size = new Vector2(400, 300);
            PositionCondition = ImGuiCond.Always;
            SizeCondition = ImGuiCond.Always;
            _resetPending = true;
            IsOpen = true;
        }

        private void DrawToolbar()
        {
            var enabled = _configuration.IncomingTranslationEnabled;
            if (ImGui.Checkbox(Resources.ChatWindow_Active, ref enabled))
            {
                _configuration.IncomingTranslationEnabled = enabled;
                _configuration.Save();
            }

            ImGui.SameLine();
            ImGui.Checkbox(Resources.ChatWindow_AutoScroll, ref _autoScroll);

            // Romanization checkbox
            ImGui.SameLine();
            var romanizationEnabled = _configuration.EnableRomanization;
            if (ImGui.Checkbox(Resources.ChatWindow_Romanization, ref romanizationEnabled))
            {
                _configuration.EnableRomanization = romanizationEnabled;
                _configuration.Save();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.ChatWindow_RomanizationTooltip);

            ImGui.SameLine();
            if (Dalamud.Interface.Components.ImGuiComponents.IconButton("##ChatWindowSettings", Dalamud.Interface.FontAwesomeIcon.Cog))
            {
                OnRequestSettings?.Invoke();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Resources.ChatWindow_SettingsTooltip);

            ImGui.SameLine();
            if (ImGui.Button(Resources.ChatWindow_Retry))
            {
                RetryStuckTranslations();
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Resources.ChatWindow_RetryTooltip);
            }

            ImGui.SameLine();
            if (ImGui.Button(Resources.ChatWindow_Clear))
            {
                ClearDisplayedMessages();
            }

            if (_configuration.VerboseLogging)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(_isOnMainScreen ? Resources.ChatWindow_Internal : Resources.ChatWindow_External);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(_isOnMainScreen ? Resources.ChatWindow_InternalTooltip : Resources.ChatWindow_ExternalTooltip);
                }
            }

            ImGui.SameLine();
            lock (_messagesLock)
            {
                var engineName = _configuration.SelectedEngine.ToString();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"[{engineName}] ({_messages.Count})");
            }

            if (_configuration.LocalAiUnavailable)
            {
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1f, 0.6f, 0f, 1f), Resources.ChatWindow_IAOffline);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(Resources.ChatWindow_IAOfflineTooltip);
                }
            }

            if (_configuration.PrimaryInstance == null)
            {
                ImGui.TextColored(new Vector4(1f, 0.7f, 0.2f, 1f), Resources.Engines_NoneEnabled);
            }
        }

        private void DrawMessageList()
        {
            using var child = ImRaii.Child("MessageList", new Vector2(0, 0), true);
            if (!child) return;

            // Compact table spacing like vanilla chat
            using var cellSpacing = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(2, 0));
            using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

            // 3-column table setup
            // Column 0: timestamps (fixed width)
            // Column 1: content (stretch)
            // Column 2: [?] indicator (fixed min width)
            if (ImGui.BeginTable("ChatBodyTable", 3, ImGuiTableFlags.None))
            {
                // Time column width based on font scale
                float timeWidth = 0.0f;
                if (_configuration.ShowTimestamps)
                {
                    timeWidth = ImGui.CalcTextSize("[00:00] ").X;
                }
                
                float originWidth = ImGui.CalcTextSize("[?]").X + 5.0f;
                
                ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, timeWidth);
                ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Origin", ImGuiTableColumnFlags.WidthFixed, originWidth);

                lock (_messagesLock)
                {
                    foreach (var message in _messages)
                    {
                        DrawMessageRow(message);

                        if (_configuration.ChatMessageSpacing > 0)
                        {
                            ImGui.TableNextRow();
                            ImGui.TableSetColumnIndex(1);
                            ImGui.Dummy(new Vector2(0, _configuration.ChatMessageSpacing));
                        }
                    }
                }

                if (_autoScroll)
                {
                // Keep the view pinned to the last line unless the user scrolls up.
                    var scrollY = ImGui.GetScrollY();
                    var scrollMaxY = ImGui.GetScrollMaxY();
                    if (scrollMaxY <= 0f || scrollY >= scrollMaxY - 20f)
                    {
                        _stickToBottom = true;
                    }
                    else
                    {
                        _stickToBottom = false;
                    }

                    if (_stickToBottom)
                    {
                        ImGui.SetScrollHereY(1.0f);
                    }
                }

                ImGui.EndTable();
            }
        }

        private void DrawMessageRow(TranslatedChatMessage message)
        {
            ImGui.TableNextRow();
            var channelColor = GetChannelColor(message.ChatType);
            var channelName = GetChannelName(message.ChatType);

            // Romanization flags shared by both blocks
            bool hasRomanization = !string.IsNullOrEmpty(message.RomanizedText);
            bool showRomanization = _configuration.EnableRomanization && hasRomanization;
            bool isMsgOutgoing = message.IsOutgoing || message.ChatType == XivChatType.TellOutgoing;
            
            // --- COLUMN 0: TIMESTAMP ---
            if (_configuration.ShowTimestamps)
            {
                ImGui.TableSetColumnIndex(0);
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), $"[{FormatTimestamp(message.Timestamp)}]");
            }

            // --- COLUMN 1: MESSAGE BLOCK ---
            ImGui.TableSetColumnIndex(1);
            
            // Wrap at cell edge to avoid clipping
            using (var wrap = ImRaii.TextWrapPos(ImGui.GetCursorPos().X + ImGui.GetContentRegionAvail().X))
            {
                // Channel and sender
                string senderPart = "";
                if (message.ChatType == XivChatType.TellOutgoing)
                {
                    var name = string.IsNullOrEmpty(message.Recipient) ? message.Sender : message.Recipient;
                    senderPart = $"[{Resources.Channel_Tell}] >> {name}: ";
                }
                else if (message.ChatType == XivChatType.TellIncoming)
                {
                    senderPart = $"[{Resources.Channel_Tell}] << {message.Sender}: ";
                }
                else
                {
                    senderPart = $"[{channelName}] {message.Sender}: ";
                }

                if (message.IsTranslating)
                {
                    ImGui.TextColored(channelColor, senderPart);
                    ImGui.SameLine(0, 0);
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1f), Resources.ChatWindow_Translating);
                }
                else
                {
                    // Main line shows romaji when enabled
                    
                    string displayText;
                    if (showRomanization && isMsgOutgoing)
                    {
                        // Outgoing with romaji: show romaji as main line
                        displayText = message.RomanizedText!;
                    }
                    else
                    {
                        // Otherwise show plain translation
                        displayText = message.TranslatedText;
                    }
                    
                    ImGui.TextColored(channelColor, senderPart + displayText);
                }
            }

            // --- COLUMN 2: ORIGINAL INDICATOR [?] ---
            bool showOriginal = _configuration.ShowOriginalText && 
                                !string.Equals(message.OriginalText, message.TranslatedText, StringComparison.OrdinalIgnoreCase);
            
            if (showOriginal || hasRomanization)
            {
                ImGui.TableSetColumnIndex(2);
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "[?]");
                if (ImGui.IsItemHovered())
                {
                    using var tooltip = ImRaii.Tooltip();
                    
                    if (isMsgOutgoing)
                    {
                        if (hasRomanization)
                        {
                            // Outgoing: avoid repeating the native translation when it is already visible.
                            if (showRomanization)
                            {
                                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.ChatWindow_TranslationNative);
                                ImGui.TextUnformatted(message.TranslatedText);
                            }
                            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.ChatWindow_RomanizationLabel);
                            ImGui.TextUnformatted(message.RomanizedText!);
                            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.ChatWindow_Original);
                            ImGui.TextUnformatted(message.OriginalText);
                        }
                        else
                        {
                            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.ChatWindow_Original);
                            ImGui.TextUnformatted(message.OriginalText);
                        }
                    }
                    else
                    {
                        // Incoming: tooltip = original (native script) + romaji
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.ChatWindow_Original);
                        ImGui.TextUnformatted(message.OriginalText); // Sender's original (e.g., konnichiwa)
                        
                        if (hasRomanization)
                        {
                            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.ChatWindow_RomanizationLabel);
                            ImGui.TextUnformatted(message.RomanizedText!); // Romaji (e.g., konnichiwa)
                        }
                    }
                }
            }
        }

        private string FormatTimestamp(DateTime timestamp)
        {
            var format = _configuration.TimestampFormat;
            if (string.IsNullOrWhiteSpace(format) || format == "Short")
                return timestamp.ToString("HH:mm");
            try
            {
                return timestamp.ToString(format);
            }
            catch
            {
                return timestamp.ToString("HH:mm");
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
            return new Vector4(204f/255f, 204f/255f, 204f/255f, 1f);
        }

        private static Vector4 ColorToVector4(uint color)
        {
            float a = ((color >> 24) & 0xFF) / 255f;
            float r = ((color >> 16) & 0xFF) / 255f;
            float g = ((color >> 8) & 0xFF) / 255f;
            float b = (color & 0xFF) / 255f;
            return new Vector4(r, g, b, a);
        }

        private static string GetChannelName(XivChatType type)
        {
            return type switch
            {
                XivChatType.Say => Resources.Channel_Say,
                XivChatType.Shout => Resources.Channel_Shout,
                XivChatType.Yell => Resources.Channel_Yell,
                XivChatType.Party => Resources.Channel_Party,
                XivChatType.Alliance => Resources.Channel_Alliance,
                XivChatType.FreeCompany => Resources.Channel_FC,
                XivChatType.Ls1 => "LS1",
                XivChatType.Ls2 => "LS2",
                XivChatType.Ls3 => "LS3",
                XivChatType.Ls4 => "LS4",
                XivChatType.Ls5 => "LS5",
                XivChatType.Ls6 => "LS6",
                XivChatType.Ls7 => "LS7",
                XivChatType.Ls8 => "LS8",
                XivChatType.CrossLinkShell1 => "CWLS1",
                XivChatType.CrossLinkShell2 => "CWLS2",
                XivChatType.CrossLinkShell3 => "CWLS3",
                XivChatType.CrossLinkShell4 => "CWLS4",
                XivChatType.CrossLinkShell5 => "CWLS5",
                XivChatType.CrossLinkShell6 => "CWLS6",
                XivChatType.CrossLinkShell7 => "CWLS7",
                XivChatType.CrossLinkShell8 => "CWLS8",
                XivChatType.NoviceNetwork => Resources.Channel_NN,
                XivChatType.TellOutgoing => Resources.Channel_Tell,
                XivChatType.TellIncoming => Resources.Channel_Tell,
                XivChatType.Debug => "Echo",
                _ => type.ToString()
            };
        }

        public void Dispose()
        {
            _historyManager.OnMessageAdded -= AddPendingMessage;
            _historyManager.OnMessageUpdated -= UpdateMessage;
            _historyManager.OnHistoryCleared -= ClearMessages;
        }

        private void ClearDisplayedMessages()
        {
            lock (_messagesLock)
            {
                _messages.Clear();
            }
        }

        private void RetryStuckTranslations()
        {
            List<TranslatedChatMessage> toRetry;
            int total;

            lock (_messagesLock)
            {
                total = _messages.Count;
                toRetry = _messages
                    .Where(m => m.IsTranslating || string.IsNullOrEmpty(m.TranslatedText))
                    .ToList();
            }

            foreach (var message in toRetry)
            {
                OnRequestTranslation?.Invoke(message);
            }

            Plugin.PluginLog.Info($"[Retry] Scanned {total} messages, requested retry for {toRetry.Count}.");
        }
    }
}
