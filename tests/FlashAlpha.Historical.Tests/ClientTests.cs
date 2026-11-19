using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FlashAlpha.Historical.Tests;

/// <summary>Unit tests — mocked HTTP only. No live API hits.</summary>
public class ClientTests
{
    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _f;
        public List<HttpRequestMessage> Calls { get; } = new();
        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> f) { _f = f; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Calls.Add(req);
            return Task.FromResult(_f(req));
        }
    }

    private static FlashAlphaHistoricalClient MakeClient(HttpResponseMessage resp, out MockHandler handler)
    {
        handler = new MockHandler(_ => resp);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://historical.flashalpha.com") };
        http.DefaultRequestHeaders.Add("X-Api-Key", "TEST");
        return new FlashAlphaHistoricalClient(http);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    [Fact]
    public void FormatAt_DateTime_ReturnsIsoMinute()
    {
        Assert.Equal("2026-03-05T15:30:00", FlashAlphaHistoricalClient.FormatAt(new DateTime(2026, 3, 5, 15, 30, 0)));
    }

    [Fact]
    public void FormatAt_DateOnly_ReturnsIsoDate()
    {
        Assert.Equal("2026-03-05", FlashAlphaHistoricalClient.FormatAt(new DateOnly(2026, 3, 5)));
    }

    [Fact]
    public void Constructor_RejectsEmptyApiKey()
    {
        Assert.Throws<ArgumentException>(() => new FlashAlphaHistoricalClient(""));
    }

    [Fact]
    public async Task ExposureSummaryAsync_ForwardsAtAsQueryString()
    {
        using var client = MakeClient(Json("""{"regime":"positive_gamma"}"""), out var handler);
        var resp = await client.ExposureSummaryAsync("SPY", "2026-03-05T15:30:00");
        Assert.Equal("positive_gamma", resp.GetProperty("regime").GetString());
        Assert.Single(handler.Calls);
        var url = handler.Calls[0].RequestUri!.ToString();
        Assert.Contains("/v1/exposure/summary/SPY", url);
        Assert.Contains("at=2026-03-05T15%3A30%3A00", url);
    }

    [Fact]
    public async Task InvalidAt_400_MapsToTypedException()
    {
        using var client = MakeClient(
            Json("""{"error":"invalid_at","message":"bad"}""", HttpStatusCode.BadRequest), out _);
        await Assert.ThrowsAsync<InvalidAtException>(() =>
            client.ExposureSummaryAsync("SPY", "garbage"));
    }

    [Fact]
    public async Task NoCoverage_404_MapsToTypedException()
    {
        using var client = MakeClient(
            Json("""{"error":"no_coverage"}""", HttpStatusCode.NotFound), out _);
        await Assert.ThrowsAsync<NoCoverageException>(() => client.TickersAsync("ZZZZZ"));
    }

    [Fact]
    public async Task NoData_404_MapsToTypedException()
    {
        using var client = MakeClient(
            Json("""{"error":"no_data"}""", HttpStatusCode.NotFound), out _);
        await Assert.ThrowsAsync<NoDataException>(() => client.ExposureSummaryAsync("SPY", "2017-01-01"));
    }

    [Fact]
    public async Task SymbolNotFound_404_MapsToTypedException()
    {
        using var client = MakeClient(
            Json("""{"error":"symbol_not_found"}""", HttpStatusCode.NotFound), out _);
        await Assert.ThrowsAsync<SymbolNotFoundException>(() =>
            client.StockQuoteAsync("XYZ", "2024-01-02"));
    }

    [Fact]
    public async Task InsufficientData_404_MapsToTypedException()
    {
        using var client = MakeClient(
            Json("""{"error":"insufficient_data"}""", HttpStatusCode.NotFound), out _);
        await Assert.ThrowsAsync<InsufficientDataException>(() =>
            client.SurfaceAsync("SPY", "2018-04-16"));
    }

    [Fact]
    public async Task TierRestricted_403_PopulatesPlanFields()
    {
        using var client = MakeClient(
            Json("""{"error":"tier_restricted","current_plan":"Growth","required_plan":"Alpha","message":"needs Alpha"}""",
                HttpStatusCode.Forbidden), out _);
        var ex = await Assert.ThrowsAsync<TierRestrictedException>(() =>
            client.ExposureSummaryAsync("SPY", "2026-03-05"));
        Assert.Equal("Growth", ex.CurrentPlan);
        Assert.Equal("Alpha", ex.RequiredPlan);
    }

    [Fact]
    public async Task Authentication_401_MapsToTypedException()
    {
        using var client = MakeClient(Json("", HttpStatusCode.Unauthorized), out _);
        await Assert.ThrowsAsync<AuthenticationException>(() => client.TickersAsync());
    }

    [Fact]
    public async Task OptionQuote_PassesAllFilters()
    {
        using var client = MakeClient(Json("""{"strike":680,"type":"C"}"""), out var handler);
        await client.OptionQuoteAsync("SPY", "2026-03-05T15:30:00",
            expiry: "2026-03-06", strike: 680, type: "C");
        var url = handler.Calls[0].RequestUri!.ToString();
        Assert.Contains("strike=680", url);
        Assert.Contains("type=C", url);
        Assert.Contains("expiry=2026-03-06", url);
    }

    [Fact]
    public async Task At_DateTimeOverload_FormatsCorrectly()
    {
        using var client = MakeClient(Json("""{"symbol":"SPY"}"""), out var handler);
        await client.VrpAsync("SPY", new DateTime(2025, 6, 18, 12, 0, 0));
        Assert.Contains("at=2025-06-18T12%3A00%3A00", handler.Calls[0].RequestUri!.ToString());
    }
}

