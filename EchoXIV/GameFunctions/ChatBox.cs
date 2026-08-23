using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace EchoXIV.GameFunctions
{
    /// <summary>
    /// Direct chat message sending helpers
    /// </summary>
    public static unsafe class ChatBox
    {
        public static void SendMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            var maxBytes = EchoXIV.Services.TranslationDefaults.MaxChatBytes;
            if (System.Text.Encoding.UTF8.GetByteCount(message) > maxBytes)
            {
                while (message.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(message) > maxBytes)
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
