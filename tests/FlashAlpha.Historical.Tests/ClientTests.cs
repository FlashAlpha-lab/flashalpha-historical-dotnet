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
