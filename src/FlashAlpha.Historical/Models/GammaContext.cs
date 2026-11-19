using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

// ── /v1/exposure/expiry-impact/{symbol}?at=... and /v1/exposure/strike-dependency/{symbol}?at=... ──

/// <summary>
/// Metadata shared by <see cref="ExpiryImpactResponse"/> and <see cref="StrikeDependencyResponse"/>
/// (SPY and QQQ only). Timestamps are ISO-8601 UTC strings.
/// </summary>
public abstract class GammaContextMetadata : FlashAlphaResponse
{
    /// <summary>The symbol, upper case.</summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    [JsonPropertyName("underlying_price")]
    public double? UnderlyingPrice { get; set; }

    /// <summary>SHA-256 fingerprint of the captured inputs; identical inputs give identical IDs.</summary>
    [JsonPropertyName("snapshot_id")]
    public string? SnapshotId { get; set; }

    [JsonPropertyName("generated_at")]
    public string? GeneratedAt { get; set; }

    [JsonPropertyName("quote_at")]
    public string? QuoteAt { get; set; }

    [JsonPropertyName("greeks_oldest_at")]
    public string? GreeksOldestAt { get; set; }

    [JsonPropertyName("greeks_newest_at")]
    public string? GreeksNewestAt { get; set; }

    [JsonPropertyName("calculation_version")]
    public string? CalculationVersion { get; set; }

    [JsonPropertyName("pricing_basis")]
    public string? PricingBasis { get; set; }

    /// <summary><c>partial</c> in normal operation (open-interest vintage is unknown), <c>stale</c>
    /// when an input predates the market-session freshness cutoff, <c>unavailable</c> when no
    /// contract qualifies.</summary>
    [JsonPropertyName("data_status")]
    public string? DataStatus { get; set; }

    /// <summary>Machine codes and excluded-contract counts behind <see cref="DataStatus"/>.</summary>
    [JsonPropertyName("reasons")]
    public List<GammaContextReason>? Reasons { get; set; }

    [JsonPropertyName("oi_as_of")]
    public string? OiAsOf { get; set; }

    [JsonPropertyName("oi_vintage_status")]
    public string? OiVintageStatus { get; set; }

    [JsonPropertyName("eligible_contract_count")]
    public int? EligibleContractCount { get; set; }

    [JsonPropertyName("rejected_contract_count")]
    public int? RejectedContractCount { get; set; }

    [JsonPropertyName("observed_min_strike")]
    public double? ObservedMinStrike { get; set; }

    [JsonPropertyName("observed_max_strike")]
    public double? ObservedMaxStrike { get; set; }

    [JsonPropertyName("gex_unit")]
    public string? GexUnit { get; set; }

    [JsonPropertyName("sign_convention")]
    public string? SignConvention { get; set; }

    [JsonPropertyName("analysis_type")]
    public string? AnalysisType { get; set; }
}

/// <summary>One entry of <see cref="GammaContextMetadata.Reasons"/>.</summary>
public sealed class GammaContextReason
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>Contracts excluded for this reason. Null for reasons that are not exclusions.</summary>
    [JsonPropertyName("contract_count")]
    public int? ContractCount { get; set; }
}

/// <summary>Gross, net, call and put GEX (USD per 1% move) and the contract count.</summary>
public class GammaTotals
{
    [JsonPropertyName("gross_gex_usd")]
    public double? GrossGexUsd { get; set; }

    [JsonPropertyName("net_gex_usd")]
    public double? NetGexUsd { get; set; }

    [JsonPropertyName("call_gex_usd")]
    public double? CallGexUsd { get; set; }

    [JsonPropertyName("put_gex_usd")]
    public double? PutGexUsd { get; set; }

    [JsonPropertyName("contract_count")]
    public int? ContractCount { get; set; }

