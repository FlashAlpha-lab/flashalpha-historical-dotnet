// FlashAlpha options backtester: a complete, runnable template (C# port of flashalpha_backtest.py).
//
// Replays the FlashAlpha Historical API day by day, decides entries and exits from the same endpoint outputs you
// would read live, fills trades on real historical bid/ask quotes, and writes trades.csv and equity.csv.
//
// Edit the three marked methods in the Strategy class to change the strategy:
//     EntryRules()  - when to open a position (reads FlashAlpha signals)
//     SelectLegs()  - which option contracts to trade (reads the option chain)
//     ExitRules()   - when to close (reads the position's marks and FlashAlpha signals)
//
// Requirements: .NET 8 SDK or newer, no NuGet packages. An Alpha plan or higher (Historical API).
//
//     export FLASHALPHA_API_KEY=your_key          (PowerShell: $env:FLASHALPHA_API_KEY = "your_key")
//     dotnet run -- --symbol SPY --start 2024-01-02 --end 2024-06-28
//
// Every API response is cached in .fa_cache/, so a rerun with changed rules costs no API calls for days already fetched.
// The cache uses the same file names as the Python version, so both can share one cache folder.

using System.Globalization;
using System.Net;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace FlashAlphaBacktest;

// ============================================================================================================
// Settings
// ============================================================================================================
public sealed record Config
{
    public string Symbol { get; init; } = "SPY";
    public string Start { get; init; } = "2024-01-02";
    public string End { get; init; } = "2024-06-28";
    public string DecisionTime { get; init; } = "15:45";  // ET; every decision and fill uses quotes at this minute
    // Trade selection (a put credit spread by default)
    public int TargetDte { get; init; } = 35;             // pick the expiry closest to this many calendar days out...
    public int MinDte { get; init; } = 25;                // ...within this window
    public int MaxDte { get; init; } = 50;
    public double ShortDelta { get; init; } = 0.20;       // sell the put closest to this absolute delta
    public double Width { get; init; } = 5.0;             // buy the put this many points lower
    public int Contracts { get; init; } = 1;
    public int MaxPositions { get; init; } = 1;           // open positions allowed at once
    // Exits
    public double ProfitTarget { get; init; } = 0.50;     // close when 50% of the entry credit is captured
    public double StopLoss { get; init; } = 2.00;         // close when the loss reaches 2x the entry credit
    public int ExitDte { get; init; } = 21;               // close with this many calendar days left
    // Costs
    public double Slippage { get; init; } = 0.5;          // 0 = fill at mid, 1 = fill at the bid or ask, 0.5 = halfway
    public double Commission { get; init; } = 0.65;       // per contract per leg, each side
}

// ============================================================================================================
// FlashAlpha data: a small cached client for the Historical API
// ============================================================================================================

/// <summary>The endpoints the backtest reads. FlashAlphaData calls the API; tests can pass a stub instead.</summary>
public interface IFlashAlphaData
{
    JsonNode? StockQuote(string symbol, string at);
    JsonNode? ExposureSummary(string symbol, string at);
    JsonNode? Vrp(string symbol, string at);
    List<OptionQuote> Chain(string symbol, string at, string expiry);
    OptionQuote? Option(string symbol, string at, string expiry, double strike, string optType);
}

public sealed class FlashAlphaData : IFlashAlphaData
{
    public const string DefaultBaseUrl = "https://historical.flashalpha.com";

    private readonly string _baseUrl;
    private readonly string _cacheDir;
    private readonly HttpClient _http;

    /// <summary>HTTP requests sent this run (cache hits are free and not counted).</summary>
    public int Calls { get; private set; }

