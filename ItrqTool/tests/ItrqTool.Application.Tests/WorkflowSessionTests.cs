using FluentAssertions;
using Xunit;
using ItrqTool.Application;

namespace ItrqTool.Application.Tests;

public sealed class WorkflowSessionTests
{
    // TODO: implement when WorkflowSession.RunCurrentTaskAsync() is filled in.

    [Fact(Skip = "Placeholder — implement alongside WorkflowSession")]
    public void InitialStatus_IsReadyToRun() { }

    [Fact(Skip = "Placeholder — implement alongside WorkflowSession")]
    public void RunCurrentTask_SetsStatusToRunning_ThenAwaitingReview() { }

    [Fact(Skip = "Placeholder — implement alongside WorkflowSession")]
    public void RunCurrentTask_SetsStatusToFailed_WhenTaskReturnsFailed() { }

    [Fact(Skip = "Placeholder — implement alongside WorkflowSession")]
    public void RunCurrentTask_SetsStatusToCompleted_AfterLastTask() { }

    [Fact(Skip = "Placeholder — implement alongside WorkflowSession")]
    public void RunCurrentTask_PropagatesInputPaths_FromUpstreamOutputs() { }

    [Fact(Skip = "Placeholder — implement alongside WorkflowSession")]
    public void RunCurrentTask_PlacesOutputs_InWorkingDirectory() { }

    [Fact(Skip = "Placeholder — implement alongside WorkflowSession")]
    public void RunCurrentTask_PropagatesCancellation() { }
}
