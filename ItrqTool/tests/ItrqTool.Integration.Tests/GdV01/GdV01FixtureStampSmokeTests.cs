using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Infrastructure;
using ItrqTool.Tasks.WorksheetStructure;
using ItrqTool.Integration.Tests.WorksheetStructure;

namespace ItrqTool.Integration.Tests.GdV01;

/// <summary>
/// Positively proves that <see cref="GdV01BaselineFactory.WriteCurrent"/> stamps the (gd,v01)
/// structure header so that StructureGate returns Match. Exercises the stamp path before E2
/// depends on it (GD-E1 lesson 19).
/// </summary>
public sealed class GdV01FixtureStampSmokeTests
{
    [Fact]
    public void WriteCurrent_DefaultStamp_StructureGateReturnsMatch()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gd-stamp-smoke-{Guid.NewGuid():N}.xlsx");
        try
        {
            GdV01BaselineFactory.WriteCurrent(path);

            var reader   = new ClosedXmlExcelStructureReader(NullLogger<ClosedXmlExcelStructureReader>.Instance);
            var mediator = StructureGateTestSupport.Mediator(reader);

            var result = mediator.Verify(path, new WorksheetSchemaRef("gd", "v01"));
            result.Outcome.Should().Be(WorksheetStructureOutcome.Match);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
