using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlashAlpha.Historical.Models;
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
        new() { "positive_gamma", "negative_gamma", "unknown" };

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
    public async Task ExposureSummary_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var s = await client.ExposureSummaryAsync("SPY", SpyAt);
        // ── top-level scalars ──
        Assert.Equal("SPY", s.GetProperty("symbol").GetString());
        Assert.True(Math.Abs(s.GetProperty("underlying_price").GetDouble() - ExpectedSpot) < SpotTol);
        Assert.Equal(JsonValueKind.String, s.GetProperty("as_of").ValueKind);
        Assert.False(string.IsNullOrEmpty(s.GetProperty("as_of").GetString()));
        Assert.Equal(SpyAt, s.GetProperty("as_of").GetString()); // historical snaps to requested minute
        Assert.Contains(s.GetProperty("regime").GetString()!, Regimes);
        Assert.Equal(JsonValueKind.Number, s.GetProperty("gamma_flip").ValueKind);
        // ── exposures block (4 fields) ──
        var e = s.GetProperty("exposures");
        foreach (var k in new[] { "net_gex", "net_dex", "net_vex", "net_chex" })
            Assert.Equal(JsonValueKind.Number, e.GetProperty(k).ValueKind);
        // ── interpretation block (3 fields) ──
        var interp = s.GetProperty("interpretation");
        foreach (var k in new[] { "gamma", "vanna", "charm" })
        {
            Assert.Equal(JsonValueKind.String, interp.GetProperty(k).ValueKind);
            Assert.False(string.IsNullOrEmpty(interp.GetProperty(k).GetString()));
        }
        // ── hedging_estimate (every leaf on both sides) ──
        var h = s.GetProperty("hedging_estimate");
        foreach (var sideKey in new[] { "spot_up_1pct", "spot_down_1pct" })
        {
            var side = h.GetProperty(sideKey);
            Assert.Contains(side.GetProperty("direction").GetString(), new[] { "buy", "sell" });
            Assert.Equal(JsonValueKind.Number, side.GetProperty("dealer_shares_to_trade").ValueKind);
            Assert.Equal(JsonValueKind.Number, side.GetProperty("notional_usd").ValueKind);
            Assert.NotEqual(0, side.GetProperty("notional_usd").GetInt64());
        }
        var up = h.GetProperty("spot_up_1pct").GetProperty("dealer_shares_to_trade").GetInt64();
        var down = h.GetProperty("spot_down_1pct").GetProperty("dealer_shares_to_trade").GetInt64();
        Assert.Equal(up, -down);
        // ── zero_dte block (3 fields) ──
        var z = s.GetProperty("zero_dte");
        Assert.True(z.TryGetProperty("net_gex", out var zng));
        Assert.True(zng.ValueKind == JsonValueKind.Null || zng.ValueKind == JsonValueKind.Number);
        Assert.True(z.TryGetProperty("pct_of_total_gex", out var zpct));
        Assert.True(zpct.ValueKind == JsonValueKind.Null || zpct.ValueKind == JsonValueKind.Number);
        Assert.True(z.TryGetProperty("expiration", out var zexp));
        Assert.True(zexp.ValueKind == JsonValueKind.Null || zexp.ValueKind == JsonValueKind.String);
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
    public async Task Vrp_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var v = await client.VrpAsync("SPY", SpyAt);

        // ── top-level scalars ──
        Assert.Equal("SPY", v.GetProperty("symbol").GetString());
        Assert.Equal(JsonValueKind.Number, v.GetProperty("underlying_price").ValueKind);
        Assert.Equal(JsonValueKind.String, v.GetProperty("as_of").ValueKind);
        var mkt = v.GetProperty("market_open").ValueKind;
        Assert.True(mkt == JsonValueKind.True || mkt == JsonValueKind.False, "market_open must be a boolean");
        Assert.Equal(JsonValueKind.Number, v.GetProperty("variance_risk_premium").ValueKind);
        Assert.Equal(JsonValueKind.Number, v.GetProperty("convexity_premium").ValueKind);
        Assert.Equal(JsonValueKind.Number, v.GetProperty("fair_vol").ValueKind);
        Assert.True(v.TryGetProperty("dealer_flow_risk", out _));
        Assert.Equal(JsonValueKind.Array, v.GetProperty("warnings").ValueKind);
        // strategy_scores / net_harvest_score: nullable on historical
        Assert.True(v.TryGetProperty("strategy_scores", out var ssElem));
        Assert.True(v.TryGetProperty("net_harvest_score", out _));
        if (ssElem.ValueKind != JsonValueKind.Null)
        {
            foreach (var k in new[] { "short_put_spread", "short_strangle", "iron_condor", "calendar_spread" })
                Assert.True(ssElem.TryGetProperty(k, out _));
        }
        // Customer trap: net_gex must NOT be top-level
        Assert.False(v.TryGetProperty("net_gex", out _));

        // ── vrp.* core block ──
        var core = v.GetProperty("vrp");
        foreach (var k in new[] { "atm_iv", "rv_5d", "rv_10d", "rv_20d", "rv_30d",
                                  "vrp_5d", "vrp_10d", "vrp_20d", "vrp_30d" })
            Assert.Equal(JsonValueKind.Number, core.GetProperty(k).ValueKind);
        Assert.True(core.TryGetProperty("z_score", out _)); // nullable
        Assert.True(core.TryGetProperty("percentile", out _)); // nullable
        Assert.Equal(JsonValueKind.Number, core.GetProperty("history_days").ValueKind);

        // ── directional ──
        var dir = v.GetProperty("directional");
        foreach (var k in new[] { "put_wing_iv_25d", "call_wing_iv_25d",
                                  "downside_rv_20d", "upside_rv_20d",
                                  "downside_vrp", "upside_vrp" })
            Assert.Equal(JsonValueKind.Number, dir.GetProperty(k).ValueKind);
        Assert.False(dir.TryGetProperty("put_vrp", out _));
        Assert.False(dir.TryGetProperty("call_vrp", out _));

        // ── term_vrp[] ──
        var term = v.GetProperty("term_vrp");
        Assert.Equal(JsonValueKind.Array, term.ValueKind);
        Assert.True(term.GetArrayLength() > 0);
        var first = term[0];
        foreach (var k in new[] { "dte", "iv", "rv", "vrp" })
            Assert.True(first.TryGetProperty(k, out _));

        // ── gex_conditioned ──
        var gc = v.GetProperty("gex_conditioned");
        Assert.Equal(JsonValueKind.String, gc.GetProperty("regime").ValueKind);
        Assert.Equal(JsonValueKind.Number, gc.GetProperty("harvest_score").ValueKind);
        Assert.Equal(JsonValueKind.String, gc.GetProperty("interpretation").ValueKind);

        // ── vanna_conditioned ──
        var vc = v.GetProperty("vanna_conditioned");
        Assert.Equal(JsonValueKind.String, vc.GetProperty("outlook").ValueKind);
        Assert.Equal(JsonValueKind.String, vc.GetProperty("interpretation").ValueKind);

        // ── regime — net_gex lives HERE ──
        var reg = v.GetProperty("regime");
        Assert.Equal(JsonValueKind.String, reg.GetProperty("gamma").ValueKind);
        Assert.True(reg.TryGetProperty("vrp_regime", out _)); // nullable
        Assert.Equal(JsonValueKind.Number, reg.GetProperty("net_gex").ValueKind);
        Assert.Equal(JsonValueKind.Number, reg.GetProperty("gamma_flip").ValueKind);

        // ── macro (historical-specific shape) ──
        var macro = v.GetProperty("macro");
        foreach (var k in new[] { "vix", "vix_3m", "vix_term_slope", "dgs10", "hy_spread" })
            Assert.Equal(JsonValueKind.Number, macro.GetProperty(k).ValueKind);
        // fed_funds is live-only — must NOT be present on historical
        Assert.False(macro.TryGetProperty("fed_funds", out _));
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

    [SkippableFact]
    public async Task MaxPain_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        // Full-chain (no expiration) so max_pain_by_expiration is populated.
        var r = await client.MaxPainAsync("SPY", SpyAt);

        // ── top-level scalars ──
        Assert.Equal("SPY", r.GetProperty("symbol").GetString());
        Assert.Equal(JsonValueKind.Number, r.GetProperty("underlying_price").ValueKind);
        Assert.Equal(JsonValueKind.String, r.GetProperty("as_of").ValueKind);
        Assert.Equal(JsonValueKind.Number, r.GetProperty("max_pain_strike").ValueKind);
        Assert.Contains(r.GetProperty("signal").GetString(), new[] { "bullish", "bearish", "neutral" });
        Assert.Equal(JsonValueKind.String, r.GetProperty("expiration").ValueKind);
        Assert.Equal(JsonValueKind.Number, r.GetProperty("put_call_oi_ratio").ValueKind);
        Assert.Contains(r.GetProperty("regime").GetString(),
            new[] { "positive_gamma", "negative_gamma", "unknown" });
        var pin = r.GetProperty("pin_probability").GetInt32();
        Assert.InRange(pin, 0, 100);

        // ── distance ──
        var dist = r.GetProperty("distance");
        Assert.Equal(JsonValueKind.Number, dist.GetProperty("absolute").ValueKind);
        Assert.Equal(JsonValueKind.Number, dist.GetProperty("percent").ValueKind);
        Assert.Contains(dist.GetProperty("direction").GetString(), new[] { "above", "below", "at" });

        // ── pain_curve[] ──
        var pc = r.GetProperty("pain_curve");
        Assert.Equal(JsonValueKind.Array, pc.ValueKind);
        Assert.True(pc.GetArrayLength() > 0);
        var pcRow = pc[0];
        foreach (var k in new[] { "strike", "call_pain", "put_pain", "total_pain" })
            Assert.Equal(JsonValueKind.Number, pcRow.GetProperty(k).ValueKind);

        // ── oi_by_strike[] — historical: volume fields are 0 placeholders ──
        var oi = r.GetProperty("oi_by_strike");
        Assert.Equal(JsonValueKind.Array, oi.ValueKind);
        Assert.True(oi.GetArrayLength() > 0);
        var oiRow = oi[0];
        foreach (var k in new[] { "strike", "call_oi", "put_oi", "total_oi", "call_volume", "put_volume" })
            Assert.Equal(JsonValueKind.Number, oiRow.GetProperty(k).ValueKind);
        Assert.Equal(0, oiRow.GetProperty("call_volume").GetInt32());
        Assert.Equal(0, oiRow.GetProperty("put_volume").GetInt32());

        // ── max_pain_by_expiration[] ──
        var mpe = r.GetProperty("max_pain_by_expiration");
        Assert.Equal(JsonValueKind.Array, mpe.ValueKind);
        Assert.True(mpe.GetArrayLength() > 0);
        var mpeRow = mpe[0];
        Assert.Equal(JsonValueKind.String, mpeRow.GetProperty("expiration").ValueKind);
        Assert.Equal(JsonValueKind.Number, mpeRow.GetProperty("max_pain_strike").ValueKind);
        Assert.Equal(JsonValueKind.Number, mpeRow.GetProperty("dte").ValueKind);
        Assert.Equal(JsonValueKind.Number, mpeRow.GetProperty("total_oi").ValueKind);

        // ── dealer_alignment ──
        var da = r.GetProperty("dealer_alignment");
        Assert.Contains(da.GetProperty("alignment").GetString(),
            new[] { "converging", "moderate", "diverging", "unknown" });
        Assert.Equal(JsonValueKind.String, da.GetProperty("description").ValueKind);
        foreach (var k in new[] { "gamma_flip", "call_wall", "put_wall" })
            Assert.Equal(JsonValueKind.Number, da.GetProperty(k).ValueKind);

        // ── expected_move ──
        var em = r.GetProperty("expected_move");
        Assert.Equal(JsonValueKind.Number, em.GetProperty("straddle_price").ValueKind);
        Assert.Equal(JsonValueKind.Number, em.GetProperty("atm_iv").ValueKind);
        var rng = em.GetProperty("max_pain_within_expected_range").ValueKind;
        Assert.True(rng == JsonValueKind.True || rng == JsonValueKind.False);
    }

    [SkippableFact]
    public async Task MaxPain_ExpirationFilter_SuppressesCalendar()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var mp = await client.MaxPainAsync("SPY", SpyAt, expiration: "2024-08-09");
        // When expiration filter is set, max_pain_by_expiration is null.
        var kind = mp.GetProperty("max_pain_by_expiration").ValueKind;
        Assert.Equal(JsonValueKind.Null, kind);
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

    // ── rc.4 typed-POCO field-walk coverage ──────────────────────────────────
    //
    // Each test below deserializes a historical response into the matching
    // typed POCO and asserts every property is non-null. A renamed/deleted
    // JsonPropertyName will surface immediately as a NotNull failure.
    //
    // Historical-specific gaps documented in StockSummary docs are honoured:
    //   - options_flow volume fields are 0 placeholders → pc_ratio_volume null
    //   - macro.vix_futures / macro.fear_and_greed are null on historical

    [SkippableFact]
    public async Task StockSummary_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var elem = await client.StockSummaryAsync("SPY", SpyAt);
        var r = JsonSerializer.Deserialize<StockSummaryResponse>(elem);
        Assert.NotNull(r);

        // top-level
        Assert.Equal("SPY", r!.Symbol);
        Assert.NotNull(r.AsOf);
        Assert.NotNull(r.MarketOpen);

        // price
        Assert.NotNull(r.Price);
        Assert.NotNull(r.Price!.Bid);
        Assert.NotNull(r.Price.Ask);
        Assert.NotNull(r.Price.Mid);
        Assert.NotNull(r.Price.Last);
        Assert.NotNull(r.Price.LastUpdate);

        // volatility
        Assert.NotNull(r.Volatility);
        Assert.NotNull(r.Volatility!.AtmIv);
        Assert.NotNull(r.Volatility.Hv20);
        Assert.NotNull(r.Volatility.Hv60);
        Assert.NotNull(r.Volatility.Vrp);
        Assert.NotNull(r.Volatility.Skew25d);
        Assert.NotNull(r.Volatility.Skew25d!.Expiry);
        Assert.NotNull(r.Volatility.Skew25d.DaysToExpiry);
        Assert.NotNull(r.Volatility.Skew25d.Put25dIv);
        Assert.NotNull(r.Volatility.Skew25d.AtmIv);
        Assert.NotNull(r.Volatility.Skew25d.Call25dIv);
        Assert.NotNull(r.Volatility.Skew25d.Skew25dValue);
        Assert.NotNull(r.Volatility.Skew25d.SmileRatio);
        Assert.NotNull(r.Volatility.IvTermStructure);
        if (r.Volatility.IvTermStructure!.Count > 0)
        {
            var t = r.Volatility.IvTermStructure[0];
            Assert.NotNull(t.Expiry);
            Assert.NotNull(t.Iv);
            Assert.NotNull(t.DaysToExpiry);
        }

        // options_flow — historical: volume fields are 0 placeholders, so
        // pc_ratio_volume is null
        Assert.NotNull(r.OptionsFlow);
        Assert.NotNull(r.OptionsFlow!.TotalCallOi);
        Assert.NotNull(r.OptionsFlow.TotalPutOi);
        Assert.NotNull(r.OptionsFlow.TotalCallVolume);
        Assert.NotNull(r.OptionsFlow.TotalPutVolume);
        Assert.NotNull(r.OptionsFlow.PcRatioOi);
        // pc_ratio_volume nullable on historical (volumes are 0)
        Assert.NotNull(r.OptionsFlow.ActiveExpirations);

        // exposure
        Assert.NotNull(r.Exposure);
        Assert.NotNull(r.Exposure!.NetGex);
        Assert.NotNull(r.Exposure.NetDex);
        Assert.NotNull(r.Exposure.NetVex);
        Assert.NotNull(r.Exposure.NetChex);
        Assert.NotNull(r.Exposure.GammaFlip);
        Assert.NotNull(r.Exposure.CallWall);
        Assert.NotNull(r.Exposure.PutWall);
        Assert.NotNull(r.Exposure.MaxPain);
        Assert.NotNull(r.Exposure.HighestOiStrike);
        Assert.NotNull(r.Exposure.Regime);
        Assert.Contains(r.Exposure.Regime, Regimes);
        Assert.NotNull(r.Exposure.Interpretation);
        Assert.NotNull(r.Exposure.Interpretation!.Gamma);
        Assert.NotNull(r.Exposure.Interpretation.Vanna);
        Assert.NotNull(r.Exposure.Interpretation.Charm);
        Assert.NotNull(r.Exposure.HedgingEstimate);
        Assert.NotNull(r.Exposure.HedgingEstimate!.SpotDown1Pct);
        Assert.NotNull(r.Exposure.HedgingEstimate.SpotDown1Pct!.DealerShares);
        Assert.NotNull(r.Exposure.HedgingEstimate.SpotDown1Pct.Direction);
        Assert.NotNull(r.Exposure.HedgingEstimate.SpotDown1Pct.NotionalUsd);
        Assert.NotNull(r.Exposure.HedgingEstimate.SpotUp1Pct);
        Assert.NotNull(r.Exposure.HedgingEstimate.SpotUp1Pct!.DealerShares);
        Assert.NotNull(r.Exposure.HedgingEstimate.SpotUp1Pct.Direction);
        Assert.NotNull(r.Exposure.HedgingEstimate.SpotUp1Pct.NotionalUsd);
        Assert.NotNull(r.Exposure.ZeroDte);
        Assert.NotNull(r.Exposure.ZeroDte!.NetGex);
        Assert.NotNull(r.Exposure.ZeroDte.PctOfTotal);
        // expiration may be null when the requested minute had no 0DTE expiry
        Assert.NotNull(r.Exposure.TopStrikes);
        if (r.Exposure.TopStrikes!.Count > 0)
        {
            var ts = r.Exposure.TopStrikes[0];
            Assert.NotNull(ts.Strike);
            Assert.NotNull(ts.NetGex);
            Assert.NotNull(ts.CallOi);
            Assert.NotNull(ts.PutOi);
            Assert.NotNull(ts.TotalOi);
        }
        Assert.NotNull(r.Exposure.OiWeightedDte);

        // macro — historical: vix_futures / fear_and_greed are null;
        // the value/change/change_pct macro quotes are populated.
        Assert.NotNull(r.Macro);
        foreach (var (q, name) in new[]
        {
            (r.Macro!.Vix, "vix"), (r.Macro.Vvix, "vvix"),
            (r.Macro.Skew, "skew"), (r.Macro.Spx, "spx"), (r.Macro.Move, "move"),
        })
        {
            if (q is null) continue; // best-effort per docs
            Assert.True(q.Value is not null, $"macro.{name}.value null");
            Assert.True(q.Change is not null, $"macro.{name}.change null");
            Assert.True(q.ChangePct is not null, $"macro.{name}.change_pct null");
        }
        if (r.Macro.VixTermStructure is not null)
        {
            Assert.NotNull(r.Macro.VixTermStructure.Levels);
            Assert.NotNull(r.Macro.VixTermStructure.Levels!.Vix9d);
            Assert.NotNull(r.Macro.VixTermStructure.Levels.Vix);
            Assert.NotNull(r.Macro.VixTermStructure.Levels.Vix3m);
            Assert.NotNull(r.Macro.VixTermStructure.Levels.Vix6m);
            Assert.NotNull(r.Macro.VixTermStructure.NearSlopePct);
            Assert.NotNull(r.Macro.VixTermStructure.Structure);
        }
        // vix_futures and fear_and_greed are null on historical — documented
    }

    [SkippableFact]
    public async Task Narrative_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var elem = await client.NarrativeAsync("SPY", SpyAt);
        var r = JsonSerializer.Deserialize<NarrativeResponse>(elem);
        Assert.NotNull(r);

        Assert.Equal("SPY", r!.Symbol);
        Assert.NotNull(r.UnderlyingPrice);
        Assert.NotNull(r.AsOf);
        Assert.NotNull(r.Narrative);
        Assert.NotNull(r.Narrative!.Regime);
        Assert.NotNull(r.Narrative.GexChange);
        Assert.NotNull(r.Narrative.KeyLevels);
        Assert.NotNull(r.Narrative.Flow);
        Assert.NotNull(r.Narrative.Vanna);
        Assert.NotNull(r.Narrative.Charm);
        Assert.NotNull(r.Narrative.ZeroDte);
        Assert.NotNull(r.Narrative.Outlook);

        Assert.NotNull(r.Narrative.Data);
        var d = r.Narrative.Data!;
        Assert.NotNull(d.NetGex);
        Assert.NotNull(d.NetGexPrior);
        Assert.NotNull(d.NetGexChangePct);
        Assert.NotNull(d.Vix);
        Assert.NotNull(d.GammaFlip);
        Assert.NotNull(d.CallWall);
        Assert.NotNull(d.PutWall);
        Assert.NotNull(d.Regime);
        Assert.Contains(d.Regime, Regimes);
        Assert.NotNull(d.ZeroDtePct);
        // top_oi_changes is empty on historical (no intraday volume) but the
        // list itself must be non-null and walkable
        Assert.NotNull(d.TopOiChanges);
    }

    [SkippableFact]
    public async Task ExposureLevels_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var elem = await client.ExposureLevelsAsync("SPY", SpyAt);
        var r = JsonSerializer.Deserialize<ExposureLevelsResponse>(elem);
        Assert.NotNull(r);

        Assert.Equal("SPY", r!.Symbol);
        Assert.NotNull(r.UnderlyingPrice);
        Assert.NotNull(r.AsOf);
        Assert.NotNull(r.Levels);
        Assert.NotNull(r.Levels!.GammaFlip);
        Assert.NotNull(r.Levels.MaxPositiveGamma);
        Assert.NotNull(r.Levels.MaxNegativeGamma);
        Assert.NotNull(r.Levels.CallWall);
        Assert.NotNull(r.Levels.PutWall);
        Assert.NotNull(r.Levels.HighestOiStrike);
        // ZeroDteMagnet — explicit assertion was missing in Levels_KeysPresent
        Assert.NotNull(r.Levels.ZeroDteMagnet);
    }

    // ── rc.9 typed-POCO field-walk coverage ──────────────────────────────────
    //
    // Each test below calls the typed wrapper and asserts every public
    // property is populated (non-null) recursively. A renamed/deleted
    // JsonPropertyName surfaces immediately as a NotNull failure.
    // Historical caveats: per-strike call_volume / put_volume are 0
    // placeholders; volume-derived ratios under Volatility's PutCallProfile
    // are nullable.

    [SkippableFact]
    public async Task Volatility_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var r = await client.VolatilityTypedAsync("SPY", SpyAt);
        Assert.NotNull(r);

        // top-level
        Assert.Equal("SPY", r!.Symbol);
        Assert.NotNull(r.UnderlyingPrice);
        Assert.NotNull(r.AsOf);
        Assert.NotNull(r.MarketOpen);
        Assert.NotNull(r.AtmIv);

        // realized_vol
        Assert.NotNull(r.RealizedVol);
        Assert.NotNull(r.RealizedVol!.Rv5d);
        Assert.NotNull(r.RealizedVol.Rv10d);
        Assert.NotNull(r.RealizedVol.Rv20d);
        Assert.NotNull(r.RealizedVol.Rv30d);
        Assert.NotNull(r.RealizedVol.Rv60d);

        // iv_rv_spreads
        Assert.NotNull(r.IvRvSpreads);
        Assert.NotNull(r.IvRvSpreads!.Vrp5d);
        Assert.NotNull(r.IvRvSpreads.Vrp10d);
        Assert.NotNull(r.IvRvSpreads.Vrp20d);
        Assert.NotNull(r.IvRvSpreads.Vrp30d);
        Assert.NotNull(r.IvRvSpreads.Assessment);

        // skew_profiles[0]
        Assert.NotNull(r.SkewProfiles);
        if (r.SkewProfiles!.Count > 0)
        {
            var sp = r.SkewProfiles[0];
            Assert.NotNull(sp.Expiry);
            Assert.NotNull(sp.DaysToExpiry);
            Assert.NotNull(sp.Put10dIv);
            Assert.NotNull(sp.Put25dIv);
            Assert.NotNull(sp.AtmIv);
            Assert.NotNull(sp.Call25dIv);
            Assert.NotNull(sp.Call10dIv);
            Assert.NotNull(sp.Skew25d);
            Assert.NotNull(sp.SmileRatio);
            Assert.NotNull(sp.TailConvexity);
        }

        // term_structure
        Assert.NotNull(r.TermStructure);
        Assert.NotNull(r.TermStructure!.NearSlopePct);
        Assert.NotNull(r.TermStructure.FarSlopePct);
        Assert.NotNull(r.TermStructure.State);

        // iv_dispersion
        Assert.NotNull(r.IvDispersion);
        Assert.NotNull(r.IvDispersion!.CrossExpiry);
        Assert.NotNull(r.IvDispersion.CrossStrike);

        // gex_by_dte[0]
        Assert.NotNull(r.GexByDte);
        if (r.GexByDte!.Count > 0)
        {
            var g = r.GexByDte[0];
            Assert.NotNull(g.Bucket);
            Assert.NotNull(g.NetGex);
            Assert.NotNull(g.PctOfTotal);
            Assert.NotNull(g.ContractCount);
        }

        // theta_by_dte[0]
        Assert.NotNull(r.ThetaByDte);
        if (r.ThetaByDte!.Count > 0)
        {
            var t = r.ThetaByDte[0];
            Assert.NotNull(t.Bucket);
            Assert.NotNull(t.NetTheta);
            Assert.NotNull(t.ContractCount);
        }

        // put_call_profile
        Assert.NotNull(r.PutCallProfile);
        Assert.NotNull(r.PutCallProfile!.ByExpiry);
        if (r.PutCallProfile.ByExpiry!.Count > 0)
        {
            var pcr = r.PutCallProfile.ByExpiry[0];
            Assert.NotNull(pcr.Expiry);
            Assert.NotNull(pcr.CallOi);
            Assert.NotNull(pcr.PutOi);
            Assert.NotNull(pcr.PcRatioOi);
            // call_volume / put_volume are 0 placeholders on historical but
            // the int? is still populated (with 0)
            Assert.NotNull(pcr.CallVolume);
            Assert.NotNull(pcr.PutVolume);
            // pc_ratio_volume nullable on historical (volumes are 0)
        }
        Assert.NotNull(r.PutCallProfile.ByMoneyness);
        Assert.NotNull(r.PutCallProfile.ByMoneyness!.OtmCallOi);
        Assert.NotNull(r.PutCallProfile.ByMoneyness.AtmCallOi);
        Assert.NotNull(r.PutCallProfile.ByMoneyness.ItmCallOi);
        Assert.NotNull(r.PutCallProfile.ByMoneyness.OtmPutOi);
        Assert.NotNull(r.PutCallProfile.ByMoneyness.AtmPutOi);
        Assert.NotNull(r.PutCallProfile.ByMoneyness.ItmPutOi);

        // oi_concentration
        Assert.NotNull(r.OiConcentration);
        Assert.NotNull(r.OiConcentration!.Top3Pct);
        Assert.NotNull(r.OiConcentration.Top5Pct);
        Assert.NotNull(r.OiConcentration.Top10Pct);
        Assert.NotNull(r.OiConcentration.Herfindahl);

        // hedging_scenarios[0]
        Assert.NotNull(r.HedgingScenarios);
        if (r.HedgingScenarios!.Count > 0)
        {
            var h = r.HedgingScenarios[0];
            Assert.NotNull(h.MovePct);
            Assert.NotNull(h.DealerShares);
            Assert.NotNull(h.Direction);
            Assert.NotNull(h.NotionalUsd);
        }

        // liquidity
        Assert.NotNull(r.Liquidity);
        Assert.NotNull(r.Liquidity!.AtmAvgSpreadPct);
        Assert.NotNull(r.Liquidity.WingAvgSpreadPct);
        Assert.NotNull(r.Liquidity.AtmContracts);
        Assert.NotNull(r.Liquidity.WingContracts);
    }

    [SkippableFact]
    public async Task AdvVolatility_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var r = await client.AdvVolatilityTypedAsync("SPY", SpyAt);
        Assert.NotNull(r);

        Assert.Equal("SPY", r!.Symbol);
        Assert.NotNull(r.UnderlyingPrice);
        Assert.NotNull(r.AsOf);
        Assert.NotNull(r.MarketOpen);

        // svi_parameters[0]
        Assert.NotNull(r.SviParameters);
        if (r.SviParameters!.Count > 0)
        {
            var s = r.SviParameters[0];
            Assert.NotNull(s.Expiry);
            Assert.NotNull(s.DaysToExpiry);
            Assert.NotNull(s.Forward);
            Assert.NotNull(s.A);
            Assert.NotNull(s.B);
            Assert.NotNull(s.Rho);
            Assert.NotNull(s.M);
            Assert.NotNull(s.Sigma);
            Assert.NotNull(s.AtmTotalVariance);
            Assert.NotNull(s.AtmIv);
        }

        // forward_prices[0]
        Assert.NotNull(r.ForwardPrices);
        if (r.ForwardPrices!.Count > 0)
        {
            var f = r.ForwardPrices[0];
            Assert.NotNull(f.Expiry);
            Assert.NotNull(f.DaysToExpiry);
            Assert.NotNull(f.Forward);
            Assert.NotNull(f.Spot);
            Assert.NotNull(f.BasisPct);
        }

        // total_variance_surface
        Assert.NotNull(r.TotalVarianceSurface);
        Assert.NotNull(r.TotalVarianceSurface!.Moneyness);
        Assert.NotNull(r.TotalVarianceSurface.Expiries);
        Assert.NotNull(r.TotalVarianceSurface.Tenors);
        Assert.NotNull(r.TotalVarianceSurface.TotalVariance);
        Assert.NotNull(r.TotalVarianceSurface.ImpliedVol);

        // arbitrage_flags — list itself must be present (may be empty)
        Assert.NotNull(r.ArbitrageFlags);
        foreach (var a in r.ArbitrageFlags!)
        {
            Assert.NotNull(a.Expiry);
            Assert.NotNull(a.Type);
            Assert.NotNull(a.StrikeOrK);
            Assert.NotNull(a.Description);
        }

        // variance_swap_fair_values[0]
        Assert.NotNull(r.VarianceSwapFairValues);
        if (r.VarianceSwapFairValues!.Count > 0)
        {
            var vs = r.VarianceSwapFairValues[0];
            Assert.NotNull(vs.Expiry);
            Assert.NotNull(vs.DaysToExpiry);
            Assert.NotNull(vs.FairVariance);
            Assert.NotNull(vs.FairVol);
            Assert.NotNull(vs.AtmIv);
            Assert.NotNull(vs.ConvexityAdjustment);
        }

        // greeks_surfaces — vanna/charm/volga/speed each a strike×expiry grid
        Assert.NotNull(r.GreeksSurfaces);
        foreach (var (g, name) in new[]
        {
            (r.GreeksSurfaces!.Vanna, "vanna"),
            (r.GreeksSurfaces.Charm, "charm"),
            (r.GreeksSurfaces.Volga, "volga"),
            (r.GreeksSurfaces.Speed, "speed"),
        })
        {
            Assert.True(g is not null, $"greeks_surfaces.{name} null");
            Assert.NotNull(g!.Strikes);
            Assert.NotNull(g.Expiries);
            Assert.NotNull(g.Values);
        }
    }

    [SkippableFact]
    public async Task Surface_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var r = await client.SurfaceTypedAsync("SPY", SpyAt);
        Assert.NotNull(r);

        Assert.Equal("SPY", r!.Symbol);
        Assert.NotNull(r.Spot);
        Assert.NotNull(r.AsOf);
        Assert.NotNull(r.GridSize);
        Assert.NotNull(r.Tenors);
        Assert.NotNull(r.Moneyness);
        Assert.NotNull(r.Iv);
        Assert.True(r.Iv!.Length > 0);
        Assert.NotNull(r.Iv[0]);
        Assert.NotNull(r.SlicesUsed);
    }

    [SkippableFact]
    public async Task Gex_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var r = await client.GexTypedAsync("SPY", SpyAt);
        Assert.NotNull(r);

        Assert.Equal("SPY", r!.Symbol);
        Assert.NotNull(r.UnderlyingPrice);
        Assert.NotNull(r.AsOf);
        Assert.NotNull(r.GammaFlip);
        Assert.NotNull(r.NetGex);
        Assert.NotNull(r.NetGexLabel);

        Assert.NotNull(r.Strikes);
        if (r.Strikes!.Count > 0)
        {
            var row = r.Strikes[0];
            Assert.NotNull(row.Strike);
            Assert.NotNull(row.CallGex);
            Assert.NotNull(row.PutGex);
            Assert.NotNull(row.NetGex);
            Assert.NotNull(row.CallOi);
            Assert.NotNull(row.PutOi);
            // call_volume / put_volume are 0 placeholders on historical
            Assert.NotNull(row.CallVolume);
            Assert.NotNull(row.PutVolume);
            // call_oi_change / put_oi_change documented null on historical
        }
    }

    [SkippableFact]
    public async Task Dex_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var r = await client.DexTypedAsync("SPY", SpyAt);
        Assert.NotNull(r);

        Assert.Equal("SPY", r!.Symbol);
        Assert.NotNull(r.UnderlyingPrice);
        Assert.NotNull(r.AsOf);
        Assert.NotNull(r.NetDex);

        Assert.NotNull(r.Strikes);
        if (r.Strikes!.Count > 0)
        {
            var row = r.Strikes[0];
            Assert.NotNull(row.Strike);
            Assert.NotNull(row.CallDex);
            Assert.NotNull(row.PutDex);
            Assert.NotNull(row.NetDex);
        }
    }

    [SkippableFact]
    public async Task Vex_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var r = await client.VexTypedAsync("SPY", SpyAt);
        Assert.NotNull(r);

        Assert.Equal("SPY", r!.Symbol);
        Assert.NotNull(r.UnderlyingPrice);
        Assert.NotNull(r.AsOf);
        Assert.NotNull(r.NetVex);
        Assert.NotNull(r.VexInterpretation);

        Assert.NotNull(r.Strikes);
        if (r.Strikes!.Count > 0)
        {
            var row = r.Strikes[0];
            Assert.NotNull(row.Strike);
            Assert.NotNull(row.CallVex);
            Assert.NotNull(row.PutVex);
            Assert.NotNull(row.NetVex);
        }
    }

    [SkippableFact]
    public async Task Chex_EveryFieldDeclaredInPocoMustBeReferenced()
    {
        Skip.IfNot(HasKey, SkipReason);
        using var client = MakeClient();
        var r = await client.ChexTypedAsync("SPY", SpyAt);
        Assert.NotNull(r);

        Assert.Equal("SPY", r!.Symbol);
        Assert.NotNull(r.UnderlyingPrice);
        Assert.NotNull(r.AsOf);
        Assert.NotNull(r.NetChex);
        Assert.NotNull(r.ChexInterpretation);

        Assert.NotNull(r.Strikes);
        if (r.Strikes!.Count > 0)
        {
            var row = r.Strikes[0];
            Assert.NotNull(row.Strike);
            Assert.NotNull(row.CallChex);
            Assert.NotNull(row.PutChex);
            Assert.NotNull(row.NetChex);
        }
    }
}

// SkippableFact / Skip.IfNot are provided by the Xunit.SkippableFact NuGet package.
