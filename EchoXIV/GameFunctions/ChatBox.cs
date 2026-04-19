using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EchoXIV.GameFunctions
{
    /// <summary>
    /// Envío directo de mensajes al chat del juego
    /// Funciones base para ChatBox
    /// </summary>
    public static unsafe class ChatBox
    {
        public static void SendMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            
            if (System.Text.Encoding.UTF8.GetByteCount(message) > 500)
            {
                while (message.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(message) > 500)
                {
                    message = message[..^1];
                }
            }
            
            SendMessageUnsafe(message);
        }
        
        private static void SendMessageUnsafe(string message)
        {
            var mes = Utf8String.FromString(message);
            mes->SanitizeString(
                AllowedEntities.UppercaseLetters |
                AllowedEntities.LowercaseLetters |
                AllowedEntities.Numbers |
                AllowedEntities.SpecialCharacters |
                AllowedEntities.Payloads |
                AllowedEntities.CJK
            );
            UIModule.Instance()->ProcessChatBoxEntry(mes);
            mes->Dtor(true);
        }
    }
}
