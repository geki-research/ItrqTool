using ItrqTool.Domain;
using ItrqTool.Tasks.WorksheetStructure;

namespace ItrqTool.Integration.Tests.WorksheetStructure;

/// <summary>
/// Builds a production-shaped <see cref="IWorksheetStructureMediator"/> for use in real-reader
/// integration tests. Schemas resolve from <see cref="AppContext.BaseDirectory"/> — the integration
/// test bin has all five structure schemas copied there by the Presentation csproj copy-glob.
/// </summary>
public static class StructureGateTestSupport
{
    public static IWorksheetStructureMediator Mediator(IExcelStructureReader reader) =>
        new WorksheetStructureMediator(
            reader,
            new WorksheetStructureSchemaLoader(),
            [new SchemaVerificationStrategyV1()],
            AppContext.BaseDirectory);
}
