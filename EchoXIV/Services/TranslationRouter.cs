using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace EchoXIV.Services;

/// <summary>
/// Translation router with smart failover. Uses an ordered list of engine
/// instances: the primary is tried first, then the rest in list order.
/// Sick instances are skipped and probed periodically; each recovers
/// independently once it responds again.
/// </summary>
public class TranslationRouter : ITranslationService, ICombinedTranslator
{
    public string Name => "Translation Router";

    private readonly Guid _primaryId;
    private readonly List<(Guid Id, ITranslationService Translator, string DisplayName)> _engines;
    private readonly TimeSpan _recoveryInterval;
    private readonly IPluginLog? _log;

    private readonly object _gate = new();
    private readonly HashSet<Guid> _sick = new();
    private DateTime _lastProbeUtc = DateTime.MinValue;
    private Guid _lastUsedId;
    private string _lastUsedName = "?";

    /// <summary>
    /// Id of the instance that served the last successful translation on this async flow.
    /// Guid.Empty when nothing has succeeded yet on this flow.
    /// </summary>
    public Guid LastUsedInstanceId
    {
        get { lock (_gate) return _lastUsedId; }
    }

    public string LastUsedInstanceName
    {
        get { lock (_gate) return _lastUsedName; }
    }

    public bool SupportsCombinedRomanization => _engines.Any(e => e.Translator is ICombinedTranslator);

    public ITranslationService? GetLastUsedTranslator()
    {
        Guid id;
        lock (_gate) id = _lastUsedId;
        if (id == Guid.Empty)
            return null;

        return _engines.FirstOrDefault(e => e.Id == id).Translator;
    }

    public async Task<(string Translation, string? RomanizedText, string OriginalScript)> TranslateWithRomanizationAsync(
        string text,
        string sourceLang,
        string targetLang,
        CancellationToken cancellationToken = default,
        bool romanizeTranslation = false)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (text, null, "Latin");

        TryStartRecoveryProbes();
        List<Guid> order;
        lock (_gate)
        {
            order = BuildOrder().ToList();
        }

        foreach (var id in order)
        {
            var entry = _engines.FirstOrDefault(e => e.Id == id);
            if (entry.Translator is not ICombinedTranslator translator)
                continue;

            try
            {
                var result = await translator.TranslateWithRomanizationAsync(
                    text, sourceLang, targetLang, cancellationToken, romanizeTranslation);
                SetLastUsed(id, entry.DisplayName);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                MarkSick(id, ex.Message);
            }
        }

        throw new InvalidOperationException("No healthy translation instance supports combined romanization.");
    }

    public Task<string?> RomanizeAsync(string text, string sourceLang, CancellationToken cancellationToken = default)
    {
        return (GetLastUsedTranslator() as IRomanizationService)?.RomanizeAsync(text, sourceLang, cancellationToken)
            ?? Task.FromResult<string?>(null);
    }

    public bool CanRomanize(string sourceLang, string text)
    {
        return (GetLastUsedTranslator() as IRomanizationService)?.CanRomanize(sourceLang, text) ?? false;
    }

    public TranslationRouter(
        Guid primaryId,
        IReadOnlyList<(Guid Id, ITranslationService Translator, string DisplayName)> engines,
        int recoveryCheckMinutes = 10,
        IPluginLog? log = null)
    {
        _primaryId = primaryId;
        _engines = engines.ToList();
        _recoveryInterval = TimeSpan.FromMinutes(Math.Max(1, recoveryCheckMinutes));
        _log = log;
    }

    // Legacy ctor for callers that still pass a dictionary keyed by engine type.
    // Kept for compatibility; wraps each entry with its type name as display name.
    public TranslationRouter(
        Guid primaryId,
        IReadOnlyList<(Guid Id, ITranslationService Translator)> engines,
        int recoveryCheckMinutes = 10)
        : this(primaryId, engines.Select(e => (e.Id, e.Translator, e.Id.ToString())).ToList(), recoveryCheckMinutes)
    {
    }

    public TranslationRouter(
        TranslationEngine primary,
        IReadOnlyDictionary<TranslationEngine, ITranslationService> engines,
        int recoveryCheckMinutes = 10)
        : this(Guid.Empty, engines.Select(kv => (Guid.NewGuid(), kv.Value, kv.Key.ToString())).ToList(), recoveryCheckMinutes)
    {
        // Legacy: find primary by type name.
        var primaryEntry = _engines.FirstOrDefault(e => e.DisplayName == primary.ToString());
        if (primaryEntry != default)
            _primaryId = primaryEntry.Id;
    }

    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        TryStartRecoveryProbes();

        List<Guid> order;
        lock (_gate)
        {
            order = BuildOrder().ToList();
        }

        foreach (var id in order)
        {
            var entry = _engines.FirstOrDefault(e => e.Id == id);
            if (entry.Translator == null)
                continue;

            try
            {
                var result = await entry.Translator.TranslateAsync(text, sourceLang, targetLang, cancellationToken);
                if (result is null)
                {
                    MarkSick(id, "empty response");
                    continue;
                }
                SetLastUsed(id, entry.DisplayName);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                MarkSick(id, ex.Message);
            }
        }

        return text;
    }

    /// <summary>
    /// Attempt order: primary (if healthy) first, then remaining instances in list order.
    /// </summary>
    private IEnumerable<Guid> BuildOrder()
    {
        if (!_sick.Contains(_primaryId))
            yield return _primaryId;

        foreach (var (id, _, _) in _engines)
        {
            if (id == _primaryId)
                continue;
            if (_sick.Contains(id))
                continue;
            yield return id;
        }
    }

    private void SetLastUsed(Guid id, string name)
    {
        lock (_gate)
        {
            _lastUsedId = id;
            _lastUsedName = name;
        }
    }

    /// <summary>
    /// Starts fire-and-forget probes for all sick instances when the interval elapsed.
    /// Never blocks the caller.
    /// </summary>
    private void TryStartRecoveryProbes()
    {
        List<Guid> candidates;

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

        foreach (var id in candidates)
        {
            _ = ProbeAsync(id);
        }
    }

    private void MarkSick(Guid id, string reason)
    {
        var newlySick = false;
        lock (_gate)
        {
            newlySick = _sick.Add(id);
        }

        if (newlySick)
        {
            var entry = _engines.FirstOrDefault(e => e.Id == id);
            _log?.Warning($"Translation engine '{entry.DisplayName}' failed and was temporarily skipped: {reason}");
        }
    }

    private async Task ProbeAsync(Guid id)
    {
        var entry = _engines.FirstOrDefault(e => e.Id == id);
        if (entry.Translator == null)
            return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await entry.Translator.TranslateAsync("hi", "auto", "en", cts.Token);
            if (result is not null)
            {
                lock (_gate)
                {
                    _sick.Remove(id);
                }
                _log?.Info($"Translation engine '{entry.DisplayName}' recovered and is available again.");
            }
        }
        catch (Exception ex)
        {
            _log?.Debug($"Translation engine '{entry.DisplayName}' recovery check failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        // Intentionally left blank: the underlying translators are process-wide.
    }
}
