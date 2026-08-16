using Xunit;

namespace ProjectHorizon.Tests.Unit;

public sealed class HardSurfaceVisualRedesignTests
{
    [Fact]
    public void CompleteHardSurfaceRedesignPasses()
    {
        HardSurfaceVisualRedesignAcceptanceReport report =
            HardSurfaceVisualRedesignAcceptanceRunner.Evaluate(
                15, 11, 72,
                true, true, true,
                true, true, true);

        Assert.True(report.Passed);
        Assert.Contains("TASK-186", report.BuildOutputLine());
    }

    [Fact]
    public void PrimitiveOrIncompletePresentationFails()
    {
        HardSurfaceVisualRedesignAcceptanceReport report =
            HardSurfaceVisualRedesignAcceptanceRunner.Evaluate(
                4, 3, 7,
                false, false, false,
                true, true, true);

        Assert.False(report.Passed);
    }
}
