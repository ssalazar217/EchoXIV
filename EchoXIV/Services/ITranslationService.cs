using System;
using System.Threading;
using System.Threading.Tasks;

namespace EchoXIV.Services
{
    public interface ITranslationService : IDisposable
    {
        string Name { get; }
        Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default);
    }
}
