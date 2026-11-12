using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Windowing;
using Weatherly.Core.Diagnostics;
using Weatherly.Core.Overlay;
using Weatherly.Platform.Shell;
using Windows.Graphics;

namespace Weatherly.App;

/// <summary>
/// The soak test (PLAN.md, M13: 8 hours without a leak): all weather on, visitors coming, the app window wandering
/// over the desktop and changing its size, while memory, handles and GDI/USER objects are sampled every 30 seconds.
/// <see cref="SoakAnalysis"/> judges the growth after warm-up. Started with <c>--soak=&lt;minutes&gt;</c> together with
/// <c>--smoke-test=&lt;report&gt;</c>; the samples go to <c>soak.csv</c> next to the report.
/// </summary>
internal static class SoakRun
{
    private static readonly TimeSpan s_sampleInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_moveInterval = TimeSpan.FromSeconds(7);

    public static async Task RunAsync(SmokeTest smokeTest, MainWindow window, double minutes)
    {
        try
        {
            window.SetRain(0.6f);
            window.SetSnow(0.4f);
            window.SetLeaves(0.5f);
            window.SetFog(0.4f);
            window.SetSun(0.4f);
            window.SetGrass(0.5f);

            var samples = new List<SoakSample>();
            var clock = Stopwatch.StartNew();
            TimeSpan cpuAtStart = Process.GetCurrentProcess().TotalProcessorTime;
            TimeSpan nextSample = TimeSpan.Zero;
            int step = 0;
            RectInt32 area = DisplayArea.Primary.WorkArea;
            while (clock.Elapsed.TotalMinutes < minutes)
            {
                // The window wanders and changes its size: snow slides, puddles move, grass and visitors follow.
                step++;
                int width = 600 + ((step * 97) % 400);
                int height = 400 + ((step * 61) % 250);
                window.AppWindow.MoveAndResize(new RectInt32(
                    area.X + ((step * 173) % Math.Max(area.Width - width, 1)),
                    area.Y + 60 + ((step * 89) % Math.Max(area.Height - height - 60, 1)),
                    width,
                    height));
                if (step % 6 == 0)
                {
                    window.InviteVisitor();
                }

                if (step % 9 == 0)
                {
                    window.CollectVisitor();
                }

                if (clock.Elapsed >= nextSample)
                {
                    samples.Add(Sample(clock.Elapsed));
                    nextSample += s_sampleInterval;
                }

                await Task.Delay(s_moveInterval);
            }

            samples.Add(Sample(clock.Elapsed));
            string directory = Path.GetDirectoryName(smokeTest.ReportPath)!;
            await File.WriteAllTextAsync(Path.Combine(directory, "soak.csv"), SoakAnalysis.ToCsv(samples));

            var diagnostics = SoakAnalysis.Judge(samples)
                .Select(result => new DiagnosticResult($"Soak: {result.Name}", result.Passed, result.Detail))
                .ToList();
            OverlayStatus? status = window.Refresh();
            double cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpuAtStart).TotalSeconds / clock.Elapsed.TotalSeconds * 100;
            string budget = string.Create(
                CultureInfo.InvariantCulture,
                $"Soak: {clock.Elapsed.TotalMinutes:0} min, {samples.Count} samples, CPU {cpu:0.0} % of a core, frame {status?.FrameMilliseconds ?? 0:0.0} ms, simulation {status?.SimulationMilliseconds ?? 0:0.00} ms, {status?.Sprites ?? 0} sprites");
            int exitCode = smokeTest.WriteReport(diagnostics, [budget]);
            window.Close();
            Environment.Exit(exitCode);
        }
#pragma warning disable CA1031 // Any failure of the soak run is its result.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            smokeTest.Fail(exception);
        }
    }

    private static SoakSample Sample(TimeSpan elapsed)
    {
        using Process process = Process.GetCurrentProcess();
        (int gdi, int user) = SystemInfo.GuiResources();
        long managed = GC.GetTotalMemory(forceFullCollection: true);
        return new SoakSample(elapsed.TotalMinutes, process.PrivateMemorySize64 / 1048576.0, managed / 1048576.0, process.HandleCount, gdi, user);
    }
}
