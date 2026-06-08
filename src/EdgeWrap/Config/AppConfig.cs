using EdgeWrap.Core;

namespace EdgeWrap.Config;

public sealed class AppConfig
{
    public List<EdgeLink> Links { get; set; } = new();
    public bool Enabled { get; set; } = true;
    public bool AutoStart { get; set; } = false;

    /// <summary>Pixels to place the cursor inside the destination edge after a wrap.</summary>
    public int Margin { get; set; } = 3;

    /// <summary>Cursor polling interval in milliseconds.</summary>
    public int PollIntervalMs { get; set; } = 6;

    /// <summary>
    /// Experimental: mirror a window's overflow across a horizontal seam so it
    /// appears to wrap around the donut (visual only, click-through).
    /// </summary>
    public bool ExperimentalSeamMirror { get; set; } = false;
}
