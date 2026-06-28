using FluentAssertions;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.WorksheetStructure;
using NSubstitute;
using Xunit;

namespace ItrqTool.Tasks.Tests.WorksheetStructure;

public sealed class StructureGateTests
{
    private static readonly WorksheetSchemaRef SchemaA = new("clq", "v01");
    private static readonly WorksheetSchemaRef SchemaB = new("rlq", "v01");

    private static ValidationFinding MakeFinding(string addresses) => new(
        Check: ValidationCheck.Structure,
        Evaluation: FindingEvaluation.Fatal,
        CellAddresses: addresses,
        QuestionNumber: null,
        QuestionText: null,
        RequestedData: null,
        ProvidedBy: null,
        CheckResult: $"structure.unexpected-worksheet-structure: test finding at {addresses}");

    [Fact]
    public void AllMatch_ReturnsNoFindingsAndAssetFailed_False()
    {
        var mediator = Substitute.For<IWorksheetStructureMediator>();
        mediator.Verify("path1.xlsx", SchemaA).Returns(WorksheetStructureResult.Match());
        mediator.Verify("path2.xlsx", SchemaA).Returns(WorksheetStructureResult.Match());

        var result = StructureGate.VerifyAll(mediator, [
            ("path1.xlsx", SchemaA),
            ("path2.xlsx", SchemaA),
        ]);

        result.AssetFailed.Should().BeFalse();
        result.AssetErrorReason.Should().BeNull();
        result.StructureFindings.Should().BeEmpty();
    }

    [Fact]
    public void SingleMismatch_CollectsThatFindingAndAssetFailed_False()
    {
        var finding = MakeFinding("H2");
        var mediator = Substitute.For<IWorksheetStructureMediator>();
        mediator.Verify("path1.xlsx", SchemaA).Returns(WorksheetStructureResult.Match());
        mediator.Verify("path2.xlsx", SchemaA).Returns(WorksheetStructureResult.Mismatch([finding]));

        var result = StructureGate.VerifyAll(mediator, [
            ("path1.xlsx", SchemaA),
            ("path2.xlsx", SchemaA),
        ]);

        result.AssetFailed.Should().BeFalse();
        result.AssetErrorReason.Should().BeNull();
        result.StructureFindings.Should().HaveCount(1);
        result.StructureFindings.Should().Contain(finding);
    }

    [Fact]
    public void MultiInputMismatch_CollectsAllFindingsInInputOrder()
    {
        var findingA = MakeFinding("D2");
        var findingB = MakeFinding("K2");
        var mediator = Substitute.For<IWorksheetStructureMediator>();
        mediator.Verify("path1.xlsx", SchemaA).Returns(WorksheetStructureResult.Mismatch([findingA]));
        mediator.Verify("path2.xlsx", SchemaB).Returns(WorksheetStructureResult.Mismatch([findingB]));

        var result = StructureGate.VerifyAll(mediator, [
            ("path1.xlsx", SchemaA),
            ("path2.xlsx", SchemaB),
        ]);

        result.AssetFailed.Should().BeFalse();
        result.AssetErrorReason.Should().BeNull();
        result.StructureFindings.Should().HaveCount(2);
        result.StructureFindings[0].Should().Be(findingA);
        result.StructureFindings[1].Should().Be(findingB);
    }

    [Fact]
    public void FirstAssetError_ShortCircuits_ThirdInputNeverCalled()
    {
        var mismatchFinding = MakeFinding("E2");
        var mediator = Substitute.For<IWorksheetStructureMediator>();
        mediator.Verify("path1.xlsx", SchemaA).Returns(WorksheetStructureResult.Match());
        mediator.Verify("path2.xlsx", SchemaA).Returns(WorksheetStructureResult.AssetError("reason X"));
        // path3.xlsx should never be called

        var result = StructureGate.VerifyAll(mediator, [
            ("path1.xlsx", SchemaA),
            ("path2.xlsx", SchemaA),
            ("path3.xlsx", SchemaA),
        ]);

        result.AssetFailed.Should().BeTrue();
        result.AssetErrorReason.Should().Be("reason X");
        result.StructureFindings.Should().BeEmpty();
        mediator.DidNotReceive().Verify("path3.xlsx", Arg.Any<WorksheetSchemaRef>());
    }

    [Fact]
    public void MediatorThrowsIoException_PropagatesUnchanged()
    {
        var mediator = Substitute.For<IWorksheetStructureMediator>();
        mediator.When(x => x.Verify("bad.xlsx", SchemaA)).Throw(new IOException("disk error"));

        Action act = () => StructureGate.VerifyAll(mediator, [("bad.xlsx", SchemaA)]);

        act.Should().Throw<IOException>().WithMessage("disk error");
    }
}
