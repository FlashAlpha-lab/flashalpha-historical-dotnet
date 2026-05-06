using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/stock/{symbol}/summary?at=...</c>.
///
/// <para>Point-in-time replay of the live <c>stock summary</c> snapshot —
/// price, volatility (ATM IV, HV, VRP, 25-delta skew, IV term structure),
/// options flow, dealer exposure (GEX/DEX/VEX/CHEX, regime, walls, max
/// pain, hedging estimates, 0DTE breakdown, top strikes), and macro
/// context. The response shape mirrors the live API field-for-field, so
/// users can swap between
/// <c>FlashAlphaClient.StockSummaryAsync</c> and
/// <c>FlashAlphaHistoricalClient.StockSummaryAsync</c> without changing
/// the consumer code.</para>
///
/// <para><b>Historical-specific behavior:</b>
/// <list type="bullet">
///   <item><see cref="AsOf"/> is the as-of stamp the API actually used —
///     SNAPPED to the available minute. It will not always equal the
///     <c>at</c> value passed in the request.</item>
///   <item>Macro sub-blocks may be <c>null</c> when the historical record
///     for the requested minute is missing.</item>
///   <item>Volume-related fields in <see cref="StockSummaryOptionsFlow"/>
///     reflect end-of-day aggregates from the minute table — they are not
///     intraday running totals.</item>
/// </list></para>
///
/// <para><b>Null-safety hot spots</b> (same as live):
/// <list type="bullet">
///   <item><see cref="Exposure"/> is <c>null</c> when no options/greeks are
///     loaded for the symbol at the requested minute.</item>
///   <item><see cref="StockSummaryHedgingEstimate.DealerShares"/> is
///     <b>magnitude</b> (always positive); <see cref="StockSummaryHedgingEstimate.Direction"/>
///     carries the sign — DIFFERS from the zero-DTE endpoint.</item>
/// </list></para>
/// </summary>
public sealed class StockSummaryResponse
{
    /// <summary>Echoed from the request path (e.g. <c>"SPY"</c>).</summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    /// <summary>UTC timestamp the API actually used — snapped to the available minute, may not equal the <c>at</c> request value.</summary>
    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    /// <summary><c>true</c> if the US equity market was open at <see cref="AsOf"/>.</summary>
    [JsonPropertyName("market_open")]
    public bool? MarketOpen { get; set; }

    /// <summary>Top-of-book price block. See <see cref="StockSummaryPrice"/>.</summary>
    [JsonPropertyName("price")]
    public StockSummaryPrice? Price { get; set; }

    /// <summary>Volatility block (ATM IV, HV, VRP, 25-delta skew, IV term structure).</summary>
    [JsonPropertyName("volatility")]
    public StockSummaryVolatility? Volatility { get; set; }

    /// <summary>Aggregate options-flow stats (OI, volume, P/C ratios). See <see cref="StockSummaryOptionsFlow"/>.</summary>
    [JsonPropertyName("options_flow")]
    public StockSummaryOptionsFlow? OptionsFlow { get; set; }

    /// <summary>
    /// Dealer exposure block (GEX/DEX/VEX/CHEX, regime, walls, max pain, hedging
    /// estimates, 0DTE, top strikes). <c>null</c> when no options/greeks data
    /// is loaded for the symbol at this minute.
    /// </summary>
    [JsonPropertyName("exposure")]
    public StockSummaryExposure? Exposure { get; set; }

    /// <summary>
    /// Macro context (VIX, VVIX, SKEW, MOVE, SPX, term structure, fear-and-greed).
    /// Behavior diff vs live: individual macro sub-blocks may be <c>null</c>
    /// when historical records for the requested minute are missing.
    /// </summary>
    [JsonPropertyName("macro")]
    public StockSummaryMacro? Macro { get; set; }
}

/// <summary>Top-of-book price block (bid/ask/mid/last + last-update timestamp).</summary>
public sealed class StockSummaryPrice
{
    [JsonPropertyName("bid")]
    public double? Bid { get; set; }

    [JsonPropertyName("ask")]
    public double? Ask { get; set; }

    /// <summary>Mid price: <c>(bid + ask) / 2</c>.</summary>
    [JsonPropertyName("mid")]
    public double? Mid { get; set; }

    /// <summary>Last trade price.</summary>
    [JsonPropertyName("last")]
    public double? Last { get; set; }

    /// <summary>UTC timestamp of the most recent quote/trade behind this block.</summary>
    [JsonPropertyName("last_update")]
    public string? LastUpdate { get; set; }
}

