using System;
using System.Threading;

namespace EchoXIV.Services;

internal static class TranslationDefaults
{
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public static CancellationTokenSource CreateTimeoutTokenSource(CancellationToken cancellationToken = default)
    {
        var cancellationTokenSource = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();

        cancellationTokenSource.CancelAfter(RequestTimeout);
        return cancellationTokenSource;
    }
}