using System;
using System.Threading;
using System.Runtime.InteropServices; 
using System.Diagnostics;
using System.Linq;

namespace EchoXIV.Services
{
    public class WpfHost : IDisposable
    {
        private Thread? _wpfThread;
        private dynamic? _wpfApp;
        private dynamic? _chatWindow;
        private readonly Configuration _configuration;
        private bool _isRunning;

        // Evento para señalizar que el host está listo
        private readonly ManualResetEvent _readyEvent = new(false);
        
        // Smart Visibility
        private dynamic? _visibilityTimer;
        private IntPtr _gameWindowHandle;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private readonly Dalamud.Plugin.Services.IPluginLog _logger;
        private readonly MessageHistoryManager _historyManager;
        private IntPtr _chatWindowHandle = IntPtr.Zero;
        private uint _currentPid;

        public WpfHost(Configuration configuration, Dalamud.Plugin.Services.IPluginLog logger, MessageHistoryManager historyManager)
        {
            _configuration = configuration;
            _logger = logger;
            _historyManager = historyManager;
        }

        public bool IsInitialized { get; private set; } = false;

        public void Start()
        {
            if (_isRunning) return;
            if (!NativeUiLoader.IsWindows)
            {
                _logger.Warning("WpfHost: Intento de inicio en plataforma no compatible (No-Windows).");
                return;
            }

            _isRunning = true;
            _readyEvent.Reset();

            _wpfThread = new Thread(WpfThreadEntryPoint);
            _wpfThread.SetApartmentState(ApartmentState.STA);
            _wpfThread.IsBackground = true;
            _wpfThread.Name = "EchoXIV_WpfHost";
            _wpfThread.Start();

            // Esperar a que la ventana esté lista (con timeout)
            _readyEvent.WaitOne(5000);
        }

