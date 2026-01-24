using System;

namespace EchoXIV.Services
{
    /// <summary>
    /// Excepción lanzada cuando un servicio de traducción alcanza su límite de peticiones (Rate Limit / 429)
    /// </summary>
    public class TranslationRateLimitException : Exception
    {
        public string EngineName { get; }

        public TranslationRateLimitException(string engineName, string message) : base(message)
        {
            EngineName = engineName;
        }
    }
}
