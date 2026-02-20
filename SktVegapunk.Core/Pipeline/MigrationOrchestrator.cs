namespace SktVegapunk.Core.Pipeline;

public sealed class MigrationOrchestrator : IMigrationOrchestrator
{
    private readonly ITextFileStore _textFileStore;
    private readonly IPbScriptExtractor _pbScriptExtractor;
    private readonly IPromptBuilder _promptBuilder;
    private readonly ICodeGenerator _codeGenerator;
    private readonly IBuildValidator _buildValidator;

    public MigrationOrchestrator(
        ITextFileStore textFileStore,
        IPbScriptExtractor pbScriptExtractor,
        IPromptBuilder promptBuilder,
        ICodeGenerator codeGenerator,
        IBuildValidator buildValidator)
    {
        ArgumentNullException.ThrowIfNull(textFileStore);
        ArgumentNullException.ThrowIfNull(pbScriptExtractor);
        ArgumentNullException.ThrowIfNull(promptBuilder);
        ArgumentNullException.ThrowIfNull(codeGenerator);
        ArgumentNullException.ThrowIfNull(buildValidator);

        _textFileStore = textFileStore;
        _pbScriptExtractor = pbScriptExtractor;
        _promptBuilder = promptBuilder;
        _codeGenerator = codeGenerator;
        _buildValidator = buildValidator;
    }

    public async Task<MigrationResult> RunAsync(MigrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SystemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BuildConfiguration);

        if (request.MaxRetries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(request.MaxRetries), "MaxRetries 至少要大於 0。");
        }

        var currentState = MigrationState.Preprocessing;
        var source = await _textFileStore.ReadAllTextAsync(request.SourceFilePath, cancellationToken);
        var eventBlocks = _pbScriptExtractor.Extract(source);
        if (eventBlocks.Count == 0)
        {
            currentState = MigrationState.Failed;
            return new MigrationResult
            {
                FinalState = currentState,
                Attempts = 0,
                LastPrompt = string.Empty,
                FailureReason = "未找到可轉換的事件邏輯區塊。"
            };
        }

        var currentPrompt = _promptBuilder.BuildInitialPrompt(eventBlocks);
        string? generatedCode = null;
        string? lastValidationOutput = null;

        for (var attempt = 1; attempt <= request.MaxRetries; attempt++)
        {
            currentState = MigrationState.Generating;
            generatedCode = await _codeGenerator.GenerateAsync(
                request.SystemPrompt,
                currentPrompt,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(generatedCode))
            {
                currentState = MigrationState.Failed;
                return new MigrationResult
                {
                    FinalState = currentState,
                    Attempts = attempt,
                    LastPrompt = currentPrompt,
                    FailureReason = "模型回傳空內容。"
                };
            }

            await _textFileStore.WriteAllTextAsync(request.OutputFilePath, generatedCode, cancellationToken);

            currentState = MigrationState.Validating;
            var validationResult = await _buildValidator.ValidateAsync(
                new BuildValidationRequest(
                    request.TargetPath,
                    request.BuildConfiguration,
                    request.RunTestsAfterBuild),
                cancellationToken);

            lastValidationOutput = validationResult.Output;
            if (validationResult.Success)
            {
                currentState = MigrationState.Completed;
                return new MigrationResult
                {
                    FinalState = currentState,
                    Attempts = attempt,
                    LastPrompt = currentPrompt,
                    GeneratedCode = generatedCode,
                    LastValidationOutput = lastValidationOutput
                };
            }

            currentState = MigrationState.Repairing;
            currentPrompt = _promptBuilder.BuildRepairPrompt(
                currentPrompt,
                generatedCode,
                validationResult.Output);
        }

        currentState = MigrationState.Failed;
        return new MigrationResult
        {
            FinalState = currentState,
            Attempts = request.MaxRetries,
            LastPrompt = currentPrompt,
            GeneratedCode = generatedCode,
            LastValidationOutput = lastValidationOutput,
            FailureReason = $"已達最大重試次數 {request.MaxRetries} 次。"
        };
    }
}
