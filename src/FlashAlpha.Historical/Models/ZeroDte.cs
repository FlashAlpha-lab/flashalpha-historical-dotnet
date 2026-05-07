using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response model for <c>GET /v1/exposure/zero-dte/{symbol}?at=...</c>.
///
/// <para>Historical 0DTE analytics replayed at a point in time. Combines
/// dealer-gamma regime classification, GEX/DEX/VEX/CHEX exposures,
/// ATM-straddle implied move, OI-magnet pin scoring, dealer hedging
/// buckets at multiple spot shocks, theta/charm decay, term-structure
/// vol context, intraday flow proxies, key levels (walls, magnets,
/// max pain), liquidity scoring, and a per-strike grid — the same
/// shape as the live endpoint but anchored to the requested minute.</para>
///
/// <para>The <c>at</c> query parameter is required. The API snaps your
/// requested timestamp to the nearest available minute and echoes the
/// snapped value back as <see cref="AsOf"/>; the date portion of the
/// snapped <c>as_of</c> determines whether a 0DTE expiry was actually
/// listed for that session. This is a strongly-typed mirror of the
/// JSON response — use it via
/// <see cref="FlashAlphaHistoricalClient.ZeroDteTypedAsync(string, string, double?, System.Threading.CancellationToken)"/>.
/// The original
/// <see cref="FlashAlphaHistoricalClient.ZeroDteAsync(string, string, double?, System.Threading.CancellationToken)"/>
/// remains unchanged and continues to return <see cref="System.Text.Json.JsonElement"/>.</para>
///
/// <para>Historical-mode behaviors that differ from live:
/// <list type="bullet">
///   <item><see cref="MarketOpen"/> reflects the requested minute, not "now"
///     — pre-market / post-market / weekend timestamps return <c>false</c>
///     and the time-sensitive intraday metrics
///     (<see cref="ZeroDteDecay.ThetaPerHourRemaining"/>,
///     <see cref="ZeroDteExpectedMove.Remaining1SdDollars"/>) become
///     <c>null</c> just like they do on a closed live session.</item>
///   <item>Per-strike <see cref="ZeroDteStrike.CallVolume"/> /
///     <see cref="ZeroDteStrike.PutVolume"/> and the chain-level
///     <see cref="ZeroDteFlow.TotalVolume"/> /
///     <see cref="ZeroDteFlow.CallVolume"/> /
///     <see cref="ZeroDteFlow.PutVolume"/> are placeholders on historical
///     — the minute table doesn't carry intraday volume. OI fields are
///     fully populated.</item>
///   <item>On weekends/holidays or sessions without a 0DTE expiry,
///     <see cref="NoZeroDte"/> is <c>true</c> and most analytics fields
///     are <c>null</c> — only <see cref="Symbol"/>, <see cref="AsOf"/>,
///     <see cref="Message"/>, and <see cref="NextZeroDteExpiry"/> are populated.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ZeroDteResponse
{
    /// <summary>Echoed from the request path (e.g. <c>"SPY"</c>, <c>"SPX"</c>, <c>"QQQ"</c>).</summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    /// <summary>Spot mid at <see cref="AsOf"/>. The reference price all distances/walls/magnets are computed against.</summary>
    [JsonPropertyName("underlying_price")]
    public double? UnderlyingPrice { get; set; }

    /// <summary>0DTE expiration this view describes (<c>"yyyy-MM-dd"</c>). Always equal to the date portion of <see cref="AsOf"/> when populated.</summary>
    [JsonPropertyName("expiration")]
    public string? Expiration { get; set; }

    /// <summary>The as-of stamp the API actually used (snapped to the nearest available minute, ISO-8601 ET wall-clock).</summary>
    [JsonPropertyName("as_of")]
    public string? AsOf { get; set; }

    /// <summary><c>true</c> if NYSE was open at <see cref="AsOf"/>. Time-sensitive fields (theta-per-hour, remaining 1σ) only compute when the market was open at the requested minute.</summary>
    [JsonPropertyName("market_open")]
    public bool? MarketOpen { get; set; }

    /// <summary>Hours remaining until 4pm ET cash close for the requested minute. <c>0</c> when the market was closed at <see cref="AsOf"/> (the field is non-nullable on the wire — <see cref="ZeroDteDecay.ThetaPerHourRemaining"/> is what becomes <c>null</c> in that case).</summary>
    [JsonPropertyName("time_to_close_hours")]
    public double? TimeToCloseHours { get; set; }

    /// <summary>Percent of the regular trading day <b>elapsed</b> at the requested minute (0 = at the open, 100 = at the close). Range 0–100.</summary>
    [JsonPropertyName("time_to_close_pct")]
    public double? TimeToClosePct { get; set; }

    /// <summary>Dealer-gamma regime classifier (positive vs negative gamma) and distance-to-flip metrics. See <see cref="ZeroDteRegime"/>.</summary>
    [JsonPropertyName("regime")]
    public ZeroDteRegime? Regime { get; set; }

    /// <summary>Net 0DTE GEX/DEX/VEX/CHEX dealer exposures and the 0DTE share of full-chain GEX. See <see cref="ZeroDteExposures"/>.</summary>
    [JsonPropertyName("exposures")]
    public ZeroDteExposures? Exposures { get; set; }

    /// <summary>Implied 1σ move derived from ATM 0DTE straddle and IV — both the full-day and the time-decayed remainder. See <see cref="ZeroDteExpectedMove"/>.</summary>
    [JsonPropertyName("expected_move")]
    public ZeroDteExpectedMove? ExpectedMove { get; set; }

    /// <summary>OI-magnet pin scoring (0-100) with explainable component breakdown. See <see cref="ZeroDtePinRisk"/>.</summary>
    [JsonPropertyName("pin_risk")]
    public ZeroDtePinRisk? PinRisk { get; set; }

    /// <summary>Dealer hedging shares + USD notional projected at ±10bp / ±25bp / ±0.5% / ±1% spot moves, plus local convexity. See <see cref="ZeroDteHedging"/>.</summary>
    [JsonPropertyName("hedging")]
    public ZeroDteHedging? Hedging { get; set; }

    /// <summary>Theta and charm decay metrics — including theta-per-remaining-hour and the 0DTE/7DTE gamma acceleration ratio. See <see cref="ZeroDteDecay"/>.</summary>
    [JsonPropertyName("decay")]
    public ZeroDteDecay? Decay { get; set; }

    /// <summary>Volatility-term-structure context: 0DTE vs 7DTE ATM IV ratio, VIX, and net vanna exposure. See <see cref="ZeroDteVolContext"/>.</summary>
    [JsonPropertyName("vol_context")]
    public ZeroDteVolContext? VolContext { get; set; }

    /// <summary>Intraday flow snapshot — volume/OI splits, P/C ratios, ATM concentration, top-3 strike concentration. On historical, the <c>*_volume</c> fields are <c>0</c> placeholders; OI and OI-derived ratios are populated. See <see cref="ZeroDteFlow"/>.</summary>
    [JsonPropertyName("flow")]
    public ZeroDteFlow? Flow { get; set; }

    /// <summary>Key levels — call wall, put wall, OI peak, max-positive/negative gamma strikes, and a level-cluster pin-setup score. See <see cref="ZeroDteLevels"/>.</summary>
    [JsonPropertyName("levels")]
    public ZeroDteLevels? Levels { get; set; }

    /// <summary>Liquidity quality — ATM spread, OI-weighted spread, and a 0-100 execution score. See <see cref="ZeroDteLiquidity"/>.</summary>
    [JsonPropertyName("liquidity")]
    public ZeroDteLiquidity? Liquidity { get; set; }

    /// <summary>Snapshot freshness, contract count, and data-quality / greek-smoothness scores. See <see cref="ZeroDteMetadata"/>.</summary>
    [JsonPropertyName("metadata")]
    public ZeroDteMetadata? Metadata { get; set; }

    /// <summary>Per-strike grid: GEX/DEX/VEX/CHEX, OI, volume (0 on historical), share %, greeks, mid quotes, and bid-ask spreads. See <see cref="ZeroDteStrike"/>.</summary>
    [JsonPropertyName("strikes")]
    public List<ZeroDteStrike>? Strikes { get; set; }

    /// <summary>Optional — only present near close (&lt;5 min) when greeks may be unstable.</summary>
    [JsonPropertyName("warnings")]
    public List<string>? Warnings { get; set; }

    // ── No-0DTE fallback ───────────────────────────────────────────────────

    /// <summary><c>true</c> when no 0DTE expired on the requested session (weekends, holidays, or symbols without daily expiries on that date). All analytics fields are <c>null</c> in this case.</summary>
    [JsonPropertyName("no_zero_dte")]
    public bool? NoZeroDte { get; set; }

    /// <summary>Human-readable explanation populated alongside <see cref="NoZeroDte"/>. Safe to surface verbatim.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>The next available 0DTE expiry date (<c>"yyyy-MM-dd"</c>) after the requested session when none was listed for that session.</summary>
    [JsonPropertyName("next_zero_dte_expiry")]
    public string? NextZeroDteExpiry { get; set; }
}

