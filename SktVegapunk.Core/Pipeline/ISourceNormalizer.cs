namespace SktVegapunk.Core.Pipeline;

public interface ISourceNormalizer
{
    SourceArtifact Normalize(byte[] rawBytes, string originalPath);
}
