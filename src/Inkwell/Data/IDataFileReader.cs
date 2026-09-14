namespace Structed.Inkwell.Data;

/// <summary>
/// Supplies the raw JSON for a data file.
/// </summary>
/// <remarks>
/// Abstracted so the same loading and validation path serves both the browser, where files arrive
/// over HTTP from <c>wwwroot</c>, and the tests, which read them straight off disk.
/// </remarks>
public interface IDataFileReader
{
    /// <summary>Opens a data file identified by a path relative to the data root, e.g. <c>srd/gear.json</c>.</summary>
    Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default);
}

/// <summary>Thrown when the data files are missing, malformed or internally inconsistent.</summary>
public sealed class GameDataException(string message, Exception? innerException = null)
    : Exception(message, innerException);