/// <summary>
/// Dealer-gamma regime classifier for the 0DTE chain.
///
/// <para>The regime is determined by the position of spot relative to the
/// 0DTE gamma flip strike. In <c>positive_gamma</c> regimes dealer hedging
/// is mean-reverting (sells rallies, buys dips → vol-suppressing). In
/// <c>negative_gamma</c> regimes dealer hedging is trend-amplifying (buys
/// rallies, sells dips → vol-amplifying, classic "short-gamma trap").</para>
/// </summary>
public sealed class ZeroDteRegime
{
    /// <summary><c>"positive_gamma"</c> or <c>"negative_gamma"</c> — the dealer-gamma regime classifier derived from spot vs <see cref="GammaFlip"/>.</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>Plain-English narrative describing the regime and its expected hedging dynamics. Safe to surface verbatim.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>0DTE gamma-flip strike — where net dealer gamma crosses zero. Spot above flip = positive-gamma regime, below flip = negative-gamma regime.</summary>
    [JsonPropertyName("gamma_flip")]
    public double? GammaFlip { get; set; }

    /// <summary><c>"above"</c> or <c>"below"</c> — spot's position relative to <see cref="GammaFlip"/>.</summary>
    [JsonPropertyName("spot_vs_flip")]
    public string? SpotVsFlip { get; set; }

    /// <summary>Signed percent distance from spot to flip: <c>(spot - gamma_flip) / spot * 100</c>.</summary>
    [JsonPropertyName("spot_to_flip_pct")]
    public double? SpotToFlipPct { get; set; }

    /// <summary>Absolute dollar distance from spot to <see cref="GammaFlip"/>.</summary>
    [JsonPropertyName("distance_to_flip_dollars")]
    public double? DistanceToFlipDollars { get; set; }

