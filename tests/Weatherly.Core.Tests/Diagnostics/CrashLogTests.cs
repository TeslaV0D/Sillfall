using Weatherly.Core.Diagnostics;

namespace Weatherly.Core.Tests.Diagnostics;

public sealed class CrashLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"weatherly-logs-{Guid.NewGuid():N}");
    private DateTimeOffset _now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Write_Crash_CreatesAReportWithTheError()
    {
        var log = new CrashLog(_directory, () => _now);

        string? path = log.Write(Thrown(new InvalidOperationException("The device went away.")), fatal: true, "overlay thread");

        path.Should().NotBeNull();
        string text = File.ReadAllText(path!);
        text.Should().Contain($"{AppInfo.Name} ").And.Contain("crash report")
            .And.Contain("Time (UTC): 2026-11-12 10:00:00")
            .And.Contain("Where: overlay thread")
            .And.Contain("InvalidOperationException: The device went away.")
            .And.Contain("Nothing was sent");
        Path.GetFileName(path).Should().StartWith("crash-20261112-100000");
    }

    [Fact]
    public void Write_SameErrorTwice_IsWrittenOnce_ButCrashesAlways()
    {
        var log = new CrashLog(_directory, Tick);
        Exception error = Thrown(new IOException("disk full"));

        log.Write(error, fatal: false).Should().NotBeNull();
        log.Write(error, fatal: false).Should().BeNull("the same error is reported once per session");
        log.Write(error, fatal: true).Should().NotBeNull();
        log.Reports().Should().HaveCount(2);
    }

    [Fact]
    public void Write_KeepsOnlyTheNewestReports()
    {
        var log = new CrashLog(_directory, Tick);

        for (int i = 0; i < CrashLog.Keep + 5; i++)
        {
            log.Write(Thrown(new InvalidOperationException($"error {i}")), fatal: true);
        }

        IReadOnlyList<string> reports = log.Reports();
        reports.Should().HaveCount(CrashLog.Keep);
        File.ReadAllText(reports[0]).Should().Contain($"error {CrashLog.Keep + 4}", "the newest comes first");
    }

    [Fact]
    public void Write_UnwritableFolder_DoesNotThrow()
    {
        string file = Path.Combine(Path.GetTempPath(), $"weatherly-not-a-folder-{Guid.NewGuid():N}");
        File.WriteAllText(file, "x");
        try
        {
            var log = new CrashLog(file);

            log.Write(new InvalidOperationException("x"), fatal: true).Should().BeNull();
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Redact_ReplacesProfileFolderAndUserName()
    {
        const string Text = @"Could not open C:\Users\Anton.M\AppData\Local\Weatherly\settings.json for anton.m";

        string redacted = CrashLog.Redact(Text, @"C:\Users\Anton.M", "Anton.M");

        redacted.Should().Be(@"Could not open %USERPROFILE%\AppData\Local\Weatherly\settings.json for <user>");
    }

    private DateTimeOffset Tick()
    {
        _now = _now.AddSeconds(1);
        return _now;
    }

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }
}
