namespace SktVegapunk.Core.Pipeline.Spec;

public interface IJspExtractor
{
    JspInvocation Extract(string jspText);
}
