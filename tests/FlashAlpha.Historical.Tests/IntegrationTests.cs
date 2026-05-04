using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace FlashAlpha.Historical.Tests;

/// <summary>
/// Integration tests — hit the live https://historical.flashalpha.com.
/// Skipped unless FLASHALPHA_API_KEY is set.
///
///   FLASHALPHA_API_KEY=fa_... dotnet test --filter "FullyQualifiedName~IntegrationTests"
/// </summary>
public class IntegrationTests
{
    private const string SpyAt = "2024-08-05T15:30:00";
    private const string SpyDate = "2024-08-05";
    private const double ExpectedSpot = 516.435;
    private const double SpotTol = 1.0;

    private static readonly HashSet<string> Regimes =
        new() { "positive_gamma", "negative_gamma", "transition" };

    private static readonly string? ApiKey = Environment.GetEnvironmentVariable("FLASHALPHA_API_KEY");

    private static FlashAlphaHistoricalClient MakeClient() => new(ApiKey!, timeout: 60);

    public static bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

    private const string SkipReason = "FLASHALPHA_API_KEY not set";

    // ── Coverage ────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Tickers_ListsSpy()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var out_ = await client.TickersAsync();
        Assert.True(out_.GetProperty("count").GetInt32() >= 1);
        var symbols = out_.GetProperty("tickers").EnumerateArray()
            .Select(t => t.GetProperty("symbol").GetString()).ToList();
        Assert.Contains("SPY", symbols);
    }

    [SkippableFact]
    public async Task Tickers_FilterBySpy_ReturnsObject()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var out_ = await client.TickersAsync("SPY");
        Assert.Equal("SPY", out_.GetProperty("symbol").GetString());
        var cov = out_.GetProperty("coverage");
        Assert.True(string.Compare(cov.GetProperty("first").GetString(), "2024-08-05", StringComparison.Ordinal) <= 0);
        Assert.True(string.Compare(cov.GetProperty("last").GetString(), "2024-08-05", StringComparison.Ordinal) >= 0);
        Assert.True(cov.GetProperty("healthy_days").GetInt32() > 0);
    }

    [SkippableFact]
    public async Task Tickers_UnknownSymbol_ThrowsNoCoverage()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        await Assert.ThrowsAsync<NoCoverageException>(() => client.TickersAsync("ZZZZZ"));
    }

    // ── Market data ────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task StockQuote_AtMinuteResolution()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var q = await client.StockQuoteAsync("SPY", SpyAt);
        Assert.Equal("SPY", q.GetProperty("ticker").GetString());
        var bid = q.GetProperty("bid").GetDouble();
        var mid = q.GetProperty("mid").GetDouble();
        var ask = q.GetProperty("ask").GetDouble();
        Assert.True(bid <= mid);
        Assert.True(mid <= ask);
        Assert.True(Math.Abs(mid - ExpectedSpot) < SpotTol);
        Assert.Equal(SpyAt, q.GetProperty("lastUpdate").GetString());
    }

    [SkippableFact]
    public async Task StockQuote_DateOnly_DefaultsToSessionClose()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var q = await client.StockQuoteAsync("SPY", SpyDate);
        Assert.EndsWith("T16:00:00", q.GetProperty("lastUpdate").GetString());
    }

    [SkippableFact]
    public async Task StockQuote_DateTimeOverload_Works()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var q = await client.StockQuoteAsync("SPY", new DateTime(2024, 8, 5, 15, 30, 0));
        Assert.True(Math.Abs(q.GetProperty("mid").GetDouble() - ExpectedSpot) < SpotTol);
    }

    [SkippableFact]
    public async Task OptionQuote_AllFilters_ReturnsSingleObjectWithGreeks()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var q = await client.OptionQuoteAsync("SPY", SpyAt,
            expiry: "2024-08-09", strike: 520, type: "C");
        Assert.Equal(520, q.GetProperty("strike").GetInt32());
        Assert.Equal("C", q.GetProperty("type").GetString());
        foreach (var g in new[] { "delta", "gamma", "theta", "vega", "rho", "vanna", "charm" })
            Assert.Equal(JsonValueKind.Number, q.GetProperty(g).ValueKind);
        // Documented historical-mode gaps
        Assert.Equal(0, q.GetProperty("bidSize").GetInt32());
        Assert.Equal(0, q.GetProperty("askSize").GetInt32());
        Assert.Equal(0, q.GetProperty("volume").GetInt32());
        Assert.Equal(JsonValueKind.Null, q.GetProperty("svi_vol").ValueKind);
        Assert.Equal("backtest_mode", q.GetProperty("svi_vol_gated").GetString());
        Assert.True(q.GetProperty("open_interest").GetInt32() >= 0);
    }

    // ── Exposure ───────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task ExposureSummary_ShapeAndInvariants()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var s = await client.ExposureSummaryAsync("SPY", SpyAt);
        Assert.Equal("SPY", s.GetProperty("symbol").GetString());
        Assert.True(Math.Abs(s.GetProperty("underlying_price").GetDouble() - ExpectedSpot) < SpotTol);
        Assert.Contains(s.GetProperty("regime").GetString()!, Regimes);
        Assert.Equal(JsonValueKind.Number, s.GetProperty("gamma_flip").ValueKind);
        var e = s.GetProperty("exposures");
        foreach (var k in new[] { "net_gex", "net_dex", "net_vex", "net_chex" })
            Assert.Equal(JsonValueKind.Number, e.GetProperty(k).ValueKind);
        var h = s.GetProperty("hedging_estimate");
        var up = h.GetProperty("spot_up_1pct").GetProperty("dealer_shares_to_trade").GetInt64();
        var down = h.GetProperty("spot_down_1pct").GetProperty("dealer_shares_to_trade").GetInt64();
        Assert.Equal(up, -down);
    }

    [SkippableFact]
    public async Task Levels_KeysPresent()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var out_ = await client.ExposureLevelsAsync("SPY", SpyAt);
        var levels = out_.GetProperty("levels");
        foreach (var k in new[] { "gamma_flip", "max_positive_gamma", "max_negative_gamma",
                                  "call_wall", "put_wall", "highest_oi_strike" })
            Assert.True(levels.TryGetProperty(k, out _), $"missing {k}");
    }

    [SkippableFact]
    public async Task Gex_StrikesShapeAndDocumentedZeros()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var gex = await client.GexAsync("SPY", SpyAt, minOi: 100);
        var strikes = gex.GetProperty("strikes").EnumerateArray().ToList();
        Assert.True(strikes.Count > 5);
        var sample = strikes[0];
        Assert.Equal(0, sample.GetProperty("call_volume").GetInt32());
        Assert.Equal(0, sample.GetProperty("put_volume").GetInt32());
        Assert.Equal(JsonValueKind.Null, sample.GetProperty("call_oi_change").ValueKind);
        Assert.Equal(JsonValueKind.Null, sample.GetProperty("put_oi_change").ValueKind);
    }

    [SkippableFact]
    public async Task Dex_PayloadShape()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var out_ = await client.DexAsync("SPY", SpyAt);
        Assert.Equal(JsonValueKind.Number, out_.GetProperty("payload").GetProperty("net_dex").ValueKind);
    }

    [SkippableFact]
    public async Task Vex_PayloadAndInterpretation()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var out_ = await client.VexAsync("SPY", SpyAt);
        Assert.Equal(JsonValueKind.Number, out_.GetProperty("payload").GetProperty("net_vex").ValueKind);
        Assert.Equal(JsonValueKind.String, out_.GetProperty("payload").GetProperty("vex_interpretation").ValueKind);
    }

    [SkippableFact]
    public async Task Chex_PayloadAndInterpretation()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var out_ = await client.ChexAsync("SPY", SpyAt);
        Assert.Equal(JsonValueKind.Number, out_.GetProperty("payload").GetProperty("net_chex").ValueKind);
        Assert.Equal(JsonValueKind.String, out_.GetProperty("payload").GetProperty("chex_interpretation").ValueKind);
    }

    [SkippableFact]
    public async Task Narrative_ReturnsBlocks()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var out_ = await client.NarrativeAsync("SPY", SpyAt);
        var n = out_.GetProperty("narrative");
        foreach (var b in new[] { "regime", "gex_change", "key_levels", "flow", "vanna", "charm", "zero_dte" })
            Assert.Equal(JsonValueKind.String, n.GetProperty(b).ValueKind);
        Assert.Empty(n.GetProperty("data").GetProperty("top_oi_changes").EnumerateArray());
    }

    [SkippableFact]
    public async Task ZeroDte_BasicShape()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var out_ = await client.ZeroDteAsync("SPY", SpyAt);
        Assert.True(out_.TryGetProperty("expiration", out _));
        Assert.True(out_.TryGetProperty("regime", out _));
        Assert.True(out_.TryGetProperty("exposures", out _));
    }

    // ── Composite & Vol ────────────────────────────────────────────────────

    [SkippableFact]
    public async Task StockSummary_BlockKeysAndDocumentedGaps()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var s = await client.StockSummaryAsync("SPY", SpyAt);
        foreach (var k in new[] { "price", "volatility", "options_flow", "exposure", "macro" })
            Assert.True(s.TryGetProperty(k, out _), $"missing {k}");
        var of = s.GetProperty("options_flow");
        Assert.Equal(0, of.GetProperty("total_call_volume").GetInt64());
        Assert.Equal(0, of.GetProperty("total_put_volume").GetInt64());
        Assert.Equal(JsonValueKind.Null, of.GetProperty("pc_ratio_volume").ValueKind);
        var macro = s.GetProperty("macro");
        Assert.Equal(JsonValueKind.Null, macro.GetProperty("vix_futures").ValueKind);
        Assert.Equal(JsonValueKind.Null, macro.GetProperty("fear_and_greed").ValueKind);
    }

    [SkippableFact]
    public async Task Volatility_RealizedLadder()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var v = await client.VolatilityAsync("SPY", SpyAt);
        var rv = v.GetProperty("realized_vol");
        foreach (var w in new[] { "rv_5d", "rv_10d", "rv_20d", "rv_30d", "rv_60d" })
            Assert.True(rv.TryGetProperty(w, out _));
        Assert.Equal(JsonValueKind.Number, v.GetProperty("atm_iv").ValueKind);
    }

    [SkippableFact]
    public async Task AdvVolatility_SviFitsAndVarianceSurface()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var adv = await client.AdvVolatilityAsync("SPY", SpyAt);
        var svi = adv.GetProperty("svi_parameters");
        Assert.True(svi.GetArrayLength() > 0);
        var first = svi[0];
        foreach (var k in new[] { "expiry", "a", "b", "rho", "m", "sigma", "forward" })
            Assert.True(first.TryGetProperty(k, out _));
        Assert.True(adv.GetProperty("total_variance_surface").GetProperty("total_variance")[0].ValueKind == JsonValueKind.Array);
    }

    // ── Surface ────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Surface_50x50Grid()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var out_ = await client.SurfaceAsync("SPY", SpyAt);
        Assert.Equal(50, out_.GetProperty("grid_size").GetInt32());
        Assert.Equal(50, out_.GetProperty("tenors").GetArrayLength());
        Assert.Equal(50, out_.GetProperty("moneyness").GetArrayLength());
        Assert.Equal(50, out_.GetProperty("iv").GetArrayLength());
        Assert.Equal(50, out_.GetProperty("iv")[0].GetArrayLength());
    }

    // ── VRP ────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Vrp_DashboardKeysAndHySpreadHardcode()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var v = await client.VrpAsync("SPY", SpyAt);
        var core = v.GetProperty("vrp");
        foreach (var k in new[] { "atm_iv", "rv_5d", "rv_10d", "rv_20d", "rv_30d",
                                  "vrp_5d", "vrp_10d", "vrp_20d", "vrp_30d" })
            Assert.True(core.TryGetProperty(k, out _));
        Assert.Equal(3.5, v.GetProperty("macro").GetProperty("hy_spread").GetDouble());
    }

    // ── Max Pain ───────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task MaxPain_PainCurveMinimumIsAtMaxPainStrike()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var mp = await client.MaxPainAsync("SPY", SpyAt, expiration: "2024-08-09");
        Assert.Equal("2024-08-09", mp.GetProperty("expiration").GetString());
        var maxPainStrike = mp.GetProperty("max_pain_strike").GetDouble();
        var minPair = mp.GetProperty("pain_curve").EnumerateArray()
            .Select(r => (Strike: r.GetProperty("strike").GetDouble(),
                          Total: r.GetProperty("total_pain").GetDouble()))
            .OrderBy(x => x.Total)
            .First();
        Assert.True(Math.Abs(minPair.Strike - maxPainStrike) <= 5);
    }

    // ── Errors ─────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task InvalidAt_ThrowsTyped()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        await Assert.ThrowsAsync<InvalidAtException>(() =>
            client.ExposureSummaryAsync("SPY", "garbage"));
    }

    [SkippableFact]
    public async Task OutOfCoverage_ThrowsNoData()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        await Assert.ThrowsAsync<NoDataException>(() =>
            client.ExposureSummaryAsync("SPY", "2017-01-01"));
    }

    [SkippableFact]
    public async Task Holiday_ThrowsNoData()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        await Assert.ThrowsAsync<NoDataException>(() =>
            client.ExposureSummaryAsync("SPY", "2024-01-01"));
    }

    [SkippableFact]
    public async Task OptionQuote_NonexistentStrike_ThrowsNoData()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        await Assert.ThrowsAsync<NoDataException>(() =>
            client.OptionQuoteAsync("SPY", SpyAt,
                expiry: "2024-08-09", strike: 99999, type: "C"));
    }

    // ── Replay & Backtester ────────────────────────────────────────────────

    [SkippableFact]
    public async Task Replay_OneTradingWeek()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var steps = new List<Replay.ReplayStep>();
        await foreach (var s in Replay.RunAsync(
            client, Backtester.ExposureSummaryEndpoint, "SPY",
            Replay.IterDays(new(2024, 8, 5), new(2024, 8, 9))))
        {
            steps.Add(s);
        }
        Assert.Equal(5, steps.Count);
        foreach (var s in steps)
        {
            Assert.Equal("SPY", s.Response.GetProperty("symbol").GetString());
            Assert.Contains(s.Response.GetProperty("regime").GetString()!, Regimes);
        }
    }

    [SkippableFact]
    public async Task Replay_OneDayAt30MinStep()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var steps = new List<Replay.ReplayStep>();
        await foreach (var s in Replay.RunAsync(
            client, Backtester.ExposureSummaryEndpoint, "SPY",
            Replay.IterMinutes(new(2024, 8, 5), new(2024, 8, 5), stepMinutes: 30)))
        {
            steps.Add(s);
        }
        Assert.Equal(14, steps.Count);
        var spots = steps.Select(s => s.Response.GetProperty("underlying_price").GetDouble()).ToHashSet();
        Assert.True(spots.Count > 1);
    }

    [SkippableFact]
    public async Task Replay_SkipsHolidaySilently()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var errors = new List<DateTime>();
        var steps = new List<Replay.ReplayStep>();
        await foreach (var s in Replay.RunAsync(
            client, Backtester.ExposureSummaryEndpoint, "SPY",
            new[] { new DateTime(2024, 8, 5, 15, 30, 0), new DateTime(2024, 1, 1, 16, 0, 0) },
            onError: (ts, _) => errors.Add(ts)))
        {
            steps.Add(s);
        }
        Assert.Single(steps);
        Assert.Single(errors);
    }

    [SkippableFact]
    public async Task Backtester_RunsStrategyAndCollectsOutputs()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var bt = new Backtester(client, Backtester.StockSummaryEndpoint, "SPY");
        var results = await bt.RunAsync(
            Replay.IterDays(new(2024, 8, 5), new(2024, 8, 9)),
            (at, snap) => new
            {
                Vrp = snap.GetProperty("volatility").GetProperty("vrp").GetDouble(),
                Regime = snap.GetProperty("exposure").GetProperty("regime").GetString(),
            });
        Assert.Equal(5, results.Count);
        foreach (var r in results)
        {
            var output = (dynamic)r.Output!;
            Assert.Contains((string)output.Regime, Regimes);
        }
    }
}

// SkippableFact / Skip.IfNot are provided by the Xunit.SkippableFact NuGet package.
