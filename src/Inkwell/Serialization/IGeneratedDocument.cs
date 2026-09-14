namespace Structed.Inkwell.Serialization;

/// <summary>
/// The envelope every exported document carries.
/// </summary>
/// <remarks>
/// An interface rather than a base record on purpose. A base record would put its properties at the
/// front of the serialised object and quietly reorder every file a tool has ever written; an
/// interface leaves the document's own declaration order, and so its JSON, exactly as it was.
/// </remarks>
public interface IGeneratedDocument
{
    /// <summary>
    /// What kind of file this is, e.g. <c>your-tool/place</c>.
    /// </summary>
    /// <remarks>
    /// Nullable because a file that does not carry one is precisely the case worth rejecting, and
    /// the JSON source generator will hand over a null rather than any default written here.
    /// </remarks>
    string? Format { get; }

    /// <summary>The format version the file was written at.</summary>
    int Version { get; }
}