    /// <summary>
    /// 1σ-normalized distance from spot to flip — the most actionable measure of regime fragility.
    ///
    /// <para>Computed as <c>distance_to_flip_dollars / (spot × ATM_IV × √(t_remain))</c>
    /// during market hours, falling back to a full-day 1σ when the market is
    /// closed. A value &lt; 1.0 means the flip strike is well within a single
    /// expected 1σ move — i.e. a regime change (and the cascading dealer
    /// hedging behavior shift that comes with it) is plausibly within the
    /// day's range. Values &gt; 2.0 indicate a structurally entrenched regime.</para>
    /// </summary>
    [JsonPropertyName("distance_to_flip_sigmas")]
    public double? DistanceToFlipSigmas { get; set; }
}

/// <summary>
/// Net dealer exposures for the 0DTE chain.
///
/// <para>GEX = gamma × OI × spot² × 100 × multiplier (dollars per 1% spot
/// move squared). DEX, VEX, CHEX are the analogous net delta, vega, and
/// charm exposures. <see cref="PctOfTotalGex"/> tells you how much of the
/// full-chain dealer gamma was concentrated in the requested session's
/// 0DTE expiry.</para>
/// </summary>
public sealed class ZeroDteExposures
{
    /// <summary>Net 0DTE dealer gamma exposure (dollars per 1% spot move squared). Positive = dealers long gamma → vol-suppressing hedging. Negative = dealers short gamma → vol-amplifying hedging.</summary>
    [JsonPropertyName("net_gex")]
    public double? NetGex { get; set; }

    /// <summary>Net 0DTE dealer delta exposure (dollar-delta units). Positive = dealers net long stock-equivalent.</summary>
    [JsonPropertyName("net_dex")]
    public double? NetDex { get; set; }

    /// <summary>Net 0DTE dealer vega exposure (dollars per 1 vol point). Drives vanna effects when IV moves.</summary>
    [JsonPropertyName("net_vex")]
    public double? NetVex { get; set; }

    /// <summary>Net 0DTE dealer charm exposure — the rate of delta decay through time. Drives the late-day pinning bias.</summary>
    [JsonPropertyName("net_chex")]
    public double? NetChex { get; set; }

    /// <summary>0DTE GEX as a percentage of full-chain GEX. <c>&gt;50%</c> = today's expiry dominated intraday dealer hedging at the requested minute.</summary>
    [JsonPropertyName("pct_of_total_gex")]
    public double? PctOfTotalGex { get; set; }

    /// <summary>Net dealer GEX across the entire option chain (all expiries) — the denominator for <see cref="PctOfTotalGex"/>.</summary>
    [JsonPropertyName("total_chain_net_gex")]
    public double? TotalChainNetGex { get; set; }
}

/// <summary>
/// Implied move expectations from ATM 0DTE straddle and IV.
///
/// <para>The full-day 1σ values describe what the market priced at the
/// session's open; the <c>remaining_*</c> values shrink in real-time as
/// the close approaches (intraday vol scales with √(t_remain)). The 0DTE
/// straddle mid is the most direct market-implied 1σ proxy available.</para>
/// </summary>
public sealed class ZeroDteExpectedMove
{
    /// <summary>Full-day implied 1σ dollar move at the open: <c>spot × ATM_IV × √(1/252)</c>.</summary>
    [JsonPropertyName("implied_1sd_dollars")]
    public double? Implied1SdDollars { get; set; }

    /// <summary>Full-day implied 1σ move as a percentage of spot.</summary>
    [JsonPropertyName("implied_1sd_pct")]
    public double? Implied1SdPct { get; set; }

    /// <summary>
    /// Time-decayed 1σ dollar move for the remainder of the trading day,
    /// computed for the requested minute. Scales as
    /// <c>spot × ATM_IV × √(t_remain)</c>. <c>null</c> when the market
    /// was closed at the requested minute.
    /// </summary>
    [JsonPropertyName("remaining_1sd_dollars")]
    public double? Remaining1SdDollars { get; set; }

    /// <summary>Time-decayed remaining 1σ move as a percentage of spot. See <see cref="Remaining1SdDollars"/>.</summary>
    [JsonPropertyName("remaining_1sd_pct")]
    public double? Remaining1SdPct { get; set; }

    /// <summary>Spot + <see cref="Implied1SdDollars"/> — the upper bound of the full-day 1σ band.</summary>
    [JsonPropertyName("upper_bound")]
    public double? UpperBound { get; set; }

    /// <summary>Spot − <see cref="Implied1SdDollars"/> — the lower bound of the full-day 1σ band.</summary>
    [JsonPropertyName("lower_bound")]
    public double? LowerBound { get; set; }

    /// <summary>ATM 0DTE straddle mid (call_mid + put_mid). The most direct market-implied 1σ move — useful as an alternative to IV-derived expectations.</summary>
    [JsonPropertyName("straddle_price")]
    public double? StraddlePrice { get; set; }

    /// <summary>ATM 0DTE implied volatility (annualised %, e.g. 18.5 = 18.5%).</summary>
    [JsonPropertyName("atm_iv")]
    public double? AtmIv { get; set; }
}

/// <summary>
/// Component breakdown for <see cref="ZeroDtePinRisk.PinScore"/>.
///
/// <para>Each component is the unscaled 0-N contribution to the composite —
/// useful when you want to explain the score or filter on a specific input
/// (e.g. a high pin score driven solely by time remaining vs one driven by
/// OI concentration).</para>
/// </summary>
public sealed class ZeroDtePinComponents
{
    /// <summary>OI-concentration contribution. Higher when OI is concentrated at the magnet strike (sticky positioning).</summary>
    [JsonPropertyName("oi_score")]
    public int? OiScore { get; set; }

