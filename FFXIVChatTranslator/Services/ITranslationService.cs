using System.Threading.Tasks;

namespace FFXIVChatTranslator.Services
{
    public interface ITranslationService
    {
        string Name { get; }
        Task<string> TranslateAsync(string text, string sourceLang, string targetLang);
    }
}
