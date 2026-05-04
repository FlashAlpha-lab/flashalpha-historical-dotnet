using System;
using System.Text.Json;

namespace FlashAlpha.Historical;

/// <summary>Base exception for the FlashAlpha Historical SDK.</summary>
public class FlashAlphaHistoricalException : Exception
{
    public int StatusCode { get; }
    public JsonElement? Response { get; }

    public FlashAlphaHistoricalException(string message, int statusCode, JsonElement? response = null)
        : base(message)
    {
        StatusCode = statusCode;
        Response = response;
    }
}

/// <summary>Raised when the API key is missing or invalid (HTTP 401).</summary>
public class AuthenticationException : FlashAlphaHistoricalException
{
    public AuthenticationException(string message, JsonElement? response = null)
        : base(message, 401, response) { }
}

/// <summary>Raised when the user's tier is below Alpha (HTTP 403).</summary>
public class TierRestrictedException : FlashAlphaHistoricalException
{
    public string? CurrentPlan { get; }
    public string? RequiredPlan { get; }

    public TierRestrictedException(string message, string? currentPlan = null, string? requiredPlan = null, JsonElement? response = null)
        : base(message, 403, response)
    {
        CurrentPlan = currentPlan;
        RequiredPlan = requiredPlan;
    }
}

/// <summary>Raised when the <c>at</c> parameter is missing or malformed (HTTP 400, error=invalid_at).</summary>
public class InvalidAtException : FlashAlphaHistoricalException
{
    public InvalidAtException(string message, JsonElement? response = null)
        : base(message, 400, response) { }
}

/// <summary>Raised when (symbol, at) has no data — outside coverage or in a gap (HTTP 404, error=no_data).</summary>
public class NoDataException : FlashAlphaHistoricalException
{
    public NoDataException(string message, JsonElement? response = null)
        : base(message, 404, response) { }
}

/// <summary>Raised when the symbol is not in the historical dataset (HTTP 404, error=no_coverage).</summary>
public class NoCoverageException : FlashAlphaHistoricalException
{
    public NoCoverageException(string message, JsonElement? response = null)
        : base(message, 404, response) { }
}

/// <summary>Raised when the symbol has no data at the requested <c>at</c> (HTTP 404, error=symbol_not_found).</summary>
public class SymbolNotFoundException : FlashAlphaHistoricalException
{
    public SymbolNotFoundException(string message, JsonElement? response = null)
        : base(message, 404, response) { }
}

/// <summary>Raised when the surface grid can't be built — too few OTM+liquid contracts (HTTP 404, error=insufficient_data).</summary>
public class InsufficientDataException : FlashAlphaHistoricalException
{
    public InsufficientDataException(string message, JsonElement? response = null)
        : base(message, 404, response) { }
}

/// <summary>Raised when the daily quota is exhausted (HTTP 429). Quota is shared with the live API.</summary>
public class RateLimitException : FlashAlphaHistoricalException
{
    public int? RetryAfter { get; }

    public RateLimitException(string message, int? retryAfter = null, JsonElement? response = null)
        : base(message, 429, response)
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>Raised on 5xx server errors.</summary>
public class ServerException : FlashAlphaHistoricalException
{
    public ServerException(string message, int statusCode, JsonElement? response = null)
        : base(message, statusCode, response) { }
}