    /// <summary>Set on <see cref="ExpiryImpactResponse.ExcludingExpiry"/>: <c>available</c>,
    /// <c>zero_gross_exposure</c> or <c>no_remaining_contracts</c>. Null elsewhere.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

/// <summary>One expiry's share of the gamma at the strike in <see cref="StrikeDependencyResponse.ByExpiry"/>.</summary>
public sealed class GammaExpiryRow : GammaTotals
{
    /// <summary><c>yyyy-MM-dd</c>.</summary>
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("valuation_cutoff_at")]
    public string? ValuationCutoffAt { get; set; }

    [JsonPropertyName("last_trading_at")]
    public string? LastTradingAt { get; set; }

    [JsonPropertyName("last_trading_status")]
    public string? LastTradingStatus { get; set; }

    [JsonPropertyName("settlement_at")]
    public string? SettlementAt { get; set; }

    [JsonPropertyName("settlement_status")]
    public string? SettlementStatus { get; set; }

    [JsonPropertyName("calendar_days_to_expiry")]
    public int? CalendarDaysToExpiry { get; set; }

    [JsonPropertyName("gross_share_pct")]
    public double? GrossSharePct { get; set; }

    [JsonPropertyName("share_of_strike_gross_pct")]
    public double? ShareOfStrikeGrossPct { get; set; }
}

/// <summary>One strike's current, excluded and remaining gross/net GEX.</summary>
public sealed class GammaStrikeImpact
{
    [JsonPropertyName("strike")]
    public double? Strike { get; set; }

    [JsonPropertyName("distance_from_spot_pct")]
    public double? DistanceFromSpotPct { get; set; }

    [JsonPropertyName("current_gross_gex_usd")]
    public double? CurrentGrossGexUsd { get; set; }

    [JsonPropertyName("current_net_gex_usd")]
    public double? CurrentNetGexUsd { get; set; }

    [JsonPropertyName("excluded_gross_gex_usd")]
    public double? ExcludedGrossGexUsd { get; set; }

    [JsonPropertyName("excluded_net_gex_usd")]
    public double? ExcludedNetGexUsd { get; set; }

    [JsonPropertyName("remaining_gross_gex_usd")]
    public double? RemainingGrossGexUsd { get; set; }

    [JsonPropertyName("remaining_net_gex_usd")]
    public double? RemainingNetGexUsd { get; set; }

    [JsonPropertyName("excluded_share_at_strike_pct")]
    public double? ExcludedShareAtStrikePct { get; set; }
}

/// <summary>The <c>impact</c> block of <see cref="ExpiryImpactResponse"/>.</summary>
public sealed class GammaRemovalImpact
{
    /// <summary><c>100 × excluded gross / current gross</c>. Null when current gross is zero.</summary>
    [JsonPropertyName("removed_gross_share_pct")]
    public double? RemovedGrossSharePct { get; set; }

    [JsonPropertyName("retained_gross_share_pct")]
    public double? RetainedGrossSharePct { get; set; }

    /// <summary><c>remaining net − current net</c> (minus the excluded net).</summary>
    [JsonPropertyName("net_gex_change_usd")]
    public double? NetGexChangeUsd { get; set; }

    /// <summary>Sign of the modeled net total before exclusion.</summary>
    [JsonPropertyName("net_sign_before")]
    public string? NetSignBefore { get; set; }

    /// <summary>Sign of the modeled net total after exclusion.</summary>
    [JsonPropertyName("net_sign_after")]
    public string? NetSignAfter { get; set; }

    [JsonPropertyName("net_sign_changed")]
    public bool? NetSignChanged { get; set; }

    /// <summary>Top <c>limit</c> strikes by gross removed.</summary>
    [JsonPropertyName("largest_gross_reductions")]
    public List<GammaStrikeImpact>? LargestGrossReductions { get; set; }

    /// <summary>Top <c>limit</c> strikes by gross remaining.</summary>
    [JsonPropertyName("strongest_remaining_concentrations")]
    public List<GammaStrikeImpact>? StrongestRemainingConcentrations { get; set; }
}

