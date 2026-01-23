using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using EchoXIV.Resources;
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
            : base(Loc.ChatWindow_Title + "###TranslatedChatWindow")
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

        public override bool DrawConditions() 
        {
            if (!Plugin.IsChatVisible()) return false;

            // Smart Visibility: Ocultar si el juego no tiene el foco (opcional)
            if (_configuration.SmartVisibility)
            {
                var foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero) return false;

                GetWindowThreadProcessId(foreground, out uint foregroundPid);
                var currentPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                
                // Si el foco no está en nuestro proceso, ocultamos
                if (foregroundPid != currentPid) return false;
            }

            return true;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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
            if (ImGui.Checkbox(Loc.ChatWindow_Active, ref enabled))
            {
                _configuration.IncomingTranslationEnabled = enabled;
                _configuration.Save();
            }

            ImGui.SameLine();

            // Auto-scroll toggle
            ImGui.Checkbox(Loc.ChatWindow_AutoScroll, ref _autoScroll);

            ImGui.SameLine();

            // Botón limpiar
            if (ImGui.Button(Loc.ChatWindow_Clear))
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
            if (ImGui.BeginChild("MessageList", new Vector2(0, 0), true))
            {
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
            ImGui.EndChild();
        }

        private void DrawMessage(TranslatedChatMessage message)
        {
            // Color según canal
            var channelColor = GetChannelColor(message.ChatType);
            var channelName = GetChannelName(message.ChatType);
            
            var startX = ImGui.GetCursorPosX();
            var startY = ImGui.GetCursorPosY();
            
            // Calculamos una indentación fija para paridad con WPF (24-30px aprox)
            // O podemos hacerla dinámica según el contenido del prefix.
            // Para paridad exacta con el estilo de "hanging indent" de WPF:
            float indentSize = 30.0f;
            if (_configuration.ShowTimestamps) indentSize += 40.0f; // Ajuste si hay timestamp

            // 1. Dibujar Metadatos (Prefix)
            ImGui.BeginGroup();
            
            // Timestamp
            if (_configuration.ShowTimestamps)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), $"[{message.Timestamp:HH:mm}]");
                ImGui.SameLine();
            }

            // Canal + Sender
            if (message.ChatType == XivChatType.TellOutgoing)
            {
                var name = string.IsNullOrEmpty(message.Recipient) ? message.Sender : message.Recipient;
                ImGui.TextColored(channelColor, $"[Tell] >> {name}:");
            }
            else if (message.ChatType == XivChatType.TellIncoming)
            {
                ImGui.TextColored(channelColor, $"[Tell] << {message.Sender}:");
            }
            else
            {
                ImGui.TextColored(channelColor, $"[{channelName}] {message.Sender}:");
            }
            
            ImGui.EndGroup();

            // 2. Dibujar Mensaje con Alineación (Hanging Indent simulado)
            // Si el prefix es corto, el texto empieza en la misma línea.
            // Si el texto envuelve, queremos que las siguientes líneas estén indentadas.
            
            ImGui.SameLine();
            
            if (message.IsTranslating)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.2f, 1f), Loc.ChatWindow_Translating);
            }
            else
            {
                ImGui.PushTextWrapPos(0.0f);
                
                // Si el texto traducido es igual al original, solo mostramos uno (el original)
                if (string.Equals(message.OriginalText, message.TranslatedText, StringComparison.OrdinalIgnoreCase))
                {
                    ImGui.Text(message.OriginalText);
                }
                else
                {
                    // Mostramos "Original -> Traducción" o similar para satisfacer "idioma de origen"
                    ImGui.Text(message.OriginalText);
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.5f, 1f), "->");
                    ImGui.SameLine();
                    ImGui.Text(message.TranslatedText);
                }
                
                // Indicador de original [?] (Mantenemos por si acaso, o como tooltip de ayuda)
                if (_configuration.ShowOriginalText)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "[?]");
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), Loc.ChatWindow_Original);
                        ImGui.Text(message.OriginalText);
                        ImGui.EndTooltip();
                    }
                }
                ImGui.PopTextWrapPos();
            }
        }

        /// <summary>
        /// Colores de chat (valores RGB normalizados a 0-1)
        /// </summary>
        private static Vector4 GetChannelColor(XivChatType type)
        {
            return type switch
            {
                // Colores oficiales (RGB/255 convertidos a 0-1)
                XivChatType.Say => new Vector4(247f/255f, 247f/255f, 247f/255f, 1f),           // #F7F7F7
                XivChatType.Shout => new Vector4(255f/255f, 166f/255f, 102f/255f, 1f),         // #FFA666
                XivChatType.Yell => new Vector4(255f/255f, 255f/255f, 0f/255f, 1f),            // #FFFF00
                XivChatType.Party => new Vector4(102f/255f, 229f/255f, 255f/255f, 1f),         // #66E5FF
                XivChatType.Alliance => new Vector4(255f/255f, 127f/255f, 0f/255f, 1f),        // #FF7F00
                XivChatType.FreeCompany => new Vector4(171f/255f, 219f/255f, 229f/255f, 1f),   // #ABDBE5
                XivChatType.TellIncoming or XivChatType.TellOutgoing 
                    => new Vector4(255f/255f, 184f/255f, 222f/255f, 1f),                       // #FFB8DE
                XivChatType.NoviceNetwork => new Vector4(212f/255f, 255f/255f, 125f/255f, 1f), // #D4FF7D
                XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4
                    or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8
                    or XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3
                    or XivChatType.CrossLinkShell4 or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6
                    or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8
                    => new Vector4(212f/255f, 255f/255f, 125f/255f, 1f),                       // #D4FF7D (Linkshells)
                _ => new Vector4(204f/255f, 204f/255f, 204f/255f, 1f)                          // #CCCCCC default
            };
        }

        private static string GetChannelName(XivChatType type)
        {
            return type switch
            {
                XivChatType.Say => "Say",
                XivChatType.Shout => "Shout",
                XivChatType.Yell => "Yell",
                XivChatType.Party => "Party",
                XivChatType.Alliance => "Alliance",
                XivChatType.FreeCompany => "FC",
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
                XivChatType.NoviceNetwork => "NN",
                XivChatType.TellOutgoing => "Tell",
                XivChatType.TellIncoming => "Tell",
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
