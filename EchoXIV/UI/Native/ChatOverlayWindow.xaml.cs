using System;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Linq.Expressions;
using Dalamud.Game.Text;
using EchoXIV.Services;
using EchoXIV.Properties;

namespace EchoXIV.UI.Native
{
    public class ChatOverlayWindow
    {
        private readonly Configuration _configuration;
        private readonly MessageHistoryManager _historyManager;
        private bool _autoScroll = true;
        private bool _isLocked = false;
        
        private dynamic _window = null!;
        private dynamic _chatOutput = null!;
        private dynamic _titleText = null!;
        private dynamic _mainBorder = null!;
        private dynamic _headerBorder = null!;
        private dynamic _btnUnlockOverlay = null!;
        private dynamic _btnLock = null!;

        public ChatOverlayWindow(Configuration configuration, MessageHistoryManager historyManager)
        {
            _configuration = configuration;
            _historyManager = historyManager;

            if (NativeUiLoader.IsWindows)
            {
                InitializeDynamicWindow();
            }
        }

        private void InitializeDynamicWindow()
        {
            var xaml = NativeUiLoader.GetEmbeddedResource("EchoXIV.UI.Native.ChatOverlayWindow.xaml");
            _window = NativeUiLoader.LoadXaml(xaml);
            if (_window == null) return;

            // Encontrar elementos por nombre
            _chatOutput = _window.FindName("ChatOutput");
            _titleText = _window.FindName("TitleText");
            _mainBorder = _window.FindName("MainBorder");
            _headerBorder = _window.FindName("HeaderBorder");
            _btnUnlockOverlay = _window.FindName("BtnUnlockOverlay");
            _btnLock = _window.FindName("BtnLock");

            // Usaremos una forma robusta con Expression Trees para evitar problemas de tipos de delegados
            HookEvent(_window.FindName("BtnLock"), "Click", (Action<object, dynamic>)((s, e) => Lock_Click(s, e)));
            HookEvent(_window.FindName("BtnUnlockOverlay"), "Click", (Action<object, dynamic>)((s, e) => UnlockOverlay_Click(s, e)));
            HookEvent(_window.FindName("BtnClear"), "Click", (Action<object, dynamic>)((s, e) => Clear_Click(s, e)));
            HookEvent(_window.FindName("BtnHide"), "Click", (Action<object, dynamic>)((s, e) => Hide_Click(s, e)));
            
            if (_headerBorder != null) {
                HookEvent(_headerBorder, "MouseLeftButtonDown", (Action<object, dynamic>)((s, e) => Header_MouseDown(s, e)));
            }

            if (_chatOutput != null)
            {
                // Suscribirse a historial
                _historyManager.OnMessageAdded += m => _window?.Dispatcher.InvokeAsync((Action)(() => AddMessage(m)));
                _historyManager.OnMessageUpdated += m => _window?.Dispatcher.InvokeAsync((Action)(() => UpdateMessage(m)));
                _historyManager.OnHistoryCleared += () => _window?.Dispatcher.InvokeAsync((Action)(() => _chatOutput?.Document.Blocks.Clear()));
            }

            // Estado inicial
            if (!_configuration.OverlayVisible && _window != null) 
            {
                var collapsed = NativeUiLoader.GetEnumValue("PresentationCore", "System.Windows.Visibility", 2);
                if (collapsed != null) _window.Visibility = (dynamic)collapsed;
            }
            
            SetOpacity(_configuration.WindowOpacity);
            if (_window != null)
            {
                _window.Left = _configuration.WindowLeft;
                _window.Top = _configuration.WindowTop;
                _window.Width = _configuration.WindowWidth;
                _window.Height = _configuration.WindowHeight;

                _window.Closed += (EventHandler)((s, e) => SaveGeometry());
            }

            // Localizar elementos estáticos
            if (_titleText != null) _titleText.Text = Resources.ChatWindow_Title;
            if (_headerBorder != null) _headerBorder.ToolTip = Resources.ChatWindow_HideTooltip; // Opcional, o al botón X
            if (_btnLock != null) _btnLock.ToolTip = Resources.ChatWindow_LockTooltip;
            var btnHide = _window.FindName("BtnHide");
            if (btnHide != null) btnHide.ToolTip = Resources.ChatWindow_HideTooltip;
            var btnClear = _window.FindName("BtnClear");
            if (btnClear != null) {
                btnClear.Content = Resources.ChatWindow_Clear;
                btnClear.ToolTip = Resources.ChatWindow_ClearTooltip;
            }
            if (_btnUnlockOverlay != null) _btnUnlockOverlay.ToolTip = Resources.ChatWindow_UnlockTooltip;

            UpdateTitle();

            foreach (var msg in _historyManager.GetHistory()) AddMessage(msg);
        }

