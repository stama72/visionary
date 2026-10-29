using Visionary.Sim;
using Visionary.Sim.Commands;
using Visionary.Sim.Systems;
using Visionary.Sim.Time;

namespace Visionary.Presentation.Tests;

public class ArchitectureAndLabelsTests
{
    [Fact]
    public void PresentationAssembly_DoesNotReferenceGodot()
    {
        var references = typeof(TickPump).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, r => r.Name!.StartsWith("Godot", StringComparison.Ordinal));
    }

    [Fact]
    public void Labels_CoverEveryEnumValueWithDistinctNonEmptyNames()
    {
        AssertCovered(Enum.GetValues<Occupation>(), Labels.Of);
        AssertCovered(Enum.GetValues<Season>(), Labels.Of);
        AssertCovered(Enum.GetValues<TimetableActivity>(), Labels.Of);
        AssertCovered(Enum.GetValues<CommandStatus>(), Labels.Of);
    }

    [Fact]
    public void Labels_UnknownValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Labels.Of((Occupation)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Labels.Of((Season)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Labels.Of((TimetableActivity)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => Labels.Of((CommandStatus)99));
    }

    [Fact]
    public void Labels_AcceptedIsUkeTsuketa()
    {
        Assert.Equal("受け付けた", Labels.Of(CommandStatus.Accepted));
    }

    private static void AssertCovered<T>(T[] values, Func<T, string> label)
    {
        var names = values.Select(label).ToList();

        Assert.All(names, n => Assert.False(string.IsNullOrEmpty(n)));
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}
