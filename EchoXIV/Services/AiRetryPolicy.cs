using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace EchoXIV.Services
{
    /// <summary>
    /// Retry policy with exponential backoff for the Local AI engine.
    /// </summary>
    internal static class AiRetryPolicy
    {
        public const int MaxAttempts = 3;
        public const int BaseBackoffMs = 600;

        public static bool IsTransientStatus(int status)
        {
            return status is 408 or 425 or 429 or >= 500 and <= 599;
        }

        public static bool IsTransientException(Exception ex)
        {
            return ex is HttpRequestException or TaskCanceledException or OperationCanceledException
                or IOException;
        }

        public static Task DelayAsync(int attempt, CancellationToken cancellationToken,
            int baseBackoffMs = BaseBackoffMs)
        {
            return Task.Delay(BackoffDelayMs(attempt, baseBackoffMs), cancellationToken);
        }

        public static int BackoffDelayMs(int attempt, int baseBackoffMs = BaseBackoffMs)
        {
            return baseBackoffMs * (1 << (attempt - 1));
        }
    }
}
