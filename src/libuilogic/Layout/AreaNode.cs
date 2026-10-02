using System.Text.Json.Serialization;

namespace Nikse.SubtitleEdit.UiLogic.Layout;

[JsonConverter(typeof(JsonStringEnumConverter<SplitDirection>))]
public enum SplitDirection
{
    None,      // leaf
    LeftRight, // A left, B right
    TopBottom, // A top, B bottom
}

[JsonConverter(typeof(JsonStringEnumConverter<SizeMode>))]
public enum SizeMode
{
    Ratio,       // A gets Ratio of the space
    FixedSecond, // B is SecondPx pixels
    AutoSecond,  // B sizes to its content
    AutoFirst,   // A sizes to its content (toolbars docked left/top)
}

[JsonConverter(typeof(JsonStringEnumConverter<DockSide>))]
public enum DockSide
{
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>Where a dragged tab/area lands relative to the area under the cursor.</summary>
public enum DropZone
{
    Tab, // join the area as a tab
    Left,
    Right,
    Top,
    Bottom,
}

/// <summary>
/// Blender-style area tree: a node is either a split (two children) or a leaf holding panel tabs.
/// </summary>
public class AreaNode
{
    public SplitDirection Split { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AreaNode? A { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AreaNode? B { get; set; }
    public SizeMode SizeMode { get; set; }
    public double Ratio { get; set; } = 0.5;
    public double SecondPx { get; set; }

    public List<string> Panels { get; set; } = [];
    public int Active { get; set; }

    [JsonIgnore] public bool IsLeaf => Split == SplitDirection.None;
    [JsonIgnore] public string? ActivePanel => Panels.Count == 0 ? null : Panels[Math.Clamp(Active, 0, Panels.Count - 1)];

    public static AreaNode Leaf(params string[] panels) => new() { Panels = [.. panels] };

    public static AreaNode LeftRight(AreaNode a, AreaNode b, double ratio = 0.5) =>
        new() { Split = SplitDirection.LeftRight, A = a, B = b, Ratio = ratio };

    public static AreaNode TopBottom(AreaNode a, AreaNode b, double ratio = 0.5) =>
        new() { Split = SplitDirection.TopBottom, A = a, B = b, Ratio = ratio };

    public AreaNode FixedSecond(double px)
    {
        SizeMode = SizeMode.FixedSecond;
        SecondPx = px;
        return this;
    }

    public AreaNode AutoSecond()
    {
        SizeMode = SizeMode.AutoSecond;
        return this;
    }

    public AreaNode Clone() => new()
    {
        Split = Split,
        A = A?.Clone(),
        B = B?.Clone(),
        SizeMode = SizeMode,
        Ratio = Ratio,
        SecondPx = SecondPx,
        Panels = [.. Panels],
        Active = Active,
    };
}

/// <summary>
/// A window of its own holding an area tree (so floating windows can be split and tabbed too).
/// The Anchor/Dock fields remember where it came from so closing the window puts it back in the
/// same place with the same size.
/// </summary>
public class FloatingArea
{
    public AreaNode Root { get; set; } = new();
    public double X { get; set; }      // screen pixels
    public double Y { get; set; }
    public double Width { get; set; }  // DIPs; 0 = default size
    public double Height { get; set; }
    public bool Maximized { get; set; }

    public List<string> AnchorPanels { get; set; } = []; // panels of the subtree it was split from
    public DockSide? AnchorSide { get; set; }            // null = it was a tab next to AnchorPanels[0]
    public SizeMode DockSizeMode { get; set; }
    public double DockRatio { get; set; } = 0.5;
    public double DockSecondPx { get; set; }

    public FloatingArea Clone() => new()
    {
        Root = Root.Clone(), X = X, Y = Y, Width = Width, Height = Height, Maximized = Maximized,
        AnchorPanels = [.. AnchorPanels], AnchorSide = AnchorSide,
        DockSizeMode = DockSizeMode, DockRatio = DockRatio, DockSecondPx = DockSecondPx,
    };
}

public class Workspace
{
    public string Name { get; set; } = string.Empty;
    public AreaNode Root { get; set; } = new();
    public List<FloatingArea> Floating { get; set; } = [];

    public Workspace Clone(string? name = null) => new()
    {
        Name = name ?? Name,
        Root = Root.Clone(),
        Floating = [.. Floating.Select(f => f.Clone())],
    };
}

public static class PanelIds
{
    public const string Subtitles = "subtitles";
    public const string TextEdit = "textedit";
    public const string Video = "video";
    public const string Waveform = "waveform";
    public const string Styles = "styles";
    public const string ScriptInfo = "scriptinfo";
    public const string Notes = "notes";
    public const string AutoReplace = "autoreplace";
    public const string Project = "project";
    public const string GitHub = "github";
    public const string AssaTools = "assatools";

    /// <summary>Toolbar-like panels: no header (a grip instead), sized to their content, never tabbed.</summary>
    public static readonly IReadOnlySet<string> Compact = new HashSet<string> { AssaTools };

    public static bool IsCompact(string id) => Compact.Contains(id);
}
