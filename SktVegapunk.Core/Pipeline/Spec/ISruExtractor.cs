namespace SktVegapunk.Core.Pipeline.Spec;

public interface ISruExtractor
{
    SruSpec Extract(string normalizedText);
}
