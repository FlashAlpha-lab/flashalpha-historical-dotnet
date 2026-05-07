using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FlashAlpha.Historical;

/// <summary>
/// Thin HTTP wrapper around the FlashAlpha Historical REST API.
/// Every analytics method takes a required <c>at</c> parameter (string or DateTime)
/// and returns the same response shape as the live API at that moment in history.
/// Base URL: https://historical.flashalpha.com.
/// </summary>
public sealed class FlashAlphaHistoricalClient : IDisposable
{
    /// <summary>Default base URL for the FlashAlpha Historical API.</summary>
    public const string DefaultBaseUrl = "https://historical.flashalpha.com";

    private readonly HttpClient _http;
    private bool _disposed;

    /// <summary>
    /// Creates a new <see cref="FlashAlphaHistoricalClient"/>.
    /// </summary>
    /// <param name="apiKey">Your FlashAlpha API key — same key as live. Alpha plan or higher.</param>
    /// <param name="baseUrl">Override the API base URL.</param>
    /// <param name="timeout">Request timeout in seconds. Default 60 (cold adv_volatility / stock_summary can take ~1.5s).</param>
    public FlashAlphaHistoricalClient(string apiKey, string? baseUrl = null, int timeout = 60)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key must not be null or empty.", nameof(apiKey));

        var effectiveBase = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');

        _http = new HttpClient
        {
            BaseAddress = new Uri(effectiveBase),
            Timeout = TimeSpan.FromSeconds(timeout),
        };
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Constructor for mocking — pass a pre-configured <see cref="HttpClient"/> with a test handler.</summary>
    public FlashAlphaHistoricalClient(HttpClient httpClient)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>Format an as-of <see cref="DateTime"/> as the ET wall-clock string the API expects.</summary>
    public static string FormatAt(DateTime at) => at.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Format an as-of <see cref="DateOnly"/> (yyyy-MM-dd → defaults to 16:00 ET on the API side).</summary>
    public static string FormatAt(DateOnly at) => at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string BuildQuery(Dictionary<string, string?> parameters)
    {
        var parts = new List<string>();
        foreach (var (key, value) in parameters)
        {
            if (value is not null)
                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }
        return parts.Count > 0 ? "?" + string.Join("&", parts) : string.Empty;
    }

    private async Task<JsonElement> GetAsync(string path, Dictionary<string, string?>? parameters = null, CancellationToken ct = default)
    {
        var query = parameters is not null ? BuildQuery(parameters) : string.Empty;
        using var response = await _http.GetAsync(path + query, ct).ConfigureAwait(false);
        return await HandleResponseAsync(response).ConfigureAwait(false);
    }

