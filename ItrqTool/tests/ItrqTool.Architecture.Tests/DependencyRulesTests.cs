using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;
using ItrqTool.Application;
using ItrqTool.Domain;
using ItrqTool.Infrastructure;
using ItrqTool.Presentation;
using ItrqTool.Tasks;

namespace ItrqTool.Architecture.Tests;

public sealed class DependencyRulesTests
{
    private static readonly Assembly DomainAssembly        = typeof(IWorkflowTask).Assembly;
    private static readonly Assembly ApplicationAssembly   = typeof(WorkflowSession).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(ClosedXmlExcelReader).Assembly;
    private static readonly Assembly TasksAssembly         = typeof(IWorkflowTaskMarker).Assembly;
    private static readonly Assembly PresentationAssembly  = typeof(App).Assembly;

    // Rule 1a — Domain must not depend on any other layer
    [Fact]
    public void Domain_MustNotDependOnApplicationLayer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Domain must have zero project references; it is the innermost layer.");
    }

    [Fact]
    public void Domain_MustNotDependOnInfrastructureLayer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Domain must have zero project references; it is the innermost layer.");
    }

    [Fact]
    public void Domain_MustNotDependOnTasksLayer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Tasks")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Domain must have zero project references; it is the innermost layer.");
    }

    [Fact]
    public void Domain_MustNotDependOnPresentationLayer()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Presentation")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Domain must have zero project references; it is the innermost layer.");
    }

    // Rule 1b — Application must not depend on Infrastructure, Tasks, or Presentation
    [Fact]
    public void Application_MustNotDependOnInfrastructureLayer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Application must only reference Domain; Infrastructure is an outer layer.");
    }

    [Fact]
    public void Application_MustNotDependOnTasksLayer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Tasks")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Application must only reference Domain; Tasks is an outer layer.");
    }

    [Fact]
    public void Application_MustNotDependOnPresentationLayer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Presentation")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Application must only reference Domain; Presentation is an outer layer.");
    }

    // Rule 1c — Infrastructure must not depend on Application, Tasks, or Presentation
    [Fact]
    public void Infrastructure_MustNotDependOnApplicationLayer()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Infrastructure must only reference Domain; it must not know about Application.");
    }

    [Fact]
    public void Infrastructure_MustNotDependOnTasksLayer()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Tasks")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Infrastructure and Tasks must never reference each other.");
    }

    [Fact]
    public void Infrastructure_MustNotDependOnPresentationLayer()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Presentation")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Infrastructure must only reference Domain; Presentation is an outer layer.");
    }

    // Rule 1d — Tasks must not depend on Application, Infrastructure, or Presentation
    [Fact]
    public void Tasks_MustNotDependOnApplicationLayer()
    {
        var result = Types.InAssembly(TasksAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Application")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Tasks must only reference Domain; Application is an outer layer.");
    }

    [Fact]
    public void Tasks_MustNotDependOnInfrastructureLayer()
    {
        var result = Types.InAssembly(TasksAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Infrastructure and Tasks must never reference each other.");
    }

    [Fact]
    public void Tasks_MustNotDependOnPresentationLayer()
    {
        var result = Types.InAssembly(TasksAssembly)
            .Should()
            .NotHaveDependencyOn("ItrqTool.Presentation")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Tasks must only reference Domain; Presentation is an outer layer.");
    }

    // Rule 2 — ClosedXML must not appear in Tasks
    [Fact]
    public void Tasks_MustNotUseClosedXml()
    {
        var result = Types.InAssembly(TasksAssembly)
            .Should()
            .NotHaveDependencyOn("ClosedXML")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Tasks must read Excel files through IExcelReader only; ClosedXML belongs in Infrastructure.");
    }
}
