namespace SktVegapunk.Core.Pipeline.Spec;

public interface ISrdExtractor
{
    SrdSpec Extract(string normalizedText);
}
