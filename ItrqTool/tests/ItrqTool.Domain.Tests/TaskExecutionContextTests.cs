using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;

namespace ItrqTool.Domain.Tests;

public sealed class TaskExecutionContextTests
{
    [Fact]
    public void DefaultParameters_IsNonNullAndEmpty()
    {
        var ctx = new TaskExecutionContext(
            TaskId: "t",
            InputPaths: new Dictionary<string, string>(),
            OutputPaths: new Dictionary<string, string>(),
            Logger: NullLogger.Instance,
            WorkingDirectory: @"C:\tmp");

        ctx.Parameters.Should().NotBeNull();
        ctx.Parameters.Should().BeEmpty();
    }

    [Fact]
    public void Parameters_KeyLookupIsCaseInsensitive()
    {
        var ctx = new TaskExecutionContext(
            TaskId: "t",
            InputPaths: new Dictionary<string, string>(),
            OutputPaths: new Dictionary<string, string>(),
            Logger: NullLogger.Instance,
            WorkingDirectory: @"C:\tmp")
        {
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["oldworkbookpath"] = "some-path.xlsx"
            }
        };

        ctx.Parameters["OldWorkbookPath"].Should().Be("some-path.xlsx");
    }
}
