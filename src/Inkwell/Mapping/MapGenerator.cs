using Structed.Inkwell.Generation;
using Structed.Inkwell.Randomness;

namespace Structed.Inkwell.Mapping;

/// <summary>
/// Lays out a map inside the silhouette of the place it describes.
/// </summary>
/// <remarks>
/// <para>
/// Roads are grown first and buildings placed along them, which is the approach that suits a small
/// rural settlement: the alternative, subdividing a block into lots, produces dense urban patches
/// and makes the building count an emergent property rather than something the place's size can
/// dictate.
/// </para>
/// <para>
/// Growth uses a priority queue rather than an L-system. Each candidate road is popped, tested
/// against the boundary and the roads already placed, and then pushes its own successors. Every
/// candidate is given a distinct priority so the dequeue order cannot vary between runs, which
/// matters because the whole map has to be reproducible from its seed.
/// </para>
/// </remarks>
public static class MapGenerator
{
    /// <summary>The reroll key a map redraw is counted under.</summary>
    public const string RerollKey = "map";

    private const double CanvasWidth = 420;
    private const double CanvasHeight = 320;
    private const double EdgeMargin = 26;

    /// <summary>
    /// The seed a place's map is drawn from.
    /// </summary>
    /// <remarks>
    /// Derived from the place's own seed so a shared link reproduces the same map, but shifted by
    /// how many times the map alone has been re-drawn, so its layout can be changed without
    /// disturbing a result the user is happy with. It lives here rather than at the call site
    /// because more than one caller needs it and they must agree: an export that derived the seed
    /// differently would ship a different map from the one on screen.
    /// </remarks>
    public static uint SeedFor(RollPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return plan.Rerolls.TryGetValue(RerollKey, out int redraws) && redraws > 0
            ? (uint)SeedDerivation.Derive(plan.Seed, $"map#{redraws}")
            : plan.Seed;
    }

    /// <summary>Builds the map for a place.</summary>
    /// <param name="brief">What is being drawn: its silhouette, its size and what earns a key.</param>
    /// <param name="seed">The map's own seed, so it can be redrawn without re-rolling the place.</param>
    public static PlaceMap Generate(MapBrief brief, uint seed)
    {
        ArgumentNullException.ThrowIfNull(brief);

        DiceRoller dice = new(SeedDerivation.CreateStream(seed, "map"));

        string shape = string.IsNullOrWhiteSpace(brief.Shape) ? "hollow" : brief.Shape;
        MapPolygon boundary = BuildBoundary(dice, shape, brief.Scale);

        MapPolygon? water = brief.HasWater
            ? BuildWater(new DiceRoller(SeedDerivation.CreateStream(seed, "map/water")))
            : null;

        IReadOnlyList<RoadSegment> roads = GrowRoads(
            new DiceRoller(SeedDerivation.CreateStream(seed, "map/roads")),
            boundary,
            water,
            shape,
            brief.Scale);

        IReadOnlyList<MapBuilding> buildings = PlaceBuildings(
            new DiceRoller(SeedDerivation.CreateStream(seed, "map/buildings")),
            boundary,
            water,
            roads,
            brief.Scale);

        (buildings, IReadOnlyList<MapLegendEntry> legend) = AssignKeys(brief.Keys, buildings);

        IReadOnlyList<MapScatter> scatter = PlaceScatter(
            new DiceRoller(SeedDerivation.CreateStream(seed, "map/scatter")),
            boundary,
            water,
            roads,
            buildings);

        return new PlaceMap
        {
            Boundary = boundary,
            Subject = brief.Subject,
            Shape = shape,
            Roads = roads,
            Buildings = buildings,
            Scatter = scatter,
            Water = water,
            Legend = legend,
            Width = CanvasWidth,
            Height = CanvasHeight
        };
    }

    #region Boundary

