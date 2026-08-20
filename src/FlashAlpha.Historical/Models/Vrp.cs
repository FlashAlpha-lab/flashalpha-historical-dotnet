using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/vrp/{symbol}?at=...</c> (Alpha+).
///
/// <para>Same shape as the live API with two macro diffs:
/// <list type="bullet">
///   <item><c>Macro.HySpread</c> is populated here; live currently returns <c>null</c>.</item>
///   <item><c>Macro.FedFunds</c> is absent here; live includes it (so the field is omitted from <see cref="VrpMacro"/>).</item>
/// </list></para>
///
/// <para><b>Common silent-null traps</b> (now type-checked at the SDK boundary):
/// <list type="bullet">
///   <item><c>response.ZScore</c> ✗ → use <c>response.Vrp.ZScore</c></item>
///   <item><c>response.Percentile</c> ✗ → use <c>response.Vrp.Percentile</c></item>
///   <item><c>response.PutVrp</c> ✗ → use <c>response.Directional.DownsideVrp</c></item>
///   <item><c>response.NetGex</c> ✗ → use <c>response.Regime.NetGex</c></item>
///   <item><c>response.HarvestScore</c> (top-level) ✗ → use
///     <c>response.GexConditioned.HarvestScore</c>;
///     <c>response.NetHarvestScore</c> is a SEPARATE composite.</item>
/// </list></para>
///
/// <para>On historical responses with insufficient warm-up (<c>at</c> near
/// 2017-01-03), <c>Vrp.ZScore</c>, <c>Vrp.Percentile</c>,
/// <c>Regime.VrpRegime</c>, <c>StrategyScores</c>, and <c>NetHarvestScore</c>
/// are all <c>null</c>. <c>Warnings</c> will explain.</para>
///
/// <para>Returns 403 <c>tier_restricted</c> for anything below Alpha plan.</para>
/// </summary>
public sealed class VrpResponse
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("underlying_price")]
    public double? UnderlyingPrice { get; set; }

    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    [JsonPropertyName("market_open")]
    public bool? MarketOpen { get; set; }

    /// <summary>Core VRP metrics block. See <see cref="VrpCore"/>.</summary>
    [JsonPropertyName("vrp")]
    public VrpCore? Vrp { get; set; }

    [JsonPropertyName("variance_risk_premium")]
    public double? VarianceRiskPremium { get; set; }

    [JsonPropertyName("convexity_premium")]
    public double? ConvexityPremium { get; set; }

    [JsonPropertyName("fair_vol")]
    public double? FairVol { get; set; }

    [JsonPropertyName("directional")]
    public VrpDirectional? Directional { get; set; }

    [JsonPropertyName("term_vrp")]
    public List<VrpTermItem>? TermVrp { get; set; }

    [JsonPropertyName("gex_conditioned")]
    public VrpGexConditioned? GexConditioned { get; set; }

    [JsonPropertyName("vanna_conditioned")]
    public VrpVannaConditioned? VannaConditioned { get; set; }

    /// <summary>Regime snapshot block. <c>NetGex</c> lives HERE, not at the top level.</summary>
    [JsonPropertyName("regime")]
    public VrpRegime? Regime { get; set; }

    /// <summary>0-100 strategy suitability scores. <c>null</c> on early historical timestamps.</summary>
    [JsonPropertyName("strategy_scores")]
    public VrpStrategyScores? StrategyScores { get; set; }

    /// <summary>0-100 composite harvest signal. <c>null</c> on early historical timestamps.</summary>
    [JsonPropertyName("net_harvest_score")]
    public int? NetHarvestScore { get; set; }

    [JsonPropertyName("dealer_flow_risk")]
    public int? DealerFlowRisk { get; set; }

    /// <summary>Server-side warnings about data quality. Always present (possibly empty).</summary>
    [JsonPropertyName("warnings")]
    public List<string>? Warnings { get; set; }

    [JsonPropertyName("macro")]
    public VrpMacro? Macro { get; set; }
}

/// <summary>Core VRP metrics block — the heart of the response.</summary>
public sealed class VrpCore
{
    [JsonPropertyName("atm_iv")]
    public double? AtmIv { get; set; }

    [JsonPropertyName("rv_5d")]
    public double? Rv5d { get; set; }

    [JsonPropertyName("rv_10d")]
    public double? Rv10d { get; set; }

    [JsonPropertyName("rv_20d")]
    public double? Rv20d { get; set; }

    [JsonPropertyName("rv_30d")]
    public double? Rv30d { get; set; }

    [JsonPropertyName("vrp_5d")]
    public double? Vrp5d { get; set; }

    [JsonPropertyName("vrp_10d")]
    public double? Vrp10d { get; set; }

