namespace Bullgate.Access.AspNetCore;

/// <summary>Represents a structured rejection returned by Bullgate Access.</summary>
public sealed class BullgateAccessRejectedException : Exception
{
    /// <summary>Initializes an Access rejection with its HTTP status, error code, and optional field.</summary>
    /// <param name="statusCode">The upstream HTTP status code.</param>
    /// <param name="error">The stable Access error code.</param>
    /// <param name="field">The rejected public field name, when supplied.</param>
    public BullgateAccessRejectedException(
        int statusCode,
        string error,
        string? field = null)
        : base($"Bullgate Access rejected the request with '{error}'.")
    {
        StatusCode = statusCode;
        Error = error;
        Field = field;
    }

    /// <summary>Gets the upstream HTTP status code.</summary>
    public int StatusCode { get; }

    /// <summary>Gets the stable machine-readable Access error code.</summary>
    public string Error { get; }

    /// <summary>Gets the rejected public field name, when supplied.</summary>
    public string? Field { get; }
}

/// <summary>Represents a timeout, transport failure, or invalid Access response.</summary>
public sealed class BullgateAccessUnavailableException : Exception
{
    /// <summary>Initializes an Access availability failure.</summary>
    /// <param name="message">A safe diagnostic message without bearer material.</param>
    /// <param name="innerException">The transport or parsing failure, when available.</param>
    public BullgateAccessUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>Represents a structured rejection produced by the consumer profile adapter.</summary>
public sealed class BullgateApplicationRejectedException : Exception
{
    /// <summary>Initializes a consumer application rejection.</summary>
    /// <param name="statusCode">The HTTP status to expose through the BFF.</param>
    /// <param name="error">The stable consumer error code.</param>
    /// <param name="field">The rejected public field name, when supplied.</param>
    public BullgateApplicationRejectedException(
        int statusCode,
        string error,
        string? field = null)
        : base($"The host application rejected the request with '{error}'.")
    {
        StatusCode = statusCode;
        Error = error;
        Field = field;
    }

    /// <summary>Gets the HTTP status code exposed by the BFF.</summary>
    public int StatusCode { get; }

    /// <summary>Gets the stable machine-readable consumer error code.</summary>
    public string Error { get; }

    /// <summary>Gets the rejected public field name, when supplied.</summary>
    public string? Field { get; }
}
