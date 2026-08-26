# FlashAlpha.Historical (.NET)

Official .NET SDK for the **FlashAlpha Historical API** — point-in-time
replay of every live analytics endpoint. Ask what GEX, gamma flip, VRP,
narrative, max pain, or the full stock summary looked like at any **minute
back to 2017-01-03**, in the same response shape as the live API.

> **Point-in-time replay since 2017.** Backtest dealer positioning (GEX, VRP,
> vanna/charm, max pain) at any minute since 2017-01-03, then trade the same
> endpoints live. No look-ahead, no training-serving skew. The Historical API
> is an **Alpha tier** capability.

```bash
dotnet add package FlashAlpha.Historical
```

.NET 8.0+. Same `X-Api-Key` you use for `api.flashalpha.com` — Alpha plan or
higher on every endpoint.

## Quickstart

```csharp
using FlashAlpha.Historical;

using var hx = new FlashAlphaHistoricalClient("YOUR_API_KEY");

// One snapshot — what dealer positioning looked like during the COVID crash
var snap = await hx.ExposureSummaryAsync("SPY", "2020-03-16T15:30:00");
Console.WriteLine(snap.GetProperty("regime").GetString());
// → "negative_gamma"
```

The `at` parameter accepts strings (`"2026-03-05T15:30:00"` or
`"2026-03-05"` → defaults to 16:00 ET) and `DateTime` overloads.

## Data provenance: `data_as_of`

Every successful response carries `data_as_of`, reporting when each upstream feed last
delivered to the node that answered, plus `endpoint_version` identifying the deployment
that produced it. Every endpoint on this replay service returns a JSON object, so unlike
the live SDK there is no bare-array case where the envelope is unavailable.

```csharp
var gex = await client.GexTypedAsync("SPY", "2024-03-15T14:30:00Z");

gex.ArchiveAsOf.EquityOptionsFeed;  // "2024-03-15T14:29:58.100Z"  the rows replayed
gex.ArchiveAsOf.OiFeed;             // "2024-03-14T20:00:00.000Z"  prior session's close
gex.DataAsOf.EquityOptionsFeed;     // null - a replay node consumes no live feed
gex.EndpointVersion;                // the deployment that answered
```

Every response model inherits `FlashAlphaResponse`, which carries `EndpointVersion`,
`DataAsOf` and `ArchiveAsOf`, so the envelope is a typed member on all of them rather
than a field the deserializer silently drops.

| Field | Feed | Expected cadence |
|---|---|---|
| `node` | Which node answered | Nodes hydrate independently |
| `equity_feed` | Equity and ETF spot quotes | seconds, during market hours |
| `equity_options_feed` | Equity and ETF option quotes | seconds, during market hours |
| `index_feed` | Index spot (SPX, RUT, VIX and the other index roots) | seconds, during market hours |
| `index_options_feed` | Index option quotes | seconds, during market hours |
| `futures_feed` | Futures prices | seconds, during the futures session |
| `futures_options_feed` | Futures option quotes | seconds, during the futures session |
| `flow_feed` | Classified options and stock trade tape | seconds, during market hours |
| `oi_feed` | Settled open interest | daily, dated to the prior 16:00 ET close |
| `macro_feed` | VIX, VVIX, SKEW, MOVE, SPX, Fear & Greed | minutes; reports its OLDEST component |

Historical responses carry a second object, `archive_as_of`, in the same shape: the
vintage of the archive rows actually replayed for the timestamp you requested. Its
every feed in `data_as_of` is `null`, because a replay node reads the archive and consumes no
live feed.

`archive_as_of` is what makes an archive gap detectable. Request a moment with no row
and the query returns the most recent earlier row; nothing else in the response
distinguishes the two. Point-in-time work should read it and drop or flag observations
whose inputs precede the requested instant by more than the study tolerates.

### How to read it

- **Check the feeds your call depends on.** A GEX call on an equity is answered from
  `equity_feed`, `equity_options_feed` and `oi_feed`. `futures_feed` being `null` in that
  response says nothing about the answer.
- **Compare against the cadence, not the clock.** `oi_feed` at the previous session's
  close is correct: settled open interest is published once per session, so on a Monday
  the newest figure that exists is Friday's. An options feed an hour behind during the
  regular session is not correct.
- **`null` means "not seen on this node", not "broken".** A node that has never been
  asked for a futures symbol has never opened that feed.
- **Spot and options are separate on purpose.** They arrive over different pipes and can
  fail independently.
- **It evidences feed activity, not per-contract freshness.** An illiquid strike may not
  have quoted for hours while its feed is healthy.
