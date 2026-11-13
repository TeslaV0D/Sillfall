namespace Sillfall.Core.Tests;

/// <summary>Measures managed allocations of a frame-loop body for the "does not allocate" tests.</summary>
internal static class Allocations
{
    /// <summary>
    /// Bytes <paramref name="body"/> allocates on the current thread. <see cref="GC.GetAllocatedBytesForCurrentThread"/>
    /// can jump by an allocation quantum (about 8 KB) when a garbage collection, started by a test running in
    /// parallel, lands inside the measurement. A real allocation in the body shows up in every attempt, such a jump
    /// does not, so the smallest of a few attempts is the honest number.
    /// </summary>
    public static long Measure(Action body, int attempts = 3)
    {
        long smallest = long.MaxValue;
        for (int attempt = 0; attempt < attempts && smallest > 0; attempt++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            body();
            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return smallest;
    }
}
