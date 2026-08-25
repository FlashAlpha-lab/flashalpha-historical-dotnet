using System.Linq;
using System.Text.Json;
using FlashAlpha.Historical.Models;
using Xunit;

namespace FlashAlpha.Historical.Tests;

/// <summary>
/// The envelope is carried on <see cref="FlashAlphaResponse"/> rather than repeated on
/// each response model. That is only sound if the base actually binds through the
/// deserializer, so these tests exercise the binding rather than the declaration - a
/// model that quietly stopped inheriting would still compile.
/// </summary>
public class ResponseEnvelopeTests
{
    private const string Body = """
    {
      "symbol": "SPY",
      "net_gex": 1234.5,
      "endpoint_version": "2026.08.25",
      "data_as_of": {
        "node": "f3",
        "equity_feed": null,
        "equity_options_feed": null,
        "index_feed": null,
        "index_options_feed": null,
        "futures_feed": null,
        "futures_options_feed": null,
        "flow_feed": null,
        "oi_feed": null,
        "macro_feed": null
      },
      "archive_as_of": {
        "node": "f3",
        "equity_feed": "2024-03-15T14:29:59.500Z",
        "equity_options_feed": "2024-03-15T14:29:58.100Z",
        "index_feed": null,
        "index_options_feed": null,
        "futures_feed": null,
        "futures_options_feed": null,
        "flow_feed": null,
        "oi_feed": "2024-03-14T20:00:00.000Z",
        "macro_feed": null
      }
    }
    """;

    [Fact]
    public void Envelope_BindsThroughTheBaseClass()
    {
        var gex = JsonSerializer.Deserialize<GexResponse>(Body);

        Assert.NotNull(gex);
        Assert.Equal("2026.08.25", gex!.EndpointVersion);
        Assert.NotNull(gex.ArchiveAsOf);
        Assert.Equal("f3", gex.ArchiveAsOf!.Node);
        Assert.Equal("2024-03-15T14:29:58.100Z", gex.ArchiveAsOf.EquityOptionsFeed);
    }

    /// <summary>
    /// A replay node reads the archive and consumes no live feed, so every live feed is
    /// null. The object is still returned, and that all-null shape is what stops a
    /// historical response being mistaken for a live one - so it must survive as an
    /// object with null members, not collapse to a null object.
    /// </summary>
    [Fact]
    public void LiveFeeds_AreAllNullButTheObjectSurvives()
    {
        var gex = JsonSerializer.Deserialize<GexResponse>(Body);

        Assert.NotNull(gex!.DataAsOf);
        Assert.Null(gex.DataAsOf!.EquityFeed);
        Assert.Null(gex.DataAsOf.EquityOptionsFeed);
        Assert.Null(gex.DataAsOf.OiFeed);
        Assert.Null(gex.DataAsOf.MacroFeed);
    }

    /// <summary>
    /// The archive vintage is what makes a gap detectable, so it must pass through
    /// untouched rather than being normalised toward the requested instant. Normalising
    /// it would erase exactly the signal a point-in-time study reads.
    /// </summary>
    [Fact]
    public void ArchiveVintage_PassesThroughUnmodified()
    {
        var gex = JsonSerializer.Deserialize<GexResponse>(Body);

        Assert.Equal("2024-03-15T14:29:59.500Z", gex!.ArchiveAsOf!.EquityFeed);
        Assert.Equal("2024-03-14T20:00:00.000Z", gex.ArchiveAsOf.OiFeed);
    }

    /// <summary>A response that did not read a class of data reports null for it.</summary>
    [Fact]
    public void UnreadDataClasses_StayNull()
    {
        var gex = JsonSerializer.Deserialize<GexResponse>(Body);

        Assert.Null(gex!.ArchiveAsOf!.FuturesFeed);
        Assert.Null(gex.ArchiveAsOf.FlowFeed);
    }

    /// <summary>Responses predating the envelope still deserialize; all members are optional.</summary>
    [Fact]
    public void PreEnvelopeResponses_StillDeserialize()
    {
        var gex = JsonSerializer.Deserialize<GexResponse>("""{"symbol":"SPY","net_gex":1.0}""");

        Assert.NotNull(gex);
        Assert.Null(gex!.EndpointVersion);
        Assert.Null(gex.DataAsOf);
        Assert.Null(gex.ArchiveAsOf);
    }

    /// <summary>
    /// Guard the sweep: every public response model must reach the envelope. Trusting
    /// that one regex touched every file is exactly the assumption worth testing, and a
    /// model added later would otherwise slip through silently.
    /// </summary>
    [Fact]
    public void EveryResponseModel_CarriesTheEnvelope()
    {
        var types = typeof(FlashAlphaHistoricalClient).Assembly
            .GetExportedTypes()
            .Where(t => t.IsClass && t.Name.EndsWith("Response") && t != typeof(FlashAlphaResponse))
            .ToList();

        Assert.True(types.Count > 0, "found no response models; the guard is not actually checking anything");

        var missing = types
            .Where(t => !typeof(FlashAlphaResponse).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.True(missing.Count == 0,
            $"response models not inheriting FlashAlphaResponse: {string.Join(", ", missing)}");
    }
}
