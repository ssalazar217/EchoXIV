using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using FFXIVChatTranslator.UI.Native;

namespace FFXIVChatTranslator.Services
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
            _wpfThread.Name = "FFXIVChatTranslator_WpfHost";
            _wpfThread.Start();

            // Esperar a que la ventana esté lista (con timeout)
            _readyEvent.WaitOne(5000);
        }

        private void WpfThreadEntryPoint()
        {
             // Log de diagnóstico
            _logger.Info("[FFXIVChatTranslator] Iniciando hilo WPF...");
            
            // Intentar obtener la App actual o crear una nueva
            // ESTRATEGIA: Nunca matar la App. Solo usarla como contenedor.
            if (Application.Current == null)
            {
                try
                {
                    _wpfApp = new Application();
                    _wpfApp.ShutdownMode = ShutdownMode.OnExplicitShutdown; // Importante: Que no se cierre sola
                    _weOwnTheApp = true;
                    _logger.Info("[FFXIVChatTranslator] Nueva System.Windows.Application creada.");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "[FFXIVChatTranslator] Error creando App (Posiblemente ya existe una zombie?)");
                    _wpfApp = Application.Current;
                    _weOwnTheApp = false;
                }
            }
            else
            {
                _wpfApp = Application.Current;
                _weOwnTheApp = false;
                _logger.Info("[FFXIVChatTranslator] Usando System.Windows.Application existente.");
            }

            // Crear la ventana
            try
            {
                _logger.Info($"[FFXIVChatTranslator] Creando ventana...");
                _chatWindow = new ChatOverlayWindow(_configuration);
                _chatWindow.Closed += (s, e) => _chatWindow = null;
                _chatWindow.Show();
                _logger.Info("[FFXIVChatTranslator] Ventana creada y mostrada.");
                IsInitialized = true; // SUCCESS
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[FFXIVChatTranslator] CRITICAL: Error creando ventana");
                IsInitialized = false;
            }

            // Señalizar que estamos listos
            _readyEvent.Set();

            // Iniciar dispatcher loop
            try 
            {
                // SIEMPRE usamos Dispatcher.Run().
                // App.Run() bloquea y requiere Shutdown() para salir, lo cual mata la App para siempre.
                // Dispatcher.Run() solo corre el loop de este hilo y sale con InvokeShutdown().
                _logger.Info("[FFXIVChatTranslator] Ejecutando Dispatcher.Run()...");
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                 _logger.Error(ex, "[FFXIVChatTranslator] Error en Dispatcher loop");
            }
        }

        // ... (AddMessage, UpdateMessage, etc. unchanged) ...

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

        public void Dispose()
        {
            if (!_isRunning) return;
            _isRunning = false;

            if (_chatWindow != null)
            {
                try 
                {
                    // Cerrar ventana gracefully
                    _chatWindow.Dispatcher.Invoke(() => 
                    {
                        _chatWindow.Close();
                    }, DispatcherPriority.Send); 
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
                        // Esto hace que Dispatcher.Run() retorne, permitiendo que el hilo termine.
                        dispatcher.InvokeShutdown();
                    }
                }
                catch { }
            }

            // NUNCA LLAMAR A _wpfApp.Shutdown() !!!
            // Dejamos la App viva para la próxima vez (o para otros plugins).

            // Esperar a que el hilo muera
            if (_wpfThread != null)
            {
                if (!_wpfThread.Join(2000))
                {
                    _logger.Warning("[FFXIVChatTranslator] El hilo WPF no terminó a tiempo.");
                }
            }
            
            _wpfApp = null;
            _chatWindow = null;
            _wpfThread = null;
        }
    }
}
