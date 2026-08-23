using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EchoXIV.Services;

/// <summary>
/// Translation router with smart failover. The primary engine never changes:
/// sick engines are skipped and probed periodically; each one recovers
/// independently once it responds again.
/// </summary>
public class TranslationRouter : ITranslationService
{
    public string Name => "Translation Router";

    private readonly Dictionary<TranslationEngine, ITranslationService> _engines;
    private readonly TranslationEngine _primary;
    private readonly TranslationEngine? _fallback;
    private readonly TimeSpan _recoveryInterval;

    private readonly object _gate = new();
    private readonly HashSet<TranslationEngine> _sick = new();
    private DateTime _lastProbeUtc = DateTime.MinValue;

    public TranslationRouter(
        TranslationEngine primary,
        TranslationEngine? fallback,
        IReadOnlyDictionary<TranslationEngine, ITranslationService> engines,
        int recoveryCheckMinutes = 10)
    {
        _primary = primary;
        _fallback = fallback;
        _engines = new Dictionary<TranslationEngine, ITranslationService>(engines);
        _recoveryInterval = TimeSpan.FromMinutes(Math.Max(1, recoveryCheckMinutes));
    }

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        TryStartRecoveryProbes();

        List<TranslationEngine> order;
        lock (_gate)
        {
            order = BuildOrder().ToList();
        }

        foreach (var engine in order)
        {
            if (!_engines.TryGetValue(engine, out var translator))
                continue;

            try
            {
                var result = await translator.TranslateAsync(text, sourceLang, targetLang, cancellationToken);
                if (result is null)
                {
                    MarkSick(engine);
                    continue;
                }
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                MarkSick(engine);
            }
        }

        return text;
    }

    /// <summary>
    /// Attempt order: primary (if healthy) -> configured fallback -> remaining engines.
    /// </summary>
    private IEnumerable<TranslationEngine> BuildOrder()
    {
        if (!_sick.Contains(_primary))
            yield return _primary;

        if (_fallback is not null && _fallback.Value != _primary && !_sick.Contains(_fallback.Value))
            yield return _fallback.Value;

        foreach (var engine in _engines.Keys)
        {
            if (engine == _primary || engine == _fallback)
                continue;
            if (_sick.Contains(engine))
                continue;
            yield return engine;
        }
    }

    /// <summary>
    /// Starts fire-and-forget probes for all sick engines when the interval elapsed.
    /// Never blocks the caller.
    /// </summary>
    private void TryStartRecoveryProbes()
    {
        List<TranslationEngine> candidates;

        lock (_gate)
        {
            if (_sick.Count == 0)
                return;

            var now = DateTime.UtcNow;
            if (now - _lastProbeUtc < _recoveryInterval)
                return;

            _lastProbeUtc = now;
            candidates = _sick.ToList();
        }

        foreach (var engine in candidates)
        {
            _ = ProbeAsync(engine);
        }
    }

    private void MarkSick(TranslationEngine engine)
    {
        lock (_gate)
        {
            _sick.Add(engine);
        }
    }

    private async Task ProbeAsync(TranslationEngine engine)
    {
        if (!_engines.TryGetValue(engine, out var translator))
            return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await translator.TranslateAsync("hi", "auto", "en", cts.Token);
            if (result is not null)
            {
                lock (_gate)
                {
                    _sick.Remove(engine);
                }
            }
        }
        catch
        {
            // Still sick; retried on the next interval.
        }
    }

    public void Dispose()
    {
        // Intentionally left blank: the underlying translators are process-wide.
    }
}
