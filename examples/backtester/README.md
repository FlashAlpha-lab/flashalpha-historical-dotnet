# FlashAlpha options backtester (C#)

A complete, runnable backtest template in one file. It replays the FlashAlpha Historical API day by day, decides
entries and exits from the same endpoint outputs you would read live, fills trades on real historical bid/ask
quotes, and writes `trades.csv` and `equity.csv`.

Guide: [How to Backtest Options Strategies in Python](https://flashalpha.com/articles/how-to-backtest-options-strategies-python-flashalpha-historical-api).
The reference version is the [Python backtester](https://github.com/FlashAlpha-lab/flashalpha-historical-python/tree/main/examples/backtester).

This is a C# port of `flashalpha_backtest.py`. Given the same data, it produces the same trades, equity curve and
summary, number for number.

## Requirements

- .NET 8 SDK or newer. No NuGet packages.
- A FlashAlpha API key on the Alpha plan or higher (Historical API access).

## Run it

Set your API key, then run from this folder.

Bash:

```bash
export FLASHALPHA_API_KEY=your_key
dotnet run -c Release -- --symbol SPY --start 2024-01-02 --end 2024-06-28
```

PowerShell:

```powershell
$env:FLASHALPHA_API_KEY = "your_key"
dotnet run -c Release -- --symbol SPY --start 2024-01-02 --end 2024-06-28
```

The program prints a JSON summary (sessions, trades, win rate, total P&L, average win and loss, max drawdown,
Sharpe, exit reasons, positions still open) and writes:

- `results/trades.csv` - one row per closed trade: entry and exit dates, expiry, legs, credit, P&L, exit reason,
  days held.
- `results/equity.csv` - one row per session: realized, unrealized and total P&L, open positions, gamma regime and
  20-day VRP.

## Options

Every setting in the `Config` record is also a command-line flag. `MaxPositions` becomes `--max-positions`, and so
on. Run `dotnet run -- --help` for the full list with defaults.

| Flag | Default | Meaning |
|---|---|---|
| `--symbol` | SPY | Underlying |
| `--start`, `--end` | 2024-01-02, 2024-06-28 | Backtest window (inclusive) |
| `--decision-time` | 15:45 | ET minute used for every decision and fill |
| `--target-dte`, `--min-dte`, `--max-dte` | 35, 25, 50 | Expiry selection window, in calendar days |
| `--short-delta` | 0.20 | Sell the put closest to this absolute delta |
| `--width` | 5.0 | Buy the put this many points lower |
| `--contracts` | 1 | Spreads per trade |
| `--max-positions` | 1 | Open positions allowed at once |
| `--profit-target` | 0.50 | Close at this fraction of the entry credit |
| `--stop-loss` | 2.00 | Close when the loss reaches this multiple of the credit |
| `--exit-dte` | 21 | Close with this many calendar days left |
| `--slippage` | 0.5 | 0 = fill at mid, 1 = fill at the bid or ask |
| `--commission` | 0.65 | Per contract per leg, each side |
| `--out` | results | Output folder |
| `--cache` | .fa_cache | API response cache folder |

Example with two positions allowed at once:

```bash
dotnet run -c Release -- --start 2024-07-01 --end 2024-12-31 --max-positions 2 --out results_h2
```

## Change the strategy

All strategy logic lives in the `Strategy` class in `Program.cs`, under the banner
`STRATEGY: edit these three methods`. You don't need to change anything else.

- `EntryRules(snap, history, cfg)` - return `true` to open a position. The default enters when implied vol is
  above 20-day realized (VRP > 0) and dealers have been long gamma for two sessions in a row.
- `SelectLegs(chain, snap, cfg)` - return the legs to trade from the option chain. The default sells the put
  closest to 0.20 delta and buys the put 5 points lower.
- `ExitRules(pos, snap, history, cfg)` - return a reason string to close, or `null` to hold. The default takes 50%
  of the credit, stops at 2x the credit, closes at 21 days left, or closes after two sessions of negative dealer
  gamma.

The rules read a `Snapshot` record built by `Snapshots.BuildSnapshot`. It holds spot, the gamma regime, net GEX,
the gamma flip and the VRP fields. A field is `null` when the API had no value, so you can tell missing data from
zero. To use another FlashAlpha endpoint in your rules, add a property to `Snapshot` and fill it in
`BuildSnapshot`. To read more option fields in `SelectLegs`, add a property to `OptionQuote`.

## Run the tests

The offline tests in `tests/FlashAlphaBacktest.Tests` replace the API with a stub, so they need no key and no
network. They check the fill math, leg selection, the exact accounting of one trade (credit 56.70, P&L 32.90),
strike formatting, and holiday skipping. They mirror `test_flashalpha_backtest.py`.

```bash
dotnet test tests/FlashAlphaBacktest.Tests
```

The test project uses xUnit from NuGet. The backtester itself still needs no packages. The stub implements
`IFlashAlphaData`, the interface `Engine.Run` reads data through, so you can also run a backtest on your own data.

## API usage and the cache

Every API response is saved in `.fa_cache/`. A rerun with changed rules costs no API calls for days already
fetched. A half-year run of the default strategy takes about 400 to 450 calls the first time and none after that.
The last line of output shows the number of calls made.

The cache file names and contents are compatible with the Python version, so both can share one `--cache` folder.

The client sends your key in the `X-Api-Key` header. It retries connection errors, timeouts, rate limits (429)
and server errors (5xx) up to 5 times. It waits for `Retry-After` when that header is a number of seconds, and
otherwise backs off 1, 2, 4, 8 and 16 seconds. A 404 means the archive has no data for that request and is cached
as empty.
