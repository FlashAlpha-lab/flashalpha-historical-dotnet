using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/surface/{symbol}?at=...</c>.
///
/// <para>50×50 historical implied volatility surface grid (tenor × log-moneyness)
/// at the requested point in time.</para>
///
/// <para><see cref="Iv"/> is indexed as <c>[tenor_idx][moneyness_idx]</c> —
/// outer dimension matches <see cref="Tenors"/>, inner matches <see cref="Moneyness"/>.</para>
///
/// <para>On sparse historical days the API may return
/// <see cref="InsufficientDataException"/> rather than a partial surface.</para>
/// </summary>
public sealed class SurfaceResponse
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("spot")]
    public double? Spot { get; set; }

    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    /// <summary>Grid resolution per axis (typically 50 → 50×50 surface).</summary>
    [JsonPropertyName("grid_size")]
    public int? GridSize { get; set; }

    /// <summary>Tenor grid axis (years to expiry).</summary>
    [JsonPropertyName("tenors")]
    public double[]? Tenors { get; set; }

    /// <summary>Log-moneyness grid axis.</summary>
    [JsonPropertyName("moneyness")]
    public double[]? Moneyness { get; set; }

    /// <summary>Implied vol matrix (annualised %) — <c>[tenor][moneyness]</c>.</summary>
    [JsonPropertyName("iv")]
    public double[][]? Iv { get; set; }

    /// <summary>Source expiries actually used in the fit.</summary>
    [JsonPropertyName("slices_used")]
    public List<string>? SlicesUsed { get; set; }
}
