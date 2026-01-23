using System;
using System.Threading.Tasks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using EchoXIV.Services;

namespace EchoXIV
{
    /// <summary>
    /// Intercepta mensajes salientes del chat y los traduce antes de enviarlos
    /// </summary>
    public class ChatInterceptor : IDisposable
    {
        private readonly Configuration _configuration;
        private readonly GoogleTranslatorService _translatorService;
        private readonly IChatGui _chatGui;
        private readonly IPluginLog _pluginLog;
        
        public ChatInterceptor(
            Configuration configuration,
            GoogleTranslatorService translatorService,
            IChatGui chatGui,
            IPluginLog pluginLog)
        {
            _configuration = configuration;
            _translatorService = translatorService;
            _chatGui = chatGui;
            _pluginLog = pluginLog;
            
            // Suscribirse al evento de mensajes del chat
            _chatGui.ChatMessage += OnChatMessage;
        }
        
        private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
        {
            // Solo procesar si la traducción está activada
            if (!_configuration.TranslationEnabled)
                return;
            
            // Solo traducir ciertos tipos de chat (mensajes salientes del jugador)
            // Esto es un enfoque básico, se puede refinar según necesidad
            if (!IsSupportedChatType(type))
                return;
            
            try
            {
                var originalText = message.TextValue;
                
                // No traducir si el mensaje está vacío
                if (string.IsNullOrWhiteSpace(originalText))
                    return;
                
                // No traducir comandos del juego
                if (originalText.StartsWith("/"))
                    return;
                
                // Traducir de forma síncrona (necesario para interceptar antes de enviar)
                var translatedText = _translatorService.Translate(
                    originalText,
                    _configuration.SourceLanguage,
                    _configuration.TargetLanguage
                );
                
                // Si la traducción fue exitosa, reemplazar el mensaje
                if (!string.IsNullOrWhiteSpace(translatedText) && translatedText != originalText)
                {
                    message = new SeString(new Dalamud.Game.Text.SeStringHandling.Payloads.TextPayload(translatedText));
                    _pluginLog.Info($"Traducido: '{originalText}' → '{translatedText}'");
                }
            }
            catch (Exception ex)
            {
                _pluginLog.Error(ex, "Error al traducir mensaje de chat");
            }
        }
        
        private bool IsSupportedChatType(XivChatType type)
        {
            // Tipos de chat que se pueden traducir (mensajes salientes del jugador)
            return type switch
            {
                XivChatType.Say => true,
                XivChatType.Yell => true,
                XivChatType.Shout => true,
                XivChatType.TellOutgoing => true,
                XivChatType.Party => true,
                XivChatType.Alliance => true,
                XivChatType.FreeCompany => true,
                XivChatType.Ls1 => true,
                XivChatType.Ls2 => true,
                XivChatType.Ls3 => true,
                XivChatType.Ls4 => true,
                XivChatType.Ls5 => true,
                XivChatType.Ls6 => true,
                XivChatType.Ls7 => true,
                XivChatType.Ls8 => true,
                XivChatType.CrossLinkShell1 => true,
                XivChatType.CrossLinkShell2 => true,
                XivChatType.CrossLinkShell3 => true,
                XivChatType.CrossLinkShell4 => true,
                XivChatType.CrossLinkShell5 => true,
                XivChatType.CrossLinkShell6 => true,
                XivChatType.CrossLinkShell7 => true,
                XivChatType.CrossLinkShell8 => true,
                _ => false
            };
        }
        
        public void Dispose()
        {
            // Desuscribirse del evento
            _chatGui.ChatMessage -= OnChatMessage;
        }
    }
}
