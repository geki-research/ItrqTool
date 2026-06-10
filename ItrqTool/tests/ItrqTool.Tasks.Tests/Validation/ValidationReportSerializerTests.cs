using System.Text.Json;
using FluentAssertions;
using Xunit;
using ItrqTool.Domain.Validation;
using ItrqTool.Tasks.Validation;

namespace ItrqTool.Tasks.Tests.Validation;

public sealed class ValidationReportSerializerTests
{
    // ── round-trip: all optional fields populated ─────────────────────────────

    [Fact]
    public void RoundTrip_AllOptionalsPopulated_Equals()
    {
        var report = new ValidationReport(
            Sheet: "Control Level Questions",
            TaskType: "ControlLevelValidation",
            Findings:
            [
                new ValidationFinding(
                    Check:          ValidationCheck.Deviation,
                    Evaluation:     FindingEvaluation.Error,
                    CellAddresses:  "C12",
                    QuestionNumber: "1.1",
                    QuestionText:   "What is risk?",
                    RequestedData:  "Yes/No",
                    ProvidedBy:     "OrgUnit A",
                    CheckResult:    "Value 'Maybe' is not in the allowed set.")
            ]);

        var json = ValidationReportSerializer.Serialize(report);
        var restored = ValidationReportSerializer.Deserialize(json);

        restored.Should().BeEquivalentTo(report);
    }

    // ── round-trip: all optionals null ────────────────────────────────────────

    [Fact]
    public void RoundTrip_AllOptionalsNull_Equals()
    {
        var report = new ValidationReport(
            Sheet: "General Data",
            TaskType: "GeneralDataValidation",
            Findings:
            [
                new ValidationFinding(
                    Check:          ValidationCheck.Structure,
                    Evaluation:     FindingEvaluation.Fatal,
                    CellAddresses:  "A1",
                    QuestionNumber: null,
                    QuestionText:   null,
                    RequestedData:  null,
                    ProvidedBy:     null,
                    CheckResult:    "Sheet header row is missing.")
            ]);

        var json = ValidationReportSerializer.Serialize(report);
        var restored = ValidationReportSerializer.Deserialize(json);

        restored.Should().BeEquivalentTo(report);
    }

    // ── round-trip: multi-finding report ─────────────────────────────────────

    [Fact]
    public void RoundTrip_MultipleFindingsReport_Equals()
    {
        var report = new ValidationReport(
            Sheet: "Risk Level Questions",
            TaskType: "RiskLevelValidation",
            Findings:
            [
                new ValidationFinding(ValidationCheck.FrozenValue,      FindingEvaluation.Warning,     "B5",  "2.1", "Is access logged?",    null,        null,        "Value changed from 'Yes' to 'No'."),
                new ValidationFinding(ValidationCheck.FrozenConstraint, FindingEvaluation.Information, "D10", null,  null,                   "Dropdown",  "OrgUnit B", "Constraint intact."),
                new ValidationFinding(ValidationCheck.MissingResponse,  FindingEvaluation.Error,       "C7",  "3.2", "Explain mitigation.",  "Free text", null,        "Cell is empty."),
                new ValidationFinding(ValidationCheck.SanityCheck,      FindingEvaluation.Fatal,       "E2",  null,  null,                   null,        null,        "Unexpected merged region detected.")
            ]);

        var json = ValidationReportSerializer.Serialize(report);
        var restored = ValidationReportSerializer.Deserialize(json);

        restored.Should().BeEquivalentTo(report);
    }

    // ── enum string values are exact member names ─────────────────────────────

    [Theory]
    [InlineData(FindingEvaluation.Information, "\"Information\"")]
    [InlineData(FindingEvaluation.Warning,     "\"Warning\"")]
    [InlineData(FindingEvaluation.Error,       "\"Error\"")]
    [InlineData(FindingEvaluation.Fatal,       "\"Fatal\"")]
    public void FindingEvaluation_SerializesAsExactMemberName(
        FindingEvaluation evaluation, string expectedToken)
    {
        var report = SingleFindingReport(evaluation: evaluation);
        var json = ValidationReportSerializer.Serialize(report);

        json.Should().Contain(expectedToken);
    }

    [Theory]
    [InlineData(ValidationCheck.Structure,        "\"Structure\"")]
    [InlineData(ValidationCheck.FrozenValue,      "\"FrozenValue\"")]
    [InlineData(ValidationCheck.FrozenConstraint, "\"FrozenConstraint\"")]
    [InlineData(ValidationCheck.MissingResponse,  "\"MissingResponse\"")]
    [InlineData(ValidationCheck.Deviation,        "\"Deviation\"")]
    [InlineData(ValidationCheck.SanityCheck,      "\"SanityCheck\"")]
    public void ValidationCheck_SerializesAsExactMemberName(
        ValidationCheck check, string expectedToken)
    {
        var report = SingleFindingReport(check: check);
        var json = ValidationReportSerializer.Serialize(report);

        json.Should().Contain(expectedToken);
    }

    // ── property names are camelCase ──────────────────────────────────────────

    [Fact]
    public void PropertyNames_AreCamelCase()
    {
        var report = SingleFindingReport();
        var json = ValidationReportSerializer.Serialize(report);

        var doc = JsonDocument.Parse(json).RootElement;
        doc.TryGetProperty("sheet",    out _).Should().BeTrue("'sheet' must be camelCase");
        doc.TryGetProperty("taskType", out _).Should().BeTrue("'taskType' must be camelCase");
        doc.TryGetProperty("findings", out _).Should().BeTrue("'findings' must be camelCase");

        var finding = doc.GetProperty("findings")[0];
        finding.TryGetProperty("check",         out _).Should().BeTrue();
        finding.TryGetProperty("evaluation",    out _).Should().BeTrue();
        finding.TryGetProperty("cellAddresses", out _).Should().BeTrue();
        finding.TryGetProperty("checkResult",   out _).Should().BeTrue();
    }

    // ── null optionals are omitted from JSON ──────────────────────────────────

    [Fact]
    public void NullOptionals_AreOmittedFromJson()
    {
        var report = new ValidationReport(
            Sheet: "Sheet1",
            TaskType: "T",
            Findings:
            [
                new ValidationFinding(ValidationCheck.Structure, FindingEvaluation.Information,
                    "A1", null, null, null, null, "ok")
            ]);

        var json = ValidationReportSerializer.Serialize(report);

        json.Should().NotContain("questionNumber");
        json.Should().NotContain("questionText");
        json.Should().NotContain("requestedData");
        json.Should().NotContain("providedBy");
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static ValidationReport SingleFindingReport(
        FindingEvaluation evaluation = FindingEvaluation.Information,
        ValidationCheck   check      = ValidationCheck.Structure)
        => new(
            Sheet:    "S",
            TaskType: "T",
            Findings: [new ValidationFinding(check, evaluation, "A1", null, null, null, null, "ok")]);
}
