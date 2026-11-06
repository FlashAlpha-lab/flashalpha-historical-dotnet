using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// Typed response for <c>GET /v1/strategies/sell-window/{symbol}?at=...</c>: should you sell options
/// today? One answer per session for SPY, QQQ and IWM, built from more than 5 million backtests.
///
/// <para>Own response shape, not the strategy decision envelope. <see cref="Spot"/>,
/// <see cref="QuotedAt"/> and <see cref="LegsIndicative"/> are set when a window sells, and null
/// otherwise. Docs: https://flashalpha.com/docs/historical-sell-window</para>
/// </summary>
public sealed class SellWindowResponse : FlashAlphaResponse
{
    /// <summary>The symbol, upper case.</summary>
    [JsonPropertyName("symbol")]
    public string? Symbol { get; set; }

    /// <summary><c>yyyy-MM-dd</c>. The trading session the answer applies to.</summary>
    [JsonPropertyName("session")]
    public string? Session { get; set; }

    /// <summary>Underlying mid the strikes were picked against.</summary>
    [JsonPropertyName("spot")]
    public double? Spot { get; set; }

    /// <summary>UTC time of the quotes behind the strikes.</summary>
    [JsonPropertyName("quoted_at")]
    public string? QuotedAt { get; set; }

    /// <summary>True when the quotes are from before that session's 09:35 ET entry: call again at
    /// or after 09:35 ET for the strikes to trade.</summary>
    [JsonPropertyName("legs_indicative")]
    public bool? LegsIndicative { get; set; }

    /// <summary>Always four entries: same day, then about one week, two weeks and one month out.</summary>
    [JsonPropertyName("windows")]
    public List<SellWindowWindow>? Windows { get; set; }
}

/// <summary>One of the four sell-window windows, named by its listed expiry.</summary>
public sealed class SellWindowWindow
{
    /// <summary><c>yyyy-MM-dd</c>. Null when no listed expiry falls in the window's range.</summary>
    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    /// <summary>Calendar days from the session to <see cref="Expiry"/>.</summary>
    [JsonPropertyName("days_to_expiry")]
    public int? DaysToExpiry { get; set; }

    /// <summary><c>sell</c>, <c>skip</c> or <c>unavailable</c>. Treat unavailable like a skip.</summary>
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>Set only when <see cref="Action"/> is <c>sell</c>. Can be empty: a trade whose legs
    /// cannot all be found in the chain is left out.</summary>
    [JsonPropertyName("candidates")]
    public List<SellWindowCandidate>? Candidates { get; set; }
}

/// <summary>One trade to consider on a sell window.</summary>
public sealed class SellWindowCandidate
{
    /// <summary><c>short_put</c>, <c>short_call</c>, <c>short_strangle</c>, <c>short_straddle</c>,
    /// <c>tail_hedged_put</c>, <c>put_credit_spread</c> or <c>put_bwb</c>.</summary>
    [JsonPropertyName("structure")]
    public string? Structure { get; set; }

    /// <summary><c>expiry</c>: hold to expiration. <c>tp50</c>: close at half of the credit, otherwise hold.</summary>
    [JsonPropertyName("exit")]
    public string? Exit { get; set; }

    /// <summary>Null when no option chain could be loaded for the answer.</summary>
    [JsonPropertyName("legs")]
    public List<SellWindowLeg>? Legs { get; set; }

    /// <summary>Net credit per share at mid. Multiply by 100 for one unit in dollars.</summary>
    [JsonPropertyName("mid_credit")]
    public double? MidCredit { get; set; }
}

/// <summary>One contract of a sell-window trade.</summary>
public sealed class SellWindowLeg
{
    /// <summary><c>sell</c> or <c>buy</c>.</summary>
    [JsonPropertyName("side")]
    public string? Side { get; set; }

    /// <summary>Contracts per unit of the trade: 2 for the sold body of <c>put_bwb</c>, otherwise 1.</summary>
    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }

    /// <summary><c>call</c> or <c>put</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Strike price.</summary>
    [JsonPropertyName("strike")]
    public double? Strike { get; set; }

    /// <summary>The contract's own delta when the answer was served (negative for puts).</summary>
    [JsonPropertyName("delta")]
    public double? Delta { get; set; }

    /// <summary>The contract's bid when the answer was served.</summary>
    [JsonPropertyName("bid")]
    public double? Bid { get; set; }

    /// <summary>The contract's ask when the answer was served.</summary>
    [JsonPropertyName("ask")]
    public double? Ask { get; set; }
}
