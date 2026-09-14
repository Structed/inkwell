namespace Structed.Inkwell.Serialization;

/// <summary>Thrown when an imported file is not a usable export.</summary>
public sealed class DocumentFormatException(string message, Exception? innerException = null)
    : Exception(message, innerException);
