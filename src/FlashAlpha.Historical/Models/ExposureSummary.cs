using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/exposure/summary/{symbol}?at=...</c>.
///
/// <para>The Historical API returns the same response shape as the live API;
/// the only difference is every analytics endpoint requires an <c>at</c> query
/// parameter (string or <see cref="System.DateTime"/>).</para>
///
/// <para><b>Direction casing:</b> confirmed via live probe — both
/// <c>/v1/exposure/summary/</c> and <c>/v1/exposure/zero-dte/</c> return
/// lowercase <c>"buy"</c>/<c>"sell"</c>. Casing is consistent across
/// summary and zero-DTE endpoints.</para>
/// </summary>
public sealed class ExposureSummaryResponse
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("underlying_price")]
    public double? UnderlyingPrice { get; set; }

    /// <summary>The as-of stamp the API actually used (snapped to the available minute).</summary>
    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    // Note: as_of_requested exists on /v1/exposure/{gex,dex,narrative} but
    // NOT on /v1/exposure/summary. Don't add it to this type even though it
    // would be defensive — the field genuinely isn't returned for this endpoint.

    [JsonPropertyName("gamma_flip")]
    public double? GammaFlip { get; set; }

    /// <summary>
    /// One of <c>"positive_gamma"</c>, <c>"negative_gamma"</c>,
    /// <c>"neutral"</c>, or <c>"undetermined"</c>.
    /// </summary>
    [JsonPropertyName("regime")]
    public string? Regime { get; set; }

    [JsonPropertyName("exposures")]
    public ExposureSummaryExposures? Exposures { get; set; }

    [JsonPropertyName("interpretation")]
    public ExposureSummaryInterpretation? Interpretation { get; set; }

    [JsonPropertyName("hedging_estimate")]
    public ExposureSummaryHedgingEstimate? HedgingEstimate { get; set; }

    [JsonPropertyName("zero_dte")]
    public ExposureSummaryZeroDte? ZeroDte { get; set; }
}

/// <summary>Net exposure totals across the entire chain.</summary>
public sealed class ExposureSummaryExposures
{
    [JsonPropertyName("net_gex")]
    public double? NetGex { get; set; }

    [JsonPropertyName("net_dex")]
    public double? NetDex { get; set; }

    [JsonPropertyName("net_vex")]
    public double? NetVex { get; set; }

    [JsonPropertyName("net_chex")]
    public double? NetChex { get; set; }
}

/// <summary>Verbal interpretation of the gamma/vanna/charm regimes.</summary>
public sealed class ExposureSummaryInterpretation
{
    [JsonPropertyName("gamma")]
    public string? Gamma { get; set; }

    [JsonPropertyName("vanna")]
    public string? Vanna { get; set; }

    [JsonPropertyName("charm")]
    public string? Charm { get; set; }
}

/// <summary>One side (up or down) of a dealer-hedging estimate.</summary>
public sealed class ExposureSummaryHedgingMove
{
    [JsonPropertyName("dealer_shares_to_trade")]
    public double? DealerSharesToTrade { get; set; }

    /// <summary>
    /// <c>"buy"</c> or <c>"sell"</c> (lowercase on both this endpoint and
    /// zero-dte).
    /// </summary>
    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    [JsonPropertyName("notional_usd")]
    public double? NotionalUsd { get; set; }
}

/// <summary>Estimated dealer hedging flow at +/- 1% spot moves.</summary>
public sealed class ExposureSummaryHedgingEstimate
{
    [JsonPropertyName("spot_up_1pct")]
    public ExposureSummaryHedgingMove? SpotUp1Pct { get; set; }

    [JsonPropertyName("spot_down_1pct")]
    public ExposureSummaryHedgingMove? SpotDown1Pct { get; set; }
}

/// <summary>Same-day-expiration contribution to total GEX.</summary>
public sealed class ExposureSummaryZeroDte
{
    [JsonPropertyName("net_gex")]
    public double? NetGex { get; set; }

    [JsonPropertyName("pct_of_total_gex")]
    public double? PctOfTotalGex { get; set; }

    [JsonPropertyName("expiration")]
    public string? Expiration { get; set; }
}