    /// <summary>Magnet-proximity contribution. Higher when spot is close to the magnet strike.</summary>
    [JsonPropertyName("proximity_score")]
    public int? ProximityScore { get; set; }

    /// <summary>Time-remaining contribution. Higher as the close approaches and gamma compresses.</summary>
    [JsonPropertyName("time_score")]
    public int? TimeScore { get; set; }

    /// <summary>Gamma-magnitude contribution. Higher when net dealer gamma at the magnet is large in absolute terms.</summary>
    [JsonPropertyName("gamma_score")]
    public int? GammaScore { get; set; }
}

/// <summary>
/// 0DTE pin-risk scoring — likelihood that spot was drawn to and pinned at
/// the OI magnet strike into the close of the requested session.
///
/// <para>The <see cref="PinScore"/> is a 0-100 composite weighted as:
/// OI concentration (30%), magnet proximity (25%), time remaining (25%),
/// gamma magnitude (20%). Most actionable in the final 90 minutes of
/// the session.</para>
/// </summary>
public sealed class ZeroDtePinRisk
{
    /// <summary>The strike currently acting as the dealer-gamma magnet — the most likely pin candidate.</summary>
    [JsonPropertyName("magnet_strike")]
    public double? MagnetStrike { get; set; }

    /// <summary>Net dealer GEX at <see cref="MagnetStrike"/>. Larger absolute values = stronger gravitational pull.</summary>
    [JsonPropertyName("magnet_gex")]
    public double? MagnetGex { get; set; }

    /// <summary>Signed percent distance from spot to the magnet strike.</summary>
    [JsonPropertyName("distance_to_magnet_pct")]
    public double? DistanceToMagnetPct { get; set; }

    /// <summary>
    /// 0-100 composite pin score. Inputs: OI concentration (30%), magnet
    /// proximity (25%), time remaining (25%), gamma magnitude (20%).
    /// Above 70 = high-conviction pin setup; below 30 = pin unlikely.
    /// </summary>
    [JsonPropertyName("pin_score")]
    public int? PinScore { get; set; }

    /// <summary>Per-component breakdown of <see cref="PinScore"/>. See <see cref="ZeroDtePinComponents"/>.</summary>
    [JsonPropertyName("components")]
    public ZeroDtePinComponents? Components { get; set; }

    /// <summary>0DTE max-pain strike — where total option-holder intrinsic value is minimized. Often coincides with or sits adjacent to <see cref="MagnetStrike"/>.</summary>
    [JsonPropertyName("max_pain")]
    public double? MaxPain { get; set; }

    /// <summary>Share of total OI concentrated in the top 3 0DTE strikes (%). Higher = stickier positioning, stronger pin candidates.</summary>
    [JsonPropertyName("oi_concentration_top3_pct")]
    public double? OiConcentrationTop3Pct { get; set; }

    /// <summary>Plain-English explanation of the pin setup. Safe to surface verbatim.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// One bucket of the dealer-hedging projection — shares dealers must trade,
/// the direction of those trades, and the implied USD notional, evaluated
/// at a specific spot shock (±10bp / ±25bp / ±0.5% / ±1%).
/// </summary>
public sealed class ZeroDteHedgingBucket
{
    /// <summary>Estimated number of underlying shares dealers must trade to remain delta-neutral after the spot move.</summary>
    [JsonPropertyName("dealer_shares_to_trade")]
    public double? DealerSharesToTrade { get; set; }

    /// <summary><c>"buy"</c> or <c>"sell"</c> — direction of the dealer hedge. In negative-gamma regimes, dealers buy on rallies and sell on dips (trend-amplifying).</summary>
    [JsonPropertyName("direction")]
    public string? Direction { get; set; }

    /// <summary>USD notional of the projected hedge: <c>dealer_shares_to_trade × spot_at_shock</c>.</summary>
    [JsonPropertyName("notional_usd")]
    public double? NotionalUsd { get; set; }
}

/// <summary>
/// Dealer hedging projections for the 0DTE chain at multiple spot shocks,
/// plus the local GEX convexity around spot.
///
/// <para>Each bucket projects the shares (and USD notional) dealers would
/// have had to trade to remain delta-neutral after a hypothetical spot
/// move of the stated size. Reading the asymmetry between up- and
/// down-shocks reveals whether dealer flow would have reinforced or
/// fought a directional move at the requested minute.</para>
/// </summary>
public sealed class ZeroDteHedging
{
    /// <summary>Dealer hedging at +0.10% spot. See <see cref="ZeroDteHedgingBucket"/>.</summary>
    [JsonPropertyName("spot_up_10bp")]
    public ZeroDteHedgingBucket? SpotUp10Bp { get; set; }

    /// <summary>Dealer hedging at -0.10% spot. See <see cref="ZeroDteHedgingBucket"/>.</summary>
    [JsonPropertyName("spot_down_10bp")]
    public ZeroDteHedgingBucket? SpotDown10Bp { get; set; }

    /// <summary>Dealer hedging at +0.25% spot. See <see cref="ZeroDteHedgingBucket"/>.</summary>
    [JsonPropertyName("spot_up_25bp")]
    public ZeroDteHedgingBucket? SpotUp25Bp { get; set; }

    /// <summary>Dealer hedging at -0.25% spot. See <see cref="ZeroDteHedgingBucket"/>.</summary>
    [JsonPropertyName("spot_down_25bp")]
    public ZeroDteHedgingBucket? SpotDown25Bp { get; set; }