    public FlashAlphaData(string apiKey, string cacheDir = ".fa_cache", string baseUrl = DefaultBaseUrl)
    {
        if (string.IsNullOrEmpty(apiKey))
            throw new FatalException("Set FLASHALPHA_API_KEY (Alpha plan or higher).");
        _baseUrl = baseUrl.TrimEnd('/');
        _cacheDir = cacheDir;
        Directory.CreateDirectory(cacheDir);
        _http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(60),
        };
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("flashalpha-backtest-dotnet/1.0");
    }

    /// <summary>GET with a disk cache. Returns parsed JSON, or null when the API has no data (404).</summary>
    public JsonNode? Get(string path, params (string Key, string? Value)[] parameters)
    {
        var query = parameters.Where(p => p.Value is not null).Select(p => (p.Key, Value: p.Value!)).ToList();
        string hit = Path.Combine(_cacheDir, CacheKey(path, query) + ".json");
        if (File.Exists(hit))
            return JsonNode.Parse(File.ReadAllText(hit));
        JsonNode? data = Fetch(path, query);
        File.WriteAllText(hit, data?.ToJsonString() ?? "null");
        return data;
    }

    private JsonNode? Fetch(string path, List<(string Key, string Value)> query)
    {
        string url = _baseUrl + path + "?" +
                     string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        for (int attempt = 0; attempt < 5; attempt++)
        {
            TimeSpan backoff = TimeSpan.FromSeconds(Math.Pow(2, attempt));  // 1, 2, 4, 8, 16 seconds
            using HttpResponseMessage? response = TrySend(url);
            if (response is null)  // connection error or timeout: not counted as a call
            {
                Thread.Sleep(backoff);
                continue;
            }
            Calls++;
            int status = (int)response.StatusCode;
            if (status == 200)
                return JsonNode.Parse(ReadBody(response));
            if (status == 404)
                return null;
            if (status is 429 or 500 or 502 or 503 or 504)
            {
                // Wait as long as the server asks when Retry-After is a number of seconds, else back off.
                string wait = response.Headers.TryGetValues("Retry-After", out var values) ? values.First() : "";
                bool isSeconds = wait.Length > 0 && wait.All(char.IsAsciiDigit);
                Thread.Sleep(isSeconds ? TimeSpan.FromSeconds(double.Parse(wait, CultureInfo.InvariantCulture)) : backoff);
                continue;
            }
            string text = ReadBody(response);
            throw new FatalException($"FlashAlpha API error {status} on {path}: {text[..Math.Min(200, text.Length)]}");
        }
        throw new FatalException($"FlashAlpha API kept failing on {path}");
    }

    /// <summary>Sends one GET. Returns null on a connection error or timeout, so the caller can retry.</summary>
    private HttpResponseMessage? TrySend(string url)
    {
        try
        {
            return _http.Send(new HttpRequestMessage(HttpMethod.Get, url));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static string ReadBody(HttpResponseMessage response)
    {
        using var reader = new StreamReader(response.Content.ReadAsStream());
        return reader.ReadToEnd();
    }

    /// <summary>The Python version's key: sha1 of json.dumps([path, sorted(params.items())]).</summary>
    private static string CacheKey(string path, IEnumerable<(string Key, string Value)> query)
    {
        var pairs = query.OrderBy(p => p.Key, StringComparer.Ordinal)
                         .Select(p => $"[{PyJsonString(p.Key)}, {PyJsonString(p.Value)}]");
        string text = $"[{PyJsonString(path)}, [{string.Join(", ", pairs)}]]";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    /// <summary>A string as Python's json.dumps writes it (paths and parameters are plain ASCII in practice).</summary>
    private static string PyJsonString(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char ch in s)
            sb.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                < ' ' or > '\u007f' => $"\\u{(int)ch:x4}",
                _ => ch.ToString(),
            });
        return sb.Append('"').ToString();
    }

    public JsonNode? StockQuote(string symbol, string at) =>
        Get($"/v1/stockquote/{symbol}", ("at", at));

    public JsonNode? ExposureSummary(string symbol, string at) =>
        Get($"/v1/exposure/summary/{symbol}", ("at", at));

    public JsonNode? Vrp(string symbol, string at) =>
        Get($"/v1/vrp/{symbol}", ("at", at));

    public List<OptionQuote> Chain(string symbol, string at, string expiry) =>
        Get($"/v1/optionquote/{symbol}", ("at", at), ("expiry", expiry))?.Deserialize<List<OptionQuote>>() ?? [];

    public OptionQuote? Option(string symbol, string at, string expiry, double strike, string optType) =>
        Get($"/v1/optionquote/{symbol}", ("at", at), ("expiry", expiry), ("strike", StrikeText(strike)), ("type", optType))
            ?.Deserialize<OptionQuote>();

    /// <summary>Strike as the API and the trade log write it: 512 gives "512", 517.5 "517.5", 21012.25 "21012.25".</summary>
    public static string StrikeText(double strike) => Py.FormatG(strike, 10);
}

