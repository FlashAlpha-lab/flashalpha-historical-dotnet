using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FlashAlpha.Historical;

/// <summary>
/// Backtesting helpers — point-in-time replay loops over the Historical API.
/// </summary>
public static class Replay
{
    private static readonly HashSet<DateOnly> FullCloseHolidays = new()
    {
        // 2018
        new(2018,1,1), new(2018,1,15), new(2018,2,19), new(2018,3,30), new(2018,5,28),
        new(2018,7,4), new(2018,9,3), new(2018,11,22), new(2018,12,5), new(2018,12,25),
        // 2019
        new(2019,1,1), new(2019,1,21), new(2019,2,18), new(2019,4,19), new(2019,5,27),
        new(2019,7,4), new(2019,9,2), new(2019,11,28), new(2019,12,25),
        // 2020
        new(2020,1,1), new(2020,1,20), new(2020,2,17), new(2020,4,10), new(2020,5,25),
        new(2020,7,3), new(2020,9,7), new(2020,11,26), new(2020,12,25),
        // 2021
        new(2021,1,1), new(2021,1,18), new(2021,2,15), new(2021,4,2), new(2021,5,31),
        new(2021,7,5), new(2021,9,6), new(2021,11,25), new(2021,12,24),
        // 2022
        new(2022,1,17), new(2022,2,21), new(2022,4,15), new(2022,5,30), new(2022,6,20),
        new(2022,7,4), new(2022,9,5), new(2022,11,24), new(2022,12,26),
        // 2023
        new(2023,1,2), new(2023,1,16), new(2023,2,20), new(2023,4,7), new(2023,5,29),
        new(2023,6,19), new(2023,7,4), new(2023,9,4), new(2023,11,23), new(2023,12,25),
        // 2024
        new(2024,1,1), new(2024,1,15), new(2024,2,19), new(2024,3,29), new(2024,5,27),
        new(2024,6,19), new(2024,7,4), new(2024,9,2), new(2024,11,28), new(2024,12,25),
        // 2025
        new(2025,1,1), new(2025,1,9), new(2025,1,20), new(2025,2,17), new(2025,4,18),
        new(2025,5,26), new(2025,6,19), new(2025,7,4), new(2025,9,1), new(2025,11,27),
        new(2025,12,25),
        // 2026
        new(2026,1,1), new(2026,1,19), new(2026,2,16), new(2026,4,3), new(2026,5,25),
        new(2026,6,19), new(2026,7,3), new(2026,9,7), new(2026,11,26), new(2026,12,25),
    };

    /// <summary>Best-effort NYSE trading-day check: weekday and not a known holiday.</summary>
    public static bool IsTradingDay(DateOnly d)
    {
        if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) return false;
        return !FullCloseHolidays.Contains(d);
    }

    /// <summary>Yield one <see cref="DateTime"/> per trading day in [start, end] inclusive, stamped at 16:00 ET (default).</summary>
    public static IEnumerable<DateTime> IterDays(DateOnly start, DateOnly end, TimeOnly? closeAt = null)
    {
        var close = closeAt ?? new TimeOnly(16, 0);
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (IsTradingDay(d))
                yield return d.ToDateTime(close);
        }
    }

    /// <summary>Yield ET wall-clock minute timestamps inside RTH for every trading day in [start, end].</summary>
    public static IEnumerable<DateTime> IterMinutes(
        DateOnly start,
        DateOnly end,
        int stepMinutes = 1,
        TimeOnly? openAt = null,
        TimeOnly? closeAt = null)
    {
        if (stepMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(stepMinutes), "stepMinutes must be positive");

        var open = openAt ?? new TimeOnly(9, 30);
        var close = closeAt ?? new TimeOnly(16, 0);

        foreach (var dayClose in IterDays(start, end, close))
        {
            var d = DateOnly.FromDateTime(dayClose);
            var endStamp = d.ToDateTime(close);
            for (var cur = d.ToDateTime(open); cur <= endStamp; cur = cur.AddMinutes(stepMinutes))
                yield return cur;
        }
    }

    /// <summary>Endpoint signature: takes a symbol + ET wall-clock <c>at</c> string and returns the JSON.</summary>
    public delegate Task<JsonElement> AtEndpoint(FlashAlphaHistoricalClient client, string symbol, string at, CancellationToken ct);

    /// <summary>One step of a replay — formatted <c>at</c> string + the API response.</summary>
    public readonly record struct ReplayStep(string At, JsonElement Response);

    /// <summary>
    /// Replay an endpoint across a sequence of timestamps. Yields async — quota / rate
    /// limits flow through naturally. By default skips 404-class data gaps silently.
    /// </summary>
    public static async IAsyncEnumerable<ReplayStep> RunAsync(
        FlashAlphaHistoricalClient client,
        AtEndpoint endpoint,
        string symbol,
        IEnumerable<DateTime> timestamps,
        bool skipMissing = true,
        Action<DateTime, Exception>? onError = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var ts in timestamps)
        {
            ct.ThrowIfCancellationRequested();
            var atString = FlashAlphaHistoricalClient.FormatAt(ts);
            JsonElement? snap = null;
            try
            {
                snap = await endpoint(client, symbol, atString, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (skipMissing && IsDataGap(ex))
            {
                onError?.Invoke(ts, ex);
                continue;
            }
            yield return new ReplayStep(atString, snap.GetValueOrDefault());
        }
    }

    private static bool IsDataGap(Exception ex)
        => ex is NoDataException or SymbolNotFoundException or InsufficientDataException;
}

