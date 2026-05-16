using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;
using ItrqTool.Presentation.Logging;
using ItrqTool.Presentation.UIModels;

namespace ItrqTool.Integration.Tests;

public sealed class UiLogSinkTests
{
    [Fact]
    public void Add_SingleEntry_AppearsInEntries()
    {
        var sink = new UiLogSink(null);
        var entry = new LogEntry(DateTimeOffset.Now, LogLevel.Information, "Cat", "Cat", "msg");

        sink.Add(entry);

        sink.Entries.Count.Should().Be(1);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var sink = new UiLogSink(null);
        sink.Add(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "A", "A", "1"));
        sink.Add(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "A", "A", "2"));
        sink.Add(new LogEntry(DateTimeOffset.Now, LogLevel.Information, "A", "A", "3"));

        sink.Clear();

        sink.Entries.Count.Should().Be(0);
    }

    [Fact]
    public void Provider_CreatesLoggerForCategory()
    {
        var sink = new UiLogSink(null);
        var provider = new UiLogSinkProvider(sink);

        var logger = provider.CreateLogger("ItrqTool.Tasks.NoOpTask");

        logger.IsEnabled(LogLevel.Information).Should().BeTrue();
        logger.IsEnabled(LogLevel.Debug).Should().BeFalse();
    }

    [Fact]
    public void Logger_PushesToSink_OnLog()
    {
        var sink = new UiLogSink(null);
        var provider = new UiLogSinkProvider(sink);
        var logger = provider.CreateLogger("ItrqTool.Tasks.NoOpTask");

        logger.LogInformation("hello {n}", 42);

        sink.Entries.Count.Should().Be(1);
        sink.Entries[0].Message.Should().Be("hello 42");
        sink.Entries[0].ShortCategory.Should().Be("NoOpTask");
    }

    [Fact]
    public void Logger_AppendsException_WhenPresent()
    {
        var sink = new UiLogSink(null);
        var provider = new UiLogSinkProvider(sink);
        var logger = provider.CreateLogger("ItrqTool.Tasks.NoOpTask");

        logger.LogError(new InvalidOperationException("boom"), "fail");

        sink.Entries.Count.Should().Be(1);
        sink.Entries[0].Level.Should().Be(LogLevel.Error);
        sink.Entries[0].Message.Should().Contain("fail");
        sink.Entries[0].Message.Should().Contain("InvalidOperationException");
        sink.Entries[0].Message.Should().Contain("boom");
    }
}
