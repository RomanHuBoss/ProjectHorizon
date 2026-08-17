using System;

public enum ProjectHorizonPlayerPlatform
{
    Unsupported = 0,
    WindowsX64 = 1,
    LinuxX64 = 2
}

public static class CrossPlatformDeterminismPolicy
{
    public const int CanonicalSignatureSchemaVersion = 1;
    public const int RequiredPlatformFamilies = 2;
    public const bool PlatformSeedParityRequired = true;
    public const bool GeneratorVersionBumpRequiredForWorldChanges = true;
    public const bool SinglePlayerRequiresInternet = false;
    public const bool CloudFeaturesOptional = true;
    public const int PermittedProductionNetworkDependencies = 0;

    public static ProjectHorizonPlayerPlatform ClassifyPlatform(
        string osName,
        bool is64BitProcess)
    {
        if (!is64BitProcess || string.IsNullOrWhiteSpace(osName))
        {
            return ProjectHorizonPlayerPlatform.Unsupported;
        }

        if (string.Equals(osName, "Windows", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectHorizonPlayerPlatform.WindowsX64;
        }

        if (string.Equals(osName, "Linux", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectHorizonPlayerPlatform.LinuxX64;
        }

        return ProjectHorizonPlayerPlatform.Unsupported;
    }
}
