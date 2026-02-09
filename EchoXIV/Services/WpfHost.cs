using System;
using System.Threading;
using System.Runtime.InteropServices; 
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using EchoXIV.UI.Native;

namespace EchoXIV.Services
{
    public class WpfHost : IDisposable
    {
        private Thread? _wpfThread;
        private Application? _wpfApp;
        private ChatOverlayWindow? _chatWindow;
        private readonly Configuration _configuration;
        private bool _isRunning;

        // Evento para señalizar que el host está listo
        private readonly ManualResetEvent _readyEvent = new(false);
        
        // Smart Visibility
        private DispatcherTimer? _visibilityTimer;
        private IntPtr _gameWindowHandle;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);


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
            // Verificación simple de SO, aunque el build target ya fuerza Windows
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
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
            _logger.Info("Iniciando hilo WPF (Strict Mode)...");
            
            try
            {
                // Gestionar Application de WPF
                if (Application.Current != null)
                {
                    _wpfApp = Application.Current;
                    _logger.Info("WpfHost: Reutilizando Application existente.");
                }
                else
                {
                    _wpfApp = new Application();
                    _wpfApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    _logger.Info("WpfHost: Nueva Application creada (OnExplicitShutdown).");
                }

                _logger.Info("WpfHost: Inicializando ChatOverlayWindow...");
                _chatWindow = new ChatOverlayWindow(_configuration, _historyManager);
                _chatWindow.Show();
                
                IsInitialized = true;
                _logger.Info("WpfHost: ✅ Ventana lista.");

                _currentPid = (uint)Process.GetCurrentProcess().Id;
                _gameWindowHandle = GetGameWindowHandle();
                _chatWindowHandle = _chatWindow.GetHandle();

                // Timer de visibilidad inteligente
                _visibilityTimer = new DispatcherTimer();
                _visibilityTimer.Interval = TimeSpan.FromMilliseconds(500);
                _visibilityTimer.Tick += VisibilityTimer_Tick;
                _visibilityTimer.Start();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "WpfHost: Error crítico en WpfThreadEntryPoint");
                IsInitialized = false;
            }

            _readyEvent.Set();

            try 
            {
                Dispatcher.Run();
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
                if (_gameWindowHandle == IntPtr.Zero)
                {
                    _gameWindowHandle = GetGameWindowHandle();
                }

                var foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero)
                {
                    // Strict Handle Check: Solo visible si el foco está en la ventana del juego o la del chat.
                    shouldBeVisible = (foreground == _gameWindowHandle) || (foreground == _chatWindowHandle);
                    
                    // Fallback de seguridad si el Handle cambió.
                    if (!shouldBeVisible && _gameWindowHandle == IntPtr.Zero)
                    {
                         GetWindowThreadProcessId(foreground, out uint foregroundPid);
                         shouldBeVisible = (foregroundPid == _currentPid);
                    }
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
        // Asumiendo que SetLock existe en ChatOverlayWindow, si no la compilación fallará
        public void SetLock(bool locked) => _chatWindow?.SetLock(locked); 
        // Asumiendo que UpdateVisuals existe en ChatOverlayWindow
        public void UpdateVisuals() => _chatWindow?.UpdateVisuals();

        private IntPtr GetGameWindowHandle()
        {
            try
            {
                // 1. Intentar obtenerlo directamente del proceso actual
                using var process = Process.GetCurrentProcess();
                process.Refresh(); // Asegurar datos frescos
                var mainHandle = process.MainWindowHandle;
                
                if (mainHandle != IntPtr.Zero) 
                    return mainHandle;

                // 2. Fallback: Enumerar ventanas y buscar la que coincida con nuestro PID
                // Esto evita encontrar ventanas de OTROS clientes FFXIV.
                IntPtr foundHandle = IntPtr.Zero;
                uint myPid = (uint)process.Id;

                EnumWindows((hwnd, lParam) => 
                {
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid == myPid)
                    {
                        // Encontrar la ventana visible principal podría requerir más filtros (ej: title, style),
                        // pero por PID ya filtramos otros clientes.
                        // Asumiremos la primera ventana válida del PID como candidata si MainWindowHandle falló.
                        // Se podría refinar checking 'IsWindowVisible'.
                        foundHandle = hwnd;
                        return false; // Stop enumeration
                    }
                    return true; // Continue
                }, IntPtr.Zero);

                return foundHandle;
            }
            catch (Exception ex)
            {
                _logger.Warning($"WpfHost: Error obteniendo GameWindowHandle: {ex.Message}");
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
                    var disp = Dispatcher.FromThread(_wpfThread);
                    if (disp != null)
                    {
                        // Intentar cerrar la ventana de forma segura desede el hilo de la UI
                        if (_chatWindow != null)
                        {
                            disp.Invoke(() => { try { _chatWindow.Close(); } catch { } });
                        }

                        // Forzar el apagado del dispatcher para terminar el thread
                        disp.BeginInvokeShutdown(DispatcherPriority.Normal);
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
