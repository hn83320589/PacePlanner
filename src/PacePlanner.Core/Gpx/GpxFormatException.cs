namespace PacePlanner.Core.Gpx;

/// <summary>Raised when a file cannot be used as a GPX course. The message is user-facing.</summary>
public sealed class GpxFormatException : Exception
{
    public GpxFormatException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
