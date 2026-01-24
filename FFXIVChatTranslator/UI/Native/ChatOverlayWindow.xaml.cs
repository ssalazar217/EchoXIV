using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Dalamud.Game.Text;

namespace FFXIVChatTranslator.UI.Native
{
    public partial class ChatOverlayWindow : Window
    {
        private readonly Configuration _configuration;
        private bool _autoScroll = true;
        private bool _isLocked = false;
        
        // P/Invoke
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080; // Ocultar de Alt-Tab

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public ChatOverlayWindow(Configuration configuration)
        {
            InitializeComponent();
            _configuration = configuration;
            SetOpacity(_configuration.WindowOpacity);
            
            // Restaurar geometría
            this.Left = _configuration.WindowLeft;
            this.Top = _configuration.WindowTop;
            this.Width = _configuration.WindowWidth;
            this.Height = _configuration.WindowHeight;

            // Guardar al cerrar
            this.Closed += (s, e) => SaveGeometry();
        }

        public void SetOpacity(float opacity)
        {
            // Ajustar solo la opacidad del fondo (MainBorder)
            // opacity es 0.0 a 1.0. Color alpha es 0 a 255.
            if (MainBorder != null)
            {
                byte alpha = (byte)(Math.Clamp(opacity, 0f, 1f) * 255);
                // Usamos negro (#000000) como base, ajustando alpha
                MainBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
            }
        }

        public void AddMessage(TranslatedChatMessage message)
        {
            var paragraph = CreateMessageParagraph(message);
            ChatOutput.Document.Blocks.Add(paragraph);
            PruneMessages();
            ScrollToEnd();
        }

        public void UpdateMessage(TranslatedChatMessage message)
        {
            // En una implementación real más compleja, buscaríamos el bloque por ID.
            // Por simplicidad, y como esto suele ser para el último mensaje,
            // podemos simplemente eliminar el último "Traduciendo..." y agregar el nuevo.
            // O mejor: redibujar si es costoso buscar.
            
            // BUSQUEDA SIMPLE: Buscar por Tag (guardaremos el ID en el Tag del parrafo)
            foreach (var block in ChatOutput.Document.Blocks)
            {
                if (block is Paragraph p && p.Tag is Guid id && id == message.Id)
                {
                    // Reconstruir contenido
                    p.Inlines.Clear();
                    PopulateMessageInlines(p, message);
                    break;
                }
            }
        }
        
        public void ToggleVisibility()
        {
             if (this.Visibility == Visibility.Visible)
             {
                this.Hide();
             }
             else
             {
                this.Show();
                this.Topmost = true; // Reforzar
                this.Activate(); // Traer al frente
             }
        }
        
        public void ResetPosition()
        {
            this.Left = 100;
            this.Top = 100;
            this.Show();
            this.Topmost = true; // Forzar Topmost al resetear
            this.Activate();
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            // Re-forzar Topmost cuando perdemos foco (ej: click en el juego)
            this.Topmost = true;
            SaveGeometry();
        }

        private void SaveGeometry()
        {
            if (this.WindowState == WindowState.Normal)
            {
                _configuration.WindowLeft = this.Left;
                _configuration.WindowTop = this.Top;
                _configuration.WindowWidth = this.Width;
                _configuration.WindowHeight = this.Height;
                _configuration.Save();
            }
        }

        private Paragraph CreateMessageParagraph(TranslatedChatMessage message)
        {
            var p = new Paragraph();
            p.Tag = message.Id; // Guardar ID para actualizaciones
            
            // Sangría colgante (Hanging Indent)
            // Margen izquierdo mueve todo el bloque a la derecha
            // TextIndent negativo mueve la primera línea a la izquierda (recuperando el espacio)
            // Resultado: 1ra línea al borde, siguientes líneas indentadas 24px (aprox bajo el timestamp)
            p.Margin = new Thickness(24, 0, 0, _configuration.ChatMessageSpacing); 
            p.TextIndent = -24;
            // Aplicar FontSize localmente si se desea, o heredar de FlowDocument
            
            PopulateMessageInlines(p, message);
            return p;
        }

        private void PopulateMessageInlines(Paragraph p, TranslatedChatMessage message)
        {
            var color = GetChannelColor(message.ChatType);
            var brush = new SolidColorBrush(color);

            // Timestamp
            if (_configuration.ShowTimestamps)
            {
                // Usar formato configurado
                var fmt = _configuration.TimestampFormat;
                p.Inlines.Add(new Run($"[{message.Timestamp.ToString(fmt)}] ") { Foreground = Brushes.Gray });
            }

            // Channel
            p.Inlines.Add(new Run($"[{GetChannelName(message.ChatType)}] ") { Foreground = brush });

            // Sender
            p.Inlines.Add(new Run($"{message.Sender}: ") { Foreground = brush });

            // Message
            if (message.IsTranslating)
            {
                p.Inlines.Add(new Run("Traduciendo...") { Foreground = Brushes.Yellow });
            }
            else
            {
                p.Inlines.Add(new Run(message.TranslatedText) { Foreground = Brushes.White });

                if (_configuration.ShowOriginalText)
                {
                    // Tooltip para original (simple approach: Run con ToolTip)
                    // WPF Run.ToolTip soporta strings directos
                    var originalRun = new Run(" [?]") { Foreground = Brushes.Gray, ToolTip = message.OriginalText, Cursor = Cursors.Help };
                    p.Inlines.Add(originalRun);
                }
            }
        }

