using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;

namespace ItrqTool.Infrastructure.Tests;

public sealed class JsonWorkflowLoaderTests
{
    private static string TestWorkDir() =>
        Path.Combine(Path.GetTempPath(), "ItrqTool-infra-tests", Guid.NewGuid().ToString("N"));

    private static JsonWorkflowLoader Loader(string path) =>
        new(path, NullLogger<JsonWorkflowLoader>.Instance);

    // ── Constructor validation ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullPath_Throws()
    {
        var act = () => new JsonWorkflowLoader(null!, NullLogger<JsonWorkflowLoader>.Instance);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("workflowsDirectoryPath");
    }

    [Fact]
    public void Constructor_WhitespacePath_Throws()
    {
        var act = () => new JsonWorkflowLoader("   ", NullLogger<JsonWorkflowLoader>.Instance);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("workflowsDirectoryPath");
    }

    // ── Directory existence ────────────────────────────────────────────────────

    [Fact]
    public void LoadAll_DirectoryDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nope");
        var act = () => Loader(missing).LoadAll();
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void LoadAll_EmptyDirectory_ReturnsEmptyResult()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var result = Loader(dir).LoadAll();
            result.Workflows.Should().BeEmpty();
            result.Failures.Should().BeEmpty();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Happy paths ────────────────────────────────────────────────────────────

