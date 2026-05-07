using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/volatility/{symbol}?at=...</c> (Growth+).
///
/// <para>Comprehensive historical volatility analytics at a point in time:
/// realized-vol ladder, IV-RV spreads (variance risk premium by horizon),
/// per-expiry skew profiles, term structure, GEX/theta-by-DTE buckets,
/// put/call profile, OI concentration, hedging scenarios, and liquidity.</para>
///
/// <para>Same response shape as the live API; the only difference is every
/// historical analytics endpoint requires an <c>at</c> query parameter.</para>
/// </summary>
public sealed class VolatilityResponse
{
    /// <summary>Echoed from the request path (e.g. <c>"SPY"</c>).</summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    /// <summary>Spot mid at <c>as_of</c>.</summary>
    [JsonPropertyName("underlying_price")]
    public double? UnderlyingPrice { get; set; }

    /// <summary>The as-of stamp the API actually used (snapped to the available minute).</summary>
    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    /// <summary><c>true</c> if NYSE was open at <c>as_of</c>.</summary>
    [JsonPropertyName("market_open")]
    public bool? MarketOpen { get; set; }

    /// <summary>Realized vol ladder: 5d / 10d / 20d / 30d / 60d (annualised %).</summary>
    [JsonPropertyName("realized_vol")]
    public VolatilityRealizedVol? RealizedVol { get; set; }

    /// <summary>
    /// At-the-money implied volatility (annualised %, e.g. 18.5 = 18.5%). Top-level
    /// scalar — there is no nested <c>atm_iv</c> under <see cref="IvRvSpreads"/>.
    /// </summary>
    [JsonPropertyName("atm_iv")]
    public double? AtmIv { get; set; }

    /// <summary>IV-RV spread block — variance risk premium across 5d / 10d / 20d / 30d horizons.</summary>
    [JsonPropertyName("iv_rv_spreads")]
    public VolatilityIvRvSpreads? IvRvSpreads { get; set; }

    /// <summary>Per-expiry skew profile (10Δ / 25Δ wings, ATM, smile ratio, tail convexity).</summary>
    [JsonPropertyName("skew_profiles")]
    public List<VolatilitySkewProfile>? SkewProfiles { get; set; }

    /// <summary>Term-structure summary (near vs far slope, contango/backwardation state).</summary>
    [JsonPropertyName("term_structure")]
    public VolatilityTermStructure? TermStructure { get; set; }

    /// <summary>IV dispersion across expiries and strikes.</summary>
    [JsonPropertyName("iv_dispersion")]
    public VolatilityIvDispersion? IvDispersion { get; set; }

    /// <summary>Net dealer gamma exposure aggregated by DTE bucket.</summary>
    [JsonPropertyName("gex_by_dte")]
    public List<VolatilityGexByDte>? GexByDte { get; set; }

    /// <summary>Net option theta aggregated by DTE bucket.</summary>
    [JsonPropertyName("theta_by_dte")]
    public List<VolatilityThetaByDte>? ThetaByDte { get; set; }

    /// <summary>Put/call profile — by-expiry OI/volume ratios + by-moneyness OI breakdown.</summary>
    [JsonPropertyName("put_call_profile")]
    public VolatilityPutCallProfile? PutCallProfile { get; set; }

    /// <summary>Open interest concentration (top-3/5/10% share + Herfindahl index).</summary>
    [JsonPropertyName("oi_concentration")]
    public VolatilityOiConcentration? OiConcentration { get; set; }

    /// <summary>±X% hedging scenarios — projected dealer share rebalance and notional.</summary>
    [JsonPropertyName("hedging_scenarios")]
    public List<VolatilityHedgingScenario>? HedgingScenarios { get; set; }

    /// <summary>Bid-ask liquidity at the ATM and wing regions.</summary>
    [JsonPropertyName("liquidity")]
    public VolatilityLiquidity? Liquidity { get; set; }
}

