using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/exposure/levels/{symbol}?at=...</c>.
///
/// <para>Point-in-time replay of the live <c>exposure/levels</c> endpoint —
/// distilled set of dealer-flow key levels (gamma flip, max-positive /
/// max-negative gamma strikes, call/put walls, highest-OI strike, 0DTE
/// magnet) at the requested minute.</para>
///
/// <para>Same response shape as the live API — you can swap between
/// <c>FlashAlphaClient.ExposureLevelsAsync</c> and
/// <c>FlashAlphaHistoricalClient.ExposureLevelsAsync</c> without changing
/// the consumer code. <see cref="AsOf"/> is snapped to the available minute.</para>
/// </summary>
public sealed class ExposureLevelsResponse : FlashAlphaResponse
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("underlying_price")]
    public double? UnderlyingPrice { get; set; }

    /// <summary>UTC timestamp the API actually used — snapped to the available minute.</summary>
    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    /// <summary>The levels block.</summary>
    [JsonPropertyName("levels")]
    public ExposureLevels? Levels { get; set; }
}

/// <summary>
/// Distilled key levels derived from dealer-options exposure.
///
/// <para><b>Trading-context cheat sheet:</b>
/// <list type="bullet">
///   <item><see cref="GammaFlip"/>: Spot ABOVE = positive-gamma regime;
///     spot BELOW = negative-gamma regime.</item>
///   <item><see cref="CallWall"/>: highest call-GEX strike — resistance.</item>
///   <item><see cref="PutWall"/>: highest put-GEX strike — support.</item>
///   <item><see cref="ZeroDteMagnet"/>: 0DTE strike with highest GEX —
///     intraday price magnet on expiry day.</item>
/// </list></para>
/// </summary>
public sealed class ExposureLevels
{
    /// <summary>Strike where net dealer gamma flips sign.</summary>
    [JsonPropertyName("gamma_flip")]
    public double? GammaFlip { get; set; }

    /// <summary>Strike with the most positive net dealer gamma.</summary>
    [JsonPropertyName("max_positive_gamma")]
    public double? MaxPositiveGamma { get; set; }

    /// <summary>Strike with the most negative net dealer gamma.</summary>
    [JsonPropertyName("max_negative_gamma")]
    public double? MaxNegativeGamma { get; set; }

    /// <summary>Highest call-GEX strike — resistance.</summary>
    [JsonPropertyName("call_wall")]
    public double? CallWall { get; set; }

    /// <summary>Highest put-GEX strike — support.</summary>
    [JsonPropertyName("put_wall")]
    public double? PutWall { get; set; }

    /// <summary>Strike with the highest aggregate OI.</summary>
    [JsonPropertyName("highest_oi_strike")]
    public double? HighestOiStrike { get; set; }

    /// <summary>0DTE strike with the highest GEX — intraday price magnet.</summary>
    [JsonPropertyName("zero_dte_magnet")]
    public double? ZeroDteMagnet { get; set; }
}
