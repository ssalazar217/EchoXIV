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
    /// Ventana flotante para mostrar traducciones de mensajes entrantes
    /// Compatible con Multi-Monitor de Dalamud
    /// </summary>
    public class TranslatedChatWindow : Window, IDisposable
    {
        private readonly Configuration _configuration;
        private readonly MessageHistoryManager _historyManager;
        private readonly List<TranslatedChatMessage> _messages = new();
        private readonly object _messagesLock = new();
        private bool _autoScroll = true;
        private bool _resetPending = false;

        public TranslatedChatWindow(Configuration configuration, MessageHistoryManager historyManager)
            : base(Resources.ChatWindow_Title + "###TranslatedChatWindow")
        {
            _configuration = configuration;
            _historyManager = historyManager;

            // Sincronizar con historial existente
            lock (_messagesLock)
            {
                _messages.AddRange(_historyManager.GetHistory());
            }

            // Suscribirse a eventos
            _historyManager.OnMessageAdded += AddPendingMessage;
            _historyManager.OnMessageUpdated += UpdateMessage;
            _historyManager.OnHistoryCleared += ClearMessages;

            // Flags básicos (permitir movimiento y resize)
            Flags = ImGuiWindowFlags.NoScrollbar 
                  | ImGuiWindowFlags.NoScrollWithMouse;

            // Cargar posición y tamaño desde configuración para independencia total
            Position = _configuration.ImGuiPosition;
            Size = _configuration.ImGuiSize;
            PositionCondition = ImGuiCond.Appearing;
            SizeCondition = ImGuiCond.Appearing;

            // Sincronizar visibilidad inicial
            IsOpen = _configuration.OverlayVisible;
        }

        /// <summary>
        /// Agrega un mensaje que está siendo traducido (muestra "...")
        /// </summary>
        public void AddPendingMessage(TranslatedChatMessage message)
        {
            lock (_messagesLock)
            {
                _messages.Add(message);
                PruneMessages();
            }
        }

        /// <summary>
        /// Actualiza un mensaje con su traducción completada
        /// </summary>
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

        /// <summary>
        /// Limpia mensajes antiguos según el límite configurado
        /// </summary>
        private void PruneMessages()
        {
            while (_messages.Count > _configuration.MaxDisplayedMessages)
            {
                _messages.RemoveAt(0);
            }
        }

        /// <summary>
        /// Limpia todos los mensajes
        /// </summary>
        public void ClearMessages()
        {
            lock (_messagesLock)
            {
                _messages.Clear();
            }
        }


        public override void Draw()
        {
            // Toolbar
            DrawToolbar();

            ImGui.Separator();

            // Lista de mensajes
            DrawMessageList();

            // Sincronizar posición y tamaño con la configuración de EchoXIV
            // (Hacemos esto manualmente para que sea independiente de imgui.ini y de WPF)
            var currentPos = ImGui.GetWindowPos();
            var currentSize = ImGui.GetWindowSize();

            if (currentPos != _configuration.ImGuiPosition)
            {
                _configuration.ImGuiPosition = currentPos;
                _configuration.Save();
            }
            if (currentSize != _configuration.ImGuiSize)
            {
                _configuration.ImGuiSize = currentSize;
                _configuration.Save();
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
            // Toggle de traducción entrante
            var enabled = _configuration.IncomingTranslationEnabled;
            if (ImGui.Checkbox(Resources.ChatWindow_Active, ref enabled))
            {
                _configuration.IncomingTranslationEnabled = enabled;
                _configuration.Save();
            }

            ImGui.SameLine();

            // Auto-scroll toggle
            ImGui.Checkbox(Resources.ChatWindow_AutoScroll, ref _autoScroll);

            ImGui.SameLine();

            // Botón limpiar
            if (ImGui.Button(Resources.ChatWindow_Clear))
            {
                ClearMessages();
            }

            ImGui.SameLine();

            // Contador de mensajes
            lock (_messagesLock)
            {
                var engineName = _configuration.SelectedEngine.ToString();
                ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), $"[{engineName}] ({_messages.Count})");
            }
        }

        private void DrawMessageList()
        {
            // Región scrollable
            using var child = ImRaii.Child("MessageList", new Vector2(0, 0), true);
            if (child)
            {
                // Eliminar espacio vertical entre items
                using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(4.0f, 0.0f));
                lock (_messagesLock)
                {
                    foreach (var message in _messages)
                    {
                        DrawMessage(message);
                        
                        // Espaciado entre mensajes (paridad con WPF)
                        if (_configuration.ChatMessageSpacing > 0)
                        {
                            ImGui.Dummy(new Vector2(0, _configuration.ChatMessageSpacing));
                        }
                    }
                }

                // Auto-scroll al final
                if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 20)
                {
                    ImGui.SetScrollHereY(1.0f);
                }
            }
        }

        private void DrawMessage(TranslatedChatMessage message)
        {
            var channelColor = GetChannelColor(message.ChatType);
            var channelName = GetChannelName(message.ChatType);
            
            // 1. Construir el prefijo exactamente igual a WPF
            string prefix = "";
            if (_configuration.ShowTimestamps)
            {
                prefix += $"[{message.Timestamp:HH:mm}] ";
            }

            if (message.ChatType == XivChatType.TellOutgoing)
            {
                var name = string.IsNullOrEmpty(message.Recipient) ? message.Sender : message.Recipient;
                prefix += $"[{Resources.Channel_Tell}] >> {name}: ";
            }
            else if (message.ChatType == XivChatType.TellIncoming)
            {
                prefix += $"[{Resources.Channel_Tell}] << {message.Sender}: ";
            }
            else
            {
                prefix += $"[{channelName}] {message.Sender}: ";
            }

            // 2. Dibujar Prefijo
            ImGui.TextColored(channelColor, prefix);
            
            // 3. Dibujar Mensaje junto al nombre con wrapping
            ImGui.SameLine(0, 0);
            
            using (var wrap = ImRaii.TextWrapPos(0.0f))
            {
                if (message.IsTranslating)
                {
                    ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1f), Resources.ChatWindow_Translating);
                }
                else
                {
                    ImGui.TextUnformatted(message.TranslatedText);
                }
            }
            
            // Indicador de original [?] con tooltip
            if (_configuration.ShowOriginalText && !string.Equals(message.OriginalText, message.TranslatedText, StringComparison.OrdinalIgnoreCase))
            {
                ImGui.SameLine();
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

        /// <summary>
        /// Colores de chat (valores RGB normalizados a 0-1)
        /// </summary>
        private Vector4 GetChannelColor(XivChatType type)
        {
            int typeId = (int)type;
            
            // Especiales (Tells y Linkshells suelen compartir color)
            if (type == XivChatType.TellIncoming || type == XivChatType.TellOutgoing) typeId = 13;
            if (typeId >= 16 && typeId <= 23) typeId = 16; // LS1-8
            if (typeId >= 101 && typeId <= 108) typeId = 16; // CWLS1-8

            if (_configuration.ChannelColors.TryGetValue(typeId, out var colorValue))
            {
                return ColorToVector4(colorValue);
            }

            // Fallback
            return new Vector4(204f/255f, 204f/255f, 204f/255f, 1f); // #CCCCCC default
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
    }
}
