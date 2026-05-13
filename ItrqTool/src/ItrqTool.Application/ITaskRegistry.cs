using ItrqTool.Domain;

namespace ItrqTool.Application;

public interface ITaskRegistry
{
    IWorkflowTask? FindTask(string taskType);
}
