using Structed.Inkwell.Generation;
using Structed.Inkwell.Mapping;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Covers laying a map out inside a silhouette.
/// </summary>
/// <remarks>
/// Driven straight from a brief rather than through a generated place, so every archetype and every
/// scale can be swept exhaustively instead of waiting for a seed that happens to roll one.
/// </remarks>
public class MapGeneratorTests
{
    public static TheoryData<string> Shapes() =>
        ["hollow", "linear", "vessel", "boxy", "warren", "sprawl", "something-unknown"];

    private static MapBrief Brief(
        string shape = "hollow", int scale = 5, bool water = false, int keys = 0) => new()
        {
            Shape = shape,
            Scale = scale,
            Subject = "a hollow oak",
            HasWater = water,
            Keys = [.. Enumerable.Range(1, keys).Select(i => new MapKeySubject($"Name {i}", $"Detail {i}"))]
        };

    private static PlaceMap Map(uint seed, string shape = "hollow", int scale = 5, bool water = false, int keys = 0) =>
        MapGenerator.Generate(Brief(shape, scale, water, keys), seed);

    [Fact]
    public void SameSeedProducesAnIdenticalMap()
    {
        // A map that drifted between renders would make a shared link a lie.
        PlaceMap first = Map(4242);
        PlaceMap second = Map(4242);

        Assert.Equal(
            first.Roads.Select(r => (r.From, r.To, r.Depth)),
            second.Roads.Select(r => (r.From, r.To, r.Depth)));
        Assert.Equal(
            first.Buildings.Select(b => (b.Centre, b.Width, b.Depth)),
            second.Buildings.Select(b => (b.Centre, b.Width, b.Depth)));
        Assert.Equal(first.Boundary.Points, second.Boundary.Points);
        Assert.Equal(first.Scatter, second.Scatter);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentMaps()
    {
        string Signature(uint seed)
        {
            PlaceMap map = Map(seed);

            return string.Join(
                '|',
                map.Roads.Select(r => $"{r.From.X:F1},{r.From.Y:F1},{r.To.X:F1},{r.To.Y:F1}")
                    .Concat(map.Buildings.Select(b => $"{b.Centre.X:F1},{b.Centre.Y:F1},{b.Width:F1}")));
        }

        string[] signatures = [.. Enumerable.Range(1, 25).Select(i => Signature((uint)i * 37))];

        Assert.Equal(signatures.Length, signatures.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryShapeAndScaleProducesAUsableMap(string shape)
    {
        for (int scale = 1; scale <= 6; scale++)
        {
            for (uint seed = 1; seed <= 20; seed++)
            {
                PlaceMap map = Map(seed, shape, scale);

                Assert.NotNull(map.Boundary);
                Assert.True(map.Boundary.Points.Count >= 3);
                Assert.True(map.Width > 0 && map.Height > 0);
                Assert.NotEmpty(map.Roads);
            }
        }
    }

    [Fact]
    public void BiggerScalesGetMoreRoadsAndBuildings()
    {
        double AverageRoads(int scale) => Enumerable.Range(1, 20).Average(i => Map((uint)i, scale: scale).Roads.Count);
        double AverageBuildings(int scale) =>
            Enumerable.Range(1, 20).Average(i => Map((uint)i, scale: scale).Buildings.Count);

        Assert.True(AverageRoads(6) > AverageRoads(3));
        Assert.True(AverageRoads(3) > AverageRoads(1));
        Assert.True(AverageBuildings(6) > AverageBuildings(3));
        Assert.True(AverageBuildings(3) > AverageBuildings(1));
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EverythingStaysInsideTheBoundary(string shape)
    {
        // The place occupies the silhouette; a building outside it would break the whole conceit.
        for (int scale = 1; scale <= 6; scale++)
        {
            for (uint seed = 1; seed <= 12; seed++)
            {
                PlaceMap map = Map(seed, shape, scale);

                foreach (RoadSegment road in map.Roads)
                {
                    Assert.True(map.Boundary.Contains(road.From), $"road start outside (seed {seed}, scale {scale})");
                    Assert.True(map.Boundary.Contains(road.To), $"road end outside (seed {seed}, scale {scale})");
                }

                foreach (MapBuilding building in map.Buildings)
                {
                    Assert.True(
                        map.Boundary.Contains(building.Centre),
                        $"building outside (seed {seed}, scale {scale})");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void BoundaryKeepsAMarginSoTheInkedOutlineIsNotClipped(string shape)
    {
        // The outline is drawn with a wobble, so a silhouette flush against the viewBox would lose
        // strokes at the edge.
        for (int scale = 1; scale <= 6; scale++)
        {
            for (uint seed = 1; seed <= 12; seed++)
            {
                PlaceMap map = Map(seed, shape, scale);
                MapBounds bounds = map.Boundary.Bounds;

                Assert.True(bounds.MinX >= 8, $"too close to the left edge (seed {seed}, scale {scale})");
                Assert.True(bounds.MinY >= 8, $"too close to the top edge (seed {seed}, scale {scale})");
                Assert.True(bounds.MaxX <= map.Width - 8, $"too close to the right edge (seed {seed}, scale {scale})");
                Assert.True(bounds.MaxY <= map.Height - 8, $"too close to the bottom edge (seed {seed}, scale {scale})");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void BoundaryFillsAReasonableShareOfTheCanvas(string shape)
    {
        // Fitting must not shrink a silhouette so far that the map becomes a small island of ink.
        for (uint seed = 1; seed <= 20; seed++)
        {
            PlaceMap map = Map(seed, shape);
            MapBounds bounds = map.Boundary.Bounds;

            Assert.True(
                bounds.Width >= map.Width * 0.55,
                $"'{shape}' only spans {bounds.Width:F0} of {map.Width} on seed {seed}");
        }
    }

    [Fact]
    public void BuildingsDoNotOverlapEachOther()
    {
        for (uint seed = 1; seed <= 25; seed++)
        {
            IReadOnlyList<MapBuilding> buildings = Map(seed, scale: 6).Buildings;

            for (int i = 0; i < buildings.Count; i++)
            {
                for (int j = i + 1; j < buildings.Count; j++)
                {
                    double gap = buildings[i].Centre.DistanceTo(buildings[j].Centre);
                    double minimum = buildings[i].Radius + buildings[j].Radius;

                    Assert.True(gap >= minimum, $"buildings {i} and {j} overlap on seed {seed}");
                }
            }
        }
    }

    [Fact]
    public void BuildingsDoNotSitOnRoads()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            PlaceMap map = Map(seed);

            foreach (MapBuilding building in map.Buildings)
            {
                foreach (RoadSegment road in map.Roads)
                {
                    Assert.True(
                        MapPolygon.DistanceToSegment(building.Centre, road.From, road.To) >= road.Width / 2,
                        $"building sits on a road on seed {seed}");
                }
            }
        }
    }

    [Fact]
    public void RoadsDoNotCrossOneAnother()
    {
        // Roads may meet at junctions, but a crossing partway along reads as a mistake.
        for (uint seed = 1; seed <= 25; seed++)
        {
            IReadOnlyList<RoadSegment> roads = Map(seed, scale: 6).Roads;

            for (int i = 0; i < roads.Count; i++)
            {
                for (int j = i + 1; j < roads.Count; j++)
                {
                    Assert.False(
                        MapPolygon.SegmentsCross(roads[i].From, roads[i].To, roads[j].From, roads[j].To),
                        $"roads {i} and {j} cross on seed {seed}");
                }
            }
        }
    }

    [Fact]
    public void RoadNetworkIsConnected()
    {
        // Every road must be reachable from the first one, or part of the place is stranded.
        for (uint seed = 1; seed <= 20; seed++)
        {
            IReadOnlyList<RoadSegment> roads = Map(seed).Roads;
            if (roads.Count < 2)
            {
                continue;
            }

            HashSet<int> reached = [0];
            Queue<int> pending = new([0]);

            while (pending.Count > 0)
            {
                int current = pending.Dequeue();

                for (int i = 0; i < roads.Count; i++)
                {
                    if (reached.Contains(i) || !SharesEndpoint(roads[current], roads[i]))
                    {
                        continue;
                    }

                    reached.Add(i);
                    pending.Enqueue(i);
                }
            }

            Assert.Equal(roads.Count, reached.Count);
        }
    }

    private static bool SharesEndpoint(RoadSegment a, RoadSegment b)
    {
        const double epsilon = 0.5;

        return a.From.DistanceTo(b.From) < epsilon || a.From.DistanceTo(b.To) < epsilon ||
               a.To.DistanceTo(b.From) < epsilon || a.To.DistanceTo(b.To) < epsilon;
    }

    [Fact]
    public void WaterAppearsOnlyWhenTheBriefAsksForIt()
    {
        Assert.NotNull(Map(7, water: true).Water);
        Assert.Null(Map(7).Water);
    }

    [Fact]
    public void WaterDoesNotDisplaceTheMapOffTheCanvas()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            PlaceMap map = Map(seed, water: true);

            Assert.All(map.Buildings, b => Assert.True(map.Boundary.Contains(b.Centre)));
        }
    }

    [Fact]
    public void ScatterAvoidsRoadsAndBuildings()
    {
        for (uint seed = 1; seed <= 15; seed++)
        {
            PlaceMap map = Map(seed);

            foreach (MapScatter item in map.Scatter)
            {
                Assert.True(map.Boundary.Contains(item.Position));

                foreach (RoadSegment road in map.Roads)
                {
                    Assert.True(MapPolygon.DistanceToSegment(item.Position, road.From, road.To) >= 10);
                }

                foreach (MapBuilding building in map.Buildings)
                {
                    Assert.True(item.Position.DistanceTo(building.Centre) >= building.Radius);
                }
            }
        }
    }

    [Fact]
    public void KeysAreNumberedFromOneInTheOrderTheBriefListsThem()
    {
        PlaceMap map = Map(999, scale: 6, keys: 5);

        Assert.Equal([1, 2, 3, 4, 5], map.Legend.Select(l => l.Key));
        Assert.Equal(
            ["Name 1", "Name 2", "Name 3", "Name 4", "Name 5"],
            map.Legend.Select(l => l.Name));
        Assert.Equal("Detail 3", map.Legend[2].Detail);
    }

    [Fact]
    public void EveryLegendEntryHasABuildingAndEveryKeyedBuildingIsLabelled()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            PlaceMap map = Map(seed, scale: 6, keys: 4);

            int[] legendKeys = [.. map.Legend.Select(l => l.Key).Order()];
            int[] buildingKeys = [.. map.Buildings.Where(b => b.IsKeyed).Select(b => b.Key!.Value).Order()];

            Assert.Equal(legendKeys, buildingKeys);
            Assert.All(
                map.Buildings.Where(b => b.IsKeyed),
                b => Assert.False(string.IsNullOrWhiteSpace(b.Label)));
        }
    }

    [Fact]
    public void KeysStopWhenTheBuildingsRunOut()
    {
        // A tiny place with more subjects than houses should key what it can rather than throw.
        PlaceMap map = Map(3, scale: 1, keys: 40);

        Assert.Equal(map.Buildings.Count(b => b.IsKeyed), map.Legend.Count);
        Assert.True(map.Legend.Count <= map.Buildings.Count);
    }

    [Fact]
    public void NothingIsKeyedWhenTheBriefListsNothing() => Assert.Empty(Map(11).Legend);

    [Theory]
    [MemberData(nameof(Shapes))]
    public void NoShapeStarvesALargePlace(string shape)
    {
        // A city drawn inside a farmhouse wall once came out smaller than a hamlet in a tree
        // stump, because the narrow archetype rejected most of the growth. Narrow silhouettes now
        // open up as the place grows.
        double AverageBuildings(int scale) =>
            Enumerable.Range(1, 12).Average(i => Map((uint)i, shape, scale).Buildings.Count);

        double hamlet = AverageBuildings(3);
        double city = AverageBuildings(6);

        Assert.True(
            city > hamlet * 1.5,
            $"A city in a '{shape}' silhouette averaged {city:F1} buildings against a hamlet's {hamlet:F1}.");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryShapeSupportsAReadableCity(string shape)
    {
        // Guards the weakest archetype in absolute terms, not just relative to a hamlet.
        double average = Enumerable.Range(1, 25).Average(i => Map((uint)i, shape, 6).Buildings.Count);

        Assert.True(average >= 20, $"Cities in a '{shape}' silhouette averaged only {average:F1} buildings.");
    }

    [Fact]
    public void TheBriefsSubjectAndShapeAreCarriedThrough()
    {
        PlaceMap map = Map(77, "vessel");

        Assert.Equal("vessel", map.Shape);
        Assert.Equal("a hollow oak", map.Subject);
    }

    [Fact]
    public void AnUnrecognisedShapeFallsBackToAHollow() =>
        Assert.Equal("hollow", MapGenerator.Generate(Brief(shape: ""), 1).Shape);

    private sealed record Plan : RollPlan;

    [Fact]
    public void MapSeedFollowsThePlansSeedUntilTheMapIsRedrawn() =>
        Assert.Equal(1234u, MapGenerator.SeedFor(new Plan { Seed = 1234 }));

    [Fact]
    public void RedrawingTheMapShiftsItsSeedWithoutTouchingThePlans()
    {
        Plan redrawn = new()
        {
            Seed = 1234,
            Rerolls = new Dictionary<string, int> { [MapGenerator.RerollKey] = 1 }
        };

        Assert.NotEqual(1234u, MapGenerator.SeedFor(redrawn));
        Assert.NotEqual(
            MapGenerator.SeedFor(redrawn),
            MapGenerator.SeedFor(redrawn with
            {
                Rerolls = new Dictionary<string, int> { [MapGenerator.RerollKey] = 2 }
            }));
    }

    [Fact]
    public void ARedrawCountOfZeroLeavesTheSeedAlone() =>
        Assert.Equal(
            1234u,
            MapGenerator.SeedFor(new Plan
            {
                Seed = 1234,
                Rerolls = new Dictionary<string, int> { [MapGenerator.RerollKey] = 0 }
            }));
}