        private void HookEvent(dynamic element, string eventName, Action<object, dynamic> action)
        {
            if (element == null) return;
            try {
                EventInfo? ev = element.GetType().GetEvent(eventName);
                if (ev == null) return;
                
                var handlerType = ev.EventHandlerType!;
                var methodInfo = handlerType.GetMethod("Invoke")!;
                var parameterTypes = methodInfo.GetParameters().Select(p => p.ParameterType).ToArray();
                
                var parameters = parameterTypes.Select(t => Expression.Parameter(t)).ToArray();
                var actionInvoke = typeof(Action<object, dynamic>).GetMethod("Invoke")!;
                var callAction = Expression.Call(
                    Expression.Constant(action),
                    actionInvoke,
                    Expression.Convert(parameters[0], typeof(object)),
                    Expression.Convert(parameters[1], typeof(object))
                );
                
                var lambda = Expression.Lambda(handlerType, callAction, parameters);
                ev.AddEventHandler(element, lambda.Compile());
            } catch { }
        }


        public IntPtr GetHandle()
        {
            if (!NativeUiLoader.IsWindows || _window == null) return IntPtr.Zero;
            var helperType = Type.GetType("System.Windows.Interop.WindowInteropHelper, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            if (helperType == null) return IntPtr.Zero;
            
            var helper = Activator.CreateInstance(helperType, (object)_window);
            var handleProp = helperType.GetProperty("Handle");
            if (handleProp == null || helper == null) return IntPtr.Zero;
            
            var value = handleProp.GetValue(helper);
            return value is IntPtr ptr ? ptr : IntPtr.Zero;
        }

        public void Show() { if (NativeUiLoader.IsWindows && _window != null) _window.Dispatcher.InvokeAsync((Action)(() => ((object)_window).GetType().GetMethod("Show")?.Invoke(_window, null))); }
        public void Hide() { if (NativeUiLoader.IsWindows && _window != null) _window.Dispatcher.InvokeAsync((Action)(() => ((object)_window).GetType().GetMethod("Hide")?.Invoke(_window, null))); }
        public void Close() { if (NativeUiLoader.IsWindows && _window != null) _window.Dispatcher.InvokeAsync((Action)(() => ((object)_window).GetType().GetMethod("Close")?.Invoke(_window, null))); }
        public bool IsVisible() => NativeUiLoader.IsWindows && _window != null && Equals(_window.Visibility, NativeUiLoader.GetEnumValue("PresentationCore", "System.Windows.Visibility", 0));

        private void UpdateTitle()
        {
            if (_titleText != null && _chatOutput != null)
            {
                var count = _chatOutput.Document.Blocks.Count;
                _titleText.Text = $"{Resources.ChatWindow_Title} [{_configuration.SelectedEngine}] ({count})";
            }
        }

        public void SetOpacity(float opacity)
        {
            if (_window == null || _mainBorder == null) return;
            
            _window.Dispatcher.Invoke((Action)(() => 
            {
                try {
                    byte alpha = (byte)(Math.Clamp(opacity, 0f, 1f) * 255);
                    var colorType = Type.GetType("System.Windows.Media.Color, PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                    if (colorType == null) return;
                    
                    var fromRgb = colorType.GetMethod("FromArgb", new[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte) });
                    if (fromRgb == null) return;
                    
                    var color = fromRgb.Invoke(null, new object[] { alpha, (byte)0, (byte)0, (byte)0 });
                    if (color == null) return;
                    
                    var brushType = Type.GetType("System.Windows.Media.SolidColorBrush, PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                    if (brushType == null) return;
                    
                    dynamic? brush = Activator.CreateInstance(brushType, color);
                    if (brush != null) _mainBorder.Background = brush;
                } catch { }
            }));
        }

        public void AddMessage(TranslatedChatMessage message)
        {
            if (!NativeUiLoader.IsWindows || _chatOutput == null) return;
            dynamic? paragraph = CreateMessageParagraph(message);
            if (paragraph != null)
            {
                _chatOutput.Document.Blocks.Add(paragraph);
                PruneMessages();
                ScrollToEnd();
                UpdateTitle();
            }
        }

        public void UpdateMessage(TranslatedChatMessage message)
        {
            if (!NativeUiLoader.IsWindows || _chatOutput == null) return;
            foreach (dynamic block in _chatOutput.Document.Blocks)
            {
                if (block.Tag is Guid id && id == message.Id)
                {
                    block.Inlines.Clear();
                    PopulateMessageInlines(block, message);
                    break;
                }
            }
        }
        
        public void ToggleVisibility()
        {
             if (IsVisible())
             {
                _configuration.OverlayVisible = false;
                Hide();
             }
             else
             {
                _configuration.OverlayVisible = true;
                Show();
                if (_window != null)
                {
                    _window.Topmost = true;
                    _window.Activate();
                }
             }
             _configuration.Save();
        }
        
        public void ResetPosition()
        {
            if (!NativeUiLoader.IsWindows || _window == null) return;
            _window.Dispatcher.InvokeAsync((Action)(() =>
            {
                _window.Left = 100;
                _window.Top = 100;
                Show();
                if (_window != null)
                {
                    _window.Topmost = true;
                    _window.Activate();
                }
            }));
        }

        private void SaveGeometry()
        {
            if (_window.WindowState == 0) // Normal
            {
                _configuration.WindowLeft = _window.Left;
                _configuration.WindowTop = _window.Top;
                _configuration.WindowWidth = _window.Width;
                _configuration.WindowHeight = _window.Height;
                _configuration.Save();
            }
        }

        private dynamic? CreateMessageParagraph(TranslatedChatMessage message)
        {
            dynamic? p = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Documents.Paragraph");
            if (p == null) return null;
            p.Tag = message.Id; 
            
            var thicknessType = Type.GetType("System.Windows.Thickness, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            if (thicknessType != null)
            {
                var thickness = Activator.CreateInstance(thicknessType, 2.0, 0.0, 0.0, 0.0);
                if (thickness != null) p.Margin = (dynamic)thickness;
            }
            p.TextIndent = 0.0;
            
            // Ajustar altura de línea de forma segura para diseño denso
            try {
                p.LineHeight = 1.0; 
                var stackType = Type.GetType("System.Windows.LineStackingStrategy, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                if (stackType != null) {
                    p.LineStackingStrategy = Enum.Parse(stackType, "BlockLineHeight");
                }
            } catch { /* Ignorar si falla el bind dinámico */ }
            
            PopulateMessageInlines(p, message);
            return p;
        }

        private void PopulateMessageInlines(dynamic p, TranslatedChatMessage message)
        {
            var color = GetChannelColor(message.ChatType);
            var brushType = Type.GetType("System.Windows.Media.SolidColorBrush, PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            if (brushType == null) return;
            dynamic? brush = Activator.CreateInstance(brushType, color);
            if (brush == null) return;

            if (_configuration.ShowTimestamps)
            {
                dynamic? run = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Documents.Run", $"[{message.Timestamp.ToString(_configuration.TimestampFormat)}] ");
                if (run != null)
                {
                    run.Foreground = (dynamic)GetStaticProperty("System.Windows.Media.Brushes, PresentationCore", "Gray");
                    p.Inlines.Add(run);
                }
            }

            string prefix = "";
            if (message.ChatType == XivChatType.TellOutgoing) prefix = $"[Tell] >> {(string.IsNullOrEmpty(message.Recipient) ? message.Sender : message.Recipient)}: ";
            else if (message.ChatType == XivChatType.TellIncoming) prefix = $"[Tell] << {message.Sender}: ";
            else prefix = $"[{GetChannelName(message.ChatType)}] {message.Sender}: ";

            dynamic? prefixRun = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Documents.Run", prefix);
            if (prefixRun != null)
            {
                prefixRun.Foreground = (dynamic)brush;
                p.Inlines.Add(prefixRun);
            }

            if (message.IsTranslating)
            {
                dynamic? run = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Documents.Run", Resources.ChatWindow_Translating);
                if (run != null)
                {
                    run.Foreground = (dynamic)GetStaticProperty("System.Windows.Media.Brushes, PresentationCore", "Yellow");
                    p.Inlines.Add(run);
                }
            }
            else
            {
                AddTextWithUrls(p, message.TranslatedText, (dynamic)GetStaticProperty("System.Windows.Media.Brushes, PresentationCore", "White"));
                if (_configuration.ShowOriginalText)
                {
                    dynamic? originalRun = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Documents.Run", " [?]");
                    if (originalRun != null)
                    {
                        originalRun.Foreground = (dynamic)GetStaticProperty("System.Windows.Media.Brushes, PresentationCore", "Gray");
                        originalRun.ToolTip = message.OriginalText;
                        p.Inlines.Add(originalRun);
                    }
                }
            }
        }

        private void PruneMessages()
        {
            if (_chatOutput == null) return;
            while ((int)_chatOutput.Document.Blocks.Count > _configuration.MaxDisplayedMessages)
            {
                _chatOutput.Document.Blocks.Remove(_chatOutput.Document.Blocks.FirstBlock);
            }
            UpdateTitle();
        }

        private void ScrollToEnd()
        {
            if (_autoScroll) _chatOutput?.ScrollToEnd();
        }

        private void Header_MouseDown(object sender, dynamic e)
        {
            if (NativeUiLoader.IsWindows && _window != null)
            {
                try {
                    if (e.ChangedButton == 0) _window?.DragMove();
                } catch { }
            }
        }

        private void Lock_Click(object? sender, EventArgs e) => SetLock(!_isLocked);
        private void UnlockOverlay_Click(object? sender, EventArgs e) => SetLock(false);
        private void Clear_Click(object? sender, EventArgs e) { _chatOutput.Document.Blocks.Clear(); UpdateTitle(); }
        private void Hide_Click(object? sender, EventArgs e) { _configuration.OverlayVisible = false; _configuration.Save(); Hide(); }

        public void SetLock(bool state)
        {
            if (_window == null) return;
            _window.Dispatcher.InvokeAsync((Action)(() =>
            {
                _isLocked = state;
                if (_isLocked)
                {
                    _mainBorder.IsHitTestVisible = false;
                    _mainBorder.Background = (dynamic)GetStaticProperty("System.Windows.Media.Brushes, PresentationCore", "Transparent");
                    var thinType = Type.GetType("System.Windows.Thickness, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                    var thickness = Activator.CreateInstance(thinType!, 0.0);
                    if (thickness != null) _mainBorder.BorderThickness = (dynamic)thickness;
                    
                    if (_headerBorder != null) 
                    {
                        var collapsed = NativeUiLoader.GetEnumValue("PresentationCore", "System.Windows.Visibility", 2);
                        if (collapsed != null) _headerBorder.Visibility = (dynamic)collapsed;
                    }
                    if (_btnUnlockOverlay != null)
                    {
                        var visible = NativeUiLoader.GetEnumValue("PresentationCore", "System.Windows.Visibility", 0);
                        if (visible != null) _btnUnlockOverlay.Visibility = (dynamic)visible;
                    }
                    
                    var noResize = NativeUiLoader.GetEnumValue("PresentationFramework", "System.Windows.ResizeMode", 0);
                    if (noResize != null) _window.ResizeMode = (dynamic)noResize;

                    if (_btnLock != null) _btnLock.Content = "🔒";
                }
                else
                {
                    _mainBorder.IsHitTestVisible = true;
                    SetOpacity(_configuration.WindowOpacity);
                    var thinType = Type.GetType("System.Windows.Thickness, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                    var thickness = Activator.CreateInstance(thinType!, 1.0);
                    if (thickness != null) _mainBorder.BorderThickness = (dynamic)thickness;
                    
                    if (_headerBorder != null)
                    {
                        var visible = NativeUiLoader.GetEnumValue("PresentationCore", "System.Windows.Visibility", 0);
                        if (visible != null) _headerBorder.Visibility = (dynamic)visible;
                    }
                    if (_btnUnlockOverlay != null)
                    {
                        var collapsed = NativeUiLoader.GetEnumValue("PresentationCore", "System.Windows.Visibility", 2);
                        if (collapsed != null) _btnUnlockOverlay.Visibility = (dynamic)collapsed;
                    }

                    var canResize = NativeUiLoader.GetEnumValue("PresentationFramework", "System.Windows.ResizeMode", 2);
                    if (canResize != null) _window.ResizeMode = (dynamic)canResize;

                    if (_btnLock != null) _btnLock.Content = "🔓";
                }
            }));
        }
        
        public void UpdateVisuals()
        {
            if (_window == null || _chatOutput == null) return;

            _window.Dispatcher.Invoke((Action)(() =>
            {
                try {
                    _chatOutput.Document.FontSize = (double)_configuration.FontSize;
                    var thinType = Type.GetType("System.Windows.Thickness, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                    if (thinType != null)
                    {
                        foreach (dynamic block in _chatOutput.Document.Blocks)
                        {
                            var thickness = Activator.CreateInstance(thinType, 2.0, 0.0, 0.0, (double)_configuration.ChatMessageSpacing);
                            if (thickness != null) block.Margin = (dynamic)thickness;
                        }
                    }
                } catch { }
            }));
        }

        private void AddTextWithUrls(dynamic p, string text, dynamic foreground)
        {
            var regex = new Regex(@"(https?://[^\s]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            var matches = regex.Matches(text);
            int lastIndex = 0;
            foreach (Match match in matches)
            {
                if (match.Index > lastIndex) {
                    dynamic? run = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Documents.Run", text.Substring(lastIndex, match.Index - lastIndex));
                    if (run != null)
                    {
                        run.Foreground = foreground;
                        p.Inlines.Add(run);
                    }
                }
                
                string url = match.Value;
                try
                {
                    dynamic? runUrl = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Documents.Run", url);
                    if (runUrl != null)
                    {
                        dynamic? link = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Documents.Hyperlink", runUrl);
                        if (link != null)
                        {
                            link.NavigateUri = new Uri(url);
                            p.Inlines.Add(link);
                        }
                    }
                }
                catch { 
                    dynamic? run = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Documents.Run", url);
                    if (run != null)
                    {
                        run.Foreground = foreground;
                        p.Inlines.Add(run); 
                    }
                }
                lastIndex = match.Index + match.Length;
            }
            if (lastIndex < text.Length) {
                dynamic? run = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Documents.Run", text.Substring(lastIndex));
                if (run != null)
                {
                    run.Foreground = foreground;
                    p.Inlines.Add(run);
                }
            }
        }

        private object GetChannelColor(XivChatType type)
        {
            var colorType = Type.GetType("System.Windows.Media.Color, PresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
            var fromRgb = colorType?.GetMethod("FromRgb", new[] { typeof(byte), typeof(byte), typeof(byte) });
            
            var (r, g, b) = type switch {
                XivChatType.Say => (247, 247, 247),
                XivChatType.Shout => (255, 166, 102),
                XivChatType.Yell => (255, 255, 0),
                XivChatType.Party => (102, 229, 255),
                XivChatType.Alliance => (255, 127, 0),
                XivChatType.FreeCompany => (171, 219, 229),
                XivChatType.TellIncoming or XivChatType.TellOutgoing => (255, 184, 222),
                XivChatType.NoviceNetwork => (212, 255, 125),
                XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4 
                    or XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8 
                    => (212, 255, 125),
                XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3 
                    or XivChatType.CrossLinkShell4 or XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6 
                    or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8 
                    => (212, 255, 125),
                _ => (204, 204, 204)
            };
            return fromRgb?.Invoke(null, new object[] { (byte)r, (byte)g, (byte)b }) ?? new object();
        }

        private dynamic GetStaticProperty(string typeName, string propName)
        {
            var type = Type.GetType(typeName);
            return type?.GetProperty(propName)?.GetValue(null) ?? new object();
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
                XivChatType.TellOutgoing => "Tell",
                XivChatType.TellIncoming => "Tell",
                XivChatType.Debug => "Echo",
                _ => type.ToString()
            };
        }
    }
}