/// <summary>sell-window replay unit tests.</summary>
public class SellWindowTests
{
    private sealed class Handler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? Last { get; private set; }
        public Handler(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Last = req;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static FlashAlphaHistoricalClient Make(Handler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://historical.flashalpha.com") };
        http.DefaultRequestHeaders.Add("X-Api-Key", "TEST");
        return new FlashAlphaHistoricalClient(http);
    }

    [Fact]
    public async Task SellWindowTypedAsync_SendsAtAndBindsEveryField()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"symbol":"SPY","session":"2026-10-16","spot":772.255,"quoted_at":"2026-10-16T13:35:00Z","legs_indicative":false,"windows":[{"expiry":"2026-10-16","days_to_expiry":0,"action":"sell","candidates":[{"structure":"short_put","exit":"expiry","legs":[{"side":"sell","quantity":1,"type":"put","strike":769,"delta":-0.19,"bid":0.39,"ask":0.4}],"mid_credit":0.395}]},{"expiry":"2026-10-23","days_to_expiry":7,"action":"skip"},{"expiry":"2026-10-30","days_to_expiry":14,"action":"skip"},{"action":"unavailable"}],"endpoint_version":"2026.09.30","data_as_of":{"node":"f3"},"archive_as_of":{"node":"f3"}}""");
        using var client = Make(handler);

        var r = await client.SellWindowTypedAsync("SPY", "2026-10-16T09:35:00");

        var uri = handler.Last!.RequestUri!;
        Assert.Equal("/v1/strategies/sell-window/SPY", uri.AbsolutePath);
        Assert.Contains("at=2026-10-16T09%3A35%3A00", uri.ToString());
        Assert.Equal(4, r!.Windows!.Count);
        var trade = r.Windows[0].Candidates![0];
        Assert.Equal(0.395, trade.MidCredit);
        Assert.Equal(769, trade.Legs![0].Strike);
        Assert.Null(r.Windows[3].Expiry);
        Assert.Equal("f3", r.ArchiveAsOf!.Node);
    }

    [Fact]
    public async Task SellWindowAsync_DateTimeOverload_FormatsAt()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"symbol":"SPY"}""");
        using var client = Make(handler);
        await client.SellWindowAsync("SPY", new DateTime(2026, 10, 16, 9, 35, 0));
        Assert.Contains("at=2026-10-16T09%3A35%3A00", handler.Last!.RequestUri!.ToString());
    }

    [Fact]
    public async Task SellWindowAsync_InvalidAt_Throws()
    {
        var handler = new Handler(HttpStatusCode.BadRequest, """{"error":"invalid_at","message":"at is in the future."}""");
        using var client = Make(handler);
        await Assert.ThrowsAsync<InvalidAtException>(() => client.SellWindowAsync("SPY", "2099-01-02T09:35:00"));
    }
}

