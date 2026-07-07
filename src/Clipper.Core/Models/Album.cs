namespace Clipper.Core;

/// <summary>A named collection of clips.</summary>
public sealed class Album
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