/// <summary>
/// Volatility block — ATM IV, historical vol, VRP, 25-delta skew, IV term
/// structure.
///
/// <para><b>Units:</b> <see cref="AtmIv"/>, <see cref="Hv20"/>,
/// <see cref="Hv60"/>, <see cref="Vrp"/> are PERCENT (e.g. <c>18.45</c> = 18.45%).</para>
///
/// <para><b>Term-structure filter:</b> <see cref="IvTermStructure"/> rows
/// with IV below 5% or above 200% are filtered out server-side as bad SVI
/// fits.</para>
/// </summary>
public sealed class StockSummaryVolatility
{
    /// <summary>At-the-money implied volatility, near-term expiry (annualised %).</summary>
    [JsonPropertyName("atm_iv")]
    public double? AtmIv { get; set; }

    /// <summary>20-day trailing realized vol (annualised %).</summary>
    [JsonPropertyName("hv_20")]
    public double? Hv20 { get; set; }

    /// <summary>60-day trailing realized vol (annualised %).</summary>
    [JsonPropertyName("hv_60")]
    public double? Hv60 { get; set; }

    /// <summary>Variance risk premium: <c>atm_iv - hv_20</c>.</summary>
    [JsonPropertyName("vrp")]
    public double? Vrp { get; set; }

    /// <summary>25-delta skew block.</summary>
    [JsonPropertyName("skew_25d")]
    public StockSummarySkew25d? Skew25d { get; set; }

    /// <summary>IV term structure (rows with IV &lt;5% or &gt;200% filtered out).</summary>
    [JsonPropertyName("iv_term_structure")]
    public List<StockSummaryIvTermItem>? IvTermStructure { get; set; }
}

/// <summary>
/// 25-delta skew block — directional pricing of OTM puts vs OTM calls at
/// the 25-delta wings. <see cref="Skew25dValue"/> = <c>put_25d_iv - call_25d_iv</c>.
/// </summary>
public sealed class StockSummarySkew25d
{
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("days_to_expiry")]
    public int? DaysToExpiry { get; set; }

    /// <summary>IV at the 25-delta put wing (annualised %).</summary>
    [JsonPropertyName("put_25d_iv")]
    public double? Put25dIv { get; set; }

    /// <summary>ATM IV for this expiry (annualised %).</summary>
    [JsonPropertyName("atm_iv")]
    public double? AtmIv { get; set; }

    /// <summary>IV at the 25-delta call wing (annualised %).</summary>
    [JsonPropertyName("call_25d_iv")]
    public double? Call25dIv { get; set; }

    /// <summary><c>put_25d_iv - call_25d_iv</c>. Positive = put-skew.</summary>
    [JsonPropertyName("skew_25d")]
    public double? Skew25dValue { get; set; }

    /// <summary><c>put_25d_iv / call_25d_iv</c>.</summary>
    [JsonPropertyName("smile_ratio")]
    public double? SmileRatio { get; set; }
}

/// <summary>One row of the IV term structure.</summary>
public sealed class StockSummaryIvTermItem
{
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    /// <summary>ATM implied vol at this expiry (annualised %).</summary>
    [JsonPropertyName("iv")]
    public double? Iv { get; set; }

    [JsonPropertyName("days_to_expiry")]
    public int? DaysToExpiry { get; set; }
}

/// <summary>Aggregate options-flow stats — total OI/volume by side and put/call ratios.</summary>
public sealed class StockSummaryOptionsFlow
{
    [JsonPropertyName("total_call_oi")]
    public long? TotalCallOi { get; set; }

    [JsonPropertyName("total_put_oi")]
    public long? TotalPutOi { get; set; }

    [JsonPropertyName("total_call_volume")]
    public long? TotalCallVolume { get; set; }

    [JsonPropertyName("total_put_volume")]
    public long? TotalPutVolume { get; set; }

    /// <summary><c>total_put_oi / total_call_oi</c>. &gt;1.0 = put-heavy.</summary>
    [JsonPropertyName("pc_ratio_oi")]
    public double? PcRatioOi { get; set; }

    /// <summary><c>total_put_volume / total_call_volume</c>.</summary>
    [JsonPropertyName("pc_ratio_volume")]
    public double? PcRatioVolume { get; set; }

    [JsonPropertyName("active_expirations")]
    public int? ActiveExpirations { get; set; }
}

/// <summary>
/// Dealer exposure block — GEX/DEX/VEX/CHEX with regime classification,
/// walls, max pain, hedging estimates, 0DTE breakdown, top strikes.
///
/// <para><see cref="Regime"/> is one of <c>"positive_gamma"</c>,
/// <c>"negative_gamma"</c>, <c>"unknown"</c> — derived from spot vs
/// <see cref="GammaFlip"/>.</para>
///
/// <para><see cref="TopStrikes"/> returns up to 5 strikes ranked by
/// absolute net GEX.</para>
/// </summary>
public sealed class StockSummaryExposure
{
    /// <summary>Net dealer gamma exposure in dollars per 1% spot move.</summary>
    [JsonPropertyName("net_gex")]
    public double? NetGex { get; set; }