/// <summary>Realized vol ladder. All values are annualised % (e.g. 18.5 = 18.5%).</summary>
public sealed class VolatilityRealizedVol
{
    [JsonPropertyName("rv_5d")]
    public double? Rv5d { get; set; }

    [JsonPropertyName("rv_10d")]
    public double? Rv10d { get; set; }

    [JsonPropertyName("rv_20d")]
    public double? Rv20d { get; set; }

    [JsonPropertyName("rv_30d")]
    public double? Rv30d { get; set; }

    [JsonPropertyName("rv_60d")]
    public double? Rv60d { get; set; }
}

/// <summary>
/// IV-RV spread block — the variance risk premium at standard horizons plus
/// a coarse <see cref="Assessment"/> label.
/// </summary>
public sealed class VolatilityIvRvSpreads
{
    [JsonPropertyName("vrp_5d")]
    public double? Vrp5d { get; set; }

    [JsonPropertyName("vrp_10d")]
    public double? Vrp10d { get; set; }

    [JsonPropertyName("vrp_20d")]
    public double? Vrp20d { get; set; }

    [JsonPropertyName("vrp_30d")]
    public double? Vrp30d { get; set; }

    /// <summary>Plain-English label (e.g. <c>"normal"</c>, <c>"elevated"</c>, <c>"depressed"</c>).</summary>
    [JsonPropertyName("assessment")]
    public string? Assessment { get; set; }
}

/// <summary>One row of the per-expiry skew profile.</summary>
public sealed class VolatilitySkewProfile
{
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("days_to_expiry")]
    public int? DaysToExpiry { get; set; }

    [JsonPropertyName("put_10d_iv")]
    public double? Put10dIv { get; set; }

    [JsonPropertyName("put_25d_iv")]
    public double? Put25dIv { get; set; }

    [JsonPropertyName("atm_iv")]
    public double? AtmIv { get; set; }

    [JsonPropertyName("call_25d_iv")]
    public double? Call25dIv { get; set; }

    [JsonPropertyName("call_10d_iv")]
    public double? Call10dIv { get; set; }

    /// <summary>25Δ put IV minus 25Δ call IV. Positive = downside-skewed.</summary>
    [JsonPropertyName("skew_25d")]
    public double? Skew25d { get; set; }

    /// <summary>Mean wing IV / ATM IV. >1 = smile; ~1 = flat.</summary>
    [JsonPropertyName("smile_ratio")]
    public double? SmileRatio { get; set; }

    /// <summary>Curvature in the far tails (10Δ wings).</summary>
    [JsonPropertyName("tail_convexity")]
    public double? TailConvexity { get; set; }
}

/// <summary>
/// Term-structure summary. <see cref="State"/> is a categorical label
/// (e.g. <c>"contango"</c>, <c>"backwardation"</c>, <c>"flat"</c>).
/// </summary>
public sealed class VolatilityTermStructure
{
    [JsonPropertyName("near_slope_pct")]
    public double? NearSlopePct { get; set; }

    [JsonPropertyName("far_slope_pct")]
    public double? FarSlopePct { get; set; }

    /// <summary><c>"contango"</c> | <c>"backwardation"</c> | <c>"flat"</c>.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }
}

/// <summary>Cross-sectional IV dispersion across expiries and strikes.</summary>
public sealed class VolatilityIvDispersion
{
    [JsonPropertyName("cross_expiry")]
    public double? CrossExpiry { get; set; }

    [JsonPropertyName("cross_strike")]
    public double? CrossStrike { get; set; }
}

/// <summary>Net dealer gamma exposure for a DTE bucket.</summary>
public sealed class VolatilityGexByDte
{
    /// <summary>DTE bucket label (e.g. <c>"0-7"</c>, <c>"8-30"</c>).</summary>
    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    /// <summary>Net dealer gamma exposure in dollars per 1% spot move.</summary>
    [JsonPropertyName("net_gex")]
    public double? NetGex { get; set; }

    [JsonPropertyName("pct_of_total")]
    public double? PctOfTotal { get; set; }

