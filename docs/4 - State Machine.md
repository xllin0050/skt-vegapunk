# 狀態機（State Machine）設計

## 核心原則：解析「結構」而非「語法」

與其去解析每一行程式碼的「語法（Syntax）」，只需要解析檔案的「結構（Structure）」。

PowerBuilder 的匯出檔（.sru 使用者物件）是具有高度規律的純文字檔。不需要懂裡面的邏輯，只需要把**「UI 定義區塊」跟「業務邏輯區塊（Event Scripts）」**乾淨地切開就好。

## 實作：`PbScriptExtractor`（`SktVegapunk.Core/Pipeline/PbScriptExtractor.cs`）

`PbScriptExtractor` 以逐行狀態機實作，不使用 Regex 處理巢狀結構（容易崩潰），改用 `StringReader` 逐行掃描。

### 狀態轉換

```
Scanning → InEventScript → Scanning
```

- **Scanning**：略過所有非事件開頭的行（UI 屬性如 `x=100 y=200 font.face="Tahoma"` 全部丟棄）
- **InEventScript**：從 `event xxx;` 或 `on xxx;` 開始收集，直到 `end event` 或 `end on`

### 輸出：`PbEventBlock`

每個提取出的區塊變成一筆 `PbEventBlock`：

```csharp
public sealed record PbEventBlock(string EventName, string ScriptBody);
```

`ScriptBody` 是去除 UI 雜訊後的純業務邏輯文字，直接作為送給 LLM 的 prompt 材料。

### 好處

- **極省 Token**：只把 `ScriptBody` 送給 LLM，不含 UI 座標屬性
- **減少幻覺**：AI 不會被落落長的 UI 屬性干擾
- **100% 準確**：確定性解析，相同輸入永遠得到相同輸出

## Migration 路徑的狀態機：`MigrationState`

`MigrationOrchestrator` 用 `MigrationState` enum 追蹤整條 pipeline 的階段：

```
Preprocessing → Generating → Validating
                                  │
                  ┌───────────────┤
                  ↓               ↓
              Repairing ──→  Completed
              (重新 Generating)
                  │
                  ↓（次數耗盡）
                Failed
```

每次進入下一個狀態都有明確的邊界條件：

| 狀態 | 進入條件 | 退出條件 |
|------|----------|----------|
| `Preprocessing` | 開始執行 | bytes 讀取 + normalize 完成 |
| `Generating` | 每次迴圈開始 | Copilot SDK 回傳非空字串 |
| `Validating` | 生成完成且已寫入檔案 | build/test 成功或失敗 |
| `Repairing` | 驗證失敗且還有重試次數 | 重新進入 Generating |
| `Completed` | 驗證通過 | — |
| `Failed` | 次數耗盡或致命錯誤 | — |

`MigrationState` 還保留 `Normalizing` 與 `Analyzing` 枚舉值供後續擴充（例如把 spec 分析整合進 migration 路徑）。