/// <summary>One contract from /v1/optionquote. Add any other response field you want the rules to read.</summary>
public sealed class OptionQuote
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";     // "C" or "P"
    [JsonPropertyName("expiry")] public string Expiry { get; init; } = "";  // yyyy-MM-dd
    [JsonPropertyName("strike")] public double Strike { get; init; }
    [JsonPropertyName("bid")] public double? Bid { get; init; }
    [JsonPropertyName("ask")] public double? Ask { get; init; }
    [JsonPropertyName("mid")] public double? Mid { get; init; }
    [JsonPropertyName("implied_vol")] public double? ImpliedVol { get; init; }
    [JsonPropertyName("delta")] public double? Delta { get; init; }
    [JsonPropertyName("gamma")] public double? Gamma { get; init; }
    [JsonPropertyName("theta")] public double? Theta { get; init; }
    [JsonPropertyName("vega")] public double? Vega { get; init; }
    [JsonPropertyName("open_interest")] public double? OpenInterest { get; init; }
    [JsonPropertyName("volume")] public double? Volume { get; init; }
}

/// <summary>Everything the rules can read for one decision time. A null field means the API had no value.</summary>
public sealed record Snapshot
{
    public required string Date { get; init; }
    public required string At { get; init; }
    public double? Spot { get; init; }
    // /v1/exposure/summary
    public string? GammaRegime { get; init; }    // "positive_gamma" or "negative_gamma"
    public double? NetGex { get; init; }
    public double? GammaFlip { get; init; }
    // /v1/vrp
    public double? AtmIv { get; init; }          // vol points
    public double? Rv20d { get; init; }
    public double? Vrp20d { get; init; }         // implied minus 20-day realized, vol points
    public double? VrpPercentile { get; init; }
    public double? VrpZ { get; init; }
    public double? HarvestScore { get; init; }
}

public static class Snapshots
{
    /// <summary>
    /// Everything the rules can read for one decision time, as one flat record. Returns null on a non-trading day.
    ///
    /// Add fields from any other FlashAlpha endpoint here (exposure levels, narrative, max pain, 0DTE, surface...).
    /// </summary>
    public static Snapshot? BuildSnapshot(IFlashAlphaData fa, string symbol, string at)
    {
        JsonNode? quote = fa.StockQuote(symbol, at);
        if (quote is not JsonObject { Count: > 0 } ||
            !((string?)quote["lastUpdate"] ?? "").StartsWith(at[..10], StringComparison.Ordinal))
            return null;  // weekend or market holiday: the archive returned an earlier session's quote
        JsonNode? ex = fa.ExposureSummary(symbol, at);
        JsonNode? v = fa.Vrp(symbol, at);
        JsonNode? core = v?["vrp"];
        return new Snapshot
        {
            Date = at[..10],
            At = at,
            Spot = Number(quote["mid"]),
            // /v1/exposure/summary
            GammaRegime = (string?)ex?["regime"],
            NetGex = Number(ex?["exposures"]?["net_gex"]),
            GammaFlip = Number(ex?["gamma_flip"]),
            // /v1/vrp
            AtmIv = Number(core?["atm_iv"]),
            Rv20d = Number(core?["rv_20d"]),
            Vrp20d = Number(core?["vrp_20d"]),
            VrpPercentile = Number(core?["percentile"]),
            VrpZ = Number(core?["z_score"]),
            HarvestScore = Number(v?["gex_conditioned"]?["harvest_score"]),
        };
    }

    /// <summary>A number field, or null when it is missing or null. Whole numbers read too (60 gives 60.0).</summary>
    private static double? Number(JsonNode? node) => node?.Deserialize<double?>();
}

// ============================================================================================================
// STRATEGY: edit these three methods
// ============================================================================================================
public static class Strategy
{
    /// <summary>
    /// ENTRY RULES. Return true to open a position at this decision time.
    ///
    /// snap is today's snapshot (see BuildSnapshot); history holds the earlier sessions' snapshots, oldest first.
    /// Default: implied vol above 20-day realized (VRP > 0) and dealers long gamma for 2 sessions running.
    /// </summary>
    public static bool EntryRules(Snapshot snap, IReadOnlyList<Snapshot> history, Config cfg)
    {
        var gammaStreak = history.TakeLast(1).Append(snap).Select(s => s.GammaRegime).ToList();
        return snap.Vrp20d is not null && snap.Vrp20d > 0
            && gammaStreak.Count == 2 && gammaStreak.All(g => g == "positive_gamma");
    }

