using System.Text;

namespace SktVegapunk.Core.Pipeline;

public sealed class DotnetBuildValidator : IBuildValidator
{
    private readonly IProcessRunner _processRunner;

    public DotnetBuildValidator(IProcessRunner processRunner)
    {
        ArgumentNullException.ThrowIfNull(processRunner);
        _processRunner = processRunner;
    }

    public async Task<BuildValidationResult> ValidateAsync(
        BuildValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BuildConfiguration);

        var outputBuilder = new StringBuilder();
        var workingDirectory = Directory.GetCurrentDirectory();

        var buildArgs = $"build \"{request.TargetPath}\" -c {request.BuildConfiguration}";
        var buildResult = await _processRunner.RunAsync(
            new ProcessCommand("dotnet", buildArgs, workingDirectory),
            cancellationToken);
        AppendProcessResult(outputBuilder, $"dotnet {buildArgs}", buildResult);

        if (buildResult.ExitCode != 0)
        {
            return new BuildValidationResult(false, outputBuilder.ToString());
        }

        if (!request.RunTestsAfterBuild)
        {
            return new BuildValidationResult(true, outputBuilder.ToString());
        }

        var testArgs = $"test \"{request.TargetPath}\" -c {request.BuildConfiguration}";
        var testResult = await _processRunner.RunAsync(
            new ProcessCommand("dotnet", testArgs, workingDirectory),
            cancellationToken);
        AppendProcessResult(outputBuilder, $"dotnet {testArgs}", testResult);

        return new BuildValidationResult(testResult.ExitCode == 0, outputBuilder.ToString());
    }

    private static void AppendProcessResult(StringBuilder outputBuilder, string command, ProcessResult result)
    {
        outputBuilder.AppendLine($"> {command}");

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            outputBuilder.AppendLine(result.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            outputBuilder.AppendLine(result.StandardError.TrimEnd());
        }

        outputBuilder.AppendLine($"ExitCode: {result.ExitCode}");
        outputBuilder.AppendLine();
    }
}
