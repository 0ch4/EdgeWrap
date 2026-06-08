namespace EdgeWrap.Core;

/// <summary>A bidirectional connection between two monitor edges.</summary>
public sealed class EdgeLink
{
    public EdgeRef A { get; set; }
    public EdgeRef B { get; set; }

    public EdgeLink() { }

    public EdgeLink(EdgeRef a, EdgeRef b)
    {
        A = a;
        B = b;
    }
}