    /// <summary>Dealer hedging at +0.50% spot. See <see cref="ZeroDteHedgingBucket"/>.</summary>
    [JsonPropertyName("spot_up_half_pct")]
    public ZeroDteHedgingBucket? SpotUpHalfPct { get; set; }

    /// <summary>Dealer hedging at -0.50% spot. See <see cref="ZeroDteHedgingBucket"/>.</summary>
    [JsonPropertyName("spot_down_half_pct")]
    public ZeroDteHedgingBucket? SpotDownHalfPct { get; set; }

    /// <summary>Dealer hedging at +1.00% spot. See <see cref="ZeroDteHedgingBucket"/>.</summary>
    [JsonPropertyName("spot_up_1pct")]
    public ZeroDteHedgingBucket? SpotUp1Pct { get; set; }

    /// <summary>Dealer hedging at -1.00% spot. See <see cref="ZeroDteHedgingBucket"/>.</summary>
    [JsonPropertyName("spot_down_1pct")]
    public ZeroDteHedgingBucket? SpotDown1Pct { get; set; }

    /// <summary>
    /// Local GEX convexity around spot, in GEX-units per dollar².
    ///
    /// <para>Computed as the second finite-difference of net GEX across the
    /// three strikes nearest spot. The sign carries the structural meaning:
    /// a <b>negative GEX trough</b> at ATM produces a <b>positive</b>
    /// convexity (the classic short-gamma trap — small moves accelerate as
    /// dealers chase delta), while a <b>positive GEX peak</b> at ATM
    /// produces a <b>negative</b> convexity (dampening, vol-suppressing
    /// pin attractor). Read alongside the regime label for full context.</para>
    /// </summary>
    [JsonPropertyName("convexity_at_spot")]
    public double? ConvexityAtSpot { get; set; }
}

/// <summary>
/// Theta and charm decay metrics for the 0DTE chain.
///
/// <para>Both theta-burn and gamma-acceleration grow super-linearly into
/// the close as the time-to-expiry denominator shrinks toward zero. The
/// <see cref="GammaAcceleration"/> ratio quantifies how much more
/// sensitive the 0DTE chain was to spot moves than the next-week chain
/// at the requested minute.</para>
/// </summary>
public sealed class ZeroDteDecay
{
    /// <summary>Net 0DTE theta exposure in dollars (per day). Negative for option buyers, positive for dealers who are net short premium.</summary>
    [JsonPropertyName("net_theta_dollars")]
    public double? NetThetaDollars { get; set; }

    /// <summary>
    /// Theta burn per remaining hour: <c>net_theta_dollars / time_to_close_hours</c>.
    /// Accelerates as the denominator shrinks — late-day burn rates are
    /// dramatically higher than the morning equivalent. <c>null</c> when
    /// the market was closed at the requested minute (no remaining intraday
    /// horizon).
    /// </summary>
    [JsonPropertyName("theta_per_hour_remaining")]
    public double? ThetaPerHourRemaining { get; set; }

    /// <summary>Charm regime label — describes how delta is bleeding through time across the chain (e.g. <c>"call_decay"</c>, <c>"put_decay"</c>, <c>"balanced"</c>).</summary>
    [JsonPropertyName("charm_regime")]
    public string? CharmRegime { get; set; }

    /// <summary>Plain-English explanation of the charm regime. Safe to surface verbatim.</summary>
    [JsonPropertyName("charm_description")]
    public string? CharmDescription { get; set; }

    /// <summary>
    /// 0DTE ATM gamma divided by 7DTE ATM gamma — quantifies how much more
    /// gamma-sensitive the requested session's expiry was relative to next
    /// week's at the requested minute.
    ///
    /// <para>Typically ranges 2-5× through most of the session and can
    /// spike to 10×+ in the final 30 minutes as 0DTE gamma compresses
    /// toward a delta function. High values flag conditions where small
    /// spot moves drove outsized dealer hedging activity.</para>
    /// </summary>
    [JsonPropertyName("gamma_acceleration")]
    public double? GammaAcceleration { get; set; }

    /// <summary>Plain-English narrative summarising the decay environment. Safe to surface verbatim.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Volatility-term-structure context for the 0DTE chain.
///
/// <para>Compares 0DTE IV to the 7DTE benchmark and the broader VIX
/// regime, plus a net vanna exposure read for IV-driven dealer hedging
/// flow. Useful for sizing whether 0DTE premium was rich or cheap relative
/// to the term structure at the requested minute.</para>
/// </summary>
public sealed class ZeroDteVolContext
{
    /// <summary>0DTE ATM implied volatility (annualised %).</summary>
    [JsonPropertyName("zero_dte_atm_iv")]
    public double? ZeroDteAtmIv { get; set; }

    /// <summary>7DTE ATM implied volatility (annualised %) — the term-structure benchmark.</summary>
    [JsonPropertyName("seven_dte_atm_iv")]
    public double? SevenDteAtmIv { get; set; }

    /// <summary>
    /// 0DTE ATM IV divided by 7DTE ATM IV — the most actionable
    /// term-structure signal at this tenor.
    ///
    /// <para>Values <c>&lt; 1.0</c> indicate 0DTE was "cheap" relative to
    /// the next-week tenor (favors long-gamma 0DTE structures). Values
    /// <c>&gt; 1.0</c> indicate event premium — the market was paying up
    /// for that session specifically (FOMC, CPI, OPEX, earnings spillover).
    /// Around 1.0 is the typical no-event baseline.</para>
    /// </summary>
    [JsonPropertyName("iv_ratio_0dte_7dte")]
    public double? IvRatio0Dte7Dte { get; set; }

