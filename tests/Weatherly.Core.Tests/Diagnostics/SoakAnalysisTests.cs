using Weatherly.Core.Diagnostics;

namespace Weatherly.Core.Tests.Diagnostics;

public class SoakAnalysisTests
{
    [Fact]
    public void Judge_SteadyApp_Passes()
    {
        IReadOnlyList<SoakResult> results = SoakAnalysis.Judge(Samples(hours: 8, (t, i) => Noise(i)));

        results.Should().OnlyContain(result => result.Passed);
    }

    [Fact]
    public void Judge_LeakOfAHandleAMinute_Fails()
    {
        IReadOnlyList<SoakResult> results = SoakAnalysis.Judge(Samples(hours: 8, (t, i) => Noise(i), handles: t => t));

        results.Single(result => result.Name == "Handles").Passed.Should().BeFalse("60 handles an hour is a leak");
        results.Where(result => result.Name != "Handles").Should().OnlyContain(result => result.Passed);
    }

    [Fact]
    public void Judge_MemoryLeak_FailsEvenInAShortRun()
    {
        // 10 MB a minute: a short run catches a fast leak like this one (slow ones need the long run).
        IReadOnlyList<SoakResult> results = SoakAnalysis.Judge(Samples(hours: 0.25, (t, i) => Noise(i), privateMegabytes: t => t * 10));

        results.Single(result => result.Name == "Private memory").Passed.Should().BeFalse();
    }

    [Fact]
    public void Judge_WarmUpGrowthAndASingleSpike_Pass()
    {
        // Memory climbs in the first quarter (caches fill), then one sample spikes (a resize).
        IReadOnlyList<SoakResult> results = SoakAnalysis.Judge(Samples(
            hours: 2,
            (t, i) => Noise(i),
            privateMegabytes: t => (t < 30 ? t * 3 : 90) + (Math.Abs(t - 100) < 0.5 ? 200 : 0)));

        results.Should().OnlyContain(result => result.Passed);
    }

    [Fact]
    public void Judge_TooFewSamples_Fails()
    {
        SoakAnalysis.Judge(Samples(hours: 0.01, (t, i) => 0)).Should().OnlyContain(result => !result.Passed);
    }

    [Fact]
    public void ToCsv_WritesAHeaderAndARowPerSample()
    {
        string csv = SoakAnalysis.ToCsv([new SoakSample(0.5, 120.25, 30, 812, 41, 37)]);

        csv.Should().StartWith("minutes,private_mb,managed_mb,handles,gdi,user").And.Contain("0.50,120.3,30.0,812,41,37");
    }

    private static double Noise(int i) => ((i * 7919) % 13) - 6;

    /// <summary>A sample every minute (every 10 s for short runs).</summary>
    private static List<SoakSample> Samples(double hours, Func<double, int, double> noise, Func<double, double>? handles = null, Func<double, double>? privateMegabytes = null)
    {
        double step = hours < 1 ? 1 / 6.0 : 1;
        var samples = new List<SoakSample>();
        int i = 0;
        for (double minutes = 0; minutes <= hours * 60; minutes += step, i++)
        {
            double n = noise(minutes, i);
            samples.Add(new SoakSample(
                minutes,
                (privateMegabytes?.Invoke(minutes) ?? 150) + n,
                40 + (n / 4),
                (handles?.Invoke(minutes) ?? 900) + n,
                45 + Math.Sign(n),
                40 + Math.Sign(n)));
        }

        return samples;
    }
}
