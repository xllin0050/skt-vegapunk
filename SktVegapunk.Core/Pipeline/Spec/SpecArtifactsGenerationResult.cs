namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 規格提取流程的輸出摘要。
/// </summary>
public sealed record SpecArtifactsGenerationResult(
    int DataWindowCount,
    int ComponentCount,
    int JspInvocationCount,
    int JspPrototypeCount,
    int WarningCount,
    IReadOnlyList<string> Warnings,
    int SchemaTableCount,
    int SchemaTriggerCount,
    int InferredEndpointCount = 0);
