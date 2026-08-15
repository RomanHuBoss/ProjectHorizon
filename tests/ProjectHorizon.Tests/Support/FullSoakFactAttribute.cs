namespace ProjectHorizon.Tests.Support;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class FullSoakFactAttribute : FactAttribute
{
    public FullSoakFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("PROJECT_HORIZON_FULL_SOAK"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Set PROJECT_HORIZON_FULL_SOAK=1 to run destructive/full-size load tests.";
        }
    }
}
