using System;
using System.Net;

namespace Jellyfin.Plugin.MDBList.Api;

/// <summary>
/// Thrown for any MDBList API failure -- a non-2xx response, an unparseable
/// body, or a response missing a field callers depend on. Never swallowed
/// into a default/empty value: a diff-based sync run must abort rather than
/// treat a failed call as "there is nothing to sync".
/// </summary>
public class MDBListApiException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListApiException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    public MDBListApiException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListApiException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public MDBListApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MDBListApiException"/>
    /// class for a non-2xx HTTP response, carrying enough of the response
    /// for a caller to tell a rate limit (retryable) apart from a hard
    /// failure -- <see cref="MDBListApiClient"/> already retries a 429
    /// itself, so this mainly matters for a 429 that survives its retry
    /// budget and for every other non-2xx status.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The response's HTTP status code.</param>
    /// <param name="retryAfter">
    /// The server's requested wait, parsed from a 429 response's
    /// <c>Retry-After</c> header; null if absent or not applicable.
    /// </param>
    public MDBListApiException(string message, HttpStatusCode statusCode, TimeSpan? retryAfter)
        : base(message)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    /// <summary>
    /// Gets the response's HTTP status code, if this exception was raised
    /// from a non-2xx response rather than a transport-level failure.
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>
    /// Gets the server's requested wait before retrying, if this was a 429
    /// with a <c>Retry-After</c> header.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>
    /// Gets a value indicating whether this exception represents a rate
    /// limit (429) response rather than some other failure.
    /// </summary>
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;
}
