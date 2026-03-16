namespace Aion2Dashboard;

internal static class BuildFlavor
{
#if DISTRIBUTION
    public const bool IsDistribution = true;
#else
    public const bool IsDistribution = false;
#endif
}