        private void PruneMessages()
        {
            while (ChatOutput.Document.Blocks.Count > _configuration.MaxDisplayedMessages)
            {
                ChatOutput.Document.Blocks.Remove(ChatOutput.Document.Blocks.FirstBlock);
            }
        }

        private void ScrollToEnd()
        {
            if (_autoScroll)
            {
                ChatOutput.ScrollToEnd();
            }
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ChatOutput.Document.Blocks.Clear();
        }

        private void Hide_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        private void Lock_Click(object sender, RoutedEventArgs e)
        {
            SetLock(!_isLocked);
        }

        public void SetLock(bool state)
        {
            _isLocked = state;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            
            if (_isLocked)
            {
                // MODO COMPACTO / CLICK-THROUGH
                // 1. Ocultar header y bordes
                // (Necesitamos acceso a los elementos por nombre, asegúrate de añadirlos en XAML si no existen)
                // Asumimos que el border principal es MainBorder
                MainBorder.BorderThickness = new Thickness(0);
                MainBorder.Background = Brushes.Transparent; // Transparente pero... ¿clickeable? No si WS_EX_TRANSPARENT
                
                // Ocultar Header (Asumiendo que el border header es el child 0 del grid)
                // Mejor asignar x:Name="HeaderBorder" al border del header en XAML para ser seguro
                // Por ahora usamos la estructura visual conocida:
                if (MainBorder.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Border header)
                {
                    header.Visibility = Visibility.Collapsed;
                }

                // 2. Hacer click-through
                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
                
                // 3. Bloquear resize
                this.ResizeMode = ResizeMode.NoResize;
                
                BtnLock.Content = "🔒";
            }
            else
            {
                // MODO NORMAL
                // 1. Restaurar estilos visuales
                SetOpacity(_configuration.WindowOpacity); // Restaura color de fondo
                MainBorder.BorderThickness = new Thickness(1);

                if (MainBorder.Child is Grid grid && grid.Children.Count > 0 && grid.Children[0] is Border header)
                {
                    header.Visibility = Visibility.Visible;
                }

                // 2. Quitar click-through
                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
                
                // 3. Restaurar resize
                this.ResizeMode = ResizeMode.CanResizeWithGrip;
                
                 BtnLock.Content = "🔓";
            }
        }
        
        public void UpdateVisuals()
        {
            // Actualizar estilo global del documento
            if (ChatOutput.Document != null)
            {
                ChatOutput.Document.FontSize = _configuration.FontSize;
                
                // Actualizar bloques existentes (spacing, timestamps format si cambió)
                // Nota: Cambiar formato de timestamp en mensajes ya recibidos requiere reconstruir Inlines
                // Por simplicidad, solo actualizamos Spacing y FontSize aquí.
                foreach (var block in ChatOutput.Document.Blocks)
                {
                    if (block is Paragraph p)
                    {
                        p.Margin = new Thickness(24, 0, 0, _configuration.ChatMessageSpacing);
                    }
                }
            }
        }

        // --- Helpers de Colores y Nombres (Copia adaptada de TranslatedChatWindow) ---

        private Color GetChannelColor(XivChatType type)
        {
             // Usando los colores definidos previamente (Chat2 style)
             // Nota: WPF Color usa bytes 0-255
            return type switch
            {
                XivChatType.Say => Color.FromRgb(247, 247, 247),
                XivChatType.Shout => Color.FromRgb(255, 166, 102),
                XivChatType.Yell => Color.FromRgb(255, 255, 0),
                XivChatType.Party => Color.FromRgb(102, 229, 255),
                XivChatType.Alliance => Color.FromRgb(255, 127, 0),
                XivChatType.FreeCompany => Color.FromRgb(171, 219, 229),
                XivChatType.TellIncoming or XivChatType.TellOutgoing => Color.FromRgb(255, 184, 222),
                XivChatType.NoviceNetwork => Color.FromRgb(212, 255, 125),
                // Linkshells
                XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4 or
                XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8 or
                XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3 or
                XivChatType.CrossLinkShell4 or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6 or
                XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8 
                    => Color.FromRgb(212, 255, 125),
                _ => Color.FromRgb(204, 204, 204)
            };
        }
        
        private string GetChannelName(XivChatType type)
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
                _ => type.ToString()
            };
        }
    }
}
