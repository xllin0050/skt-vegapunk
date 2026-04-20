namespace SktVegapunk.Core.Pipeline.Spec;

public interface ISpecReportBuilder
{
    MigrationSpec Build(
        IReadOnlyList<SrdSpec> dataWindows,
        IReadOnlyList<SruSpec> components,
        IReadOnlyList<JspInvocation> jspInvocations);

    Task WriteReportAsync(MigrationSpec spec, string specDirectory, CancellationToken cancellationToken = default);
}
