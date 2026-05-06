using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/maxpain/{symbol}?at=...</c> (Basic+).
///
/// <para>Max pain is the strike where total option-holder intrinsic value
/// across all OI in the chain is minimized — equivalently, the strike at
/// which dealers (the counterparty) lose the least to expiring contracts.
/// The endpoint also overlays GEX-based dealer alignment, a multi-expiry
/// calendar (full chain only), and a 0-100 pin probability score.</para>
///
/// <para>Same response shape as the live API with one operational diff:
/// <see cref="MaxPainOiRow.CallVolume"/> and <see cref="MaxPainOiRow.PutVolume"/>
/// are always <c>0</c> on historical (the minute-resolution options table
/// doesn't carry intraday volume). Use <c>CallOi</c> / <c>PutOi</c> for the
/// historical positioning view; the volume fields are placeholders for
/// shape parity.</para>
///
/// <para>The endpoint accepts an optional <c>expiration</c> query filter
/// (<c>yyyy-MM-dd</c>). When present, the response is scoped to that single
/// expiry and <see cref="MaxPainByExpiration"/> is <c>null</c>. When absent,
/// the full-chain max pain is returned alongside the multi-expiry calendar.</para>
///
/// <para>Returns 403 <c>tier_restricted</c> for Free-tier users.</para>
/// </summary>
public sealed class MaxPainResponse
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("underlying_price")]
    public double? UnderlyingPrice { get; set; }

    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    /// <summary>The headline number. Strike where total chain pain is minimized.</summary>
    [JsonPropertyName("max_pain_strike")]
    public double? MaxPainStrike { get; set; }

    /// <summary>Distance from spot to <see cref="MaxPainStrike"/> (absolute, percent, direction).</summary>
    [JsonPropertyName("distance")]
    public MaxPainDistance? Distance { get; set; }

    /// <summary>
    /// <c>"bullish"</c> (spot >= 5% below max_pain — pin attracts upside),
    /// <c>"bearish"</c> (>= 5% above), or <c>"neutral"</c> (within 5%).
    /// </summary>
    [JsonPropertyName("signal")]
    public string? Signal { get; set; }

    /// <summary>
    /// Expiration this view is scoped to. When the request omits the
    /// <c>expiration</c> filter, this is the front-month expiry that the
    /// full-chain max pain landed on.
    /// </summary>
    [JsonPropertyName("expiration")]
    public string? Expiration { get; set; }

    /// <summary>Total put OI / total call OI. >1.0 = put-heavy chain.</summary>
    [JsonPropertyName("put_call_oi_ratio")]
    public double? PutCallOiRatio { get; set; }

    /// <summary>Strike-by-strike pain curve. Minimum is at <see cref="MaxPainStrike"/>.</summary>
    [JsonPropertyName("pain_curve")]
    public List<MaxPainCurveRow>? PainCurve { get; set; }

    /// <summary>Per-strike OI + volume breakdown. Same strike grid as <see cref="PainCurve"/>.</summary>
    [JsonPropertyName("oi_by_strike")]
    public List<MaxPainOiRow>? OiByStrike { get; set; }

    /// <summary>Per-expiry calendar. <c>null</c> when the request specified an expiry.</summary>
    [JsonPropertyName("max_pain_by_expiration")]
    public List<MaxPainByExpirationRow>? MaxPainByExpiration { get; set; }

    /// <summary>GEX-based dealer alignment overlay. See <see cref="MaxPainDealerAlignment"/>.</summary>
    [JsonPropertyName("dealer_alignment")]
    public MaxPainDealerAlignment? DealerAlignment { get; set; }

    /// <summary>
    /// Same gamma classification as on <c>exposure_summary.regime</c>:
    /// <c>"positive_gamma"</c> | <c>"negative_gamma"</c> | <c>"neutral"</c> | <c>"undetermined"</c>.
    /// </summary>
    [JsonPropertyName("regime")]
    public string? Regime { get; set; }

    /// <summary>Expected move from the ATM straddle, contextualized vs max pain.</summary>
    [JsonPropertyName("expected_move")]
    public MaxPainExpectedMove? ExpectedMove { get; set; }

    /// <summary>
    /// 0-100 composite — likelihood of pinning to <see cref="MaxPainStrike"/>.
    /// Inputs: OI concentration (30%), magnet proximity (25%), time
    /// remaining (25%), gamma magnitude (20%). Most meaningful for
    /// near-term expiries.
    /// </summary>
    [JsonPropertyName("pin_probability")]
    public int? PinProbability { get; set; }
}

/// <summary>Distance from spot to the max-pain strike.</summary>
public sealed class MaxPainDistance
{
    /// <summary>Dollar distance: <c>|underlying_price - max_pain_strike|</c>.</summary>
    [JsonPropertyName("absolute")]
    public double? Absolute { get; set; }

