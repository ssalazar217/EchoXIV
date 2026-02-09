using System.Text;
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
            var bytes = Encoding.UTF8.GetBytes(message);
            if (bytes.Length == 0)
                return;
            
            if (bytes.Length > 500)
            {
                // Truncar si es muy largo
                Array.Resize(ref bytes, 500);
            }
            
            SendMessageUnsafe(bytes);
        }
        
        private static void SendMessageUnsafe(byte[] message)
        {
            var mes = Utf8String.FromSequence(message);
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
