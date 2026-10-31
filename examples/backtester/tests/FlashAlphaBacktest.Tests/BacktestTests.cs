// Offline tests for the backtester: a stub replaces the API, so no key or network is needed.
//
//     dotnet test tests/FlashAlphaBacktest.Tests

using System.Globalization;
using System.Text.Json.Nodes;
using Xunit;

namespace FlashAlphaBacktest.Tests;

/// <summary>Three sessions of canned API responses. The put spread opens on day 2 and hits the profit target on day 3.</summary>
public sealed class StubData : IFlashAlphaData
{
    private readonly Dictionary<string, (string Regime, double Vrp)> _days = new()
    {
        ["2024-03-04"] = ("positive_gamma", 2.0),
        ["2024-03-05"] = ("positive_gamma", 2.0),
        ["2024-03-06"] = ("positive_gamma", 2.0),
    };

    // Marks per date for the two legs: (bid, ask)
    private readonly Dictionary<string, Dictionary<(string Type, double Strike), (double Bid, double Ask)>> _marks = new()
    {
        ["2024-03-06"] = new() { [("P", 490.0)] = (0.30, 0.34), [("P", 485.0)] = (0.10, 0.12) },
    };

    public JsonNode? StockQuote(string symbol, string at)
    {
        if (!_days.ContainsKey(at[..10]))
            return new JsonObject { ["mid"] = 500.0, ["lastUpdate"] = "2024-02-29T16:00:00" };  // holiday: an earlier session's quote
        return new JsonObject { ["mid"] = 500.0, ["lastUpdate"] = at };
    }

    public JsonNode? ExposureSummary(string symbol, string at) => new JsonObject
    {
        ["regime"] = _days[at[..10]].Regime,
        ["gamma_flip"] = 495.0,
        ["exposures"] = new JsonObject { ["net_gex"] = 1e9 },
    };

    public JsonNode? Vrp(string symbol, string at) => new JsonObject
    {
        ["vrp"] = new JsonObject
        {
            ["vrp_20d"] = _days[at[..10]].Vrp, ["atm_iv"] = 14.0, ["rv_20d"] = 12.0, ["percentile"] = 60, ["z_score"] = 0.5,
        },
        ["gex_conditioned"] = new JsonObject { ["harvest_score"] = 70 },
    };

    public List<OptionQuote> Chain(string symbol, string at, string expiry)
    {
        if (expiry != "2024-04-05")
            return [];
        return
        [
            new() { Type = "P", Strike = 490.0, Expiry = expiry, Bid = 1.00, Ask = 1.10, Mid = 1.05, Delta = -0.21 },
            new() { Type = "P", Strike = 485.0, Expiry = expiry, Bid = 0.40, Ask = 0.46, Mid = 0.43, Delta = -0.12 },
            new() { Type = "P", Strike = 480.0, Expiry = expiry, Bid = 0.20, Ask = 0.24, Mid = 0.22, Delta = -0.07 },
            new() { Type = "C", Strike = 510.0, Expiry = expiry, Bid = 1.00, Ask = 1.10, Mid = 1.05, Delta = 0.20 },
        ];
    }

    public OptionQuote? Option(string symbol, string at, string expiry, double strike, string optType)
    {
        if (!_marks.TryGetValue(at[..10], out var day) || !day.TryGetValue((optType, strike), out var mark))
            return null;
        return new OptionQuote { Bid = mark.Bid, Ask = mark.Ask, Mid = (mark.Bid + mark.Ask) / 2 };
    }
}

public sealed class BacktestTests : IDisposable
{
    private readonly string _outDir = Path.Combine(Path.GetTempPath(), "fa-backtest-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_outDir))
            Directory.Delete(_outDir, recursive: true);
    }

    [Fact]
    public void FillPriceMovesFromMidTowardTheFarSide()
    {
        var q = new OptionQuote { Bid = 1.00, Ask = 1.20 };
        Assert.Equal(1.10, Engine.FillPrice(q, -1, 0.0), 1e-9);
        Assert.Equal(1.00, Engine.FillPrice(q, -1, 1.0), 1e-9);
        Assert.Equal(1.15, Engine.FillPrice(q, +1, 0.5), 1e-9);
    }

    [Fact]
    public void SelectLegsBuildsAPutCreditSpread()
    {
        var chain = new StubData().Chain("SPY", "2024-03-05T15:45:00", "2024-04-05");
        var snap = new Snapshot { Date = "2024-03-05", At = "2024-03-05T15:45:00" };
        List<Leg>? legs = Strategy.SelectLegs(chain, snap, new Config());
        Assert.NotNull(legs);
        Assert.Equal(new[] { (490.0, -1), (485.0, +1) }, legs.Select(l => (l.Strike, l.Side)));
    }

    [Fact]
    public void RunOpensAndClosesOneTradeWithExactAccounting()
    {
        var cfg = new Config { Start = "2024-03-04", End = "2024-03-06", MinDte = 25, MaxDte = 40, TargetDte = 31 };
        Summary summary = Engine.Run(cfg, new StubData(), _outDir);
        var trades = ReadCsv(Path.Combine(_outDir, "trades.csv"));
        Assert.Equal(1, summary.Trades);
        var t = Assert.Single(trades);
        // Entry on 03-05 (2 positive-gamma sessions, VRP > 0), halfway fills: sell 490 at 1.025, buy 485 at 0.445.
        // Credit = (1.025 - 0.445) * 100 - 2 legs * 0.65 = 56.70
        Assert.Equal("2024-03-05", t["entry_date"]);
        Assert.Equal(56.70, double.Parse(t["credit"], CultureInfo.InvariantCulture), 1e-9);
        // Day 3 mid value of the spread: 0.32 - 0.11 = 0.21 -> open profit 56.70 - 21 = 35.70 >= 50% of credit.
        // Close halfway: buy 490 at 0.33, sell 485 at 0.105 -> -22.50, less 1.30 commission: 56.70 - 22.50 - 1.30 = 32.90
        Assert.Equal("profit_target", t["exit_reason"]);
        Assert.Equal(32.90, double.Parse(t["pnl"], CultureInfo.InvariantCulture), 1e-9);
        Assert.Equal("-P490 +P485", t["legs"]);
        Assert.Equal("2024-04-05", t["expiry"]);
    }

    [Fact]
    public void StrikeTextKeepsEveryDigit()
    {
        Assert.Equal(new[] { "512", "517.5", "21012.25" }, new[] { 512.0, 517.5, 21012.25 }.Select(FlashAlphaData.StrikeText));
    }

    [Fact]
    public void HolidaysAreSkipped()
    {
        var cfg = new Config { Start = "2024-03-01", End = "2024-03-04" };  // 03-01 returns an earlier quote, 03-02/03 are a weekend
        Summary summary = Engine.Run(cfg, new StubData(), _outDir);
        Assert.Equal(1, summary.Sessions);
    }

    /// <summary>Reads a CSV the backtester wrote (no quoted fields) into one dictionary per row.</summary>
    private static List<Dictionary<string, string>> ReadCsv(string path)
    {
        string[] lines = File.ReadAllLines(path);
        string[] header = lines[0].Split(',');
        return lines.Skip(1)
                    .Select(line => header.Zip(line.Split(',')).ToDictionary(p => p.First, p => p.Second))
                    .ToList();
    }
}