    /// <summary>Percent of spot: <c>absolute / underlying_price * 100</c>.</summary>
    [JsonPropertyName("percent")]
    public double? Percent { get; set; }

    /// <summary><c>"above"</c>, <c>"below"</c>, or <c>"at"</c> — spot relative to max-pain.</summary>
    [JsonPropertyName("direction")]
    public string? Direction { get; set; }
}

/// <summary>
/// One row of the strike-by-strike pain curve.
///
/// <para>Each row is the dollar pain (intrinsic value × OI × 100 contract
/// multiplier) summed across all expirations at that strike. The strike
/// where <c>total_pain</c> is minimized is the max-pain strike.</para>
/// </summary>
public sealed class MaxPainCurveRow
{
    [JsonPropertyName("strike")]
    public double? Strike { get; set; }

    /// <summary>Dollar intrinsic value of all calls at this strike.</summary>
    [JsonPropertyName("call_pain")]
    public double? CallPain { get; set; }

    /// <summary>Dollar intrinsic value of all puts at this strike.</summary>
    [JsonPropertyName("put_pain")]
    public double? PutPain { get; set; }

    /// <summary><c>call_pain + put_pain</c>. The pain curve's minimum identifies max pain.</summary>
    [JsonPropertyName("total_pain")]
    public double? TotalPain { get; set; }
}

/// <summary>
/// One row of the OI-by-strike breakdown.
///
/// <para>Note: on the Historical API, <c>CallVolume</c> and <c>PutVolume</c>
/// are always <c>0</c> (placeholder fields — the minute table doesn't carry
/// intraday volume).</para>
/// </summary>
public sealed class MaxPainOiRow
{
    [JsonPropertyName("strike")]
    public double? Strike { get; set; }

    [JsonPropertyName("call_oi")]
    public int? CallOi { get; set; }

    [JsonPropertyName("put_oi")]
    public int? PutOi { get; set; }

    [JsonPropertyName("total_oi")]
    public int? TotalOi { get; set; }

    [JsonPropertyName("call_volume")]
    public int? CallVolume { get; set; }

    [JsonPropertyName("put_volume")]
    public int? PutVolume { get; set; }
}

/// <summary>
/// Per-expiry max-pain breakdown when no <c>expiration</c> filter is applied.
///
/// <para>The parent list is <c>null</c> when the request specified an
/// expiration filter — the response is then scoped to that single expiry
/// and the multi-expiry view is suppressed.</para>
/// </summary>
public sealed class MaxPainByExpirationRow
{
    [JsonPropertyName("expiration")]
    public string? Expiration { get; set; }

    [JsonPropertyName("max_pain_strike")]
    public double? MaxPainStrike { get; set; }

    /// <summary>Days to expiry (counting from <c>as_of</c>).</summary>
    [JsonPropertyName("dte")]
    public int? Dte { get; set; }

    [JsonPropertyName("total_oi")]
    public int? TotalOi { get; set; }
}

/// <summary>
/// GEX-based dealer-alignment overlay on the max-pain view.
///
/// <para>The headline <c>Alignment</c> label tells you whether dealer
/// hedging will REINFORCE the max-pain pin or fight it.</para>
/// </summary>
public sealed class MaxPainDealerAlignment
{
    /// <summary>
    /// <c>"converging"</c>: max pain near gamma flip and between walls — strongest pin.
    /// <c>"moderate"</c>: between walls but far from flip.
    /// <c>"diverging"</c>: max pain outside the wall range.
    /// <c>"unknown"</c>: insufficient data.
    /// </summary>
    [JsonPropertyName("alignment")]
    public string? Alignment { get; set; }

    /// <summary>Plain-English explanation. Safe to surface verbatim.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Strike where net dealer gamma crosses zero.</summary>
    [JsonPropertyName("gamma_flip")]
    public double? GammaFlip { get; set; }

    /// <summary>Strike with highest absolute call GEX (dealer-side resistance).</summary>
    [JsonPropertyName("call_wall")]
    public double? CallWall { get; set; }

    /// <summary>Strike with highest absolute put GEX (dealer-side support).</summary>
    [JsonPropertyName("put_wall")]
    public double? PutWall { get; set; }
}

/// <summary>Implied move from the ATM straddle, contextualized vs max pain.</summary>
public sealed class MaxPainExpectedMove
{
    /// <summary>ATM straddle mid in dollars. Rough proxy for the 1σ implied move.</summary>
    [JsonPropertyName("straddle_price")]
    public double? StraddlePrice { get; set; }

    /// <summary>ATM implied volatility (annualised %, e.g. 18.5 = 18.5%).</summary>
    [JsonPropertyName("atm_iv")]
    public double? AtmIv { get; set; }

    /// <summary><c>true</c> when <c>|spot - max_pain_strike| &lt;= straddle_price</c>.</summary>
    [JsonPropertyName("max_pain_within_expected_range")]
    public bool? MaxPainWithinExpectedRange { get; set; }
}
