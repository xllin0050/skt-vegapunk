namespace SktVegapunk.Core.Pipeline;

public interface ITextFileStore
{
    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);

    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
}