    /// <summary>
    /// WHICH CONTRACTS. Return legs as (Type, Strike, Expiry, Side), Side -1 = sell, +1 = buy.
    ///
    /// Default: put credit spread - sell the put closest to cfg.ShortDelta, buy the put cfg.Width points lower.
    /// </summary>
    public static List<Leg>? SelectLegs(List<OptionQuote> chain, Snapshot snap, Config cfg)
    {
        var puts = chain.Where(o => o.Type == "P" && o.Delta is not null && o.Bid > 0).ToList();
        if (puts.Count == 0)
            return null;
        OptionQuote shortPut = puts.MinBy(o => Math.Abs(Math.Abs(o.Delta!.Value) - cfg.ShortDelta))!;
        var lower = puts.Where(o => o.Strike <= shortPut.Strike - cfg.Width).ToList();
        if (lower.Count == 0)
            return null;
        OptionQuote longPut = lower.MaxBy(o => o.Strike)!;
        return
        [
            new Leg("P", shortPut.Strike, shortPut.Expiry, Side: -1),
            new Leg("P", longPut.Strike, longPut.Expiry, Side: +1),
        ];
    }

    /// <summary>
    /// EXIT RULES. Return a reason string to close the position at this decision time, or null to hold.
    ///
    /// pos.PnlMid is the open profit of the whole position in dollars at mid. pos.Credit is the net premium taken in,
    /// after commission: positive for a credit, negative for a debit, so the targets below use its size.
    /// Default: take 50% of the credit, stop at 2x the credit, close at 21 days left, or close after 2 sessions
    /// of negative dealer gamma.
    /// </summary>
    public static string? ExitRules(Position pos, Snapshot snap, IReadOnlyList<Snapshot> history, Config cfg)
    {
        if (pos.PnlMid >= cfg.ProfitTarget * Math.Abs(pos.Credit))
            return "profit_target";
        if (pos.PnlMid <= -cfg.StopLoss * Math.Abs(pos.Credit))
            return "stop_loss";
        if (pos.Dte(snap.Date) <= cfg.ExitDte)
            return "time_exit";
        if (history.Count > 0 && history[^1].GammaRegime == "negative_gamma" && snap.GammaRegime == "negative_gamma")
            return "negative_gamma_2_sessions";
        return null;
    }
}

// ============================================================================================================
// Engine (no changes needed below to run a new strategy)
// ============================================================================================================

/// <summary>One option leg. Side -1 = sell, +1 = buy.</summary>
public sealed record Leg(string Type, double Strike, string Expiry, int Side);

public sealed class Position
{
    public required List<Leg> Legs { get; init; }
    public required string EntryDate { get; init; }
    public required double Credit { get; init; }  // net premium for the whole position in dollars after commission (negative = debit)
    public Dictionary<int, OptionQuote> Marks { get; init; } = [];  // last quote per leg
    public double PnlMid { get; set; }

    public string Expiry => Legs.Select(leg => leg.Expiry).Min(StringComparer.Ordinal)!;

    public int Dte(string date) => Engine.ParseDate(Expiry).DayNumber - Engine.ParseDate(date).DayNumber;
}

public sealed record TradeRow(string EntryDate, string ExitDate, string Expiry, string Legs, double Credit, double Pnl,
                              string ExitReason, int DaysHeld);

public sealed record EquityRow(string Date, double Realized, double Unrealized, double Equity, int OpenPositions,
                               string? GammaRegime, double? Vrp20d);

public static class Engine
{
    /// <summary>Price for buying (side +1) or selling (side -1) one contract: mid moved toward the ask or bid.</summary>
    public static double FillPrice(OptionQuote quote, int side, double slippage)
    {
        double bid = Need(quote.Bid, "bid"), ask = Need(quote.Ask, "ask");
        double mid = (bid + ask) / 2;
        return side > 0 ? mid + slippage * (ask - mid) : mid - slippage * (mid - bid);
    }

