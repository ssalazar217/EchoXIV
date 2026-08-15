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

    /// <summary>
    /// Exception thrown when the Local AI server is unavailable
    /// (no network response, timeout, or HTTP error after retries).
    /// Inherits from TranslationRateLimitException to reuse the failover flow.
    /// </summary>
    public class LocalAiUnavailableException : TranslationRateLimitException
    {
        public LocalAiUnavailableException(string engineName, string message) : base(engineName, message)
        {
        }
    }
}