- **`data_as_of` is not `as_of`.** `as_of` is response-generation time or the newest
  contract in the payload, depending on the endpoint. `data_as_of` describes the feeds
  behind it.

Full reference: <https://flashalpha.com/docs/lab-api-overview#response-envelope> and the
methodology whitepaper at <https://flashalpha.com/methodology#freshness-reporting>.
## Backtesting

```csharp
using FlashAlpha.Historical;

using var hx = new FlashAlphaHistoricalClient(Environment.GetEnvironmentVariable("FLASHALPHA_API_KEY")!);

var bt = new Backtester(hx, Backtester.StockSummaryEndpoint, "SPY");

var results = await bt.RunAsync(
    Replay.IterDays(new(2024, 1, 2), new(2024, 3, 29)),
    (at, snap) => new
    {
        Vrp = snap.GetProperty("volatility").GetProperty("vrp").GetDouble(),
        Regime = snap.GetProperty("exposure").GetProperty("regime").GetString(),
    });

Console.WriteLine($"days replayed: {results.Count}");
```

### Minute-level

```csharp
await foreach (var step in Replay.RunAsync(
    hx, Backtester.ExposureSummaryEndpoint, "SPY",
    Replay.IterMinutes(new(2025, 1, 15), new(2025, 1, 15), stepMinutes: 15)))
{
    Console.WriteLine($"{step.At} {step.Response.GetProperty("regime").GetString()}");
}
```

## API surface

Every analytics method takes a required `at` string (or `DateTime` overload):

| Method | Endpoint |
|---|---|
| `TickersAsync(symbol?)` | `/v1/tickers` |
| `StockQuoteAsync(t, at)` | `/v1/stockquote/{t}` |
| `OptionQuoteAsync(t, at, expiry?, strike?, type?)` | `/v1/optionquote/{t}` |
| `SurfaceAsync(s, at)` | `/v1/surface/{s}` |
| `GexAsync(s, at, expiration?, minOi?)` | `/v1/exposure/gex/{s}` |
| `DexAsync(s, at, expiration?)` | `/v1/exposure/dex/{s}` |
| `VexAsync(s, at, expiration?)` | `/v1/exposure/vex/{s}` |
| `ChexAsync(s, at, expiration?)` | `/v1/exposure/chex/{s}` |
| `ExposureSummaryAsync(s, at)` | `/v1/exposure/summary/{s}` |
| `ExposureLevelsAsync(s, at)` | `/v1/exposure/levels/{s}` |
| `NarrativeAsync(s, at)` | `/v1/exposure/narrative/{s}` |
| `ZeroDteAsync(s, at, strikeRange?)` | `/v1/exposure/zero-dte/{s}` |
| `MaxPainAsync(s, at, expiration?)` | `/v1/maxpain/{s}` |
| `StockSummaryAsync(s, at)` | `/v1/stock/{s}/summary` |
| `VolatilityAsync(s, at)` | `/v1/volatility/{s}` |
| `AdvVolatilityAsync(s, at)` | `/v1/adv_volatility/{s}` |
| `VrpAsync(s, at)` | `/v1/vrp/{s}` |

## Errors

```csharp
using FlashAlpha.Historical;

try {
    await hx.ExposureSummaryAsync("SPY", "2017-01-01");
}
catch (NoDataException) { /* outside coverage / inside gap */ }
catch (InvalidAtException) { /* 400 invalid_at */ }
catch (TierRestrictedException ex) {
    Console.WriteLine($"need {ex.RequiredPlan}, have {ex.CurrentPlan}");
}
```

| Exception | Status |
|---|---|
| `FlashAlphaHistoricalException` | base |
| `AuthenticationException` | 401 |
| `TierRestrictedException` | 403 — needs Alpha plan |
| `InvalidAtException` | 400 — bad `at` format |
| `NoDataException` | 404 — outside coverage / inside gap |
| `SymbolNotFoundException` | 404 — symbol not at this `at` |
| `NoCoverageException` | 404 — symbol not in historical dataset |
| `InsufficientDataException` | 404 — surface grid too sparse |
| `RateLimitException` | 429 |
| `ServerException` | 5xx |

## License

MIT

## Get access

The Historical API requires the **Alpha tier ($1,499/mo)**: the only public source
of aggregate vanna/charm exposure and point-in-time replay since 2017.

Quant teams, prop desks, and vol funds:
**[flashalpha.com/for-quant-teams](https://flashalpha.com/for-quant-teams?utm_source=github&utm_medium=readme&utm_campaign=repo-flashalpha-historical-dotnet)**
