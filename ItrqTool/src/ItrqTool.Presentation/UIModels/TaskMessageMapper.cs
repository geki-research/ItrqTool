using ItrqTool.Domain;

namespace ItrqTool.Presentation.UIModels;

internal static class TaskMessageMapper
{
    internal static TaskMessageSeverity ToUi(MessageSeverity severity) => severity switch
    {
        MessageSeverity.Info    => TaskMessageSeverity.Info,
        MessageSeverity.Warning => TaskMessageSeverity.Warning,
        MessageSeverity.Error   => TaskMessageSeverity.Error,
        _                       => TaskMessageSeverity.Info
    };

    internal static TaskMessageViewModel ToUi(TaskMessage msg) => new(
        Severity:  ToUi(msg.Severity),
        Text:      msg.Text,
        Timestamp: msg.Timestamp.ToLocalTime().ToString("HH:mm:ss"));
}
