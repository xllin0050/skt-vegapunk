namespace SktVegapunk.Core.Pipeline;

public sealed class MigrationOrchestrator : IMigrationOrchestrator
{
    private readonly ITextFileStore _textFileStore;
    private readonly ISourceNormalizer _sourceNormalizer;
    private readonly IPbScriptExtractor _pbScriptExtractor;
    private readonly IPromptBuilder _promptBuilder;
    private readonly ICodeGenerator _codeGenerator;
    private readonly IBuildValidator _buildValidator;

    public MigrationOrchestrator(
        ITextFileStore textFileStore,
        IPbScriptExtractor pbScriptExtractor,
        IPromptBuilder promptBuilder,
        ICodeGenerator codeGenerator,
        IBuildValidator buildValidator,
        ISourceNormalizer? sourceNormalizer = null)
    {
        ArgumentNullException.ThrowIfNull(textFileStore);
        ArgumentNullException.ThrowIfNull(pbScriptExtractor);
        ArgumentNullException.ThrowIfNull(promptBuilder);
        ArgumentNullException.ThrowIfNull(codeGenerator);
        ArgumentNullException.ThrowIfNull(buildValidator);

        _textFileStore = textFileStore;
        _sourceNormalizer = sourceNormalizer ?? new PbSourceNormalizer();
        _pbScriptExtractor = pbScriptExtractor;
        _promptBuilder = promptBuilder;
        _codeGenerator = codeGenerator;
        _buildValidator = buildValidator;
    }

    public async Task<MigrationResult> RunAsync(MigrationRequest request, CancellationToken cancellationToken = default)
    {
        // 驗證所有必要參數，確保流程能正常啟動
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

        // 前處理階段：讀取來源檔案、正規化編碼後提取事件區塊
        // 若無可轉換內容，立即回傳失敗而非進入重試迴圈
        var currentState = MigrationState.Preprocessing;
        var rawBytes = await _textFileStore.ReadAllBytesAsync(request.SourceFilePath, cancellationToken);
        var sourceArtifact = _sourceNormalizer.Normalize(rawBytes, request.SourceFilePath);
        var eventBlocks = _pbScriptExtractor.Extract(sourceArtifact.NormalizedText);
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

        // 建立初始提示詞，並初始化用於追蹤的變數
        var currentPrompt = _promptBuilder.BuildInitialPrompt(eventBlocks);
        string? generatedCode = null;
        string? lastValidationOutput = null;

        // 重試迴圈：生成 → 驗證 → 修復，直到成功或達到最大次數
        for (var attempt = 1; attempt <= request.MaxRetries; attempt++)
        {
            // 呼叫 AI 模型生成程式碼
            currentState = MigrationState.Generating;
            generatedCode = await _codeGenerator.GenerateAsync(
                request.SystemPrompt,
                currentPrompt,
                cancellationToken);

            // 若模型無回應，視為致命錯誤立即中止
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

            // 將生成的程式碼寫入輸出檔案，供後續建置驗證使用
            await _textFileStore.WriteAllTextAsync(request.OutputFilePath, generatedCode, cancellationToken);

            // 執行建置驗證，確認生成的程式碼能通過編譯與測試
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
                // 驗證通過，遷移成功完成
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

            // 驗證失敗，進入修復階段：以錯誤訊息重新建構提示詞
            // 下一輪迴圈會使用此提示詞請求模型修正
            currentState = MigrationState.Repairing;
            currentPrompt = _promptBuilder.BuildRepairPrompt(
                currentPrompt,
                generatedCode,
                validationResult.Output);
        }

        // 已達最大重試次數仍未成功，回傳失敗狀態與最後結果
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
