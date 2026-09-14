namespace Structed.Inkwell.Data;

/// <summary>
/// Reads data files from a directory on disk.
/// </summary>
/// <remarks>
/// Used by the tests so they validate the very same JSON the app ships, rather than a fixture that
/// can drift away from it.
/// </remarks>
public sealed class FileSystemDataFileReader(string rootDirectory) : IDataFileReader
{
    private readonly string _rootDirectory =
        rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));

    public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        string fullPath = Path.Combine(_rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Data file not found: {fullPath}", fullPath);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }
}