/// <summary>Expiry impact and strike dependency replay unit tests, using real production responses.</summary>
public class GammaContextTests
{
    private sealed class Handler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? Last { get; private set; }
        public Handler(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Last = req;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static FlashAlphaHistoricalClient Make(Handler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://historical.flashalpha.com") };
        http.DefaultRequestHeaders.Add("X-Api-Key", "TEST");
        return new FlashAlphaHistoricalClient(http);
    }

    /// <summary>Real production responses captured 2026-11-19 (lists trimmed).</summary>
    private static string Fixture(string name)
        => System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private const string At = "2026-10-09T15:55:00";

    [Fact]
    public async Task ExpiryImpactAsync_SendsAtAndOmitsUnsetOptionalParameters()
    {
        var handler = new Handler(HttpStatusCode.OK, Fixture("expiry_impact_historical.json"));
        using var client = Make(handler);

        await client.ExpiryImpactAsync("SPY", At);

        Assert.Equal("/v1/exposure/expiry-impact/SPY", handler.Last!.RequestUri!.AbsolutePath);
        Assert.Equal("?at=2026-10-09T15%3A55%3A00", handler.Last.RequestUri.Query);
    }

    [Fact]
    public async Task ExpiryImpactAsync_SendsExpiryAndLimit()
    {
        var handler = new Handler(HttpStatusCode.OK, Fixture("expiry_impact_historical.json"));
        using var client = Make(handler);

        await client.ExpiryImpactAsync("QQQ", At, expiry: "2026-10-12", limit: 3);

        Assert.Equal("/v1/exposure/expiry-impact/QQQ", handler.Last!.RequestUri!.AbsolutePath);
        Assert.Equal("?at=2026-10-09T15%3A55%3A00&expiry=2026-10-12&limit=3", handler.Last.RequestUri.Query);
    }

    [Fact]
    public async Task ExpiryImpactAsync_DateTimeOverload_FormatsAt()
    {
        var handler = new Handler(HttpStatusCode.OK, Fixture("expiry_impact_historical.json"));
        using var client = Make(handler);

        await client.ExpiryImpactTypedAsync("SPY", new DateTime(2026, 10, 9, 15, 55, 0), limit: 2);

        Assert.Equal("?at=2026-10-09T15%3A55%3A00&limit=2", handler.Last!.RequestUri!.Query);
    }

    [Fact]
    public async Task ExpiryImpactTypedAsync_BindsEveryLevel()
    {
        var handler = new Handler(HttpStatusCode.OK, Fixture("expiry_impact_historical.json"));
        using var client = Make(handler);

        var r = await client.ExpiryImpactTypedAsync("SPY", At, limit: 2);

        Assert.NotNull(r);
        Assert.Equal("SPY", r!.Symbol);
        Assert.Equal(778.405, r.UnderlyingPrice);
        Assert.Equal("9850cd4be38b33b949b57c1907487349a879726d521bb805cd67e22bfd244aeb", r.SnapshotId);
        Assert.Equal("2026-10-09T19:55:00Z", r.GeneratedAt);
        Assert.Equal("2026-10-09T19:55:00Z", r.QuoteAt);
        Assert.Equal("gamma-context-v2", r.CalculationVersion);
        Assert.Equal("historical_quote_repriced_dated_inputs", r.PricingBasis);
        Assert.Equal("partial", r.DataStatus);
        Assert.Equal("historical_model_reconstruction", r.Reasons![0].Code);
        Assert.Null(r.Reasons[0].ContractCount);
        Assert.Equal(302, r.Reasons[1].ContractCount);
        Assert.Null(r.OiAsOf);
        Assert.Equal("unknown", r.OiVintageStatus);
        Assert.Equal(12078, r.EligibleContractCount);
        Assert.Equal(1182, r.RejectedContractCount);
        Assert.Equal(50, r.ObservedMinStrike);
        Assert.Equal(1480, r.ObservedMaxStrike);
        Assert.Equal("usd_delta_notional_per_1pct_move", r.GexUnit);
        Assert.Equal("calls_positive_puts_negative", r.SignConvention);
        Assert.Equal("expiry_removal_sensitivity", r.AnalysisType);
        Assert.Equal("2026-10-09", r.SelectedExpiry);
        Assert.Equal("nearest_active", r.SelectionSource);
        Assert.Equal(new[] { "2026-10-09", "2026-10-12", "2026-10-13" }, r.AvailableExpiries);
        Assert.Equal(58250427831.002655, r.Current!.GrossGexUsd);
        Assert.Null(r.Current.Status);
        Assert.Equal(100, r.ExcludedExpiry!.ContractCount);
        Assert.Equal("available", r.ExcludingExpiry!.Status);
        Assert.Equal(27.355976302706107, r.Impact!.RemovedGrossSharePct);
        Assert.Equal(-7805314199.31635, r.Impact.NetGexChangeUsd);
        Assert.Equal("positive", r.Impact.NetSignBefore);
        Assert.Equal("positive", r.Impact.NetSignAfter);
        Assert.False(r.Impact.NetSignChanged);
        Assert.Equal(778, r.Impact.LargestGrossReductions![0].Strike);
        Assert.Equal(87.54989911773554, r.Impact.LargestGrossReductions[0].ExcludedShareAtStrikePct);
        Assert.Equal(785, r.Impact.StrongestRemainingConcentrations![1].Strike);
        Assert.Equal(-186739.42129722596, r.Impact.StrongestRemainingConcentrations[1].ExcludedNetGexUsd);
        Assert.Equal(55, r.Strikes![1].Strike);
        Assert.Equal(0, r.Strikes[1].ExcludedGrossGexUsd);
        Assert.Equal("expiry_gross_share", r.Facts![0].Code);
        Assert.Equal("2026-10-09", r.Facts[0].SupportingValues!["expiry"].GetString());
        Assert.Equal(15934973233.674011, r.Facts[0].SupportingValues!["gross_gex_usd"].GetDouble());
        Assert.Equal(778.405, r.Facts[1].SupportingValues!["underlying_price"].GetDouble());
        Assert.Equal("2026.09.30", r.EndpointVersion);
        Assert.Equal("f3", r.DataAsOf!.Node);
        Assert.Null(r.DataAsOf.EquityFeed);
        Assert.Equal("f3", r.ArchiveAsOf!.Node);
        Assert.Equal("2026-10-09T19:55:00Z", r.ArchiveAsOf.EquityOptionsFeed);
        Assert.Equal("2026-10-08T20:00:00Z", r.ArchiveAsOf.OiFeed);
    }

    [Fact]
    public async Task StrikeDependencyAsync_SendsAtAndFractionalStrikeExactly()
    {
        var handler = new Handler(HttpStatusCode.OK, Fixture("strike_dependency_historical.json"));
        using var client = Make(handler);

        await client.StrikeDependencyAsync("SPY", At, 672.5);
        Assert.Equal("/v1/exposure/strike-dependency/SPY", handler.Last!.RequestUri!.AbsolutePath);
        Assert.Equal("?at=2026-10-09T15%3A55%3A00&strike=672.5", handler.Last.RequestUri.Query);

        await client.StrikeDependencyAsync("SPY", new DateTime(2026, 10, 9, 15, 55, 0), 670);
        Assert.Equal("?at=2026-10-09T15%3A55%3A00&strike=670", handler.Last!.RequestUri!.Query);
    }

    [Fact]
    public async Task StrikeDependencyAsync_StrikeIsLocaleInvariant()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var handler = new Handler(HttpStatusCode.OK, Fixture("strike_dependency_historical.json"));
            using var client = Make(handler);
            await client.StrikeDependencyAsync("QQQ", "2026-10-09", 612.5);
            Assert.Equal("?at=2026-10-09&strike=612.5", handler.Last!.RequestUri!.Query);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public async Task StrikeDependencyTypedAsync_BindsEveryLevel()
    {
        var handler = new Handler(HttpStatusCode.OK, Fixture("strike_dependency_historical.json"));
        using var client = Make(handler);

        var r = await client.StrikeDependencyTypedAsync("SPY", At, 670);

        Assert.NotNull(r);
        Assert.Equal("strike_attribution", r!.AnalysisType);
        Assert.Equal(670, r.Strike);
        Assert.Equal(-13.92655494247853, r.DistanceFromSpotPct);
        Assert.Equal(115634632.87571214, r.GrossGexUsd);
        Assert.Equal(-95819046.48603098, r.NetGexUsd);
        Assert.Equal(60, r.ContractCount);
        Assert.Equal("2026-12-18", r.DominantExpiry);
        Assert.Equal(29.844720445178503, r.DominantExpirySharePct);
        var row = r.ByExpiry![0];
        Assert.Equal("2026-10-13", row.Expiry);
        Assert.Equal("2026-10-13T20:00:00Z", row.ValuationCutoffAt);
        Assert.Equal("2026-10-13T20:15:00Z", row.LastTradingAt);
        Assert.Equal("scheduled_regular_session", row.LastTradingStatus);
        Assert.Null(row.SettlementAt);
        Assert.Equal("unknown", row.SettlementStatus);
        Assert.Equal(4, row.CalendarDaysToExpiry);
        Assert.Equal(0.0026251306222739787, row.GrossSharePct);
        Assert.Equal(0.0026251306222739787, row.ShareOfStrikeGrossPct);
        Assert.Equal(0, row.CallGexUsd);
        Assert.Equal(-3035.560157574413, row.PutGexUsd);
        Assert.Equal(2, row.ContractCount);
        Assert.Equal("f3", r.ArchiveAsOf!.Node);
        Assert.Equal("2026-10-08T20:00:00Z", r.ArchiveAsOf.OiFeed);
    }

    [Fact]
    public async Task ExpiryImpactAsync_Throws403TierRestricted()
    {
        var handler = new Handler(HttpStatusCode.Forbidden, """{"status":"ERROR","error":"tier_restricted","message":"Requires Alpha.","current_plan":"Growth","required_plan":"Alpha"}""");
        using var client = Make(handler);

        var ex = await Assert.ThrowsAsync<TierRestrictedException>(() => client.ExpiryImpactTypedAsync("SPY", At));
        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("Growth", ex.CurrentPlan);
        Assert.Equal("Alpha", ex.RequiredPlan);
    }

    [Fact]
    public async Task StrikeDependencyAsync_Throws403TierRestricted()
    {
        var handler = new Handler(HttpStatusCode.Forbidden, """{"status":"ERROR","error":"tier_restricted","message":"Requires Alpha.","current_plan":"Growth","required_plan":"Alpha"}""");
        using var client = Make(handler);

        var ex = await Assert.ThrowsAsync<TierRestrictedException>(() => client.StrikeDependencyTypedAsync("SPY", At, 670));
        Assert.Equal("Alpha", ex.RequiredPlan);
    }

    [Fact]
    public async Task ExpiryImpactAsync_UnsupportedSymbolSurfaces422()
    {
        var handler = new Handler((HttpStatusCode)422, Fixture("error_unsupported_symbol.json"));
        using var client = Make(handler);

        var ex = await Assert.ThrowsAsync<FlashAlphaHistoricalException>(() => client.ExpiryImpactTypedAsync("AAPL", At));
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal("This operation currently supports standard SPY and QQQ options.", ex.Message);
        Assert.Equal("unsupported_symbol", ex.Response!.Value.GetProperty("error").GetString());
    }

    [Fact]
    public async Task StrikeDependencyAsync_UnsupportedSymbolSurfaces422()
    {
        var handler = new Handler((HttpStatusCode)422, Fixture("error_unsupported_symbol.json"));
        using var client = Make(handler);

        var ex = await Assert.ThrowsAsync<FlashAlphaHistoricalException>(() => client.StrikeDependencyAsync("AAPL", At, 200));
        Assert.Equal(422, ex.StatusCode);
        Assert.Equal("unsupported_symbol", ex.Response!.Value.GetProperty("error").GetString());
    }

    [Fact]
    public async Task StrikeDependencyAsync_StrikeNotFoundEchoesRequestedStrike()
    {
        var handler = new Handler(HttpStatusCode.NotFound, Fixture("error_strike_not_found.json"));
        using var client = Make(handler);

        var ex = await Assert.ThrowsAsync<NoDataException>(() => client.StrikeDependencyAsync("SPY", At, 671.3));
        Assert.Equal(404, ex.StatusCode);
        Assert.Equal("strike_not_found", ex.Response!.Value.GetProperty("error").GetString());
        Assert.Equal(671.3, ex.Response.Value.GetProperty("requested_strike").GetDouble());
    }
}

/// <summary>Replay / Backtester unit tests.</summary>
public class ReplayTests
{
    [Fact]
    public void IsTradingDay_RejectsWeekendsAndHolidays()
    {
        Assert.True(Replay.IsTradingDay(new DateOnly(2024, 1, 2)));
        Assert.False(Replay.IsTradingDay(new DateOnly(2024, 1, 6)));   // Sat
        Assert.False(Replay.IsTradingDay(new DateOnly(2024, 1, 7)));   // Sun
        Assert.False(Replay.IsTradingDay(new DateOnly(2024, 1, 1)));   // New Year
        Assert.False(Replay.IsTradingDay(new DateOnly(2024, 12, 25))); // Christmas
        Assert.False(Replay.IsTradingDay(new DateOnly(2024, 7, 4)));   // July 4
    }

