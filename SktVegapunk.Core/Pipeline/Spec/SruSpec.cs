using SktVegapunk.Core.Pipeline;

namespace SktVegapunk.Core.Pipeline.Spec;

public sealed record SruSpec(
    string FileName,
    string ClassName,
    string ParentClass,
    IReadOnlyList<string> InstanceVariables,
    IReadOnlyList<SruPrototype> Prototypes,
    IReadOnlyList<SruRoutine> Routines,
    IReadOnlyList<PbEventBlock> EventBlocks);
