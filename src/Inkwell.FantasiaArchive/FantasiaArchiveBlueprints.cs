namespace Structed.Inkwell.Interop.FantasiaArchive;

/// <summary>
/// The parts of Fantasia Archive's built-in document types this exporter writes to.
/// </summary>
/// <remarks>
/// <para>
/// Fantasia Archive is GPL-3.0. These are re-derived identifiers rather than copied source: the
/// handful of ids and select values needed to write a file its importer accepts, which are
/// functional interface facts. No blueprint definition, tooltip or value list is reproduced here.
/// </para>
/// <para>
/// Everything in this file is a <em>key</em> in someone else's application and is never translated,
/// exactly as this project's own ids are not. A German export still says
/// <c>"locationType": "Village"</c>, because that string is what the app's own dropdown offers; only
/// the prose around it changes language.
/// </para>
/// <para>
/// Targets Fantasia Archive v1, the format every released version reads. The v2 rewrite on
/// <c>master</c> replaces it with a single-file SQLite database and is deliberately not supported
/// while its schema is still pre-release.
/// </para>
/// </remarks>
public static class FantasiaArchiveBlueprints
{
    /// <summary>Places. Files as <c>locations.txt</c>.</summary>
    public const string Locations = "locations";

    /// <summary>People. Files as <c>characters.txt</c>.</summary>
    public const string Characters = "characters";

    /// <summary>Objects. Files as <c>items.txt</c>.</summary>
    public const string Items = "items";

    /// <summary>Every type this exporter emits, in the order the files are written.</summary>
    public static IReadOnlyList<string> AllTypes { get; } = [Locations, Characters, Items];

    /// <summary>
    /// The URL every document carries, which the app's router depends on being exactly this shape.
    /// </summary>
    public static string Url(string type, string id) => $"/project/display-content/{type}/{id}";

    /// <summary>The icon shown in the document tree, one per type.</summary>
    public static string IconFor(string type) => type switch
    {
        Locations => "mdi-map-marker-radius",
        Characters => "mdi-account",
        Items => "mdi-sword",
        _ => ""
    };

    /// <summary>
    /// The document tree path a type's documents sit under, which is the blueprint's plural name.
    /// </summary>
    public static string HierarchicalPathFor(string type) => type switch
    {
        Locations => "Locations/Geography",
        Characters => "Characters",
        Items => "Items",
        _ => ""
    };

    /// <summary>Field ids every document type shares.</summary>
    public static class Common
    {
        public const string Name = "name";

        /// <summary>Parents a document to another of the same type, building the tree.</summary>
        public const string ParentDocument = "parentDoc";

        /// <summary>Turns a document into a folder rather than a leaf.</summary>
        public const string CategorySwitch = "categorySwitch";

        public const string CategoryDescription = "categoryDescription";

        public const string Description = "description";

        public const string Tags = "tags";

        public const string Order = "order";

        public const string OtherNames = "otherNames";
    }

    /// <summary>Field ids on a place.</summary>
    public static class Location
    {
        public const string LocationType = "locationType";

        /// <summary>Free text, not a number.</summary>
        public const string Population = "population";

        /// <summary>Free text, not a number.</summary>
        public const string Size = "size";

        public const string Traits = "traits";

        public const string Traditions = "traditions";

        /// <summary>Residents. Pairs with <see cref="Character.CurrentLocation"/>.</summary>
        public const string CurrentCharacters = "pairedCurrentCharactersNew";

        /// <summary>
        /// People tied to this place without necessarily living in it. Pairs with
        /// <see cref="Character.ConnectedPlaces"/>.
        /// </summary>
        public const string ConnectedCharacters = "pairedConnectedCharacter";

        /// <summary>Objects found here. Pairs with <see cref="Item.ConnectedLocations"/>.</summary>
        public const string ConnectedItems = "pairedConnectedItems";
    }

    /// <summary>Field ids on a person.</summary>
    public static class Character
    {
        /// <summary>A free-text list, which is the only place a one-off job title fits.</summary>
        public const string Titles = "titles";

        public const string Age = "age";

        /// <summary>Unusual features, a free-text list.</summary>
        public const string Traits = "traits";

        /// <summary>Where they live. Pairs with <see cref="Location.CurrentCharacters"/>.</summary>
        public const string CurrentLocation = "pairedCurrentLocationNew";

        /// <summary>
        /// Places they are tied to, which is where a premises' keeper is linked to it. Pairs
        /// with <see cref="Location.ConnectedCharacters"/>.
        /// </summary>
        public const string ConnectedPlaces = "pairedConnectedPlaces";
    }

    /// <summary>Field ids on an object.</summary>
    public static class Item
    {
        /// <summary>
        /// A free-text list of labelled notes.
        /// </summary>
        /// <remarks>
        /// Fantasia Archive has no price, quantity or rarity field on an item, so everything a
        /// buyer would want to know goes here, where it renders as "Price: 20p".
        /// </remarks>
        public const string Features = "features";

        /// <summary>Where it can be found. Pairs with <see cref="Location.ConnectedItems"/>.</summary>
        public const string ConnectedLocations = "pairedConnectedLocations";
    }

    /// <summary>
    /// What a shop, tavern or other premises is: a building.
    /// </summary>
    public const string PremisesLocationType = "Building";
}