    /// <summary>Expiry closest to cfg.TargetDte inside [MinDte, MaxDte] that has a chain (Friday, else Thursday).</summary>
    public static (string Expiry, List<OptionQuote> Chain)? FindExpiry(IFlashAlphaData fa, string symbol, string at, Config cfg)
    {
        DateOnly today = ParseDate(at[..10]);
        var fridays = Enumerable.Range(cfg.MinDte, Math.Max(0, cfg.MaxDte - cfg.MinDte + 1))
                                .Select(d => today.AddDays(d))
                                .Where(d => d.DayOfWeek == DayOfWeek.Friday);
        foreach (DateOnly friday in fridays.OrderBy(d => Math.Abs(d.DayNumber - today.DayNumber - cfg.TargetDte)))
        {
            foreach (DateOnly day in new[] { friday, friday.AddDays(-1) })  // Thursday when the Friday is a holiday
            {
                List<OptionQuote> chain = fa.Chain(symbol, at, Iso(day));
                if (chain.Count > 0)
                    return (Iso(day), chain);
            }
        }
        return null;
    }

    public static IEnumerable<string> TradingDays(string start, string end)
    {
        for (DateOnly day = ParseDate(start), stop = ParseDate(end); day <= stop; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                yield return Iso(day);
        }
    }

    public static Summary Run(Config cfg, IFlashAlphaData fa, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var openPositions = new List<Position>();
        var trades = new List<TradeRow>();
        var equity = new List<EquityRow>();
        var history = new List<Snapshot>();
        double realized = 0.0;
        foreach (string date in TradingDays(cfg.Start, cfg.End))
        {
            string at = $"{date}T{cfg.DecisionTime}:00";
            Snapshot? snap = Snapshots.BuildSnapshot(fa, cfg.Symbol, at);
            if (snap is null)
                continue;

            // 1. Mark and exit open positions
            foreach (Position pos in openPositions.ToList())
            {
                double valueMid = 0.0;
                bool onExpiry = pos.Dte(date) <= 0;  // expiry day, or the first session after it when that day had no data
                for (int i = 0; i < pos.Legs.Count; i++)
                {
                    Leg leg = pos.Legs[i];
                    OptionQuote? q = onExpiry ? null : fa.Option(cfg.Symbol, at, leg.Expiry, leg.Strike, leg.Type);
                    if (q is { Bid: not null, Ask: > 0 })
                        pos.Marks[i] = q;
                    double mid = (Need(pos.Marks[i].Bid, "bid") + Need(pos.Marks[i].Ask, "ask")) / 2;
                    valueMid += leg.Side * mid * 100 * cfg.Contracts;
                }
                pos.PnlMid = pos.Credit + valueMid;  // credit received plus the (negative) value of what we hold
                string? reason = onExpiry ? "expiry" : Strategy.ExitRules(pos, snap, history, cfg);
                if (reason is null)
                    continue;
                double closeValue;
                if (onExpiry)  // settle at intrinsic value against the spot at the decision time
                {
                    double spot = Need(snap.Spot, "spot");
                    closeValue = Py.Sum(pos.Legs.Select(leg =>
                        leg.Side * Math.Max(0.0, leg.Type == "P" ? leg.Strike - spot : spot - leg.Strike)));
                }
                else
                {
                    closeValue = Py.Sum(pos.Legs.Select((leg, i) =>
                        leg.Side * FillPrice(pos.Marks[i], -leg.Side, cfg.Slippage)));
                }
                double costs = onExpiry ? 0.0 : cfg.Commission * pos.Legs.Count * cfg.Contracts;
                double pnl = pos.Credit + closeValue * 100 * cfg.Contracts - costs;
                realized += pnl;
                openPositions.Remove(pos);
                trades.Add(new TradeRow(
                    EntryDate: pos.EntryDate,
                    ExitDate: date,
                    Expiry: pos.Expiry,
                    Legs: string.Join(" ", pos.Legs.Select(l => $"{(l.Side > 0 ? "+" : "-")}{l.Type}{FlashAlphaData.StrikeText(l.Strike)}")),
                    Credit: Py.Round(pos.Credit, 2),
                    Pnl: Py.Round(pnl, 2),
                    ExitReason: reason,
                    DaysHeld: ParseDate(date).DayNumber - ParseDate(pos.EntryDate).DayNumber));
            }

            // 2. Entries
            if (openPositions.Count < cfg.MaxPositions && Strategy.EntryRules(snap, history, cfg))
            {
                var found = FindExpiry(fa, cfg.Symbol, at, cfg);
                List<Leg>? legs = found is null ? null : Strategy.SelectLegs(found.Value.Chain, snap, cfg);
                if (found is not null && legs is { Count: > 0 })
                {
                    var byKey = new Dictionary<(string Type, double Strike, string Expiry), OptionQuote>();
                    foreach (OptionQuote o in found.Value.Chain)
                        byKey[(o.Type, o.Strike, o.Expiry)] = o;
                    var missing = legs.Where(leg => !byKey.ContainsKey((leg.Type, leg.Strike, leg.Expiry))).ToList();
                    if (missing.Count > 0)
                        throw new InvalidOperationException(
                            $"select_legs must pick contracts from the chain it receives: {string.Join(", ", missing)}");
                    var quotes = new Dictionary<int, OptionQuote>();
                    for (int i = 0; i < legs.Count; i++)
                        quotes[i] = byKey[(legs[i].Type, legs[i].Strike, legs[i].Expiry)];
                    double openValue = Py.Sum(legs.Select((leg, i) => leg.Side * FillPrice(quotes[i], leg.Side, cfg.Slippage)));
                    double credit = -openValue * 100 * cfg.Contracts - cfg.Commission * legs.Count * cfg.Contracts;
                    var pos = new Position { Legs = legs, EntryDate = date, Credit = credit, Marks = quotes };
                    pos.PnlMid = credit + Py.Sum(legs.Select((leg, i) =>
                        leg.Side * (Need(quotes[i].Bid, "bid") + Need(quotes[i].Ask, "ask")) / 2)) * 100 * cfg.Contracts;
                    openPositions.Add(pos);
                }
            }

            double unrealized = Py.Sum(openPositions.Select(p => p.PnlMid));
            equity.Add(new EquityRow(date, Py.Round(realized, 2), Py.Round(unrealized, 2), Py.Round(realized + unrealized, 2),
                                     openPositions.Count, snap.GammaRegime, snap.Vrp20d));
            history.Add(snap);
        }

        WriteCsv(Path.Combine(outDir, "trades.csv"),
                 ["entry_date", "exit_date", "expiry", "legs", "credit", "pnl", "exit_reason", "days_held"],
                 trades.Select(t => new[]
                 {
                     t.EntryDate, t.ExitDate, t.Expiry, t.Legs, Py.FloatStr(t.Credit), Py.FloatStr(t.Pnl), t.ExitReason,
                     t.DaysHeld.ToString(CultureInfo.InvariantCulture),
                 }));
        WriteCsv(Path.Combine(outDir, "equity.csv"),
                 ["date", "realized", "unrealized", "equity", "open_positions", "gamma_regime", "vrp_20d"],
                 equity.Select(e => new[]
                 {
                     e.Date, Py.FloatStr(e.Realized), Py.FloatStr(e.Unrealized), Py.FloatStr(e.Equity),
                     e.OpenPositions.ToString(CultureInfo.InvariantCulture), e.GammaRegime ?? "",
                     e.Vrp20d is null ? "" : Py.JsonNumberStr(e.Vrp20d.Value),
                 }));
        return Summarize(trades, equity);
    }

