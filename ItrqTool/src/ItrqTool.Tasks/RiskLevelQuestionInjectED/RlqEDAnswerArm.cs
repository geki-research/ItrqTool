namespace ItrqTool.Tasks.RiskLevelQuestionInjectED;

/// <summary>
/// The three answer columns an ED RLQ question carries per version: the total answer and the
/// "Holding" and "EBOe" breakdowns. Each arm is injected independently by
/// <see cref="RlqEDInjectMapper"/>.
/// </summary>
public enum RlqEDAnswerArm { Answer, Holding, EBOe }