    /// <summary>CBOE VIX index level — broad-market volatility regime at the requested minute.</summary>
    [JsonPropertyName("vix")]
    public double? Vix { get; set; }

    /// <summary>Net 0DTE dealer vanna exposure — the cross-derivative ∂Δ/∂σ that drives IV-driven hedging flow.</summary>
    [JsonPropertyName("vanna_exposure")]
    public double? VannaExposure { get; set; }

    /// <summary>Plain-English interpretation of the vanna setup (e.g. supportive, cascade-risk). Safe to surface verbatim.</summary>
    [JsonPropertyName("vanna_interpretation")]
    public string? VannaInterpretation { get; set; }

    /// <summary>Plain-English narrative summarising the vol context. Safe to surface verbatim.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// Intraday flow snapshot for the 0DTE chain.
///
/// <para>Volume vs OI splits, P/C ratios, and concentration metrics —
/// useful for distinguishing positioning-driven sessions (low V/OI, OI
/// dominates) from day-trading-dominated sessions (high V/OI, intraday
/// flow dwarfs overnight positioning).</para>
///
/// <para><b>Historical-mode caveat:</b> the <c>*_volume</c> fields and
/// derived ratios (<see cref="PcRatioVolume"/>, <see cref="VolumeToOiRatio"/>,
/// <see cref="AtmVolumeSharePct"/>, <see cref="Top3StrikeVolumePct"/>) are
/// <c>0</c> placeholders or <c>null</c> on historical — the minute table
/// doesn't carry intraday volume. The OI fields are fully populated.</para>
/// </summary>
public sealed class ZeroDteFlow
{
    /// <summary>Total 0DTE contract volume traded across all strikes (calls + puts). <c>0</c> on historical.</summary>
    [JsonPropertyName("total_volume")]
    public long? TotalVolume { get; set; }

    /// <summary>Total 0DTE call volume. <c>0</c> on historical.</summary>
    [JsonPropertyName("call_volume")]
    public long? CallVolume { get; set; }

    /// <summary>Total 0DTE put volume. <c>0</c> on historical.</summary>
    [JsonPropertyName("put_volume")]
    public long? PutVolume { get; set; }

    /// <summary>Signed net flow: <c>call_volume - put_volume</c>. Positive values indicate bullish intraday flow; negative values indicate bearish flow. <c>0</c> on historical.</summary>
    [JsonPropertyName("net_call_minus_put_volume")]
    public long? NetCallMinusPutVolume { get; set; }

    /// <summary>Total 0DTE open interest (calls + puts).</summary>
    [JsonPropertyName("total_oi")]
    public long? TotalOi { get; set; }

    /// <summary>Total 0DTE call open interest.</summary>
    [JsonPropertyName("call_oi")]
    public long? CallOi { get; set; }

    /// <summary>Total 0DTE put open interest.</summary>
    [JsonPropertyName("put_oi")]
    public long? PutOi { get; set; }

    /// <summary>Put volume divided by call volume. <c>&gt;1.0</c> = put-heavy intraday flow. <c>null</c> on historical (volumes are 0).</summary>
    [JsonPropertyName("pc_ratio_volume")]
    public double? PcRatioVolume { get; set; }

    /// <summary>Put OI divided by call OI. <c>&gt;1.0</c> = put-heavy positioning.</summary>
    [JsonPropertyName("pc_ratio_oi")]
    public double? PcRatioOi { get; set; }

    /// <summary>Total volume divided by total OI. <c>&gt;1.0</c> = heavy day-trading session — intraday flow exceeds overnight positioning. <c>null</c> on historical.</summary>
    [JsonPropertyName("volume_to_oi_ratio")]
    public double? VolumeToOiRatio { get; set; }

    /// <summary>Share of total volume traded within ±0.5% of spot (%). High values = trader interest concentrated near current price (gamma-trap setups). <c>null</c> on historical.</summary>
    [JsonPropertyName("atm_volume_share_pct")]
    public double? AtmVolumeSharePct { get; set; }

    /// <summary>Share of total volume concentrated in the 3 most-active strikes (%). High values = focused, often pin-targeting flow. <c>null</c> on historical.</summary>
    [JsonPropertyName("top3_strike_volume_pct")]
    public double? Top3StrikeVolumePct { get; set; }
}

/// <summary>
/// Key 0DTE levels — the gamma walls, magnets, and structural strikes
/// that drove intraday tape behavior at the requested minute.
///
/// <para>Walls are the strikes with the largest call-side / put-side GEX
/// (resistance and support). The level-cluster score measures how
/// tightly the day's structural strikes are packed relative to the 1σ
/// expected move — high values indicate pin-setup conditions.</para>
/// </summary>
public sealed class ZeroDteLevels
{
    /// <summary>Strike with the largest absolute call-side GEX — dealer-side resistance.</summary>
    [JsonPropertyName("call_wall")]
    public double? CallWall { get; set; }

    /// <summary>GEX magnitude at the call wall.</summary>
    [JsonPropertyName("call_wall_gex")]
    public double? CallWallGex { get; set; }

    /// <summary>Concentration ratio: <c>call_wall_gex / total_call_side_GEX_magnitude</c>. <c>1.0</c> = single-strike concentration (very strong wall); <c>&lt;0.2</c> = weak wall, GEX is diffused.</summary>
    [JsonPropertyName("call_wall_strength")]
    public double? CallWallStrength { get; set; }

    /// <summary>Signed percent distance from spot to the call wall.</summary>
    [JsonPropertyName("distance_to_call_wall_pct")]
    public double? DistanceToCallWallPct { get; set; }

