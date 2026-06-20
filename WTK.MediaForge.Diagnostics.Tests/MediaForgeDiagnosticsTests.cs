using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Diagnostics.Tests;

public class MediaForgeDiagnosticsTests
{
    [Fact]
    public void InMemoryDiagnosticsSink_records_reported_diagnostics()
    {
        var sink = new InMemoryDiagnosticsSink();
        var diagnostic = MediaForgeDiagnosticFactory.Create(
            MediaForgeDiagnosticSeverity.Warning,
            "TEST_CODE",
            "Test message",
            sourceId: Guid.NewGuid(),
            component: "TestComponent");

        sink.Report(diagnostic);

        Assert.Single(sink.Diagnostics);
        Assert.Equal("TEST_CODE", sink.Diagnostics[0].Code);
    }

    [Fact]
    public void NullDiagnosticsSink_ignores_diagnostics()
    {
        var sink = NullDiagnosticsSink.Instance;
        var diagnostic = MediaForgeDiagnosticFactory.Create(
            MediaForgeDiagnosticSeverity.Error,
            "IGNORED",
            "Should not throw");

        var exception = Record.Exception(() => sink.Report(diagnostic));
        Assert.Null(exception);
    }

    [Fact]
    public void Static_fallback_reports_to_current_sink()
    {
        var sink = new InMemoryDiagnosticsSink();
        var previous = MediaForgeDiagnostics.Current;

        try
        {
            MediaForgeDiagnostics.Current = sink;
            MediaForgeDiagnostics.Report(
                MediaForgeDiagnosticFactory.Create(
                    MediaForgeDiagnosticSeverity.Info,
                    "STATIC",
                    "Via static fallback"));

            Assert.Single(sink.Diagnostics);
            Assert.Equal("STATIC", sink.Diagnostics[0].Code);
        }
        finally
        {
            MediaForgeDiagnostics.Current = previous;
        }
    }

    [Fact]
    public void MediaForgeDiagnosticFactory_sets_timestamp_to_utc_now()
    {
        var before = DateTimeOffset.UtcNow;
        var diagnostic = MediaForgeDiagnosticFactory.Create(
            MediaForgeDiagnosticSeverity.Info,
            "TS",
            "Timestamp test");
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(diagnostic.Timestamp, before, after);
    }
}
