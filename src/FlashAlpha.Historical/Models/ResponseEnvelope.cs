using System.Text.Json.Serialization;

namespace FlashAlpha.Historical.Models;

/// <summary>
/// When each upstream feed last delivered to the node that served the response.
///
/// <para>On this replay service every feed is <c>null</c>: a replay node reads the archive
/// and consumes no live feed. The object is still returned so the envelope has one shape
/// across the live and historical services, and so a historical response cannot be
/// mistaken for a live one.</para>
///
/// <para>The vintage that matters here is <see cref="ArchiveAsOf"/>, carried alongside it
/// as <c>archive_as_of</c>.</para>
/// </summary>
public class DataAsOf
{
    /// <summary>Which node answered.</summary>
    [JsonPropertyName("node")]
    public string? Node { get; set; }

    /// <summary>Equity and ETF spot quotes.</summary>
    [JsonPropertyName("equity_feed")]
    public string? EquityFeed { get; set; }

    /// <summary>Equity and ETF option quotes.</summary>
    [JsonPropertyName("equity_options_feed")]
    public string? EquityOptionsFeed { get; set; }

    /// <summary>Index spot - SPX, RUT, VIX and the other index roots.</summary>
    [JsonPropertyName("index_feed")]
    public string? IndexFeed { get; set; }

    /// <summary>Index option quotes.</summary>
    [JsonPropertyName("index_options_feed")]
    public string? IndexOptionsFeed { get; set; }

    /// <summary>Futures prices.</summary>
    [JsonPropertyName("futures_feed")]
    public string? FuturesFeed { get; set; }

    /// <summary>Futures option quotes.</summary>
    [JsonPropertyName("futures_options_feed")]
    public string? FuturesOptionsFeed { get; set; }

    /// <summary>Classified options and stock trade tape.</summary>
    [JsonPropertyName("flow_feed")]
    public string? FlowFeed { get; set; }

    /// <summary>Settled open interest.</summary>
    [JsonPropertyName("oi_feed")]
    public string? OiFeed { get; set; }

    /// <summary>VIX, VVIX, SKEW, MOVE, SPX and Fear &amp; Greed.</summary>
    [JsonPropertyName("macro_feed")]
    public string? MacroFeed { get; set; }
}

/// <summary>
/// The vintage of the archive rows actually replayed for the timestamp you requested.
///
/// <para>Same shape as <see cref="DataAsOf"/> - the key order is a contract shared with the
/// live service - but the values describe stored rows rather than live feeds. A property is
/// <c>null</c> when the response did not read that class of data.</para>
///
/// <para>This is what makes an archive gap detectable. Request a moment with no row and the
/// query returns the most recent earlier row; nothing else in the response distinguishes
/// the two. Point-in-time work should read this and drop or flag observations whose inputs
/// precede the requested instant by more than the study tolerates.</para>
///
/// <para><see cref="DataAsOf.OiFeed"/> trailing by a session is correct rather than a gap:
/// settled open interest is published once per session, so the newest figure that existed
/// at any intraday moment is the prior close.</para>
/// </summary>
public sealed class ArchiveAsOf : DataAsOf
{
}

/// <summary>
/// Base for every typed response model. Carries the response envelope the API returns on
/// all successful responses.
/// </summary>
public abstract class FlashAlphaResponse
{
    /// <summary>Identifies the deployment that produced this response.</summary>
    [JsonPropertyName("endpoint_version")]
    public string? EndpointVersion { get; set; }

    /// <summary>Live-feed freshness. All null on this replay service. See <see cref="FlashAlpha.Historical.Models.DataAsOf"/>.</summary>
    [JsonPropertyName("data_as_of")]
    public DataAsOf? DataAsOf { get; set; }

    /// <summary>Vintage of the archive rows actually replayed. See <see cref="FlashAlpha.Historical.Models.ArchiveAsOf"/>.</summary>
    [JsonPropertyName("archive_as_of")]
    public ArchiveAsOf? ArchiveAsOf { get; set; }
}
