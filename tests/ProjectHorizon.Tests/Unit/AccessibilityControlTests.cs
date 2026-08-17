using Xunit;

public sealed class AccessibilityControlTests
{
    [Fact]
    public void DeadZoneIsClampedToSupportedRange()
    {
        Assert.Equal(AccessibilityControlPolicy.MinimumGamepadDeadZone,
            AccessibilityControlPolicy.NormalizeDeadZone(-1.0f));
        Assert.Equal(AccessibilityControlPolicy.MaximumGamepadDeadZone,
            AccessibilityControlPolicy.NormalizeDeadZone(1.0f));
    }

    [Fact]
    public void ResponseCurvePreservesSignAndFullScale()
    {
        Assert.Equal(1.0f, AccessibilityControlPolicy.ShapeScalar(1.0f, 1.7f), 4);
        Assert.Equal(-1.0f, AccessibilityControlPolicy.ShapeScalar(-1.0f, 1.7f), 4);
        Assert.True(AccessibilityControlPolicy.ShapeScalar(0.5f, 1.5f) < 0.5f);
    }

    [Fact]
    public void StatusSeverityDoesNotDependOnColor()
    {
        Assert.Equal("CRIT", AccessibilityControlPolicy.SeverityToken(0.10));
        Assert.Equal("LOW", AccessibilityControlPolicy.SeverityToken(0.30));
        Assert.Equal("OK", AccessibilityControlPolicy.SeverityToken(0.80));
    }

    [Fact]
    public void SubtitleScaleIsBounded()
    {
        Assert.Equal(AccessibilityControlPolicy.MinimumSubtitleScale,
            AccessibilityControlPolicy.NormalizeSubtitleScale(0.1f));
        Assert.Equal(AccessibilityControlPolicy.MaximumSubtitleScale,
            AccessibilityControlPolicy.NormalizeSubtitleScale(5.0f));
    }
}
