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
    /// <summary>
    /// Ventana flotante para mostrar traducciones de mensajes entrantes.
    /// Replicando el comportamiento de ChatTwo (Layout de Tabla de 2 Columnas).
    /// </summary>
    public class TranslatedChatWindow : Window, IDisposable
    {
        private readonly Configuration _configuration;
        public event Action<TranslatedChatMessage>? OnRequestTranslation;
        private readonly MessageHistoryManager _historyManager;
        private readonly List<TranslatedChatMessage> _messages = new();
        private readonly object _messagesLock = new();
        private bool _autoScroll = true;
        private bool _resetPending = false;
        private bool _isOnMainScreen = true;

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
            lock (_messagesLock)
            {
                var existing = _messages.FirstOrDefault(m => m.Id == message.Id);
                if (existing != null)
                {
                    existing.TranslatedText = message.TranslatedText;
                    existing.IsTranslating = false;
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
            // Detectar si estamos en el monitor principal del juego
            var viewport = ImGui.GetWindowViewport();
            var mainViewport = ImGui.GetMainViewport();
            _isOnMainScreen = (viewport.ID == mainViewport.ID);

            DrawToolbar();
            ImGui.Separator();
            DrawMessageList();

            if (_resetPending)
            {
                _resetPending = false;
                PositionCondition = ImGuiCond.Appearing;
                SizeCondition = ImGuiCond.Appearing;
            }
        }

        public override void OnOpen()
        {
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
                ClearMessages();
            }

            if (_configuration.VerboseLogging)
            {
                ImGui.SameLine();
                ImGui.TextDisabled(_isOnMainScreen ? "[Internal]" : "[External]");
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(_isOnMainScreen ? "The window is inside the game monitor." : "The window is on a secondary monitor.");
                }
            }

            ImGui.SameLine();
            lock (_messagesLock)
            {
                var engineName = _configuration.SelectedEngine.ToString();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"[{engineName}] ({_messages.Count})");
            }
        }

        private void DrawMessageList()
        {
            using var child = ImRaii.Child("MessageList", new Vector2(0, 0), true);
            if (!child) return;

            // Reducir espaciado de tabla para que se vea compacto como el chat original
            using var cellSpacing = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(2, 0));
            using var itemSpacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

            // Configurar Tabla de 3 Columnas
            // Columna 0: Timestamps (ancho fijo)
            // Columna 1: Contenido (stretch)
            // Columna 2: Indicador Original [?] (ancho fijo mínimo)
            if (ImGui.BeginTable("ChatBodyTable", 3, ImGuiTableFlags.None))
            {
                // Configurar ancho de columna de tiempo basado en la escala de fuente actual
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

                if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20)
                {
                    ImGui.SetScrollHereY(1.0f);
                }

                ImGui.EndTable();
            }
        }

        private void DrawMessageRow(TranslatedChatMessage message)
        {
            ImGui.TableNextRow();
            var channelColor = GetChannelColor(message.ChatType);
            var channelName = GetChannelName(message.ChatType);

            // --- COLUMNA 0: TIMESTAMP ---
            if (_configuration.ShowTimestamps)
            {
                ImGui.TableSetColumnIndex(0);
                ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), $"[{message.Timestamp:HH:mm}]");
            }

            // --- COLUMNA 1: BLOQUE DE MENSAJE ---
            ImGui.TableSetColumnIndex(1);
            
            // Usar TextWrapPos(0.0f) dentro de la celda de la tabla
            // ImGui Tables manejan el wrapping automáticamente al borde de la celda
            using (var wrap = ImRaii.TextWrapPos(0.0f))
            {
                // Construir la cadena completa para evitar SameLine() que rompe el wrapping
                string fullContent = "";
                
                // Canal y Remitente
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
                    // Imprimir todo junto para que el wrapping sea perfecto desde el inicio de la línea
                    fullContent = senderPart + message.TranslatedText;
                    ImGui.TextColored(channelColor, fullContent);
                }
            }

            // --- COLUMNA 2: INDICADOR ORIGINAL ---
            if (_configuration.ShowOriginalText && !string.Equals(message.OriginalText, message.TranslatedText, StringComparison.OrdinalIgnoreCase))
            {
                ImGui.TableSetColumnIndex(2);
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "[?]");
                if (ImGui.IsItemHovered())
                {
                    using var tooltip = ImRaii.Tooltip();
                    if (tooltip)
                    {
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Resources.ChatWindow_Original);
                        ImGui.TextUnformatted(message.OriginalText);
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
        private void RetryStuckTranslations()
        {
            int total = 0;
            int sent = 0;
            
            lock (_messagesLock)
            {
                total = _messages.Count;
                foreach (var message in _messages)
                {
                    // Reintentar si está marcado como traduciendo o si el resultado parece ser el placeholder
                    if (message.IsTranslating || string.IsNullOrEmpty(message.TranslatedText) || message.TranslatedText == Resources.ChatWindow_Translating)
                    {
                        sent++;
                        OnRequestTranslation?.Invoke(message);
                    }
                }
            }
            
            Plugin.PluginLog.Info($"[Retry] Scanned {total} messages, requested retry for {sent}.");
        }
    }
}
