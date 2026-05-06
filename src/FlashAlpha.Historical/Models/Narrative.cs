using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/exposure/narrative/{symbol}?at=...</c>.
///
/// <para>Point-in-time replay of the live <c>narrative</c> endpoint — the
/// <b>verbal, LLM-friendly</b> view of dealer positioning at the requested
/// minute. Each string under <see cref="NarrativeResponse.Narrative"/> is a
/// hand-tuned, numbers-aware sentence describing one facet of the setup
/// (regime, gex change, key levels, flow, vanna, charm, 0DTE, outlook).
/// Lines are safe to surface verbatim to an end user, LLM agent, or chat UI.</para>
///
/// <para>The <see cref="NarrativeBlock.Data"/> sub-block carries the raw
/// numbers backing the prose. Same response shape as the live API — you
/// can swap between <c>FlashAlphaClient.NarrativeAsync</c> and
/// <c>FlashAlphaHistoricalClient.NarrativeAsync</c> without changing the
/// consumer code.</para>
///
/// <para><see cref="AsOf"/> is the as-of stamp the API actually used —
/// snapped to the available minute, may not equal the <c>at</c> request
/// value.</para>
/// </summary>
public sealed class NarrativeResponse
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("underlying_price")]
    public double? UnderlyingPrice { get; set; }

    /// <summary>UTC timestamp the API actually used — snapped to the available minute.</summary>
    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    /// <summary>The narrative payload — prose lines plus the raw <c>data</c> block.</summary>
    [JsonPropertyName("narrative")]
    public NarrativeBlock? Narrative { get; set; }
}

/// <summary>
/// The narrative payload — verbal lines plus raw numbers.
/// </summary>
public sealed class NarrativeBlock
{
    /// <summary>Sentence describing the current gamma regime.</summary>
    [JsonPropertyName("regime")]
    public string? Regime { get; set; }

    /// <summary>Sentence describing how net GEX shifted vs prior session.</summary>
    [JsonPropertyName("gex_change")]
    public string? GexChange { get; set; }

    /// <summary>Sentence naming the call wall, put wall, and gamma flip.</summary>
    [JsonPropertyName("key_levels")]
    public string? KeyLevels { get; set; }

    /// <summary>Sentence describing notable OI / volume changes by strike and side.</summary>
    [JsonPropertyName("flow")]
    public string? Flow { get; set; }

    /// <summary>Sentence describing net vanna exposure in the context of current vol.</summary>
    [JsonPropertyName("vanna")]
    public string? Vanna { get; set; }

    /// <summary>Sentence describing net charm exposure.</summary>
    [JsonPropertyName("charm")]
    public string? Charm { get; set; }

    /// <summary>Sentence describing the 0DTE share of total exposure.</summary>
    [JsonPropertyName("zero_dte")]
    public string? ZeroDte { get; set; }

    /// <summary>Forward-looking sentence summarising the setup.</summary>
    [JsonPropertyName("outlook")]
    public string? Outlook { get; set; }

    /// <summary>Raw numbers backing the prose. See <see cref="NarrativeData"/>.</summary>
    [JsonPropertyName("data")]
    public NarrativeData? Data { get; set; }
}

/// <summary>Raw numbers backing the prose lines in <see cref="NarrativeBlock"/>.</summary>
public sealed class NarrativeData
{
    [JsonPropertyName("net_gex")]
    public double? NetGex { get; set; }

    [JsonPropertyName("net_gex_prior")]
    public double? NetGexPrior { get; set; }

    [JsonPropertyName("net_gex_change_pct")]
    public double? NetGexChangePct { get; set; }

    [JsonPropertyName("vix")]
    public double? Vix { get; set; }

    [JsonPropertyName("gamma_flip")]
    public double? GammaFlip { get; set; }

    [JsonPropertyName("call_wall")]
    public double? CallWall { get; set; }

    [JsonPropertyName("put_wall")]
    public double? PutWall { get; set; }

    /// <summary><c>"positive_gamma"</c> | <c>"negative_gamma"</c> | <c>"neutral"</c> | <c>"undetermined"</c>.</summary>
    [JsonPropertyName("regime")]
    public string? Regime { get; set; }

    /// <summary>0DTE GEX as a percent of total chain GEX.</summary>
    [JsonPropertyName("zero_dte_pct")]
    public double? ZeroDtePct { get; set; }

    [JsonPropertyName("top_oi_changes")]
    public List<NarrativeOiChange>? TopOiChanges { get; set; }
}

/// <summary>One row of "top OI change" — strike + side + delta + volume.</summary>
public sealed class NarrativeOiChange
{
    [JsonPropertyName("strike")]
    public double? Strike { get; set; }

    /// <summary><c>"C"</c> for call, <c>"P"</c> for put.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("oi_change")]
    public long? OiChange { get; set; }

    [JsonPropertyName("volume")]
    public long? Volume { get; set; }
}
