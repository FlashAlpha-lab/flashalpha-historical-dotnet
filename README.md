# FlashAlpha.Historical (.NET)

Official .NET SDK for the **FlashAlpha Historical API** — point-in-time
replay of every live analytics endpoint. Ask what GEX, gamma flip, VRP,
narrative, max pain, or the full stock summary looked like at any **minute
back to 2018-04-16**, in the same response shape as the live API.

> **Point-in-time replay since 2018.** Backtest dealer positioning (GEX, VRP,
> vanna/charm, max pain) at any minute since 2018-04-16, then trade the same
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
of aggregate vanna/charm exposure and point-in-time replay since 2018.

Quant teams, prop desks, and vol funds:
**[flashalpha.com/for-quant-teams](https://flashalpha.com/for-quant-teams?utm_source=github&utm_medium=readme&utm_campaign=repo-flashalpha-historical-dotnet)**
