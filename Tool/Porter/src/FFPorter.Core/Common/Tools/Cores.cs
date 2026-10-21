namespace FFPorter.Core.Common.Tools;

public static class Cores
{
    private static volatile bool s_single;

    public static bool Single
    {
        get => s_single;
        set => s_single = value;
    }

    public static int Count => s_single ? 1 : Math.Max(1, Environment.ProcessorCount);

    public static int Half => s_single ? 1 : Math.Max(1, Environment.ProcessorCount / 2);

    public static ParallelOptions Options => new() { MaxDegreeOfParallelism = Count };
}
