using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/adv_volatility/{symbol}?at=...</c> (Alpha+).
///
/// <para>Advanced historical volatility analytics at a point in time:
/// per-expiry SVI parameter set, forward prices, full total-variance surface,
/// arbitrage flags, variance swap fair values, and second-/third-order greek
/// surfaces (vanna, charm, volga, speed).</para>
///
/// <para>Same response shape as the live API; the only difference is every
/// historical analytics endpoint requires an <c>at</c> query parameter.
/// Cold-cache responses can take ~1.5s — clients should set a generous
/// timeout.</para>
/// </summary>
public sealed class AdvVolatilityResponse
{
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("underlying_price")]
    public double? UnderlyingPrice { get; set; }

    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    [JsonPropertyName("market_open")]
    public bool? MarketOpen { get; set; }

    /// <summary>Per-expiry SVI parameter set: (a, b, ρ, m, σ) plus forward and ATM-total-variance.</summary>
    [JsonPropertyName("svi_parameters")]
    public List<AdvVolatilitySviParams>? SviParameters { get; set; }

    /// <summary>Per-expiry forward prices and basis vs spot.</summary>
    [JsonPropertyName("forward_prices")]
    public List<AdvVolatilityForwardPrice>? ForwardPrices { get; set; }

    /// <summary>Total variance surface — log-moneyness × tenor grid plus implied-vol grid.</summary>
    [JsonPropertyName("total_variance_surface")]
    public AdvVolatilityVarianceSurface? TotalVarianceSurface { get; set; }

    /// <summary>Detected butterfly / calendar arbitrage violations across the surface.</summary>
    [JsonPropertyName("arbitrage_flags")]
    public List<AdvVolatilityArbitrageFlag>? ArbitrageFlags { get; set; }

    /// <summary>Variance swap fair values per expiry, with convexity adjustment.</summary>
    [JsonPropertyName("variance_swap_fair_values")]
    public List<AdvVolatilityVarianceSwap>? VarianceSwapFairValues { get; set; }

    /// <summary>Second-/third-order greek surfaces.</summary>
    [JsonPropertyName("greeks_surfaces")]
    public AdvVolatilityGreeksSurfaces? GreeksSurfaces { get; set; }
}

/// <summary>SVI parameter set for one expiry.</summary>
public sealed class AdvVolatilitySviParams
{
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("days_to_expiry")]
    public int? DaysToExpiry { get; set; }

    [JsonPropertyName("forward")]
    public double? Forward { get; set; }

    /// <summary>SVI level parameter.</summary>
    [JsonPropertyName("a")]
    public double? A { get; set; }

    /// <summary>SVI angle parameter.</summary>
    [JsonPropertyName("b")]
    public double? B { get; set; }

    /// <summary>SVI correlation parameter; controls left/right wing asymmetry. Range: [-1, 1].</summary>
    [JsonPropertyName("rho")]
    public double? Rho { get; set; }

    /// <summary>SVI horizontal-translation parameter.</summary>
    [JsonPropertyName("m")]
    public double? M { get; set; }

    /// <summary>SVI smoothness parameter.</summary>
    [JsonPropertyName("sigma")]
    public double? Sigma { get; set; }

    [JsonPropertyName("atm_total_variance")]
    public double? AtmTotalVariance { get; set; }

    [JsonPropertyName("atm_iv")]
    public double? AtmIv { get; set; }
}

/// <summary>Forward price + basis for one expiry.</summary>
public sealed class AdvVolatilityForwardPrice
{
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("days_to_expiry")]
    public int? DaysToExpiry { get; set; }

    [JsonPropertyName("forward")]
    public double? Forward { get; set; }

    [JsonPropertyName("spot")]
    public double? Spot { get; set; }

    [JsonPropertyName("basis_pct")]
    public double? BasisPct { get; set; }
}

/// <summary>
/// Full total-variance surface as parallel arrays plus 2D matrices.
///
/// <para><see cref="TotalVariance"/> and <see cref="ImpliedVol"/> are
/// indexed as <c>[moneyness_idx][tenor_idx]</c>.</para>
/// </summary>
public sealed class AdvVolatilityVarianceSurface
{
    [JsonPropertyName("moneyness")]
    public double[]? Moneyness { get; set; }

    [JsonPropertyName("expiries")]
    public string[]? Expiries { get; set; }

    [JsonPropertyName("tenors")]
    public double[]? Tenors { get; set; }

    [JsonPropertyName("total_variance")]
    public double[][]? TotalVariance { get; set; }

    [JsonPropertyName("implied_vol")]
    public double[][]? ImpliedVol { get; set; }
}

/// <summary>One detected static-arbitrage violation on the surface.</summary>
public sealed class AdvVolatilityArbitrageFlag
{
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    /// <summary><c>"butterfly"</c> | <c>"calendar"</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("strike_or_k")]
    public double? StrikeOrK { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>Variance swap fair values for one expiry.</summary>
public sealed class AdvVolatilityVarianceSwap
{
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("days_to_expiry")]
    public int? DaysToExpiry { get; set; }

    /// <summary>Fair variance strike (squared annualised vol).</summary>
    [JsonPropertyName("fair_variance")]
    public double? FairVariance { get; set; }

    /// <summary>Fair vol strike (annualised %).</summary>
    [JsonPropertyName("fair_vol")]
    public double? FairVol { get; set; }

    [JsonPropertyName("atm_iv")]
    public double? AtmIv { get; set; }

    /// <summary><c>fair_vol - atm_iv</c>. Premium for the curvature of the smile.</summary>
    [JsonPropertyName("convexity_adjustment")]
    public double? ConvexityAdjustment { get; set; }
}

/// <summary>Second- and third-order greek surfaces over the strike × expiry grid.</summary>
public sealed class AdvVolatilityGreeksSurfaces
{
    [JsonPropertyName("vanna")]
    public AdvVolatilityGreekGrid? Vanna { get; set; }

    [JsonPropertyName("charm")]
    public AdvVolatilityGreekGrid? Charm { get; set; }

    [JsonPropertyName("volga")]
    public AdvVolatilityGreekGrid? Volga { get; set; }

    [JsonPropertyName("speed")]
    public AdvVolatilityGreekGrid? Speed { get; set; }
}

/// <summary>One greek surface on a strike × expiry grid. <see cref="Values"/> is <c>[strike][expiry]</c>.</summary>
public sealed class AdvVolatilityGreekGrid
{
    [JsonPropertyName("strikes")]
    public double[]? Strikes { get; set; }

    [JsonPropertyName("expiries")]
    public string[]? Expiries { get; set; }

    [JsonPropertyName("values")]
    public double[][]? Values { get; set; }
}