/// <summary>One step in a backtest run.</summary>
public sealed record BacktestStep(string At, JsonElement Snapshot, object? Output);

/// <summary>Strategy callback — takes (at, snapshot) and returns an opaque output object.</summary>
public delegate object? Strategy(string at, JsonElement snapshot);

/// <summary>Async strategy callback variant.</summary>
public delegate Task<object?> AsyncStrategy(string at, JsonElement snapshot);

/// <summary>
/// Run a strategy callback against the Historical API across a date range. No
/// fill simulation, no portfolio accounting — that belongs in user code.
/// </summary>
public sealed class Backtester
{
    private readonly FlashAlphaHistoricalClient _client;
    private readonly Replay.AtEndpoint _endpoint;
    private readonly string _symbol;
    private readonly bool _skipMissing;

    public Backtester(
        FlashAlphaHistoricalClient client,
        Replay.AtEndpoint endpoint,
        string symbol = "SPY",
        bool skipMissing = true)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _symbol = symbol;
        _skipMissing = skipMissing;
    }

    /// <summary>Default endpoint — full stock summary at session close.</summary>
    public static readonly Replay.AtEndpoint StockSummaryEndpoint =
        (client, symbol, at, ct) => client.StockSummaryAsync(symbol, at, ct);

    /// <summary>Endpoint helper for exposure summary.</summary>
    public static readonly Replay.AtEndpoint ExposureSummaryEndpoint =
        (client, symbol, at, ct) => client.ExposureSummaryAsync(symbol, at, ct);

    /// <summary>Endpoint helper for VRP.</summary>
    public static readonly Replay.AtEndpoint VrpEndpoint =
        (client, symbol, at, ct) => client.VrpAsync(symbol, at, ct);

    public async Task<List<BacktestStep>> RunAsync(
        IEnumerable<DateTime> timestamps,
        Strategy strategy,
        Action<DateTime, Exception>? onError = null,
        CancellationToken ct = default)
    {
        var results = new List<BacktestStep>();
        await foreach (var step in Replay.RunAsync(_client, _endpoint, _symbol, timestamps, _skipMissing, onError, ct).ConfigureAwait(false))
        {
            results.Add(new BacktestStep(step.At, step.Response, strategy(step.At, step.Response)));
        }
        return results;
    }

    public async Task<List<BacktestStep>> RunAsync(
        IEnumerable<DateTime> timestamps,
        AsyncStrategy strategy,
        Action<DateTime, Exception>? onError = null,
        CancellationToken ct = default)
    {
        var results = new List<BacktestStep>();
        await foreach (var step in Replay.RunAsync(_client, _endpoint, _symbol, timestamps, _skipMissing, onError, ct).ConfigureAwait(false))
        {
            var output = await strategy(step.At, step.Response).ConfigureAwait(false);
            results.Add(new BacktestStep(step.At, step.Response, output));
        }
        return results;
    }
}