/// <summary>A plain-language statement with its supporting values.</summary>
public sealed class GammaContextFact
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>Key/value bag. The keys vary by <see cref="Code"/>.</summary>
    [JsonPropertyName("supporting_values")]
    public Dictionary<string, JsonElement>? SupportingValues { get; set; }
}

/// <summary>
/// Typed response for <c>GET /v1/exposure/expiry-impact/{symbol}?at=...</c>: compares the SPY or
/// QQQ gamma distribution with the same captured inputs after excluding one expiry. Spot, Greeks
/// and open interest are held fixed, so this measures how much of the concentration depends on
/// one maturity. It is not a forecast of post-expiry prices, dealer hedging or the next session's
/// chain. Results are model reconstructions from archived quotes. Requires Alpha or higher.
/// </summary>
public sealed class ExpiryImpactResponse : GammaContextMetadata
{
    /// <summary><c>yyyy-MM-dd</c>. The expiry that was excluded.</summary>
    [JsonPropertyName("selected_expiry")]
    public string? SelectedExpiry { get; set; }

    [JsonPropertyName("selection_source")]
    public string? SelectionSource { get; set; }

    /// <summary><c>yyyy-MM-dd</c> values accepted by the <c>expiry</c> parameter.</summary>
    [JsonPropertyName("available_expiries")]
    public List<string>? AvailableExpiries { get; set; }

    /// <summary>The whole eligible chain.</summary>
    [JsonPropertyName("current")]
    public GammaTotals? Current { get; set; }

    /// <summary>The selected expiry.</summary>
    [JsonPropertyName("excluded_expiry")]
    public GammaTotals? ExcludedExpiry { get; set; }

    /// <summary>The chain without the selected expiry. Carries <see cref="GammaTotals.Status"/>.</summary>
    [JsonPropertyName("excluding_expiry")]
    public GammaTotals? ExcludingExpiry { get; set; }

    [JsonPropertyName("impact")]
    public GammaRemovalImpact? Impact { get; set; }

    /// <summary>Every eligible strike with current, excluded and remaining gross/net GEX.</summary>
    [JsonPropertyName("strikes")]
    public List<GammaStrikeImpact>? Strikes { get; set; }

    [JsonPropertyName("facts")]
    public List<GammaContextFact>? Facts { get; set; }
}

/// <summary>
/// Typed response for <c>GET /v1/exposure/strike-dependency/{symbol}?at=...</c>: attributes the
/// gamma at one exact observed SPY or QQQ strike to its expiries: does the level depend on a single
/// maturity? It reports concentration at that strike, not a support/resistance probability.
/// Results are model reconstructions from archived quotes. Requires Alpha or higher.
/// </summary>
public sealed class StrikeDependencyResponse : GammaContextMetadata
{
    [JsonPropertyName("strike")]
    public double? Strike { get; set; }

    [JsonPropertyName("distance_from_spot_pct")]
    public double? DistanceFromSpotPct { get; set; }

    [JsonPropertyName("gross_gex_usd")]
    public double? GrossGexUsd { get; set; }

    [JsonPropertyName("net_gex_usd")]
    public double? NetGexUsd { get; set; }

    [JsonPropertyName("call_gex_usd")]
    public double? CallGexUsd { get; set; }

    [JsonPropertyName("put_gex_usd")]
    public double? PutGexUsd { get; set; }

    [JsonPropertyName("contract_count")]
    public int? ContractCount { get; set; }

    [JsonPropertyName("by_expiry")]
    public List<GammaExpiryRow>? ByExpiry { get; set; }

    /// <summary><c>yyyy-MM-dd</c>. The dominant expiry at this strike.</summary>
    [JsonPropertyName("dominant_expiry")]
    public string? DominantExpiry { get; set; }

    [JsonPropertyName("dominant_expiry_share_pct")]
    public double? DominantExpirySharePct { get; set; }
}
