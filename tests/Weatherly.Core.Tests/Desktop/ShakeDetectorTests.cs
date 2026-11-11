using Weatherly.Core.Desktop;

namespace Weatherly.Core.Tests.Desktop;

public class ShakeDetectorTests
{
    [Fact]
    public void Add_ThreeFastLargeReversals_DetectsAShake()
    {
        Feed(Oscillation(amplitude: 40, step: 0.08, swings: 4)).Should().Equal(4);
    }

    [Fact]
    public void Add_ReversalsSpreadOverMoreThanTheWindow_IsNoShake()
    {
        // Three reversals span two steps: 0.7 s here, more than the 0.6 s window.
        Feed(Oscillation(amplitude: 40, step: 0.35, swings: 6)).Should().BeEmpty();
    }

    [Fact]
    public void Add_ThreeReversalsJustInsideTheWindow_IsAShake()
    {
        // Two steps of 0.29 s: 0.58 s between the first and the third reversal.
        Feed(Oscillation(amplitude: 40, step: 0.29, swings: 4)).Should().Equal(4);
    }

    [Fact]
    public void Add_SmallSwings_AreNoShake()
    {
        Feed(Oscillation(amplitude: 9, step: 0.05, swings: 10)).Should().BeEmpty();
    }

    [Fact]
    public void Add_FastDragInOneDirection_IsNoShake()
    {
        var samples = Enumerable.Range(0, 30).Select(i => (i * 0.016, i * 50f));

        Feed(samples).Should().BeEmpty();
    }

    [Fact]
    public void Add_LongShake_ReportsOncePerThreeReversals()
    {
        Feed(Oscillation(amplitude: 40, step: 0.08, swings: 10)).Should().Equal(4, 7, 10);
    }

    [Fact]
    public void Add_JitterBelowTheAmplitude_DoesNotCountAsReversal()
    {
        // Big swings with small wobbles at each extreme: the wobbles must not add reversals.
        (double, float)[] samples =
        [
            (0.00, 0), (0.05, 60), (0.07, 55), (0.09, 60),
            (0.14, 0), (0.16, 5), (0.18, 0),
            (0.23, 60), (0.25, 55),
        ];

        Feed(samples).Should().BeEmpty();
    }

    [Fact]
    public void Reset_ForgetsEarlierReversals()
    {
        var detector = new ShakeDetector();
        (double Time, float Position)[] firstTwo = [.. Oscillation(amplitude: 40, step: 0.08, swings: 3)];
        foreach ((double time, float position) in firstTwo)
        {
            detector.Add(time, position).Should().BeFalse();
        }

        detector.Reset();

        detector.Add(0.3, -40).Should().BeFalse();
        detector.Add(0.38, 40).Should().BeFalse();
    }

    /// <summary>Positions swinging between -amplitude and +amplitude, one sample per swing end. Starts at 0.</summary>
    private static IEnumerable<(double Time, float Position)> Oscillation(float amplitude, double step, int swings)
    {
        yield return (0, 0);
        for (int i = 1; i <= swings; i++)
        {
            yield return (i * step, i % 2 == 1 ? amplitude : -amplitude);
        }
    }

    /// <summary>Returns the indices of the samples at which a shake was reported.</summary>
    private static List<int> Feed(IEnumerable<(double Time, float Position)> samples)
    {
        var detector = new ShakeDetector();
        var hits = new List<int>();
        int index = 0;
        foreach ((double time, float position) in samples)
        {
            if (detector.Add(time, position))
            {
                hits.Add(index);
            }

            index++;
        }

        return hits;
    }
}
