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
        private bool _weOwnTheApp; // Flag para saber si nosotros creamos la App
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

        private readonly Dalamud.Plugin.Services.IPluginLog _logger;

        public WpfHost(Configuration configuration, Dalamud.Plugin.Services.IPluginLog logger)
        {
            _configuration = configuration;
            _logger = logger;
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
                    _weOwnTheApp = true;
                    _logger.Info("Nueva Application creada.");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error creando App (¿AppDomain ocupado?)");
                    _wpfApp = Application.Current;
                    _weOwnTheApp = false;
                }
            }
            else
            {
                _wpfApp = Application.Current;
                _weOwnTheApp = false;
                _logger.Info("Usando Application existente.");
            }

            // Crear la ventana
            try
            {
                _logger.Info("Instanciando ChatOverlayWindow...");
                _chatWindow = new ChatOverlayWindow(_configuration);
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
                _gameWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
                _visibilityTimer = new DispatcherTimer();
                _visibilityTimer.Interval = TimeSpan.FromMilliseconds(500);
                _visibilityTimer.Tick += VisibilityTimer_Tick;
                _visibilityTimer.Start();
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
            
            // Lógica de visibilidad MANDATORIA: Solo visible si el chat del juego (o ChatTwo) es visible
            bool isChatVisible = Plugin.IsChatVisible();
            bool shouldBeVisible = isChatVisible;

            // Lógica de visibilidad OPCIONAL (Smart Visibility): Ocultar si el juego no tiene el foco
            if (_configuration.SmartVisibility && isChatVisible)
            {
                var foreground = GetForegroundWindow();
                var windowHandle = new System.Windows.Interop.WindowInteropHelper(_chatWindow).Handle;

                // Visible si el foco está en el juego O en nuestra propia ventana
                shouldBeVisible = (foreground == _gameWindowHandle || foreground == windowHandle);
            }

            // Estado actual de la ventana
            bool isVisible = _chatWindow.Visibility == Visibility.Visible;
            
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

        public void AddMessage(TranslatedChatMessage message)
        {
            if (_chatWindow != null && _isRunning)
            {
                _chatWindow.Dispatcher.InvokeAsync(() => _chatWindow.AddMessage(message));
            }
        }

        public void UpdateMessage(TranslatedChatMessage message)
        {
            if (_chatWindow != null && _isRunning)
            {
                _chatWindow.Dispatcher.InvokeAsync(() => _chatWindow.UpdateMessage(message));
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