    /// <summary>Writes a header and rows like Python's csv module (CRLF line ends); no rows gives an empty file.</summary>
    public static void WriteCsv(string path, string[] header, IEnumerable<string[]> rows)
    {
        static string Field(string s) =>
            s.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        var lines = rows.Select(r => string.Join(",", r.Select(Field))).ToList();
        if (lines.Count == 0)
        {
            File.WriteAllText(path, "");
            return;
        }
        var text = new StringBuilder();
        foreach (string line in lines.Prepend(string.Join(",", header.Select(Field))))
            text.Append(line).Append("\r\n");
        File.WriteAllText(path, text.ToString());
    }

    public static Summary Summarize(List<TradeRow> trades, List<EquityRow> equity)
    {
        var pnls = trades.Select(t => t.Pnl).ToList();
        var curve = equity.Select(e => e.Equity).ToList();
        var daily = curve.Select((v, i) => v - (i == 0 ? 0.0 : curve[i - 1])).ToList();
        double sd = 0;
        if (daily.Count > 1)
        {
            double mean = Py.Sum(daily) / daily.Count;
            // Math.Pow(x, 0.5) rather than Math.Sqrt to match Python's "** 0.5" to the last bit.
            sd = Math.Pow(Py.Sum(daily.Select(x => Math.Pow(x - mean, 2))) / (daily.Count - 1), 0.5);
        }
        double peak = 0.0, maxDd = 0.0;
        foreach (double v in curve)
        {
            peak = Math.Max(peak, v);
            maxDd = Math.Max(maxDd, peak - v);
        }
        var wins = pnls.Where(p => p > 0).ToList();
        var losses = pnls.Where(p => p <= 0).ToList();  // break-even trades count as losses
        return new Summary(
            Sessions: equity.Count,
            Trades: trades.Count,
            WinRate: pnls.Count > 0 ? Py.Round((double)wins.Count / pnls.Count, 3) : null,
            TotalPnl: Py.Round(Py.Sum(pnls), 2),
            AvgWin: wins.Count > 0 ? Py.Round(Py.Sum(wins) / wins.Count, 2) : null,
            AvgLoss: losses.Count > 0 ? Py.Round(Py.Sum(losses) / losses.Count, 2) : null,
            MaxDrawdown: Py.Round(maxDd, 2),
            Sharpe: sd > 0 ? Py.Round(Py.Sum(daily) / daily.Count / sd * Math.Sqrt(252), 2) : null,
            ExitReasons: trades.GroupBy(t => t.ExitReason).Select(g => KeyValuePair.Create(g.Key, g.Count())).ToList(),
            OpenAtEnd: equity.Count > 0 ? equity[^1].OpenPositions : 0);
    }