    [JsonPropertyName("vrp_20d")]
    public double? Vrp20d { get; set; }

    [JsonPropertyName("vrp_30d")]
    public double? Vrp30d { get; set; }

    /// <summary>Z-score of current 20-day VRP. <c>null</c> when warmup is insufficient.</summary>
    [JsonPropertyName("z_score")]
    public double? ZScore { get; set; }

    /// <summary>Percentile rank (0-100). <c>null</c> when warmup is short.</summary>
    [JsonPropertyName("percentile")]
    public int? Percentile { get; set; }

    [JsonPropertyName("history_days")]
    public int? HistoryDays { get; set; }
}

/// <summary>Directional VRP skew. Use <c>downside_vrp</c>/<c>upside_vrp</c>, NOT <c>put_vrp</c>/<c>call_vrp</c>.</summary>
public sealed class VrpDirectional
{
    [JsonPropertyName("put_wing_iv_25d")]
    public double? PutWingIv25d { get; set; }

    [JsonPropertyName("call_wing_iv_25d")]
    public double? CallWingIv25d { get; set; }

    [JsonPropertyName("downside_rv_20d")]
    public double? DownsideRv20d { get; set; }

    [JsonPropertyName("upside_rv_20d")]
    public double? UpsideRv20d { get; set; }

    [JsonPropertyName("downside_vrp")]
    public double? DownsideVrp { get; set; }

    [JsonPropertyName("upside_vrp")]
    public double? UpsideVrp { get; set; }
}

/// <summary>One row of the VRP term structure.</summary>
public sealed class VrpTermItem
{
    [JsonPropertyName("dte")]
    public int? Dte { get; set; }

    [JsonPropertyName("iv")]
    public double? Iv { get; set; }

    [JsonPropertyName("rv")]
    public double? Rv { get; set; }

    [JsonPropertyName("vrp")]
    public double? Vrp { get; set; }
}

/// <summary>VRP harvest score conditioned on the prevailing dealer-gamma regime.</summary>
public sealed class VrpGexConditioned
{
    [JsonPropertyName("regime")]
    public string? Regime { get; set; }

    [JsonPropertyName("harvest_score")]
    public double? HarvestScore { get; set; }

    [JsonPropertyName("interpretation")]
    public string? Interpretation { get; set; }
}

/// <summary>VRP outlook conditioned on net dealer vanna exposure.</summary>
public sealed class VrpVannaConditioned
{
    [JsonPropertyName("outlook")]
    public string? Outlook { get; set; }

    [JsonPropertyName("interpretation")]
    public string? Interpretation { get; set; }
}

/// <summary>Regime snapshot block. <c>NetGex</c> lives here, not at the top level.</summary>
public sealed class VrpRegime
{
    [JsonPropertyName("gamma")]
    public string? Gamma { get; set; }

    /// <summary>
    /// Property is named <c>VrpRegimeLabel</c> rather than <c>VrpRegime</c>
    /// because C# disallows a property name matching its enclosing class.
    /// JSON wire name is unchanged: <c>"vrp_regime"</c>.
    /// </summary>
    [JsonPropertyName("vrp_regime")]
    public string? VrpRegimeLabel { get; set; }

    [JsonPropertyName("net_gex")]
    public double? NetGex { get; set; }

    [JsonPropertyName("gamma_flip")]
    public double? GammaFlip { get; set; }
}

/// <summary>0-100 strategy suitability scores. Each field can be <c>null</c> on historical when warmup is short.</summary>
public sealed class VrpStrategyScores
{
    [JsonPropertyName("short_put_spread")]
    public int? ShortPutSpread { get; set; }

    [JsonPropertyName("short_strangle")]
    public int? ShortStrangle { get; set; }

    [JsonPropertyName("iron_condor")]
    public int? IronCondor { get; set; }

    [JsonPropertyName("calendar_spread")]
    public int? CalendarSpread { get; set; }
}

/// <summary>
/// Macro-context snapshot used to condition the VRP outlook.
///
/// <para>Historical-specific: <c>FedFunds</c> is absent on historical responses
/// (so this class doesn't include it). <c>HySpread</c> is populated on
/// historical (live currently returns <c>null</c>).</para>
/// </summary>
public sealed class VrpMacro
{
    [JsonPropertyName("vix")]
    public double? Vix { get; set; }

    [JsonPropertyName("vix_3m")]
    public double? Vix3m { get; set; }

    [JsonPropertyName("vix_term_slope")]
    public double? VixTermSlope { get; set; }

    [JsonPropertyName("dgs10")]
    public double? Dgs10 { get; set; }

    [JsonPropertyName("hy_spread")]
    public double? HySpread { get; set; }
}