    [JsonPropertyName("contract_count")]
    public int? ContractCount { get; set; }
}

/// <summary>Net option theta for a DTE bucket.</summary>
public sealed class VolatilityThetaByDte
{
    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    /// <summary>Net theta in dollars per calendar day.</summary>
    [JsonPropertyName("net_theta")]
    public double? NetTheta { get; set; }

    [JsonPropertyName("contract_count")]
    public int? ContractCount { get; set; }
}

/// <summary>Put/call profile — by-expiry ratios + by-moneyness OI breakdown.</summary>
public sealed class VolatilityPutCallProfile
{
    [JsonPropertyName("by_expiry")]
    public List<VolatilityPutCallByExpiry>? ByExpiry { get; set; }

    [JsonPropertyName("by_moneyness")]
    public VolatilityPutCallByMoneyness? ByMoneyness { get; set; }
}

/// <summary>One row of the per-expiry put/call profile.</summary>
public sealed class VolatilityPutCallByExpiry
{
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("call_oi")]
    public int? CallOi { get; set; }

    [JsonPropertyName("put_oi")]
    public int? PutOi { get; set; }

    [JsonPropertyName("pc_ratio_oi")]
    public double? PcRatioOi { get; set; }

    /// <summary>On historical, intraday volume is not retained — typically <c>0</c>.</summary>
    [JsonPropertyName("call_volume")]
    public int? CallVolume { get; set; }

    /// <summary>On historical, intraday volume is not retained — typically <c>0</c>.</summary>
    [JsonPropertyName("put_volume")]
    public int? PutVolume { get; set; }

    [JsonPropertyName("pc_ratio_volume")]
    public double? PcRatioVolume { get; set; }
}

/// <summary>Open interest split by moneyness band.</summary>
public sealed class VolatilityPutCallByMoneyness
{
    [JsonPropertyName("otm_call_oi")]
    public int? OtmCallOi { get; set; }

    [JsonPropertyName("atm_call_oi")]
    public int? AtmCallOi { get; set; }

    [JsonPropertyName("itm_call_oi")]
    public int? ItmCallOi { get; set; }

    [JsonPropertyName("otm_put_oi")]
    public int? OtmPutOi { get; set; }

    [JsonPropertyName("atm_put_oi")]
    public int? AtmPutOi { get; set; }

    [JsonPropertyName("itm_put_oi")]
    public int? ItmPutOi { get; set; }
}

/// <summary>Open interest concentration metrics across the entire chain.</summary>
public sealed class VolatilityOiConcentration
{
    [JsonPropertyName("top_3_pct")]
    public double? Top3Pct { get; set; }

    [JsonPropertyName("top_5_pct")]
    public double? Top5Pct { get; set; }

    [JsonPropertyName("top_10_pct")]
    public double? Top10Pct { get; set; }

    /// <summary>Herfindahl-Hirschman Index of OI concentration. Higher = more concentrated.</summary>
    [JsonPropertyName("herfindahl")]
    public double? Herfindahl { get; set; }
}

/// <summary>One row of the dealer hedging scenario table.</summary>
public sealed class VolatilityHedgingScenario
{
    [JsonPropertyName("move_pct")]
    public double? MovePct { get; set; }

    [JsonPropertyName("dealer_shares")]
    public double? DealerShares { get; set; }

    /// <summary><c>"buy"</c> or <c>"sell"</c>.</summary>
    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("notional_usd")]
    public double? NotionalUsd { get; set; }
}

/// <summary>Bid-ask liquidity sampled at the ATM and wing strikes.</summary>
public sealed class VolatilityLiquidity
{
    [JsonPropertyName("atm_avg_spread_pct")]
    public double? AtmAvgSpreadPct { get; set; }

    [JsonPropertyName("wing_avg_spread_pct")]
    public double? WingAvgSpreadPct { get; set; }

    [JsonPropertyName("atm_contracts")]
    public int? AtmContracts { get; set; }

    [JsonPropertyName("wing_contracts")]
    public int? WingContracts { get; set; }
}