    /// <summary>Strike with the largest absolute put-side GEX — dealer-side support.</summary>
    [JsonPropertyName("put_wall")]
    public double? PutWall { get; set; }

    /// <summary>GEX magnitude at the put wall.</summary>
    [JsonPropertyName("put_wall_gex")]
    public double? PutWallGex { get; set; }

    /// <summary>Concentration ratio: <c>put_wall_gex / total_put_side_GEX_magnitude</c>. <c>1.0</c> = single-strike concentration; <c>&lt;0.2</c> = weak wall.</summary>
    [JsonPropertyName("put_wall_strength")]
    public double? PutWallStrength { get; set; }

    /// <summary>Signed percent distance from spot to the put wall.</summary>
    [JsonPropertyName("distance_to_put_wall_pct")]
    public double? DistanceToPutWallPct { get; set; }

    /// <summary>Absolute dollar distance from spot to <see cref="ZeroDtePinRisk.MagnetStrike"/>.</summary>
    [JsonPropertyName("distance_to_magnet_dollars")]
    public double? DistanceToMagnetDollars { get; set; }

    /// <summary>Strike with the highest combined OI (calls + puts) in the 0DTE chain.</summary>
    [JsonPropertyName("highest_oi_strike")]
    public double? HighestOiStrike { get; set; }

    /// <summary>Combined OI at <see cref="HighestOiStrike"/>.</summary>
    [JsonPropertyName("highest_oi_total")]
    public long? HighestOiTotal { get; set; }

    /// <summary>Strike with the most positive net dealer GEX (largest local "gamma peak" — vol-suppressing attractor).</summary>
    [JsonPropertyName("max_positive_gamma")]
    public double? MaxPositiveGamma { get; set; }

    /// <summary>Strike with the most negative net dealer GEX (largest local "gamma trough" — short-gamma trap epicenter).</summary>
    [JsonPropertyName("max_negative_gamma")]
    public double? MaxNegativeGamma { get; set; }

    /// <summary>
    /// 0-100 score measuring how tightly the day's structural strikes
    /// (gamma flip, OI magnet, max pain, highest-OI strike, call wall,
    /// put wall) are clustered relative to the 1σ expected move.
    ///
    /// <para>High values (&gt;70) indicate a tight cluster — a classic
    /// pin setup where dealer flow, OI magnetism, and max-pain gravity
    /// all reinforce each other inside a narrow band. Low values
    /// (&lt;30) indicate the structural strikes are scattered across
    /// (or beyond) the expected-move band, so no single strike has
    /// gravitational dominance.</para>
    /// </summary>
    [JsonPropertyName("level_cluster_score")]
    public int? LevelClusterScore { get; set; }
}

/// <summary>
/// Liquidity quality scoring for the 0DTE chain.
///
/// <para>Useful for filtering signals to only act when the chain was
/// liquid enough to execute on at the requested minute — wide spreads
/// invalidate most 0DTE strategy edges.</para>
/// </summary>
public sealed class ZeroDteLiquidity
{
    /// <summary>Average bid-ask spread (%) of the call+put pair at the strike nearest spot.</summary>
    [JsonPropertyName("atm_spread_pct")]
    public double? AtmSpreadPct { get; set; }

    /// <summary>OI-weighted average bid-ask spread (%) within the requested <c>strike_range</c> window — the spread the typical executable contract sees.</summary>
    [JsonPropertyName("weighted_spread_pct")]
    public double? WeightedSpreadPct { get; set; }

    /// <summary>0-100 composite execution score. Heuristic weighting: spread quality (70%) + ATM OI depth (30%). Higher = easier to execute strategies on this chain.</summary>
    [JsonPropertyName("execution_score")]
    public int? ExecutionScore { get; set; }
}

/// <summary>
/// Snapshot quality and provenance metadata.
///
/// <para>Always read these before acting on the analytics — stale
/// snapshots, missing greeks, or a degraded data-quality score should
/// downgrade or veto otherwise-strong signals.</para>
/// </summary>
public sealed class ZeroDteMetadata
{
    /// <summary>Seconds since the most recent contract update fed this snapshot. Staleness check — values much above the polling cadence indicate a degraded feed.</summary>
    [JsonPropertyName("snapshot_age_seconds")]
    public double? SnapshotAgeSeconds { get; set; }

    /// <summary>Number of 0DTE contracts (calls + puts across all strikes) feeding this response.</summary>
    [JsonPropertyName("chain_contract_count")]
    public int? ChainContractCount { get; set; }

    /// <summary>0-100 data-quality score. Penalizes NaN greeks, missing IV, and stale snapshots — values below 70 should be treated as a soft veto on actionable signals.</summary>
    [JsonPropertyName("data_quality_score")]
    public int? DataQualityScore { get; set; }

    /// <summary>
    /// 0-100 greek-smoothness score — measures how well-behaved the IV
    /// surface is across consecutive strikes.
    ///
    /// <para>Computed from the mean absolute consecutive-strike IV
    /// difference (lower diff → smoother surface → higher score). Low
    /// values flag potential IV-fitting noise that propagates into the
    /// downstream greeks (gamma, vanna, charm), which can produce
    /// spurious signals; treat low scores as a reason to widen
    /// confirmation thresholds.</para>
    /// </summary>
    [JsonPropertyName("greek_smoothness_score")]
    public int? GreekSmoothnessScore { get; set; }
}

/// <summary>
/// One row of the per-strike 0DTE grid.
///
/// <para>All exposure fields (GEX/DEX/VEX/CHEX) are dealer-perspective
/// (sell-side facing). Greeks are per-share Black-Scholes values.
/// <c>net_*</c> fields combine the call and put legs at the strike.
/// On historical, <see cref="CallVolume"/> and <see cref="PutVolume"/>
/// are <c>0</c> placeholders.</para>
/// </summary>
public sealed class ZeroDteStrike
{
    /// <summary>Strike price.</summary>
    [JsonPropertyName("strike")]
    public double Strike { get; set; }

