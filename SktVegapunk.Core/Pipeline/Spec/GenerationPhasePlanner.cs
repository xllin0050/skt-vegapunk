using System.Text;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 彙整進入 generation phase 前的補件計畫。
/// </summary>
public sealed class GenerationPhasePlanner
{
    public string GenerateMarkdown(
        MigrationSpec migrationSpec,
        IReadOnlyList<JspPrototypeArtifact> jspPrototypes,
        PageFlowGraph pageFlowGraph,
        IReadOnlyList<UnresolvedEndpointFinding> unresolvedFindings,
        IReadOnlyList<RequestBindingArtifact> requestBindings,
        IReadOnlyList<ResponseClassificationArtifact> responseClassifications)
    {
        ArgumentNullException.ThrowIfNull(migrationSpec);
        ArgumentNullException.ThrowIfNull(jspPrototypes);
        ArgumentNullException.ThrowIfNull(pageFlowGraph);
        ArgumentNullException.ThrowIfNull(unresolvedFindings);
        ArgumentNullException.ThrowIfNull(requestBindings);
        ArgumentNullException.ThrowIfNull(responseClassifications);

        var resolvedEndpoints = migrationSpec.EndpointCandidates.Count(candidate => candidate.Status == EndpointStatus.Resolved);
        var unresolvedEndpoints = unresolvedFindings.Count;
        var formCount = jspPrototypes.Sum(prototype => prototype.Forms.Count);
        var controlCount = jspPrototypes.Sum(prototype => prototype.Controls.Count);
        var eventCount = jspPrototypes.Sum(prototype => prototype.Events.Count);
        var bindingCount = requestBindings.Count(binding => binding.Parameters.Count > 0);
        var classifiedResponseCount = responseClassifications.Count;
        var mappedPayloadCount = requestBindings.Sum(binding => binding.OutgoingRequests.Count(request => request.PayloadFields.Count > 0));

        var builder = new StringBuilder();
        builder.AppendLine("# Generation Phase Plan");
        builder.AppendLine();
        builder.AppendLine("## 決策");
        builder.AppendLine();
        builder.AppendLine("- 可以進入 generation phase，但範圍應限定為 `resolved endpoints + unresolved placeholders + frontend skeleton`。");
        builder.AppendLine("- unresolved endpoint 先保留 stub，不作為當前阻塞項。");
        builder.AppendLine();
        builder.AppendLine("## 現況摘要");
        builder.AppendLine();
        builder.AppendLine($"- DataWindow: {migrationSpec.DataWindows.Count}");
        builder.AppendLine($"- Component: {migrationSpec.Components.Count}");
        builder.AppendLine($"- JSP Prototype: {jspPrototypes.Count}");
        builder.AppendLine($"- Form: {formCount}");
        builder.AppendLine($"- Control: {controlCount}");
        builder.AppendLine($"- Event: {eventCount}");
        builder.AppendLine($"- Page Flow Edge: {pageFlowGraph.Edges.Count}");
        builder.AppendLine($"- Endpoint: {migrationSpec.EndpointCandidates.Count}（resolved: {resolvedEndpoints}, unresolved: {unresolvedEndpoints}）");
        builder.AppendLine();
        builder.AppendLine("## Backend");
        builder.AppendLine();
        builder.AppendLine("已具備資料：");
        builder.AppendLine("- resolved endpoint 的 route、HTTP method、component method 對應。");
        builder.AppendLine("- DataWindow 的 SQL、資料表、欄位、參數。");
        builder.AppendLine("- component prototype 與 routine body。");
        builder.AppendLine($"- request binding artifact，已覆蓋 {bindingCount} 個 JSP component call。");
        builder.AppendLine($"- response classification artifact，已覆蓋 {classifiedResponseCount} 個 endpoint。");
        builder.AppendLine($"- payload mapping，已覆蓋 {mappedPayloadCount} 個 outgoing request。");
        builder.AppendLine();
        builder.AppendLine("最小補件：");
        builder.AppendLine("- unresolved endpoint 一律先生成 stub controller/service。");
        builder.AppendLine();
        builder.AppendLine("執行順序：");
        builder.AppendLine("1. 先凍結 unresolved placeholder 策略。");
        builder.AppendLine("2. 先用既有 request binding artifact 生成 request DTO 與 handler 入口。");
        builder.AppendLine("3. 依 response classification 切分 `json/html/file/script-redirect/text` handler。");
        builder.AppendLine("4. unresolved placeholder 一律生成 stub controller/service。");
        builder.AppendLine("5. 生成 controller、service interface、repository/query skeleton。");
        builder.AppendLine();
        builder.AppendLine("## Frontend");
        builder.AppendLine();
        builder.AppendLine("已具備資料：");
        builder.AppendLine("- 每個 JSP 的 `html/js/css` prototype。");
        builder.AppendLine("- forms、control inventory、events、script/style 來源。");
        builder.AppendLine("- page flow 對應的導頁、submit、ajax、popup 與 component call。");
        builder.AppendLine("- interaction graph，可回推出 `Click -> handler -> action` 事件鏈。");
        builder.AppendLine();
        builder.AppendLine("最小補件：");
        builder.AppendLine("- unresolved 頁面的 control 與 payload 仍可能只有 placeholder。");
        builder.AppendLine();
        builder.AppendLine("執行順序：");
        builder.AppendLine("1. 先用現有 prototype 生成頁面骨架、router 草案與 API client 佔位。");
        builder.AppendLine("2. 依 control inventory 與 payload mapping 生成表單 state 與 API client。");
        builder.AppendLine("3. 依 interaction graph 補齊較完整的頁面邏輯。");
        builder.AppendLine();
        builder.AppendLine("## Deferred Placeholders");
        builder.AppendLine();

        if (unresolvedFindings.Count == 0)
        {
            builder.AppendLine("- 目前沒有 unresolved endpoint。");
        }
        else
        {
            foreach (var finding in unresolvedFindings)
            {
                builder.AppendLine($"- `{finding.PbMethod}`：先保留 stub，原因為 `{finding.RootCause}`。");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## 進入 Generation Phase 的最低門檻");
        builder.AppendLine();
        builder.AppendLine("- 後端：resolved endpoint 已有 request binding 與 response classification。");
        builder.AppendLine("- 前端：頁面已補齊 control inventory、payload mapping 與 interaction graph。");
        builder.AppendLine("- unresolved endpoint 已明確標示為 placeholder，不阻塞主流程。");

        return builder.ToString().Trim();
    }
}