    public static DateOnly ParseDate(string iso) => DateOnly.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static double Need(double? value, string field) =>
        value ?? throw new InvalidDataException($"option quote has no {field}");
}

public sealed record Summary(int Sessions, int Trades, double? WinRate, double TotalPnl, double? AvgWin, double? AvgLoss,
                             double MaxDrawdown, double? Sharpe, List<KeyValuePair<string, int>> ExitReasons, int OpenAtEnd)
{
    /// <summary>Indented JSON with the same keys, order and number text as the Python version.</summary>
    public string ToJson()
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("sessions", Sessions);
            w.WriteNumber("trades", Trades);
            WriteFloat(w, "win_rate", WinRate);
            WriteFloat(w, "total_pnl", TotalPnl);
            WriteFloat(w, "avg_win", AvgWin);
            WriteFloat(w, "avg_loss", AvgLoss);
            WriteFloat(w, "max_drawdown", MaxDrawdown);
            WriteFloat(w, "sharpe", Sharpe);
            w.WriteStartObject("exit_reasons");
            foreach (var (reason, count) in ExitReasons)
                w.WriteNumber(reason, count);
            w.WriteEndObject();
            w.WriteNumber("open_at_end", OpenAtEnd);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteFloat(Utf8JsonWriter w, string name, double? value)
    {
        w.WritePropertyName(name);
        if (value is null)
            w.WriteNullValue();
        else
            w.WriteRawValue(Py.FloatStr(value.Value));
    }
}

/// <summary>
/// Python number behaviour, so this port reproduces the Python version's output exactly.
/// </summary>
public static class Py
{
    /// <summary>
    /// Python's round(x, digits): rounds the exact binary value of x half to even. Math.Round(x, digits) first
    /// multiplies by 10^digits, and that multiplication can itself round, so it can disagree with Python.
    /// </summary>
    public static double Round(double x, int digits)
    {
        if (!double.IsFinite(x))
            return x;
        // x == mantissa * 2^exponent exactly
        long bits = BitConverter.DoubleToInt64Bits(x);
        int exponent = (int)((bits >> 52) & 0x7FF);
        long mantissa = bits & 0xF_FFFF_FFFF_FFFF;
        if (exponent == 0)
            exponent = 1;
        else
            mantissa |= 1L << 52;
        exponent -= 1075;
        BigInteger scaled = mantissa * BigInteger.Pow(10, digits);  // |x| * 10^digits == scaled * 2^exponent
        BigInteger units;
        if (exponent >= 0)
        {
            units = scaled << exponent;
        }
        else
        {
            BigInteger denominator = BigInteger.One << -exponent;
            units = BigInteger.DivRem(scaled, denominator, out BigInteger remainder);
            BigInteger twice = remainder * 2;
            if (twice > denominator || (twice == denominator && !units.IsEven))
                units += 1;
        }
        // units / 10^digits, parsed so the result is the double nearest the rounded decimal, as in Python
        string text = string.Create(CultureInfo.InvariantCulture, $"{units}e-{digits}");
        double result = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return bits < 0 ? -result : result;  // keeps the sign of a negative number that rounds to zero (-0.0)
    }