    /// <summary>
    /// Traces the outline of the host object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A radial sweep covers every archetype cheaply: the shape is a base radius per angle,
    /// multiplied by a little low-frequency noise so no two hollow stumps look alike.
    /// </para>
    /// <para>
    /// The narrow archetypes open up as the settlement grows. A city in a farmhouse wall is a
    /// longer and rather thicker stretch of wall than a farmstead in one, and without this a large
    /// settlement in a thin host is squeezed into fewer buildings than a hamlet in a round one.
    /// </para>
    /// </remarks>
    private static MapPolygon BuildBoundary(DiceRoller dice, string shape, int sizeValue)
    {
        const int steps = 72;

        // 0 for a farm, 1 for a city.
        double growth = Math.Clamp((sizeValue - 1) / 5.0, 0, 1);

        (double scaleX, double scaleY) = shape switch
        {
            "linear" => (1.00, 0.34 + (0.26 * growth)),
            "vessel" => (0.78 + (0.08 * growth), 0.90 + (0.08 * growth)),
            "boxy" => (0.98, 0.66 + (0.16 * growth)),
            "warren" => (0.92 + (0.06 * growth), 0.78 + (0.10 * growth)),
            "sprawl" => (1.00, 0.74 + (0.10 * growth)),
            _ => (0.90 + (0.06 * growth), 0.82 + (0.08 * growth))
        };

        double radiusX = ((CanvasWidth / 2) - EdgeMargin) * scaleX;
        double radiusY = ((CanvasHeight / 2) - EdgeMargin) * scaleY;

        double phase1 = dice.NextIndex(628) / 100.0;
        double phase2 = dice.NextIndex(628) / 100.0;
        double phase3 = dice.NextIndex(628) / 100.0;
        double roughness = 0.05 + (dice.NextIndex(5) / 100.0);

        MapPoint centre = new(CanvasWidth / 2, CanvasHeight / 2);
        List<MapPoint> points = new(steps);

        for (int i = 0; i < steps; i++)
        {
            double angle = 2 * Math.PI * i / steps;

            double baseRadius = ShapeRadius(shape, angle);
            double noise =
                1 +
                (roughness * Math.Sin((3 * angle) + phase1)) +
                (roughness * 0.6 * Math.Sin((5 * angle) + phase2)) +
                (roughness * 0.35 * Math.Sin((8 * angle) + phase3));

            points.Add(new MapPoint(
                centre.X + (Math.Cos(angle) * radiusX * baseRadius * noise),
                centre.Y + (Math.Sin(angle) * radiusY * baseRadius * noise)));
        }

        return new MapPolygon(FitToCanvas(points));
    }

    /// <summary>
    /// Scales and centres an outline so it sits inside the canvas with a margin.
    /// </summary>
    /// <remarks>
    /// The radial sweep multiplies a shape's lobes by its noise, so the two can compound and push
    /// the outline past the edge. Fitting the finished polygon is robust to whatever those factors
    /// combine to, where bounding each of them separately is guesswork that breaks the next time
    /// an archetype is tuned. Only shrinks: a shape that already fits keeps its proportions.
    /// </remarks>
    private static IReadOnlyList<MapPoint> FitToCanvas(IReadOnlyList<MapPoint> points)
    {
        double minX = points.Min(p => p.X);
        double maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y);
        double maxY = points.Max(p => p.Y);

        double width = Math.Max(1e-6, maxX - minX);
        double height = Math.Max(1e-6, maxY - minY);

        double availableWidth = CanvasWidth - (EdgeMargin * 2);
        double availableHeight = CanvasHeight - (EdgeMargin * 2);

        double scale = Math.Min(1.0, Math.Min(availableWidth / width, availableHeight / height));

        MapPoint from = new((minX + maxX) / 2, (minY + maxY) / 2);
        MapPoint to = new(CanvasWidth / 2, CanvasHeight / 2);

