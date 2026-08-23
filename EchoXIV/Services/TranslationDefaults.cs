using System;
using System.Reflection;
using System.Threading;

namespace EchoXIV.Services;

internal static class TranslationDefaults
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Max UTF-8 bytes for a single chat message sent to the game.</summary>
    public const int MaxChatBytes = 450;

    public static CancellationTokenSource CreateTimeoutTokenSource(CancellationToken cancellationToken = default)
    {
        var cancellationTokenSource = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();

        cancellationTokenSource.CancelAfter(RequestTimeout);
        return cancellationTokenSource;
    }
}

internal static class EchoXIVClient
{
    public static string UserAgent => $"EchoXIV/{GetVersion()}";

    private static string GetVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version != null)
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch
        {
            // Fall through to the constant below.
        }
        return "1.0.0.0";
    }
}