        private void WpfThreadEntryPoint()
        {
            _logger.Info("Iniciando hilo WPF (Dynamic Bridge)...");
            
            if (!NativeUiLoader.TryLoadWpf(_logger))
            {
                _logger.Error("No se pudieron cargar las librerías de WPF necesarias.");
                _readyEvent.Set();
                return;
            }

            try
            {
                // dynamic app = Application.Current
                var appType = Type.GetType("System.Windows.Application, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                _wpfApp = appType?.GetProperty("Current")?.GetValue(null);

                if (_wpfApp == null)
                {
                    _wpfApp = NativeUiLoader.CreateInstance("PresentationFramework", "System.Windows.Application");
                    if (_wpfApp != null)
                    {
                        var shutdownModeType = Type.GetType("System.Windows.ShutdownMode, PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                        if (shutdownModeType != null)
                        {
                            var prop = _wpfApp.GetType().GetProperty("ShutdownMode");
                            if (prop != null)
                            {
                                // ShutdownMode.OnExplicitShutdown = 2
                                prop.SetValue(_wpfApp, Enum.ToObject(shutdownModeType, 2));
                            }
                        }
                    }
                    _logger.Info("WpfHost: Nueva Application creada (OnExplicitShutdown).");
                }
                else
                {
                    _logger.Info("WpfHost: Reutilizando Application existente.");
                }

                _logger.Info("WpfHost: Inicializando ChatOverlayWindow...");
                _chatWindow = new EchoXIV.UI.Native.ChatOverlayWindow(_configuration, _historyManager);
                _chatWindow.Show();
                
                IsInitialized = true;
                _logger.Info("WpfHost: ✅ Ventana lista.");

                _currentPid = (uint)Process.GetCurrentProcess().Id;
                _gameWindowHandle = GetGameWindowHandle();
                _chatWindowHandle = _chatWindow.GetHandle();

                // Timer de visibilidad inteligente
                _visibilityTimer = NativeUiLoader.CreateInstance("WindowsBase", "System.Windows.Threading.DispatcherTimer");
                if (_visibilityTimer != null)
                {
                    _visibilityTimer.Interval = TimeSpan.FromMilliseconds(500);
                    _visibilityTimer.Tick += (EventHandler)((s, e) => VisibilityTimer_Tick(s, e));
                    _visibilityTimer.Start();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "WpfHost: Error crítico en WpfThreadEntryPoint");
                IsInitialized = false;
            }

            _readyEvent.Set();

            try 
            {
                var dispatcherType = Type.GetType("System.Windows.Threading.Dispatcher, WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                dispatcherType?.GetMethod("Run", Array.Empty<Type>())?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "WpfHost: Error en el loop del Dispatcher");
            }
            finally
            {
                _logger.Info("WpfHost: Loop del Dispatcher finalizado.");
                IsInitialized = false;
                _visibilityTimer?.Stop();
                _visibilityTimer = null;
                _chatWindow = null;
            }
        }

        private void VisibilityTimer_Tick(object? sender, EventArgs e)
        {
            if (_chatWindow == null || !_isRunning) return;
            if (!_configuration.OverlayVisible) return;
            
            bool isChatVisible = Plugin.IsChatVisible();
            bool shouldBeVisible = isChatVisible;

            if (_configuration.SmartVisibility && isChatVisible)
            {
                var foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(foreground, out uint foregroundPid);
                    shouldBeVisible = (foregroundPid == _currentPid);
                }
                else
                {
                    shouldBeVisible = false;
                }
            }

            bool isVisible = _chatWindow.IsVisible();

            if (shouldBeVisible && !isVisible)
            {
                _chatWindow.Show();
            }
            else if (!shouldBeVisible && isVisible)
            {
                _chatWindow.Hide();
            }
        }

        public void AddMessage(TranslatedChatMessage msg) => _chatWindow?.AddMessage(msg);
        public void UpdateMessage(TranslatedChatMessage msg) => _chatWindow?.UpdateMessage(msg);
        public void ToggleWindow() => _chatWindow?.ToggleVisibility();
        public void ResetWindow() => _chatWindow?.ResetPosition();
        public void SetOpacity(float opacity) => _chatWindow?.SetOpacity(opacity);
        public void SetSmartVisibility(bool enabled) => VisibilityTimer_Tick(null, EventArgs.Empty);
        public void SetLock(bool locked) => _chatWindow?.SetLock(locked);
        public void UpdateVisuals() => _chatWindow?.UpdateVisuals();

        private IntPtr GetGameWindowHandle()
        {
            try
            {
                var mainHandle = Process.GetCurrentProcess().MainWindowHandle;
                if (mainHandle != IntPtr.Zero) return mainHandle;
                return FindWindow("FFXIVGAME", null);
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _logger.Info("WpfHost: Iniciando apagado...");

            _visibilityTimer?.Stop();
            _visibilityTimer = null;

            if (_wpfThread != null && _wpfThread.IsAlive)
            {
                try
                {
                    var dispatcherType = Type.GetType("System.Windows.Threading.Dispatcher, WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                    var dispatcher = dispatcherType?.GetMethod("FromThread")?.Invoke(null, new object[] { _wpfThread });
                    
                    if (dispatcher != null)
                    {
                        // Intentar cerrar la ventana de forma segura desede el hilo de la UI
                        if (_chatWindow != null)
                        {
                            try { _chatWindow.Close(); } catch { } 
                        }

                        // Forzar el apagado del dispatcher para terminar el thread
                        var method = dispatcher.GetType().GetMethod("BeginInvokeShutdown", new[] { Type.GetType("System.Windows.Threading.DispatcherPriority, WindowsBase") });
                        if (method != null)
                        {
                            var priorityNormal = NativeUiLoader.GetEnumValue("WindowsBase", "System.Windows.Threading.DispatcherPriority", 7); // Normal
                            method.Invoke(dispatcher, new[] { priorityNormal });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"WpfHost: Error al solicitar apagado del Dispatcher: {ex.Message}");
                }

                if (!_wpfThread.Join(1500))
                {
                    _logger.Warning("WpfHost: El hilo WPF no terminó a tiempo.");
                }
            }
            
            _chatWindow = null;
            _wpfThread = null;
            _logger.Info("WpfHost: Apagado completado.");
        }
    }
}
