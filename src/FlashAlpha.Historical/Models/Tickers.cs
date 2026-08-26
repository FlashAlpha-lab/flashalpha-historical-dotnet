using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/tickers</c> — list form (no <c>symbol</c> filter).
///
/// <para>Lists every symbol with historical coverage, each with its first/last
/// covered session and a count of healthy days. Use the single-symbol form
/// (<see cref="TickersSingleResponse"/>) when you pass <c>symbol=...</c>.</para>
/// </summary>
public sealed class TickersResponse : FlashAlphaResponse
{
    /// <summary>Per-symbol coverage rows.</summary>
    [JsonPropertyName("tickers")]
    public List<TickersRow>? Tickers { get; set; }

    /// <summary>Length of <see cref="Tickers"/>.</summary>
    [JsonPropertyName("count")]
    public int? Count { get; set; }
}

/// <summary>
/// One row of <see cref="TickersResponse.Tickers"/> — a symbol plus its
/// coverage metadata.
/// </summary>
public sealed class TickersRow
{
    /// <summary>Underlying symbol (e.g. <c>"SPY"</c>).</summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    /// <summary>Coverage range and healthy-day count for this symbol.</summary>
    [JsonPropertyName("coverage")]
    public TickersCoverage? Coverage { get; set; }
}

/// <summary>
/// Typed response model for <c>GET /v1/tickers?symbol=...</c> — single-symbol form.
///
/// <para>The list endpoint returns a different shape (<see cref="TickersResponse"/>)
/// when no <c>symbol</c> filter is supplied. Pick the typed wrapper that matches
/// your call.</para>
/// </summary>
public sealed class TickersSingleResponse : FlashAlphaResponse
{
    /// <summary>Underlying symbol (e.g. <c>"SPY"</c>).</summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    /// <summary>Coverage range and healthy-day count.</summary>
    [JsonPropertyName("coverage")]
    public TickersCoverage? Coverage { get; set; }
}

/// <summary>
/// Coverage envelope for a single symbol — first/last covered session and
/// healthy-day count.
/// </summary>
public sealed class TickersCoverage
{
    /// <summary>First covered date (<c>"yyyy-MM-dd"</c>).</summary>
    [JsonPropertyName("first")]
    public string? First { get; set; }

    /// <summary>Last covered date (<c>"yyyy-MM-dd"</c>).</summary>
    [JsonPropertyName("last")]
    public string? Last { get; set; }

    /// <summary>Number of healthy (non-empty) trading days within the covered range.</summary>
    [JsonPropertyName("healthy_days")]
    public int? HealthyDays { get; set; }
}
