using Structed.Inkwell.Generation;

namespace Structed.Inkwell.Tests;

/// <summary>
/// Covers resolving a field from its own stream, and the locks and re-rolls layered over it.
/// </summary>
/// <remarks>
/// These are the guarantees a shared seed link rests on. Each field path draws from a stream of its
/// own, so a re-roll is surgical; a lock stores a table position rather than words, so it survives
/// a change of language. Both are easy to break and neither fails loudly when broken.
/// </remarks>
public class RollContextTests
{
    private sealed record Plan : RollPlan
    {
        public Plan WithPin(string path, string value) => this with { Pins = PinsWith(path, value) };

        public Plan WithoutPin(string path) => this with { Pins = PinsWithout(path) };

        public Plan WithoutPinsUnder(string prefix) => this with { Pins = PinsWithoutPrefix(prefix) };

        public Plan WithReroll(string path) =>
            this with { Rerolls = RerollsWith(path), Pins = PinsWithout(path) };
    }

    private static readonly string[] Table =
        ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta"];

    private static readonly string[] Translated =
        ["Alpha!", "Beta!", "Gamma!", "Delta!", "Epsilon!", "Zeta!", "Eta!", "Theta!"];

    private static Plan Seeded(uint seed = 0x5EED_1234) => new() { Seed = seed };

    [Fact]
    public void TheSamePlanAlwaysResolvesTheSameValue()
    {
        Plan plan = Seeded();

        Assert.Equal(
            new RollContext(plan).Text("settlement/event", Table),
            new RollContext(plan).Text("settlement/event", Table));
    }

    [Fact]
    public void DifferentSeedsResolveIndependently()
    {
        string[] values = [.. new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 }
            .Select(seed => new RollContext(Seeded(seed)).Text("settlement/event", Table))];

        Assert.True(values.Distinct().Count() > 1);
    }

    [Fact]
    public void EachPathDrawsFromItsOwnStream()
    {
        RollContext context = new(Seeded());

        Assert.NotEqual(
            context.Dice("settlement/event").NextIndex(int.MaxValue),
            context.Dice("settlement/name").NextIndex(int.MaxValue));
    }

    [Fact]
    public void RerollingOnePathLeavesItsNeighboursAlone()
    {
        Plan before = Seeded();
        Plan after = before.WithReroll("settlement/event");

        Assert.NotEqual(
            new RollContext(before).Text("settlement/event", Table),
            new RollContext(after).Text("settlement/event", Table));

        Assert.Equal(
            new RollContext(before).Text("settlement/name", Table),
            new RollContext(after).Text("settlement/name", Table));
    }

    [Fact]
    public void EachSuccessiveRerollDrawsAgain()
    {
        Plan plan = Seeded();
        List<string> seen = [];

        for (int i = 0; i < 6; i++)
        {
            seen.Add(new RollContext(plan).Text("settlement/event", Table));
            plan = plan.WithReroll("settlement/event");
        }

        Assert.True(seen.Distinct().Count() > 1);
    }

    [Fact]
    public void APinIsReturnedInsteadOfARoll() =>
        Assert.Equal("theta", new RollContext(Seeded().WithPin("settlement/event", "#7")).Text("settlement/event", Table));

    [Fact]
    public void APinResolvesThroughWhicheverTableIsLoaded()
    {
        Plan plan = Seeded().WithPin("settlement/event", "#7");

        Assert.Equal("theta", new RollContext(plan).Text("settlement/event", Table));
        Assert.Equal("Theta!", new RollContext(plan).Text("settlement/event", Translated));
    }

    [Fact]
    public void TextThatTheUserTypedIsStoredVerbatim() =>
        Assert.Equal(
            "Hollowbridge",
            new RollContext(Seeded().WithPin("settlement/name", "Hollowbridge")).Text("settlement/name", Table));

    [Fact]
    public void APinSurvivesARerollOfEverythingAroundIt()
    {
        Plan plan = Seeded()
            .WithPin("settlement/name", "Hollowbridge")
            .WithReroll("settlement/event");

        Assert.Equal("Hollowbridge", new RollContext(plan).Text("settlement/name", Table));
    }

    [Fact]
    public void RerollingAPinnedPathReleasesThePin()
    {
        Plan plan = Seeded().WithPin("settlement/event", "#7").WithReroll("settlement/event");

        Assert.False(plan.IsPinned("settlement/event"));
        Assert.NotEqual("theta", new RollContext(plan).Text("settlement/event", Table));
    }

    [Fact]
    public void RecordsThePositionARolledValueCameFrom()
    {
        RollContext context = new(Seeded());
        string value = context.Text("settlement/event", Table);

        Assert.True(PinReference.TryGetIndex(context.PinValues["settlement/event"], out int index));
        Assert.Equal(value, Table[index]);
    }

    [Fact]
    public void RecordsAPinAsItWasWritten()
    {
        RollContext context = new(Seeded().WithPin("settlement/name", "Hollowbridge"));
        context.Text("settlement/name", Table);

        Assert.Equal("Hollowbridge", context.PinValues["settlement/name"]);
    }

    [Fact]
    public void AnEmptyTableYieldsAnEmptyValueRatherThanThrowing()
    {
        RollContext context = new(Seeded());

        Assert.Equal("", context.Text("settlement/event", []));
        Assert.Equal("", context.PinValues["settlement/event"]);
    }

    [Fact]
    public void PicksSeveralDistinctEntries()
    {
        IReadOnlyList<string> picked = new RollContext(Seeded()).TextMany("settlement/industries", Table, 3);

        Assert.Equal(3, picked.Count);
        Assert.Equal(3, picked.Distinct().Count());
        Assert.All(picked, value => Assert.Contains(value, Table));
    }

    [Fact]
    public void ASelectionIsRecordedAndPinnedAsAUnit()
    {
        RollContext rolled = new(Seeded());
        IReadOnlyList<string> picked = rolled.TextMany("settlement/industries", Table, 2);
        string pin = rolled.PinValues["settlement/industries"];

        Assert.Equal(2, pin.Split('\n').Length);
        Assert.Equal(picked, new RollContext(Seeded(99).WithPin("settlement/industries", pin))
            .TextMany("settlement/industries", Table, 2));
    }

    [Fact]
    public void WithoutPinsUnderClearsAWholeSubtree()
    {
        Plan plan = Seeded()
            .WithPin("shop/1/keeper", "#1")
            .WithPin("shop/2/keeper", "#2")
            .WithPin("settlement/name", "Hollowbridge")
            .WithoutPinsUnder("shop/");

        Assert.Equal(["settlement/name"], plan.Pins.Keys);
    }

    [Fact]
    public void WithoutPinLeavesThePlanOtherwiseIntact()
    {
        Plan plan = Seeded().WithPin("a", "#1").WithPin("b", "#2").WithoutPin("a");

        Assert.False(plan.IsPinned("a"));
        Assert.True(plan.IsPinned("b"));
    }

    [Fact]
    public void PinningDoesNotMutateThePlanItCameFrom()
    {
        Plan before = Seeded();
        before.WithPin("settlement/name", "Hollowbridge");

        Assert.Empty(before.Pins);
    }

    [Fact]
    public void ARerollCountOfZeroDrawsTheSameStreamAsNoEntryAtAll()
    {
        Plan explicitZero = Seeded() with { Rerolls = new Dictionary<string, int> { ["settlement/event"] = 0 } };

        Assert.Equal(
            new RollContext(Seeded()).Text("settlement/event", Table),
            new RollContext(explicitZero).Text("settlement/event", Table));
    }
}