    /// <summary>Signed percent distance from spot to this strike: <c>(strike - spot) / spot * 100</c>.</summary>
    [JsonPropertyName("distance_from_spot_pct")]
    public double? DistanceFromSpotPct { get; set; }

    /// <summary>OCC option symbol for the call leg at this strike.</summary>
    [JsonPropertyName("call_symbol")]
    public string? CallSymbol { get; set; }

    /// <summary>OCC option symbol for the put leg at this strike.</summary>
    [JsonPropertyName("put_symbol")]
    public string? PutSymbol { get; set; }

    /// <summary>Dealer GEX contribution from the call leg at this strike.</summary>
    [JsonPropertyName("call_gex")]
    public double? CallGex { get; set; }

    /// <summary>Dealer GEX contribution from the put leg at this strike.</summary>
    [JsonPropertyName("put_gex")]
    public double? PutGex { get; set; }

    /// <summary>Net dealer GEX at this strike: <c>call_gex + put_gex</c>.</summary>
    [JsonPropertyName("net_gex")]
    public double? NetGex { get; set; }

    /// <summary>Dealer DEX contribution from the call leg at this strike.</summary>
    [JsonPropertyName("call_dex")]
    public double? CallDex { get; set; }

    /// <summary>Dealer DEX contribution from the put leg at this strike.</summary>
    [JsonPropertyName("put_dex")]
    public double? PutDex { get; set; }

    /// <summary>Net dealer DEX at this strike: <c>call_dex + put_dex</c>.</summary>
    [JsonPropertyName("net_dex")]
    public double? NetDex { get; set; }

    /// <summary>Net dealer VEX at this strike (vega exposure, dollars per 1 vol point).</summary>
    [JsonPropertyName("net_vex")]
    public double? NetVex { get; set; }

    /// <summary>Net dealer CHEX at this strike (charm exposure — delta decay through time).</summary>
    [JsonPropertyName("net_chex")]
    public double? NetChex { get; set; }

    /// <summary>Call open interest at this strike.</summary>
    [JsonPropertyName("call_oi")]
    public long? CallOi { get; set; }

    /// <summary>Put open interest at this strike.</summary>
    [JsonPropertyName("put_oi")]
    public long? PutOi { get; set; }

    /// <summary>Call volume at this strike. <c>0</c> on historical (intraday volume is not retained).</summary>
    [JsonPropertyName("call_volume")]
    public long? CallVolume { get; set; }

    /// <summary>Put volume at this strike. <c>0</c> on historical (intraday volume is not retained).</summary>
    [JsonPropertyName("put_volume")]
    public long? PutVolume { get; set; }

    /// <summary>Share of total chain GEX magnitude attributable to this strike (%).</summary>
    [JsonPropertyName("gex_share_pct")]
    public double? GexSharePct { get; set; }

    /// <summary>Share of total chain OI at this strike (%).</summary>
    [JsonPropertyName("oi_share_pct")]
    public double? OiSharePct { get; set; }

    /// <summary>Share of total chain volume at this strike (%). <c>0</c> on historical.</summary>
    [JsonPropertyName("volume_share_pct")]
    public double? VolumeSharePct { get; set; }

    /// <summary>Implied volatility of the call leg (annualised %).</summary>
    [JsonPropertyName("call_iv")]
    public double? CallIv { get; set; }

    /// <summary>Implied volatility of the put leg (annualised %).</summary>
    [JsonPropertyName("put_iv")]
    public double? PutIv { get; set; }

    /// <summary>Black-Scholes delta of the call leg (per share, 0 to 1).</summary>
    [JsonPropertyName("call_delta")]
    public double? CallDelta { get; set; }

    /// <summary>Black-Scholes delta of the put leg (per share, -1 to 0).</summary>
    [JsonPropertyName("put_delta")]
    public double? PutDelta { get; set; }

    /// <summary>Black-Scholes gamma of the call leg (per share).</summary>
    [JsonPropertyName("call_gamma")]
    public double? CallGamma { get; set; }

    /// <summary>Black-Scholes gamma of the put leg (per share).</summary>
    [JsonPropertyName("put_gamma")]
    public double? PutGamma { get; set; }

    /// <summary>Black-Scholes theta of the call leg (per share, dollars per day).</summary>
    [JsonPropertyName("call_theta")]
    public double? CallTheta { get; set; }

    /// <summary>Black-Scholes theta of the put leg (per share, dollars per day).</summary>
    [JsonPropertyName("put_theta")]
    public double? PutTheta { get; set; }

    /// <summary>Mid price of the call leg ((bid + ask) / 2).</summary>
    [JsonPropertyName("call_mid")]
    public double? CallMid { get; set; }

    /// <summary>Mid price of the put leg ((bid + ask) / 2).</summary>
    [JsonPropertyName("put_mid")]
    public double? PutMid { get; set; }

    /// <summary>Bid-ask spread of the call leg as a percentage of mid.</summary>
    [JsonPropertyName("call_spread_pct")]
    public double? CallSpreadPct { get; set; }

    /// <summary>Bid-ask spread of the put leg as a percentage of mid.</summary>
    [JsonPropertyName("put_spread_pct")]
    public double? PutSpreadPct { get; set; }
}
