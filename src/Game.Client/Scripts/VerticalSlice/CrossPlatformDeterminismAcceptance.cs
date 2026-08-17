public static class CrossPlatformDeterminismAcceptanceRunner
{
    public static CrossPlatformDeterminismReport Run(
        PlanetEnvironmentRuntime environmentRuntime,
        PlanetaryPoiCatalog poiCatalog)
    {
        return CrossPlatformDeterminismRuntime.Run(
            environmentRuntime,
            poiCatalog);
    }
}