    /// <summary>Net dealer delta exposure in dollars.</summary>
    [JsonPropertyName("net_dex")]
    public double? NetDex { get; set; }

    /// <summary>Net dealer vanna exposure (vol/spot cross-greek).</summary>
    [JsonPropertyName("net_vex")]
    public double? NetVex { get; set; }

    /// <summary>Net dealer charm exposure (delta-decay).</summary>
    [JsonPropertyName("net_chex")]
    public double? NetChex { get; set; }

    /// <summary>Strike where net dealer gamma flips sign. Spot ABOVE = positive-gamma regime.</summary>
    [JsonPropertyName("gamma_flip")]
    public double? GammaFlip { get; set; }

    /// <summary>Strike with the highest absolute call GEX (resistance).</summary>
    [JsonPropertyName("call_wall")]
    public double? CallWall { get; set; }

    /// <summary>Strike with the highest absolute put GEX (support).</summary>
    [JsonPropertyName("put_wall")]
    public double? PutWall { get; set; }

    /// <summary>Strike where total chain pain is minimized.</summary>
    [JsonPropertyName("max_pain")]
    public double? MaxPain { get; set; }

    /// <summary>Strike with the highest aggregate OI.</summary>
    [JsonPropertyName("highest_oi_strike")]
    public double? HighestOiStrike { get; set; }

    /// <summary>
    /// <c>"positive_gamma"</c> | <c>"negative_gamma"</c> | <c>"unknown"</c>.
    /// </summary>
    [JsonPropertyName("regime")]
    public string? Regime { get; set; }

    /// <summary>Plain-English explanations per greek. Safe to surface verbatim.</summary>
    [JsonPropertyName("interpretation")]
    public StockSummaryInterpretation? Interpretation { get; set; }

    /// <summary>Estimated dealer hedging response to ±1% spot moves.</summary>
    [JsonPropertyName("hedging_estimate")]
    public StockSummaryHedgingEstimateBlock? HedgingEstimate { get; set; }

    /// <summary>0DTE-only slice of GEX.</summary>
    [JsonPropertyName("zero_dte")]
    public StockSummaryZeroDte? ZeroDte { get; set; }

    /// <summary>Up to 5 strikes ranked by absolute net GEX.</summary>
    [JsonPropertyName("top_strikes")]
    public List<StockSummaryTopStrike>? TopStrikes { get; set; }

    /// <summary>OI-weighted average days-to-expiry across the chain.</summary>
    [JsonPropertyName("oi_weighted_dte")]
    public double? OiWeightedDte { get; set; }
}

/// <summary>
/// Plain-English explanations of the gamma / vanna / charm regime. Safe to
/// surface verbatim to end users / LLM agents.
/// </summary>
public sealed class StockSummaryInterpretation
{
    [JsonPropertyName("gamma")]
    public string? Gamma { get; set; }

    [JsonPropertyName("vanna")]
    public string? Vanna { get; set; }

    [JsonPropertyName("charm")]
    public string? Charm { get; set; }
}

/// <summary>
/// Estimated dealer hedging response to ±1% spot moves.
///
/// <para><b>Sign convention:</b> <see cref="StockSummaryHedgingEstimate.DealerShares"/>
/// is MAGNITUDE (always positive); <see cref="StockSummaryHedgingEstimate.Direction"/>
/// carries the sign. DIFFERS from the zero-DTE endpoint which returns signed
/// values.</para>
/// </summary>
public sealed class StockSummaryHedgingEstimateBlock
{
    [JsonPropertyName("spot_down_1pct")]
    public StockSummaryHedgingEstimate? SpotDown1Pct { get; set; }

    [JsonPropertyName("spot_up_1pct")]
    public StockSummaryHedgingEstimate? SpotUp1Pct { get; set; }
}

/// <summary>One side of the dealer-hedging estimate (down-1% or up-1%).</summary>
public sealed class StockSummaryHedgingEstimate
{
    /// <summary>Magnitude of shares the dealer must trade. Sign is in <see cref="Direction"/>.</summary>
    [JsonPropertyName("dealer_shares")]
    public long? DealerShares { get; set; }

    /// <summary><c>"buy"</c> or <c>"sell"</c>.</summary>
    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    /// <summary>Notional dollar value: <c>dealer_shares * spot</c>.</summary>
    [JsonPropertyName("notional_usd")]
    public double? NotionalUsd { get; set; }
}

/// <summary>0DTE-only slice of net GEX.</summary>
public sealed class StockSummaryZeroDte
{
    [JsonPropertyName("net_gex")]
    public double? NetGex { get; set; }

    /// <summary>0DTE GEX as a percent of full-chain net GEX.</summary>
    [JsonPropertyName("pct_of_total")]
    public double? PctOfTotal { get; set; }