        return [.. points.Select(p => new MapPoint(
            to.X + ((p.X - from.X) * scale),
            to.Y + ((p.Y - from.Y) * scale)))];
    }

    /// <summary>The unit radius of a shape archetype at a given angle.</summary>
    private static double ShapeRadius(string shape, double angle) => shape switch
    {
        // A superellipse gives the flat sides and rounded corners of a car, skip or shed.
        "boxy" => Superellipse(angle, 5),

        // Pinched at one end, like a boot or a teapot with its spout.
        "vessel" => 1 - (0.22 * Math.Cos(angle)),

        // Lobed, like a warren of connected chambers.
        "warren" => 1 + (0.16 * Math.Sin(3 * angle)),

        _ => 1
    };

    private static double Superellipse(double angle, double exponent)
    {
        double cos = Math.Pow(Math.Abs(Math.Cos(angle)), exponent);
        double sin = Math.Pow(Math.Abs(Math.Sin(angle)), exponent);

        return 1 / Math.Pow(cos + sin, 1 / exponent);
    }

    #endregion

    #region Water

    /// <summary>A band of water crossing one corner of the map.</summary>
    private static MapPolygon BuildWater(DiceRoller dice)
    {
        bool alongBottom = dice.Chance(60);
        double drift = dice.NextIndex(40) - 20;

        List<MapPoint> points = [];

        double baseline = alongBottom ? CanvasHeight - 34 : 34;
        int steps = 14;

        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps;
            double x = -20 + (t * (CanvasWidth + 40));
            double wobble = Math.Sin((t * Math.PI * 1.7) + (drift / 10)) * 12;

            points.Add(new MapPoint(x, baseline + wobble + (drift * 0.2)));
        }

        // Close the band off the edge of the canvas so only the near bank is ever visible.
        double outer = alongBottom ? CanvasHeight + 60 : -60;
        points.Add(new MapPoint(CanvasWidth + 20, outer));
        points.Add(new MapPoint(-20, outer));

        return new MapPolygon(points);
    }

    #endregion

    #region Roads

    private sealed record RoadCandidate(MapPoint From, double Angle, int Depth, double Length);

    /// <summary>How many road segments a settlement of each size supports.</summary>
    private static int RoadBudget(int sizeValue) => sizeValue switch
    {
        1 => 5,
        2 => 9,
        3 => 16,
        4 => 26,
        5 => 38,
        _ => 52
    };

    private static IReadOnlyList<RoadSegment> GrowRoads(
        DiceRoller dice,
        MapPolygon boundary,
        MapPolygon? water,
        string shape,
        int sizeValue)
    {
        int budget = RoadBudget(sizeValue);

        List<RoadSegment> roads = [];
        List<MapPoint> nodes = [];

        PriorityQueue<RoadCandidate, long> queue = new();
        long sequence = 0;

        void Enqueue(RoadCandidate candidate)
        {
            // A distinct priority per candidate keeps the dequeue order reproducible; PriorityQueue
            // makes no ordering guarantee between equal priorities.
            queue.Enqueue(candidate, ((long)candidate.Depth * 1_000_000) + sequence++);
        }

        MapPoint start = boundary.Centre;
        nodes.Add(start);

        // A linear host has one thoroughfare running its length; anything else radiates from the
        // middle. Both cases seed opposing directions so growth spreads rather than drifting.
        double baseAngle = shape == "linear"
            ? 0
            : dice.NextIndex(628) / 100.0;

        int arms = shape switch
        {
            "linear" => 2,
            "warren" => 4,
            _ => 3 + (dice.Chance(50) ? 1 : 0)
        };

        for (int i = 0; i < arms; i++)
        {
            double angle = baseAngle + (2 * Math.PI * i / arms) + (Jitter(dice, 0.25));
            Enqueue(new RoadCandidate(start, angle, 0, SegmentLength(dice, 0)));
        }

        while (queue.Count > 0 && roads.Count < budget)
        {
            RoadCandidate candidate = queue.Dequeue();

            if (!TryPlace(dice, candidate, boundary, water, roads, nodes, out RoadSegment? placed))
            {
                continue;
            }

            roads.Add(placed!);

            MapPoint tip = placed!.To;
            if (!nodes.Any(n => n.DistanceTo(tip) < 1))
            {
                nodes.Add(tip);
            }

            // Carry on, and occasionally branch. Branches step down a level so the network reads
            // as a hierarchy of thoroughfare, lane and alley rather than a uniform mesh.
            if (candidate.Depth <= 3)
            {
                Enqueue(candidate with
                {
                    From = tip,
                    Angle = candidate.Angle + Jitter(dice, 0.35),
                    Length = SegmentLength(dice, candidate.Depth)
                });

                int branchChance = candidate.Depth switch { 0 => 75, 1 => 55, 2 => 35, _ => 18 };

                if (dice.Chance(branchChance))
                {
                    double turn = (Math.PI / 2) + Jitter(dice, 0.45);
                    double angle = candidate.Angle + (dice.Chance(50) ? turn : -turn);

                    Enqueue(new RoadCandidate(tip, angle, candidate.Depth + 1, SegmentLength(dice, candidate.Depth + 1)));
                }
            }
        }

        return roads;
    }

    private static double SegmentLength(DiceRoller dice, int depth)
    {
        double baseLength = depth switch
        {
            0 => 52,
            1 => 42,
            2 => 34,
            _ => 28
        };

        return baseLength * (0.75 + (dice.NextIndex(50) / 100.0));
    }

    private static double Jitter(DiceRoller dice, double magnitude) =>
        ((dice.NextIndex(201) - 100) / 100.0) * magnitude;

    /// <summary>
    /// Validates a candidate road and snaps it to nearby junctions.
    /// </summary>
    /// <remarks>
    /// Snapping is what produces loops. Without it the network is a pure tree, which reads as
    /// artificial and gives players only one route to anywhere.
    /// </remarks>
    private static bool TryPlace(
        DiceRoller dice,
        RoadCandidate candidate,
        MapPolygon boundary,
        MapPolygon? water,
        List<RoadSegment> roads,
        List<MapPoint> nodes,
        out RoadSegment? placed)
    {
        placed = null;

        MapPoint to = candidate.From + MapPoint.FromAngle(candidate.Angle, candidate.Length);

        // Pull the end back until it fits inside the host, rather than discarding the road outright.
        int attempts = 0;
        while (!boundary.ContainsWithMargin(to, 12) && attempts++ < 4)
        {
            to = candidate.From.Lerp(to, 0.65);
        }

        if (!boundary.ContainsWithMargin(to, 10))
        {
            return false;
        }

        if (candidate.From.DistanceTo(to) < 14)
        {
            return false;
        }

        if (water is not null && water.Contains(to))
        {
            return false;
        }

        // Join an existing junction if one is close, forming a loop.
        MapPoint? nearby = nodes
            .Where(n => n.DistanceTo(candidate.From) > 1 && n.DistanceTo(to) < 20)
            .OrderBy(n => n.DistanceTo(to))
            .Cast<MapPoint?>()
            .FirstOrDefault();

        if (nearby is { } snapTo)
        {
            to = snapTo;

            if (candidate.From.DistanceTo(to) < 12)
            {
                return false;
            }
        }

        foreach (RoadSegment road in roads)
        {
            if (MapPolygon.SegmentsCross(candidate.From, to, road.From, road.To))
            {
                return false;
            }

            // Reject roads that run alongside an existing one close enough to look like a mistake.
            if (road.From.DistanceTo(candidate.From) > 1 &&
                MapPolygon.DistanceToSegment(to, road.From, road.To) < 9 &&
                to.DistanceTo(road.To) > 1 && to.DistanceTo(road.From) > 1)
            {
                return false;
            }
        }

        placed = new RoadSegment(candidate.From, to, candidate.Depth);
        return true;
    }

    #endregion

    #region Buildings

    /// <summary>
    /// How many buildings to aim for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately far below whatever population the place claims. A city of a thousand souls
    /// drawn as a thousand roofs is unreadable; the guidance this follows is Mausritter's, to "make
    /// it small and dense" and leave the gaps for players to fill.
    /// </para>
    /// <para>
    /// This is a target rather than a guarantee. A cramped silhouette will reject placements and
    /// come in under it, which is the right outcome: a boot should not hold as much as a woodshed.
    /// </para>
    /// </remarks>
    private static (int Min, int Max) BuildingBudget(int sizeValue) => sizeValue switch
    {
        1 => (3, 5),
        2 => (6, 9),
        3 => (12, 18),
        4 => (20, 28),
        5 => (28, 38),
        _ => (36, 50)
    };

    private static IReadOnlyList<MapBuilding> PlaceBuildings(
        DiceRoller dice,
        MapPolygon boundary,
        MapPolygon? water,
        IReadOnlyList<RoadSegment> roads,
        int sizeValue)
    {
        if (roads.Count == 0)
        {
            return [];
        }

        (int min, int max) = BuildingBudget(sizeValue);
        int target = min + dice.NextIndex(max - min + 1);

        List<MapBuilding> buildings = [];

        // Frontages are visited in a shuffled order so growth does not creep along one road at a
        // time, which would leave the rest of the settlement empty when the budget runs out.
        List<(RoadSegment Road, double Along, int Side)> frontages = [];

        foreach (RoadSegment road in roads)
        {
            // Frontage slots are spaced a little tighter than a typical building so that dense
            // settlements can actually reach their target; without the surplus, rejections leave a
            // city noticeably emptier than its size implies.
            int slots = Math.Max(1, (int)(road.Length / 17));

            for (int i = 0; i < slots; i++)
            {
                double along = (i + 0.5) / slots;
                frontages.Add((road, along, -1));
                frontages.Add((road, along, 1));
            }
        }

        foreach ((RoadSegment road, double along, int side) in dice.Shuffle(frontages))
        {
            if (buildings.Count >= target)
            {
                break;
            }

            MapBuilding? building = TryPlaceBuilding(dice, road, along, side, boundary, water, roads, buildings);

            if (building is not null)
            {
                buildings.Add(building);
            }
        }

        return buildings;
    }

    private static MapBuilding? TryPlaceBuilding(
        DiceRoller dice,
        RoadSegment road,
        double along,
        int side,
        MapPolygon boundary,
        MapPolygon? water,
        IReadOnlyList<RoadSegment> roads,
        IReadOnlyList<MapBuilding> placed)
    {
        double width = 12 + dice.NextIndex(8);
        double depth = 9 + dice.NextIndex(6);

        // Buildings on the main street are a little grander.
        if (road.Depth == 0)
        {
            width += 3;
            depth += 2;
        }

        MapPoint direction = road.Direction;
        MapPoint normal = direction.Perpendicular() * side;

        double offset = (road.Width / 2) + (depth / 2) + 3 + dice.NextIndex(4);
        MapPoint centre = road.From.Lerp(road.To, along) + (normal * offset);

        if (!boundary.ContainsWithMargin(centre, 8))
        {
            return null;
        }

        if (water is not null && water.DistanceToEdge(centre) < 10 && water.Contains(centre))
        {
            return null;
        }

        double angle = Math.Atan2(direction.Y, direction.X) + Jitter(dice, 0.08);

        MapBuilding building = new()
        {
            Centre = centre,
            Width = width,
            Depth = depth,
            Angle = angle,
            RoadDepth = road.Depth
        };

        foreach (MapBuilding other in placed)
        {
            if (centre.DistanceTo(other.Centre) < building.Radius + other.Radius + 2)
            {
                return null;
            }
        }

        // Keep buildings off every road except the one they face.
        foreach (RoadSegment other in roads)
        {
            double clearance = (other.Width / 2) + 3;

            if (MapPolygon.DistanceToSegment(centre, other.From, other.To) < clearance + (depth / 2))
            {
                bool isOwnRoad = ReferenceEquals(other, road);
                if (!isOwnRoad)
                {
                    return null;
                }
            }
        }

        return building;
    }

    #endregion

    #region Keys and scatter

    /// <summary>
    /// Ties buildings to the subjects the brief wants keyed, numbering them for the legend.
    /// </summary>
    /// <remarks>
    /// Candidates are ordered by the importance of the road they face and then by how central they
    /// are, so the keyed places land on the main street rather than scattered down back alleys.
    /// </remarks>
    private static (IReadOnlyList<MapBuilding> Buildings, IReadOnlyList<MapLegendEntry> Legend) AssignKeys(
        IReadOnlyList<MapKeySubject> subjects,
        IReadOnlyList<MapBuilding> buildings)
    {
        if (subjects.Count == 0 || buildings.Count == 0)
        {
            return (buildings, []);
        }

        MapPoint centre = buildings
            .Aggregate(new MapPoint(0, 0), (sum, b) => sum + b.Centre) * (1.0 / buildings.Count);

        List<MapBuilding> ordered =
        [
            .. buildings
                .OrderBy(b => b.RoadDepth)
                .ThenBy(b => b.Centre.DistanceTo(centre))
        ];

        Dictionary<MapBuilding, (int Key, string Name)> assigned = [];
        List<MapLegendEntry> legend = [];

        for (int i = 0; i < subjects.Count && i < ordered.Count; i++)
        {
            MapKeySubject subject = subjects[i];
            int key = i + 1;

            assigned[ordered[i]] = (key, subject.Name);
            legend.Add(new MapLegendEntry(key, subject.Name, subject.Detail));
        }

        List<MapBuilding> result = new(buildings.Count);
        foreach (MapBuilding building in buildings)
        {
            result.Add(assigned.TryGetValue(building, out (int Key, string Name) entry)
                ? building with { Key = entry.Key, Label = entry.Name }
                : building);
        }

        return (result, legend);
    }

    private static IReadOnlyList<MapScatter> PlaceScatter(
        DiceRoller dice,
        MapPolygon boundary,
        MapPolygon? water,
        IReadOnlyList<RoadSegment> roads,
        IReadOnlyList<MapBuilding> buildings)
    {
        List<MapScatter> scatter = [];

        MapBounds bounds = boundary.Bounds;
        const int attempts = 220;

        for (int i = 0; i < attempts && scatter.Count < 46; i++)
        {
            MapPoint point = new(
                bounds.MinX + (dice.NextIndex((int)Math.Max(1, bounds.Width))),
                bounds.MinY + (dice.NextIndex((int)Math.Max(1, bounds.Height))));

            if (!boundary.ContainsWithMargin(point, 6))
            {
                continue;
            }

            if (water is not null && water.Contains(point))
            {
                continue;
            }

            if (roads.Any(r => MapPolygon.DistanceToSegment(point, r.From, r.To) < 12))
            {
                continue;
            }

            if (buildings.Any(b => point.DistanceTo(b.Centre) < b.Radius + 8))
            {
                continue;
            }

            if (scatter.Any(s => point.DistanceTo(s.Position) < 13))
            {
                continue;
            }

            ScatterKind kind = dice.NextIndex(10) switch
            {
                < 5 => ScatterKind.Tree,
                < 8 => ScatterKind.Shrub,
                _ => ScatterKind.Rock
            };

            scatter.Add(new MapScatter(point, 4 + dice.NextIndex(4), kind));
        }

        return scatter;
    }

    #endregion
}