    private static async Task<JsonElement> HandleResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(body))
                return JsonDocument.Parse("{}").RootElement;
            return JsonDocument.Parse(body).RootElement;
        }

        JsonElement? json = null;
        string message = body;
        string? errCode = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var doc = JsonDocument.Parse(body);
                json = doc.RootElement;
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("error", out var errProp) && errProp.ValueKind == JsonValueKind.String)
                        errCode = errProp.GetString();
                    if (doc.RootElement.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String)
                        message = msgProp.GetString() ?? message;
                    else if (doc.RootElement.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
                        message = detailProp.GetString() ?? message;
                }
            }
            catch (JsonException) { /* body not JSON */ }
        }

        int statusCode = (int)response.StatusCode;

        switch (statusCode)
        {
            case 400:
                if (errCode == "invalid_at") throw new InvalidAtException(message, json);
                throw new FlashAlphaHistoricalException(message, 400, json);
            case 401:
                throw new AuthenticationException(message, json);
            case 403:
            {
                string? currentPlan = null;
                string? requiredPlan = null;
                if (json.HasValue && json.Value.ValueKind == JsonValueKind.Object)
                {
                    if (json.Value.TryGetProperty("current_plan", out var cp) && cp.ValueKind == JsonValueKind.String)
                        currentPlan = cp.GetString();
                    if (json.Value.TryGetProperty("required_plan", out var rp) && rp.ValueKind == JsonValueKind.String)
                        requiredPlan = rp.GetString();
                }
                throw new TierRestrictedException(message, currentPlan, requiredPlan, json);
            }
            case 404:
                if (errCode == "no_coverage") throw new NoCoverageException(message, json);
                if (errCode == "symbol_not_found") throw new SymbolNotFoundException(message, json);
                if (errCode == "insufficient_data") throw new InsufficientDataException(message, json);
                throw new NoDataException(message, json);
            case 429:
            {
                int? retryAfter = null;
                if (response.Headers.TryGetValues("Retry-After", out var values))
                {
                    foreach (var v in values)
                    {
                        if (int.TryParse(v, out var ra)) { retryAfter = ra; break; }
                    }
                }
                throw new RateLimitException(message, retryAfter, json);
            }
            default:
                if (statusCode >= 500) throw new ServerException(message, statusCode, json);
                throw new FlashAlphaHistoricalException(message, statusCode, json);
        }
    }

    private static string Seg(string s) => Uri.EscapeDataString(s);

    private static string D(double v) => v.ToString(CultureInfo.InvariantCulture);

    // ── Coverage ──────────────────────────────────────────────────────────────

    /// <summary>Lists every symbol with historical coverage. Pass <paramref name="symbol"/> to get a single object.</summary>
    public Task<JsonElement> TickersAsync(string? symbol = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string?>();
        if (symbol is not null) p["symbol"] = symbol;
        return GetAsync("/v1/tickers", p.Count > 0 ? p : null, ct);
    }

    /// <summary>
    /// Strongly-typed variant of <see cref="TickersAsync(string?, CancellationToken)"/> for
    /// the <b>list</b> form (no <c>symbol</c> filter). Returns a <see cref="Models.TickersResponse"/>
    /// POCO with per-symbol coverage rows. Use
    /// <see cref="TickerTypedAsync(string, CancellationToken)"/> for the single-symbol form.
    /// </summary>
    public async Task<Models.TickersResponse?> TickersTypedAsync(CancellationToken ct = default)
    {
        var element = await TickersAsync(null, ct).ConfigureAwait(false);
        return element.Deserialize<Models.TickersResponse>();
    }

    /// <summary>
    /// Strongly-typed variant of <see cref="TickersAsync(string?, CancellationToken)"/> for
    /// the <b>single-symbol</b> form (i.e. <c>?symbol=...</c>). Returns a
    /// <see cref="Models.TickersSingleResponse"/> POCO with the symbol's coverage range
    /// and healthy-day count.
    /// </summary>
    public async Task<Models.TickersSingleResponse?> TickerTypedAsync(string symbol, CancellationToken ct = default)
    {
        var element = await TickersAsync(symbol, ct).ConfigureAwait(false);
        return element.Deserialize<Models.TickersSingleResponse>();
    }

    // ── Market Data ───────────────────────────────────────────────────────────

    /// <summary>Stock bid/ask/mid/last at the requested minute.</summary>
    public Task<JsonElement> StockQuoteAsync(string ticker, string at, CancellationToken ct = default)
        => GetAsync($"/v1/stockquote/{Seg(ticker)}", new() { ["at"] = at }, ct);

    /// <inheritdoc cref="StockQuoteAsync(string, string, CancellationToken)"/>
    public Task<JsonElement> StockQuoteAsync(string ticker, DateTime at, CancellationToken ct = default)
        => StockQuoteAsync(ticker, FormatAt(at), ct);

    /// <summary>Option quote(s) + greeks + OI at the requested minute. With all three filters → single object.</summary>
    public Task<JsonElement> OptionQuoteAsync(string ticker, string at, string? expiry = null, double? strike = null, string? type = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string?> { ["at"] = at };
        if (expiry is not null) p["expiry"] = expiry;
        if (strike.HasValue) p["strike"] = D(strike.Value);
        if (type is not null) p["type"] = type;
        return GetAsync($"/v1/optionquote/{Seg(ticker)}", p, ct);
    }

    /// <inheritdoc cref="OptionQuoteAsync(string, string, string?, double?, string?, CancellationToken)"/>
    public Task<JsonElement> OptionQuoteAsync(string ticker, DateTime at, string? expiry = null, double? strike = null, string? type = null, CancellationToken ct = default)
        => OptionQuoteAsync(ticker, FormatAt(at), expiry, strike, type, ct);

    /// <summary>50×50 IV surface grid (tenor × log-moneyness). Throws <see cref="InsufficientDataException"/> on sparse days.</summary>
    public Task<JsonElement> SurfaceAsync(string symbol, string at, CancellationToken ct = default)
        => GetAsync($"/v1/surface/{Seg(symbol)}", new() { ["at"] = at }, ct);

    /// <inheritdoc cref="SurfaceAsync(string, string, CancellationToken)"/>
    public Task<JsonElement> SurfaceAsync(string symbol, DateTime at, CancellationToken ct = default)
        => SurfaceAsync(symbol, FormatAt(at), ct);

    /// <summary>
    /// Strongly-typed variant of <see cref="SurfaceAsync(string, string, CancellationToken)"/>.
    /// Returns a <see cref="Models.SurfaceResponse"/> POCO. The original
    /// <see cref="SurfaceAsync(string, string, CancellationToken)"/> remains unchanged.
    /// </summary>
    public async Task<Models.SurfaceResponse?> SurfaceTypedAsync(string symbol, string at, CancellationToken ct = default)
    {
        var element = await SurfaceAsync(symbol, at, ct).ConfigureAwait(false);
        return element.Deserialize<Models.SurfaceResponse>();
    }

    /// <inheritdoc cref="SurfaceTypedAsync(string, string, CancellationToken)"/>
    public Task<Models.SurfaceResponse?> SurfaceTypedAsync(string symbol, DateTime at, CancellationToken ct = default)
        => SurfaceTypedAsync(symbol, FormatAt(at), ct);

    // ── Exposure ──────────────────────────────────────────────────────────────

    /// <summary>Gamma exposure by strike.</summary>
    public Task<JsonElement> GexAsync(string symbol, string at, string? expiration = null, int? minOi = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string?> { ["at"] = at };
        if (expiration is not null) p["expiration"] = expiration;
        if (minOi.HasValue) p["min_oi"] = minOi.Value.ToString(CultureInfo.InvariantCulture);
        return GetAsync($"/v1/exposure/gex/{Seg(symbol)}", p, ct);
    }

    /// <inheritdoc cref="GexAsync(string, string, string?, int?, CancellationToken)"/>
    public Task<JsonElement> GexAsync(string symbol, DateTime at, string? expiration = null, int? minOi = null, CancellationToken ct = default)
        => GexAsync(symbol, FormatAt(at), expiration, minOi, ct);

    /// <summary>
    /// Strongly-typed variant of <see cref="GexAsync(string, string, string?, int?, CancellationToken)"/>.
    /// Returns a <see cref="Models.GexResponse"/> POCO with per-strike GEX rows plus
    /// gamma flip and net GEX label. The original
    /// <see cref="GexAsync(string, string, string?, int?, CancellationToken)"/> remains unchanged.
    /// </summary>
    public async Task<Models.GexResponse?> GexTypedAsync(string symbol, string at, string? expiration = null, int? minOi = null, CancellationToken ct = default)
    {
        var element = await GexAsync(symbol, at, expiration, minOi, ct).ConfigureAwait(false);
        return element.Deserialize<Models.GexResponse>();
    }

    /// <inheritdoc cref="GexTypedAsync(string, string, string?, int?, CancellationToken)"/>
    public Task<Models.GexResponse?> GexTypedAsync(string symbol, DateTime at, string? expiration = null, int? minOi = null, CancellationToken ct = default)
        => GexTypedAsync(symbol, FormatAt(at), expiration, minOi, ct);

    /// <summary>Delta exposure by strike.</summary>
    public Task<JsonElement> DexAsync(string symbol, string at, string? expiration = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string?> { ["at"] = at };
        if (expiration is not null) p["expiration"] = expiration;
        return GetAsync($"/v1/exposure/dex/{Seg(symbol)}", p, ct);
    }

    /// <summary>
    /// Strongly-typed variant of <see cref="DexAsync(string, string, string?, CancellationToken)"/>.
    /// Returns a <see cref="Models.DexResponse"/> POCO with per-strike DEX rows plus net DEX.
    /// </summary>
    public async Task<Models.DexResponse?> DexTypedAsync(string symbol, string at, string? expiration = null, CancellationToken ct = default)
    {
        var element = await DexAsync(symbol, at, expiration, ct).ConfigureAwait(false);
        return element.Deserialize<Models.DexResponse>();
    }

    /// <inheritdoc cref="DexTypedAsync(string, string, string?, CancellationToken)"/>
    public Task<Models.DexResponse?> DexTypedAsync(string symbol, DateTime at, string? expiration = null, CancellationToken ct = default)
        => DexTypedAsync(symbol, FormatAt(at), expiration, ct);

    /// <summary>Vanna exposure by strike.</summary>
    public Task<JsonElement> VexAsync(string symbol, string at, string? expiration = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string?> { ["at"] = at };
        if (expiration is not null) p["expiration"] = expiration;
        return GetAsync($"/v1/exposure/vex/{Seg(symbol)}", p, ct);
    }

    /// <summary>
    /// Strongly-typed variant of <see cref="VexAsync(string, string, string?, CancellationToken)"/>.
    /// Returns a <see cref="Models.VexResponse"/> POCO with per-strike VEX rows plus net VEX
    /// and a textual interpretation.
    /// </summary>
    public async Task<Models.VexResponse?> VexTypedAsync(string symbol, string at, string? expiration = null, CancellationToken ct = default)
    {
        var element = await VexAsync(symbol, at, expiration, ct).ConfigureAwait(false);
        return element.Deserialize<Models.VexResponse>();
    }

    /// <inheritdoc cref="VexTypedAsync(string, string, string?, CancellationToken)"/>
    public Task<Models.VexResponse?> VexTypedAsync(string symbol, DateTime at, string? expiration = null, CancellationToken ct = default)
        => VexTypedAsync(symbol, FormatAt(at), expiration, ct);

    /// <summary>Charm exposure by strike.</summary>
    public Task<JsonElement> ChexAsync(string symbol, string at, string? expiration = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string?> { ["at"] = at };
        if (expiration is not null) p["expiration"] = expiration;
        return GetAsync($"/v1/exposure/chex/{Seg(symbol)}", p, ct);
    }

    /// <summary>
    /// Strongly-typed variant of <see cref="ChexAsync(string, string, string?, CancellationToken)"/>.
    /// Returns a <see cref="Models.ChexResponse"/> POCO with per-strike CHEX rows plus net CHEX
    /// and a textual interpretation.
    /// </summary>
    public async Task<Models.ChexResponse?> ChexTypedAsync(string symbol, string at, string? expiration = null, CancellationToken ct = default)
    {
        var element = await ChexAsync(symbol, at, expiration, ct).ConfigureAwait(false);
        return element.Deserialize<Models.ChexResponse>();
    }

    /// <inheritdoc cref="ChexTypedAsync(string, string, string?, CancellationToken)"/>
    public Task<Models.ChexResponse?> ChexTypedAsync(string symbol, DateTime at, string? expiration = null, CancellationToken ct = default)
        => ChexTypedAsync(symbol, FormatAt(at), expiration, ct);

    /// <summary>Full composite exposure dashboard — net GEX/DEX/VEX/CHEX, gamma flip, regime, ±1% hedging, 0DTE contribution.</summary>
    public Task<JsonElement> ExposureSummaryAsync(string symbol, string at, CancellationToken ct = default)
        => GetAsync($"/v1/exposure/summary/{Seg(symbol)}", new() { ["at"] = at }, ct);

    /// <inheritdoc cref="ExposureSummaryAsync(string, string, CancellationToken)"/>
    public Task<JsonElement> ExposureSummaryAsync(string symbol, DateTime at, CancellationToken ct = default)
        => ExposureSummaryAsync(symbol, FormatAt(at), ct);

    /// <summary>
    /// Strongly-typed variant of <see cref="ExposureSummaryAsync(string, string, CancellationToken)"/>.
    /// Returns a <see cref="Models.ExposureSummaryResponse"/> POCO. The original
    /// <see cref="ExposureSummaryAsync(string, string, CancellationToken)"/> remains unchanged.
    /// </summary>
    public async Task<Models.ExposureSummaryResponse?> ExposureSummaryTypedAsync(string symbol, string at, CancellationToken ct = default)
    {
        var element = await ExposureSummaryAsync(symbol, at, ct).ConfigureAwait(false);
        return element.Deserialize<Models.ExposureSummaryResponse>();
    }

    /// <inheritdoc cref="ExposureSummaryTypedAsync(string, string, CancellationToken)"/>
    public Task<Models.ExposureSummaryResponse?> ExposureSummaryTypedAsync(string symbol, DateTime at, CancellationToken ct = default)
        => ExposureSummaryTypedAsync(symbol, FormatAt(at), ct);

    /// <summary>Key technical levels — gamma flip, walls, max +/- gamma, highest-OI, 0DTE magnet.</summary>
    public Task<JsonElement> ExposureLevelsAsync(string symbol, string at, CancellationToken ct = default)
        => GetAsync($"/v1/exposure/levels/{Seg(symbol)}", new() { ["at"] = at }, ct);

    /// <summary>
    /// Strongly-typed variant of <see cref="ExposureLevelsAsync(string, string, CancellationToken)"/>.
    /// Returns a <see cref="Models.ExposureLevelsResponse"/> POCO. The original
    /// <see cref="ExposureLevelsAsync(string, string, CancellationToken)"/> remains unchanged.
    /// </summary>
    public async Task<Models.ExposureLevelsResponse?> ExposureLevelsTypedAsync(string symbol, string at, CancellationToken ct = default)
    {
        var element = await ExposureLevelsAsync(symbol, at, ct).ConfigureAwait(false);
        return element.Deserialize<Models.ExposureLevelsResponse>();
    }

    /// <inheritdoc cref="ExposureLevelsTypedAsync(string, string, CancellationToken)"/>
    public Task<Models.ExposureLevelsResponse?> ExposureLevelsTypedAsync(string symbol, DateTime at, CancellationToken ct = default)
        => ExposureLevelsTypedAsync(symbol, FormatAt(at), ct);

    /// <summary>Verbal analysis + prior-day GEX comparison + VIX context. <c>top_oi_changes</c> always empty.</summary>
    public Task<JsonElement> NarrativeAsync(string symbol, string at, CancellationToken ct = default)
        => GetAsync($"/v1/exposure/narrative/{Seg(symbol)}", new() { ["at"] = at }, ct);

    /// <summary>
    /// Strongly-typed variant of <see cref="NarrativeAsync(string, string, CancellationToken)"/>.
    /// Returns a <see cref="Models.NarrativeResponse"/> POCO. The original
    /// <see cref="NarrativeAsync(string, string, CancellationToken)"/> remains unchanged.
    /// </summary>
    public async Task<Models.NarrativeResponse?> NarrativeTypedAsync(string symbol, string at, CancellationToken ct = default)
    {
        var element = await NarrativeAsync(symbol, at, ct).ConfigureAwait(false);
        return element.Deserialize<Models.NarrativeResponse>();
    }

    /// <inheritdoc cref="NarrativeTypedAsync(string, string, CancellationToken)"/>
    public Task<Models.NarrativeResponse?> NarrativeTypedAsync(string symbol, DateTime at, CancellationToken ct = default)
        => NarrativeTypedAsync(symbol, FormatAt(at), ct);

    /// <summary>0DTE-specific analytics — regime, expected move, pin risk, hedging, decay, flow.</summary>
    public Task<JsonElement> ZeroDteAsync(string symbol, string at, double? strikeRange = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string?> { ["at"] = at };
        if (strikeRange.HasValue) p["strike_range"] = D(strikeRange.Value);
        return GetAsync($"/v1/exposure/zero-dte/{Seg(symbol)}", p, ct);
    }

    /// <inheritdoc cref="ZeroDteAsync(string, string, double?, CancellationToken)"/>
    public Task<JsonElement> ZeroDteAsync(string symbol, DateTime at, double? strikeRange = null, CancellationToken ct = default)
        => ZeroDteAsync(symbol, FormatAt(at), strikeRange, ct);

    /// <summary>
    /// Strongly-typed variant of <see cref="ZeroDteAsync(string, string, double?, CancellationToken)"/>.
    /// Returns a <see cref="Models.ZeroDteResponse"/> POCO with the full 0DTE analytics
    /// shape: regime, exposures, expected move, pin risk, hedging buckets, decay,
    /// vol context, flow, levels, liquidity, metadata, and the per-strike grid.
    /// The original <see cref="ZeroDteAsync(string, string, double?, CancellationToken)"/>
    /// remains unchanged.
    /// </summary>
    public async Task<Models.ZeroDteResponse?> ZeroDteTypedAsync(string symbol, string at, double? strikeRange = null, CancellationToken ct = default)
    {
        var element = await ZeroDteAsync(symbol, at, strikeRange, ct).ConfigureAwait(false);
        return element.Deserialize<Models.ZeroDteResponse>();
    }

    /// <inheritdoc cref="ZeroDteTypedAsync(string, string, double?, CancellationToken)"/>
    public Task<Models.ZeroDteResponse?> ZeroDteTypedAsync(string symbol, DateTime at, double? strikeRange = null, CancellationToken ct = default)
        => ZeroDteTypedAsync(symbol, FormatAt(at), strikeRange, ct);

    // ── Max Pain ──────────────────────────────────────────────────────────────

    /// <summary>Strike-by-strike pain curve, OI breakdown, dealer alignment, pin probability.</summary>
    public Task<JsonElement> MaxPainAsync(string symbol, string at, string? expiration = null, CancellationToken ct = default)
    {
        var p = new Dictionary<string, string?> { ["at"] = at };
        if (expiration is not null) p["expiration"] = expiration;
        return GetAsync($"/v1/maxpain/{Seg(symbol)}", p, ct);
    }

    /// <summary>
    /// Strongly-typed variant of <see cref="MaxPainAsync(string, string, string?, CancellationToken)"/>.
    /// Returns a <see cref="Models.MaxPainResponse"/> POCO with the pain curve, dealer alignment,
    /// expected move, and pin probability. The original
    /// <see cref="MaxPainAsync(string, string, string?, CancellationToken)"/> remains unchanged.
    /// </summary>
    public async Task<Models.MaxPainResponse?> MaxPainTypedAsync(string symbol, string at, string? expiration = null, CancellationToken ct = default)
    {
        var element = await MaxPainAsync(symbol, at, expiration, ct).ConfigureAwait(false);
        return element.Deserialize<Models.MaxPainResponse>();
    }

    /// <inheritdoc cref="MaxPainTypedAsync(string, string, string?, CancellationToken)"/>
    public Task<Models.MaxPainResponse?> MaxPainTypedAsync(string symbol, DateTime at, string? expiration = null, CancellationToken ct = default)
        => MaxPainTypedAsync(symbol, FormatAt(at), expiration, ct);

    // ── Composite ─────────────────────────────────────────────────────────────

    /// <summary>Full composite snapshot — price, vol, options flow, exposure, macro.</summary>
    public Task<JsonElement> StockSummaryAsync(string symbol, string at, CancellationToken ct = default)
        => GetAsync($"/v1/stock/{Seg(symbol)}/summary", new() { ["at"] = at }, ct);

    /// <inheritdoc cref="StockSummaryAsync(string, string, CancellationToken)"/>
    public Task<JsonElement> StockSummaryAsync(string symbol, DateTime at, CancellationToken ct = default)
        => StockSummaryAsync(symbol, FormatAt(at), ct);

    /// <summary>
    /// Strongly-typed variant of <see cref="StockSummaryAsync(string, string, CancellationToken)"/>.
    /// Returns a <see cref="Models.StockSummaryResponse"/> POCO. The original
    /// <see cref="StockSummaryAsync(string, string, CancellationToken)"/> remains unchanged.
    /// </summary>
    public async Task<Models.StockSummaryResponse?> StockSummaryTypedAsync(string symbol, string at, CancellationToken ct = default)
    {
        var element = await StockSummaryAsync(symbol, at, ct).ConfigureAwait(false);
        return element.Deserialize<Models.StockSummaryResponse>();
    }

    /// <inheritdoc cref="StockSummaryTypedAsync(string, string, CancellationToken)"/>
    public Task<Models.StockSummaryResponse?> StockSummaryTypedAsync(string symbol, DateTime at, CancellationToken ct = default)
        => StockSummaryTypedAsync(symbol, FormatAt(at), ct);

    // ── Volatility ────────────────────────────────────────────────────────────

    /// <summary>Realized vol ladder, IV-RV spreads, skew profiles, term structure, GEX/theta by DTE bucket.</summary>
    public Task<JsonElement> VolatilityAsync(string symbol, string at, CancellationToken ct = default)
        => GetAsync($"/v1/volatility/{Seg(symbol)}", new() { ["at"] = at }, ct);

    /// <inheritdoc cref="VolatilityAsync(string, string, CancellationToken)"/>
    public Task<JsonElement> VolatilityAsync(string symbol, DateTime at, CancellationToken ct = default)
        => VolatilityAsync(symbol, FormatAt(at), ct);

    /// <summary>
    /// Strongly-typed variant of <see cref="VolatilityAsync(string, string, CancellationToken)"/>.
    /// Returns a <see cref="Models.VolatilityResponse"/> POCO with realized-vol ladder, IV-RV
    /// spreads, skew profiles, term structure, GEX/theta-by-DTE, put/call profile, OI
    /// concentration, hedging scenarios, and liquidity. The original
    /// <see cref="VolatilityAsync(string, string, CancellationToken)"/> remains unchanged.
    /// </summary>
    public async Task<Models.VolatilityResponse?> VolatilityTypedAsync(string symbol, string at, CancellationToken ct = default)
    {
        var element = await VolatilityAsync(symbol, at, ct).ConfigureAwait(false);
        return element.Deserialize<Models.VolatilityResponse>();
    }

    /// <inheritdoc cref="VolatilityTypedAsync(string, string, CancellationToken)"/>
    public Task<Models.VolatilityResponse?> VolatilityTypedAsync(string symbol, DateTime at, CancellationToken ct = default)
        => VolatilityTypedAsync(symbol, FormatAt(at), ct);

    /// <summary>SVI parameters, forward prices, total variance surface, arbitrage flags, variance swap fairs.</summary>
    public Task<JsonElement> AdvVolatilityAsync(string symbol, string at, CancellationToken ct = default)
        => GetAsync($"/v1/adv_volatility/{Seg(symbol)}", new() { ["at"] = at }, ct);

    /// <inheritdoc cref="AdvVolatilityAsync(string, string, CancellationToken)"/>
    public Task<JsonElement> AdvVolatilityAsync(string symbol, DateTime at, CancellationToken ct = default)
        => AdvVolatilityAsync(symbol, FormatAt(at), ct);

    /// <summary>
    /// Strongly-typed variant of <see cref="AdvVolatilityAsync(string, string, CancellationToken)"/>.
    /// Returns an <see cref="Models.AdvVolatilityResponse"/> POCO covering SVI parameter sets,
    /// forward prices, total variance surface, arbitrage flags, variance swap fair values,
    /// and second-/third-order greek surfaces. The original
    /// <see cref="AdvVolatilityAsync(string, string, CancellationToken)"/> remains unchanged.
    /// </summary>
    public async Task<Models.AdvVolatilityResponse?> AdvVolatilityTypedAsync(string symbol, string at, CancellationToken ct = default)
    {
        var element = await AdvVolatilityAsync(symbol, at, ct).ConfigureAwait(false);
        return element.Deserialize<Models.AdvVolatilityResponse>();
    }

    /// <inheritdoc cref="AdvVolatilityTypedAsync(string, string, CancellationToken)"/>
    public Task<Models.AdvVolatilityResponse?> AdvVolatilityTypedAsync(string symbol, DateTime at, CancellationToken ct = default)
        => AdvVolatilityTypedAsync(symbol, FormatAt(at), ct);

    // ── VRP ───────────────────────────────────────────────────────────────────

    /// <summary>VRP dashboard with date-bounded percentile history (no future leakage).</summary>
    public Task<JsonElement> VrpAsync(string symbol, string at, CancellationToken ct = default)
        => GetAsync($"/v1/vrp/{Seg(symbol)}", new() { ["at"] = at }, ct);

    /// <inheritdoc cref="VrpAsync(string, string, CancellationToken)"/>
    public Task<JsonElement> VrpAsync(string symbol, DateTime at, CancellationToken ct = default)
        => VrpAsync(symbol, FormatAt(at), ct);

    /// <summary>
    /// Strongly-typed variant of <see cref="VrpAsync(string, string, CancellationToken)"/>.
    /// Returns a <see cref="Models.VrpResponse"/> POCO covering core VRP metrics, directional
    /// skew, gamma-regime conditioning, regime snapshot, strategy scores, and term-VRP series.
    /// The original <see cref="VrpAsync(string, string, CancellationToken)"/> remains unchanged.
    /// </summary>
    public async Task<Models.VrpResponse?> VrpTypedAsync(string symbol, string at, CancellationToken ct = default)
    {
        var element = await VrpAsync(symbol, at, ct).ConfigureAwait(false);
        return element.Deserialize<Models.VrpResponse>();
    }

    /// <inheritdoc cref="VrpTypedAsync(string, string, CancellationToken)"/>
    public Task<Models.VrpResponse?> VrpTypedAsync(string symbol, DateTime at, CancellationToken ct = default)
        => VrpTypedAsync(symbol, FormatAt(at), ct);

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }
}
