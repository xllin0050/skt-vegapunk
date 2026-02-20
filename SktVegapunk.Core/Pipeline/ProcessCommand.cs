namespace SktVegapunk.Core.Pipeline;

public sealed record ProcessCommand(string FileName, string Arguments, string WorkingDirectory);