    [Fact]
    public void LoadAll_SingleValidFileWithEmptyTaskList_LoadsSuccessfully()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "wf.json"),
                """{"id": "wf1", "name": "WF One", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().HaveCount(1);
            result.Workflows[0].Id.Should().Be("wf1");
            result.Workflows[0].Name.Should().Be("WF One");
            result.Workflows[0].Nodes.Should().BeEmpty();
            result.Failures.Should().BeEmpty();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_SingleValidFileWithTasks_LoadsSuccessfully()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "smoketest.json"), """
                {
                    "id": "smoketest",
                    "name": "Smoke Test",
                    "tasks": [
                        {
                            "id": "first",
                            "type": "NoOp",
                            "inputs": {},
                            "outputs": { "out": "first_output.txt" }
                        },
                        {
                            "id": "second",
                            "type": "NoOp",
                            "inputs": { "in": "first.out" },
                            "outputs": { "out": "second_output.txt" }
                        }
                    ]
                }
                """);

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows.Should().HaveCount(1);
            var wf = result.Workflows[0];
            wf.Id.Should().Be("smoketest");
            wf.Nodes.Should().HaveCount(2);
            wf.Nodes[0].Id.Should().Be("first");
            wf.Nodes[0].Inputs.Should().BeEmpty();
            wf.Nodes[1].Id.Should().Be("second");
            wf.Nodes[1].Inputs["in"].Should().Be(new TaskOutputRef("first", "out"));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Failure paths ──────────────────────────────────────────────────────────

    [Fact]
    public void LoadAll_MalformedJson_ReturnsAsFailure()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "bad.json");
            File.WriteAllText(file, "{ not valid json");

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().BeEmpty();
            result.Failures.Should().HaveCount(1);
            result.Failures[0].FilePath.Should().Be(file);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_MissingRequiredField_ReturnsAsFailure()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            // Missing top-level "id"
            var file = Path.Combine(dir, "missing.json");
            File.WriteAllText(file, """{"name": "NoId", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().BeEmpty();
            result.Failures.Should().HaveCount(1);
            result.Failures[0].FilePath.Should().Be(file);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_WorkflowWithCycle_ReturnsAsFailure()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "cycle.json");
            File.WriteAllText(file, """
                {
                    "id": "cyclic",
                    "name": "Cyclic",
                    "tasks": [
                        {"id": "a", "type": "T", "inputs": {"in": "b.out"}, "outputs": {"out": "a.txt"}},
                        {"id": "b", "type": "T", "inputs": {"in": "a.out"}, "outputs": {"out": "b.txt"}}
                    ]
                }
                """);

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().BeEmpty();
            result.Failures.Should().HaveCount(1);
            result.Failures[0].FilePath.Should().Be(file);
            result.Failures[0].ErrorMessage.Should().Contain("cycle");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_WorkflowWithMissingReference_ReturnsAsFailure()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "missing-ref.json");
            File.WriteAllText(file, """
                {
                    "id": "missing-ref",
                    "name": "Missing Ref",
                    "tasks": [
                        {"id": "a", "type": "T", "inputs": {"in": "ghost.out"}, "outputs": {}}
                    ]
                }
                """);

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().BeEmpty();
            result.Failures.Should().HaveCount(1);
            result.Failures[0].FilePath.Should().Be(file);
            result.Failures[0].ErrorMessage.Should().Contain("unknown upstream task");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_MalformedInputReference_ReturnsAsFailure()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            // "nodot" has no '.' separator
            var file = Path.Combine(dir, "malformed-ref.json");
            File.WriteAllText(file, """
                {
                    "id": "malformed",
                    "name": "Malformed",
                    "tasks": [
                        {"id": "a", "type": "T", "inputs": {"in": "nodot"}, "outputs": {}}
                    ]
                }
                """);

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().BeEmpty();
            result.Failures.Should().HaveCount(1);
            result.Failures[0].FilePath.Should().Be(file);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Mixed results ──────────────────────────────────────────────────────────

    [Fact]
    public void LoadAll_MixOfValidAndInvalid_PopulatesBothLists()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            var validFile = Path.Combine(dir, "a_valid.json");
            var badFile = Path.Combine(dir, "b_malformed.json");
            var cycleFile = Path.Combine(dir, "c_cycle.json");

            File.WriteAllText(validFile, """{"id": "valid", "name": "Valid", "tasks": []}""");
            File.WriteAllText(badFile, "{ not valid json");
            File.WriteAllText(cycleFile, """
                {
                    "id": "cyclic",
                    "name": "Cyclic",
                    "tasks": [
                        {"id": "a", "type": "T", "inputs": {"in": "b.out"}, "outputs": {"out": "a.txt"}},
                        {"id": "b", "type": "T", "inputs": {"in": "a.out"}, "outputs": {"out": "b.txt"}}
                    ]
                }
                """);

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().HaveCount(1);
            result.Workflows[0].Id.Should().Be("valid");
            result.Failures.Should().HaveCount(2);
            result.Failures.Select(f => f.FilePath)
                .Should().Contain(badFile)
                .And.Contain(cycleFile);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── File filtering ─────────────────────────────────────────────────────────

    [Fact]
    public void LoadAll_NonJsonFiles_AreIgnored()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "workflow.json"),
                """{"id": "wf1", "name": "WF1", "tasks": []}""");
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "this is not JSON");

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().HaveCount(1);
            result.Failures.Should().BeEmpty();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_SubdirectoryFiles_AreIgnored()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "root.json"),
                """{"id": "root-wf", "name": "Root", "tasks": []}""");

            var sub = Path.Combine(dir, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "sub.json"),
                """{"id": "sub-wf", "name": "Sub", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().HaveCount(1);
            result.Workflows[0].Id.Should().Be("root-wf");
            result.Failures.Should().BeEmpty();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Ordering ───────────────────────────────────────────────────────────────

    [Fact]
    public void LoadAll_ResultsOrderedByFileName()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            // Created in non-alphabetical order; result must be sorted by filename.
            File.WriteAllText(Path.Combine(dir, "c.json"), """{"id": "cid", "name": "C", "tasks": []}""");
            File.WriteAllText(Path.Combine(dir, "a.json"), """{"id": "aid", "name": "A", "tasks": []}""");
            File.WriteAllText(Path.Combine(dir, "b.json"), """{"id": "bid", "name": "B", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().HaveCount(3);
            result.Workflows[0].Id.Should().Be("aid");
            result.Workflows[1].Id.Should().Be("bid");
            result.Workflows[2].Id.Should().Be("cid");
            result.Failures.Should().BeEmpty();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Parameters deserialization ─────────────────────────────────────────────

    [Fact]
    public void LoadAll_NodeWithParameters_DeserializesCorrectly()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "params.json"), """
                {
                    "id": "wf1",
                    "name": "WF One",
                    "tasks": [
                        {
                            "id": "t1",
                            "type": "NoOp",
                            "inputs": {},
                            "outputs": {},
                            "parameters": { "foo": "bar" }
                        }
                    ]
                }
                """);

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows[0].Nodes[0].Parameters["foo"].Should().Be("bar");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Group field deserialization ────────────────────────────────────────────

    [Fact]
    public void LoadAll_GroupFieldPresent_DeserializesCorrectly()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "grouped.json"),
                """{"id": "wf1", "name": "WF One", "group": "2025 Audit", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows[0].Group.Should().Be("2025 Audit");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_GroupFieldAbsent_GroupIsNull()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "nogroup.json"),
                """{"id": "wf1", "name": "WF One", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows[0].Group.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_GroupFieldExplicitNull_GroupIsNull()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "nullgroup.json"),
                """{"id": "wf1", "name": "WF One", "group": null, "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows[0].Group.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_NodeWithoutParameters_DefaultsToEmptyNotNull()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "noparams.json"), """
                {
                    "id": "wf1",
                    "name": "WF One",
                    "tasks": [
                        {
                            "id": "t1",
                            "type": "NoOp",
                            "inputs": {},
                            "outputs": {}
                        }
                    ]
                }
                """);

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows[0].Nodes[0].Parameters.Should().NotBeNull().And.BeEmpty();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Hierarchical id — validation ───────────────────────────────────────────

    [Theory]
    [InlineData("A::B", "a_empty.json")]
    [InlineData(":B",   "b_leading.json")]
    [InlineData("A:",   "c_trailing.json")]
    public void LoadAll_IdWithEmptySegment_ReturnsAsFailure(string id, string file)
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, file),
                $$"""{ "id": "{{id}}", "name": "WF", "tasks": [] }""");

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().BeEmpty();
            result.Failures.Should().HaveCount(1);
            result.Failures[0].ErrorMessage.Should().Contain("empty segment");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Theory]
    [InlineData("A/B:C")]
    [InlineData("A:B/C")]
    public void LoadAll_IdSegmentContainsForwardSlash_ReturnsAsFailure(string id)
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "slash.json"),
                $$"""{ "id": "{{id}}", "name": "WF", "tasks": [] }""");

            var result = Loader(dir).LoadAll();

            result.Workflows.Should().BeEmpty();
            result.Failures.Should().HaveCount(1);
            result.Failures[0].ErrorMessage.Should().Contain("illegal");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_HierarchicalId_LoadsSuccessfully()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "hier.json"),
                """{"id": "A:B:task", "name": "Hierarchical", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows.Should().HaveCount(1);
            result.Workflows[0].Id.Should().Be("A:B:task");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    // ── Hierarchical id — group derivation ────────────────────────────────────

    [Fact]
    public void LoadAll_HierarchicalId_NoGroup_DeriveGroupAsFullId()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "derive.json"),
                """{"id": "A:B:task", "name": "WF", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows[0].Group.Should().Be("A:B:task");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_TwoSegmentId_NoGroup_DeriveGroupAsFullId()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "two.json"),
                """{"id": "Audit:task", "name": "WF", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows[0].Group.Should().Be("Audit:task");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_HierarchicalIdWithSpaces_NoGroup_DeriveGroupAsFullId()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "spaces.json"),
                """{"id": "RefYear2025:Phase 0", "name": "WF", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows[0].Group.Should().Be("RefYear2025:Phase 0");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_HierarchicalId_ExplicitGroupNotOverridden()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "explicit.json"),
                """{"id": "A:B:task", "name": "WF", "group": "Custom", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows[0].Group.Should().Be("Custom");
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }

    [Fact]
    public void LoadAll_FlatId_NoGroup_GroupRemainsNull()
    {
        var dir = TestWorkDir();
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "flat.json"),
                """{"id": "simple", "name": "WF", "tasks": []}""");

            var result = Loader(dir).LoadAll();

            result.Failures.Should().BeEmpty();
            result.Workflows[0].Group.Should().BeNull();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch (IOException) { } }
    }
}
