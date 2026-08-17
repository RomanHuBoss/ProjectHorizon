using System;

public static class EnduranceSoakPolicy
{
    public const double RequiredCertificationHours = 8.0;
    public const double RequiredCertificationSeconds = RequiredCertificationHours * 60.0 * 60.0;
    public const double SampleIntervalSeconds = 1.0;
    public const double HeartbeatIntervalSeconds = 60.0;
    public const double SyntheticWorkloadIntervalSeconds = 30.0;
    public const double PersistenceCheckpointIntervalSeconds = 5.0 * 60.0;
    public const double DatabaseIntegrityIntervalSeconds = 15.0 * 60.0;
    public const double MaximumQueueStallSeconds = 120.0;
    public const long MaximumManagedMemoryGrowthBytes = 768L * 1024L * 1024L;
    public const int MaximumConcurrentDatabaseWriters = 1;
    public const double MinimumCoverageRatio = 0.90;

    public static double NormalizeRequestedHours(double requestedHours) =>
        Math.Clamp(requestedHours, 0.01, RequiredCertificationHours);

    public static bool IsCertificationDuration(double durationSeconds) =>
        durationSeconds >= RequiredCertificationSeconds - 0.5;

    public static int RequiredCoverageCount(
        double durationSeconds,
        double intervalSeconds)
    {
        if (durationSeconds <= 0.0 || intervalSeconds <= 0.0)
        {
            return 0;
        }
        return Math.Max(
            1,
            (int)Math.Floor(
                (durationSeconds / intervalSeconds) * MinimumCoverageRatio));
    }
}
