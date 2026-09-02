using System;

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
}
