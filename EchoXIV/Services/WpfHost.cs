using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices; 
using System.Diagnostics;
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
             // Log de diagnóstico
            _logger.Info("Iniciando hilo WPF...");
            
            // Intentar obtener la App actual o crear una nueva
            // ESTRATEGIA: Nunca matar la App. Solo usarla como contenedor.
            if (Application.Current == null)
            {
                try
                {
                    _wpfApp = new Application();
                    _wpfApp.ShutdownMode = ShutdownMode.OnExplicitShutdown; 
                    _logger.Info("Nueva Application creada.");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error creando App (¿AppDomain ocupado?)");
                    _wpfApp = Application.Current;
                }
            }
            else
            {
                _wpfApp = Application.Current;
                _logger.Info("Usando Application existente.");
            }

            // Crear la ventana
            try
            {
                _logger.Info("Instanciando ChatOverlayWindow...");
                _chatWindow = new ChatOverlayWindow(_configuration, _historyManager);
                _chatWindow.Closed += (s, e) => 
                {
                    _chatWindow = null;
                    _logger.Info("Ventana cerrada. Terminando thread WPF.");
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
                };
                _chatWindow.Show();
                _logger.Info("✅ Ventana WPF nativa inicializada correctamente.");
                IsInitialized = true; // SUCCESS
                
                // Inicializar Smart Visibility Timer
                _currentPid = (uint)Process.GetCurrentProcess().Id;
                _gameWindowHandle = GetGameWindowHandle();
                
                _chatWindowHandle = new System.Windows.Interop.WindowInteropHelper(_chatWindow).Handle;

                _visibilityTimer = new DispatcherTimer();
                _visibilityTimer.Interval = TimeSpan.FromMilliseconds(500);
                _visibilityTimer.Tick += VisibilityTimer_Tick;
                _visibilityTimer.Start();
                
                if (_configuration.VerboseLogging) _logger.Info($"[WpfHost] PID: {_currentPid}, GameHandle: {_gameWindowHandle}, ChatHandle: {_chatWindowHandle}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "CRITICAL: Error creando ventana WPF");
                IsInitialized = false;
            }

            // Señalizar que estamos listos
            _readyEvent.Set();

            // Iniciar dispatcher loop
            try 
            {
                _logger.Info("Ejecutando Dispatcher.Run()...");
                Dispatcher.Run();
            }
            catch (ThreadAbortException) { }
            catch (Exception ex)
            {
                 _logger.Error(ex, "Error en Dispatcher loop");
            }
            finally
            {
                _logger.Info("Dispatcher loop terminado. Limpiando hilo.");
                IsInitialized = false;
                _chatWindow = null;
                _visibilityTimer?.Stop();
                _visibilityTimer = null;
            }
        }

        // ... (AddMessage, UpdateMessage, etc. unchanged) ...

        private void VisibilityTimer_Tick(object? sender, EventArgs e)
        {
            if (_chatWindow == null) return;
            
            // Si el usuario cerró la ventana manualmente, no hacer nada aquí
            if (!_configuration.OverlayVisible) return;
            
            // Lógica de visibilidad MANDATORIA: Solo visible si el chat de juego es visible
            bool isChatVisible = Plugin.IsChatVisible();
            bool shouldBeVisible = isChatVisible;

            // Estado actual de la ventana
            bool isVisible = _chatWindow.Visibility == Visibility.Visible;

            // Lógica de visibilidad OPCIONAL (Smart Visibility): Ocultar si el juego no tiene el foco
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
                    
                    // Visible si el proceso en primer plano es el mismo que el del juego
                    shouldBeVisible = (foregroundPid == _currentPid);

                    if (_configuration.VerboseLogging && isVisible != shouldBeVisible)
                    {
                        _logger.Info($"[WpfHost] SmartVis Change: ForegroundHandle={foreground}, ForegroundPID={foregroundPid}, OurPID={_currentPid}, ShouldBeVisible={shouldBeVisible}");
                    }
                }
            }

            
            // Solo actuar si cambia el estado deseado
            if (shouldBeVisible && !isVisible)
            {
                _chatWindow.Show();
                _chatWindow.Topmost = true; // Asegurar topmost al volver
            }
            else if (!shouldBeVisible && isVisible)
            {
                _chatWindow.Hide();
            }
        }


        
        public void ToggleWindow()
        {
            if (_chatWindow != null && _isRunning)
            {
                 _chatWindow.Dispatcher.InvokeAsync(() => _chatWindow.ToggleVisibility());
            }
        }
        
        public void ResetWindow()
        {
             if (_chatWindow != null && _isRunning)
            {
                 _chatWindow.Dispatcher.InvokeAsync(() => _chatWindow.ResetPosition());
            }
        }

        public void SetOpacity(float opacity)
        {
            if (_chatWindow != null && _isRunning)
            {
                 _chatWindow.Dispatcher.InvokeAsync(() => _chatWindow.SetOpacity(opacity));
            }
        }

        public void SetSmartVisibility(bool enabled)
        {
            // La configuración ya se actualizó (es referencia compartida).
            // Forzamos un checkeo inmediato del timer si es posible, o simplemente esperamos el siguiente tick.
            if (_chatWindow != null && _isRunning)
            {
                _chatWindow.Dispatcher.InvokeAsync(() => 
                {
                    // Forzar tick manual
                    VisibilityTimer_Tick(null, EventArgs.Empty);
                });
            }
        }

        public void SetLock(bool locked)
        {
            if (_chatWindow != null && _isRunning)
            {
                 _chatWindow.Dispatcher.InvokeAsync(() => _chatWindow.SetLock(locked));
            }
        }

        public void UpdateVisuals()
        {
            if (_chatWindow != null && _isRunning)
            {
                 _chatWindow.Dispatcher.InvokeAsync(() => _chatWindow.UpdateVisuals());
            }
        }

        private IntPtr GetGameWindowHandle()
        {
            try
            {
                var mainHandle = Process.GetCurrentProcess().MainWindowHandle;
                if (mainHandle != IntPtr.Zero) return mainHandle;

                // Fallback a buscar por clase si MainWindowHandle falla
                return FindWindow("FFXIVGAME", null);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error obteniendo handle de la ventana del juego");
                return IntPtr.Zero;
            }
        }

        public void Dispose()
        {
            if (!_isRunning) return;
            _isRunning = false;

            if (_visibilityTimer != null)
            {
                _visibilityTimer.Stop();
                _visibilityTimer = null;
            }

            if (_chatWindow != null)
            {
                try 
                {
                    // Cerrar ventana gracefully
                    if (_chatWindow.Dispatcher.CheckAccess())
                    {
                        _chatWindow.Close();
                    }
                    else
                    {
                        _chatWindow.Dispatcher.Invoke(() => _chatWindow.Close(), DispatcherPriority.Send);
                    }
                }
                catch { /* Ignorar errores al cerrar */ }
            }
            
            // Detener el Dispatcher Loop de ESTE hilo
            if (_wpfThread != null && _wpfThread.IsAlive)
            {
                try
                {
                    var dispatcher = Dispatcher.FromThread(_wpfThread);
                    if (dispatcher != null && !dispatcher.HasShutdownStarted)
                    {
                        _logger.Info("Solicitando cierre de Dispatcher...");
                        dispatcher.InvokeShutdown(); // Usar el síncrono para esperar
                    }
                }
                catch { }

                if (!_wpfThread.Join(1500))
                {
                    _logger.Warning("El hilo WPF no terminó a tiempo.");
                }
            }
            
            _wpfApp = null;
            _chatWindow = null;
            _wpfThread = null;
        }
    }
}