    [Fact]
    public void IterDays_SkipsWeekendsAndHolidays()
    {
        var days = new List<DateTime>(Replay.IterDays(new(2024, 1, 1), new(2024, 1, 8)));
        var dates = days.ConvertAll(d => DateOnly.FromDateTime(d));
        Assert.Equal(new[]
        {
            new DateOnly(2024, 1, 2),
            new DateOnly(2024, 1, 3),
            new DateOnly(2024, 1, 4),
            new DateOnly(2024, 1, 5),
            new DateOnly(2024, 1, 8),
        }, dates);
        Assert.All(days, d => Assert.Equal(new TimeOnly(16, 0), TimeOnly.FromDateTime(d)));
    }

    [Fact]
    public void IterMinutes_DefaultStep_Yields391Stamps()
    {
        var minutes = new List<DateTime>(Replay.IterMinutes(new(2024, 1, 2), new(2024, 1, 2)));
        Assert.Equal(391, minutes.Count); // 9:30 → 16:00 inclusive at 1m
        Assert.Equal(new TimeOnly(9, 30), TimeOnly.FromDateTime(minutes[0]));
        Assert.Equal(new TimeOnly(16, 0), TimeOnly.FromDateTime(minutes[^1]));
    }

    [Fact]
    public void IterMinutes_30MinStep_Yields14Stamps()
    {
        var minutes = new List<DateTime>(
            Replay.IterMinutes(new(2024, 1, 2), new(2024, 1, 2), stepMinutes: 30));
        Assert.Equal(14, minutes.Count);
    }

    [Fact]
    public void IterMinutes_RejectsZeroStep()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new List<DateTime>(Replay.IterMinutes(new(2024, 1, 2), new(2024, 1, 2), stepMinutes: 0)));
    }
}
