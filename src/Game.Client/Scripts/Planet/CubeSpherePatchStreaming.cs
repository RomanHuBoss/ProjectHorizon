using System.Diagnostics;
using CancellationToken = System.Threading.CancellationToken;

public readonly record struct CubeSpherePatchBuildRequest(
    long JobId,
    int PlanRevision,
    CubeSpherePatchKey Key,
    int Resolution,
    float Radius,
    float HeightAmplitude,
    float NoiseFrequency,
    int NoiseSeed,
    float SkirtDepth);

public sealed class CubeSpherePatchBuildResult
{
    public CubeSpherePatchBuildResult(
        CubeSpherePatchBuildRequest request,
        CubeSpherePatchData patchData,
        double buildMilliseconds)
    {
        Request = request;
        PatchData = patchData;
        BuildMilliseconds = buildMilliseconds;
    }

    public CubeSpherePatchBuildRequest Request { get; }

    public CubeSpherePatchData PatchData { get; }

    public double BuildMilliseconds { get; }
}

public static class CubeSpherePatchDataBuilder
{
    public static CubeSpherePatchBuildResult Build(
        CubeSpherePatchBuildRequest request,
        CancellationToken cancellationToken)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        CubeSpherePatchData patchData = CubeSpherePatchBuilder.BuildPatch(
            request.Key,
            request.Resolution,
            request.Radius,
            request.HeightAmplitude,
            request.NoiseFrequency,
            request.NoiseSeed,
            request.SkirtDepth,
            cancellationToken);
        stopwatch.Stop();

        return new CubeSpherePatchBuildResult(
            request,
            patchData,
            stopwatch.Elapsed.TotalMilliseconds);
    }
}