    /// <summary>The 0DTE expiry date used for this slice.</summary>
    [JsonPropertyName("expiration")]
    public string? Expiration { get; set; }
}

/// <summary>One of the top-N strikes ranked by absolute net GEX.</summary>
public sealed class StockSummaryTopStrike
{
    [JsonPropertyName("strike")]
    public double? Strike { get; set; }

    [JsonPropertyName("net_gex")]
    public double? NetGex { get; set; }

    [JsonPropertyName("call_oi")]
    public long? CallOi { get; set; }

    [JsonPropertyName("put_oi")]
    public long? PutOi { get; set; }

    [JsonPropertyName("total_oi")]
    public long? TotalOi { get; set; }
}

/// <summary>
/// Macro context block — VIX/VVIX/SKEW/SPX/MOVE plus VIX term structure
/// and fear-and-greed.
///
/// <para><b>Historical-specific behavior:</b> any sub-field can be
/// <c>null</c> when the historical record for that minute is missing.</para>
///
/// <para><b>Approximation note:</b>
/// <see cref="StockSummaryVixFutures.Basis"/> is approximated from VIX3M vs
/// VIX spot — NOT actual VX futures prices.</para>
/// </summary>
public sealed class StockSummaryMacro
{
    [JsonPropertyName("vix")]
    public StockSummaryMacroQuote? Vix { get; set; }

    [JsonPropertyName("vvix")]
    public StockSummaryMacroQuote? Vvix { get; set; }

    [JsonPropertyName("skew")]
    public StockSummaryMacroQuote? Skew { get; set; }

    [JsonPropertyName("spx")]
    public StockSummaryMacroQuote? Spx { get; set; }

    [JsonPropertyName("move")]
    public StockSummaryMacroQuote? Move { get; set; }

    [JsonPropertyName("vix_term_structure")]
    public StockSummaryVixTermStructure? VixTermStructure { get; set; }

    [JsonPropertyName("vix_futures")]
    public StockSummaryVixFutures? VixFutures { get; set; }

    [JsonPropertyName("fear_and_greed")]
    public StockSummaryFearAndGreed? FearAndGreed { get; set; }
}

/// <summary>One macro quote line — current value plus day change in absolute and percent.</summary>
public sealed class StockSummaryMacroQuote
{
    [JsonPropertyName("value")]
    public double? Value { get; set; }

    [JsonPropertyName("change")]
    public double? Change { get; set; }

    [JsonPropertyName("change_pct")]
    public double? ChangePct { get; set; }
}

/// <summary>VIX term structure across vix9d / vix / vix3m / vix6m + slope label.</summary>
public sealed class StockSummaryVixTermStructure
{
    [JsonPropertyName("levels")]
    public StockSummaryVixTermLevels? Levels { get; set; }

    /// <summary>Near slope: <c>(vix3m - vix) / vix * 100</c>.</summary>
    [JsonPropertyName("near_slope_pct")]
    public double? NearSlopePct { get; set; }

    /// <summary><c>"contango"</c> | <c>"backwardation"</c>.</summary>
    [JsonPropertyName("structure")]
    public string? Structure { get; set; }
}

/// <summary>VIX term-structure tenor levels.</summary>
public sealed class StockSummaryVixTermLevels
{
    [JsonPropertyName("vix9d")]
    public double? Vix9d { get; set; }

    [JsonPropertyName("vix")]
    public double? Vix { get; set; }

    [JsonPropertyName("vix3m")]
    public double? Vix3m { get; set; }

    [JsonPropertyName("vix6m")]
    public double? Vix6m { get; set; }
}

/// <summary>
/// VIX-futures snapshot. <see cref="Basis"/> is approximated from VIX3M vs
/// VIX spot, not actual VX futures prices.
/// </summary>
public sealed class StockSummaryVixFutures
{
    [JsonPropertyName("front_month")]
    public double? FrontMonth { get; set; }

    [JsonPropertyName("spot")]
    public double? Spot { get; set; }

    [JsonPropertyName("spread")]
    public double? Spread { get; set; }

    [JsonPropertyName("basis_pct")]
    public double? BasisPct { get; set; }

    /// <summary><c>"contango"</c> | <c>"backwardation"</c>.</summary>
    [JsonPropertyName("basis")]
    public string? Basis { get; set; }
}

/// <summary>CNN Fear &amp; Greed score (0-100) plus verbal bucket.</summary>
public sealed class StockSummaryFearAndGreed
{
    [JsonPropertyName("score")]
    public int? Score { get; set; }

    /// <summary><c>"Extreme Fear"</c>, <c>"Fear"</c>, <c>"Neutral"</c>, <c>"Greed"</c>, <c>"Extreme Greed"</c>.</summary>
    [JsonPropertyName("rating")]
    public string? Rating { get; set; }
}