    /// <summary>Python 3.12+ sum() of floats: Neumaier compensated summation, not a plain running total.</summary>
    public static double Sum(IEnumerable<double> values)
    {
        double total = 0.0, compensation = 0.0;
        foreach (double x in values)
        {
            double t = total + x;
            compensation += Math.Abs(total) >= Math.Abs(x) ? (total - t) + x : (x - t) + total;
            total = t;
        }
        return compensation != 0 && double.IsFinite(compensation) ? total + compensation : total;
    }

    /// <summary>Python's str() of a float: shortest round-trip digits, ".0" on whole numbers (50.7, 0.0, -4.3).</summary>
    public static string FloatStr(double x)
    {
        string s = JsonNumberStr(x);
        return double.IsFinite(x) && !s.Contains('.') && !s.Contains('e') ? s + ".0" : s;
    }

    /// <summary>Python's str() of a number read from JSON: a whole number is an int there, so no ".0" (0.53, 2).</summary>
    public static string JsonNumberStr(double x) => x.ToString("R", CultureInfo.InvariantCulture).Replace('E', 'e');

    /// <summary>Python's format(x, ".{digits}g"): that many significant digits, no trailing zeros.</summary>
    public static string FormatG(double x, int digits) =>
        x.ToString("G" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture).Replace('E', 'e');
}

/// <summary>An error that ends the run with a message and no stack trace.</summary>
public sealed class FatalException(string message, int exitCode = 1) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var (cfg, outDir, cacheDir) = ParseArgs(args);
            var fa = new FlashAlphaData(Environment.GetEnvironmentVariable("FLASHALPHA_API_KEY") ?? "", cacheDir);
            Summary summary = Engine.Run(cfg, fa, outDir);
            Console.WriteLine(summary.ToJson());
            Console.WriteLine($"API calls this run: {fa.Calls}. Trades: {Path.Combine(outDir, "trades.csv")}. " +
                              $"Equity: {Path.Combine(outDir, "equity.csv")}");
            return 0;
        }
        catch (FatalException e)
        {
            if (e.Message.Length > 0)
                Console.Error.WriteLine(e.Message);
            return e.ExitCode;
        }
    }

    /// <summary>One flag per Config property (MaxPositions becomes --max-positions), plus --out and --cache.</summary>
    private static (Config Cfg, string OutDir, string CacheDir) ParseArgs(string[] args)
    {
        var cfg = new Config();
        string outDir = "results", cacheDir = ".fa_cache";
        PropertyInfo[] props = typeof(Config).GetProperties();
        var flags = props.ToDictionary(p => "--" + Kebab(p.Name));
        for (int i = 0; i < args.Length; i++)
        {
            string name = args[i], value;
            if (name is "-h" or "--help")
            {
                Console.WriteLine("usage: dotnet run -- [options]\n\nFlashAlpha options backtester\n\noptions:");
                foreach (var (flag, prop) in flags)
                    Console.WriteLine($"  {flag,-18} default: {Convert.ToString(prop.GetValue(cfg), CultureInfo.InvariantCulture)}");
                Console.WriteLine($"  {"--out",-18} default: {outDir}");
                Console.WriteLine($"  {"--cache",-18} default: {cacheDir}");
                throw new FatalException("", 0);
            }
            int eq = name.IndexOf('=');
            if (eq > 0)
            {
                (name, value) = (name[..eq], name[(eq + 1)..]);
            }
            else
            {
                if (i + 1 >= args.Length)
                    throw new FatalException($"argument {name}: expected one argument", 2);
                value = args[++i];
            }
            if (name == "--out")
            {
                outDir = value;
            }
            else if (name == "--cache")
            {
                cacheDir = value;
            }
            else if (flags.TryGetValue(name, out PropertyInfo? prop))
            {
                try
                {
                    object parsed = prop.PropertyType == typeof(int) ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
                                  : prop.PropertyType == typeof(double) ? double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)
                                  : value;
                    prop.SetValue(cfg, parsed);
                }
                catch (FormatException)
                {
                    throw new FatalException($"argument {name}: invalid {prop.PropertyType.Name} value: '{value}'", 2);
                }
            }
            else
            {
                throw new FatalException($"unrecognized argument: {name} (see --help)", 2);
            }
        }
        return (cfg, outDir, cacheDir);
    }

    private static string Kebab(string pascal) =>
        string.Concat(pascal.Select((ch, i) => char.IsUpper(ch) && i > 0 ? "-" + char.ToLowerInvariant(ch)
                                                                          : char.ToLowerInvariant(ch).ToString()));
}
