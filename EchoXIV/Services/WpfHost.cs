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
                                prop.SetValue(_wpfApp, Enum.ToObject(shutdownModeType, 1));
                            }
                        }
                    }
                    _logger.Info("Nueva Dynamic Application creada.");
                }
                else
                {
                    _logger.Info("Usando Dynamic Application existente.");
                }

                _logger.Info("Instanciando ChatOverlayWindow (Dynamic)...");
                _chatWindow = new EchoXIV.UI.Native.ChatOverlayWindow(_configuration, _historyManager);
                
                // _chatWindow.Closed += ...
                // Nota: Los eventos en dynamic requieren cuidado o usar delegados
                
                _chatWindow.Show();
                _logger.Info("✅ Ventana WPF dinámica inicializada correctamente.");
                IsInitialized = true;

                _currentPid = (uint)Process.GetCurrentProcess().Id;
                _gameWindowHandle = GetGameWindowHandle();
                _chatWindowHandle = _chatWindow.GetHandle();

                // DispatcherTimer
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
                _logger.Error(ex, "CRITICAL: Error en WpfThreadEntryPoint dinámica");
                IsInitialized = false;
            }

            _readyEvent.Set();

            try 
            {
                // Dispatcher.Run()
                var dispatcherType = Type.GetType("System.Windows.Threading.Dispatcher, WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                dispatcherType?.GetMethod("Run", Array.Empty<Type>())?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                 _logger.Error(ex, "Error en Dispatcher loop dinámico");
            }
            finally
            {
                _logger.Info("Dispatcher loop terminado.");
                IsInitialized = false;
                _chatWindow = null;
                _visibilityTimer?.Stop();
                _visibilityTimer = null;
            }
        }

        private void VisibilityTimer_Tick(object? sender, EventArgs e)
        {
            if (_chatWindow == null) return;
            if (!_configuration.OverlayVisible) return;
            
            bool isChatVisible = Plugin.IsChatVisible();
            bool shouldBeVisible = isChatVisible;

            if (_configuration.SmartVisibility && isChatVisible)
            {
                var foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                {
                    shouldBeVisible = false;
                }
                else
                {
                    GetWindowThreadProcessId(foreground, out uint foregroundPid);
                    shouldBeVisible = (foregroundPid == _currentPid);
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

            _visibilityTimer?.Stop();
            _visibilityTimer = null;

            if (_chatWindow != null)
            {
                try { _chatWindow.Close(); } catch { }
            }
            
            if (_wpfThread != null && _wpfThread.IsAlive)
            {
                try
                {
                    // dispatcher.InvokeShutdown() vía reflexión
                    var dispatcherType = Type.GetType("System.Windows.Threading.Dispatcher, WindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                    var dispatcher = dispatcherType?.GetMethod("FromThread")?.Invoke(null, new object[] { _wpfThread });
                    if (dispatcher != null)
                    {
                        var method = dispatcher.GetType().GetMethod("InvokeShutdown");
                        method?.Invoke(dispatcher, null);
                    }
                }
                catch { }

                _wpfThread.Join(1000);
            }
            
            _wpfApp = null;
            _chatWindow = null;
            _wpfThread = null;
        }
    }
}
