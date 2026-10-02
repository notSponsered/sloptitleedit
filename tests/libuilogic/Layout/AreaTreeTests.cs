using System.Text.Json;
using Nikse.SubtitleEdit.UiLogic.Layout;

namespace LibUiLogicTests.Layout;

public class AreaTreeTests
{
    private static readonly string[] Known =
    [
        PanelIds.Subtitles, PanelIds.TextEdit, PanelIds.Video, PanelIds.Waveform,
        PanelIds.Styles, PanelIds.ScriptInfo, PanelIds.Notes, PanelIds.AutoReplace, PanelIds.Project, PanelIds.GitHub, PanelIds.AssaTools,
    ];

    private static Workspace Ws(AreaNode root) => new() { Name = "t", Root = root };

    private static string Json(object o) => JsonSerializer.Serialize(o);

    [Theory]
    [InlineData(DockSide.Left, SplitDirection.LeftRight, true)]
    [InlineData(DockSide.Right, SplitDirection.LeftRight, false)]
    [InlineData(DockSide.Top, SplitDirection.TopBottom, true)]
    [InlineData(DockSide.Bottom, SplitDirection.TopBottom, false)]
    public void Split_PutsNewPanelOnRequestedSide(DockSide side, SplitDirection expected, bool newIsA)
    {
        var target = AreaNode.Leaf(PanelIds.Subtitles);
        var ws = Ws(target);

        AreaTree.Split(ws, target, side, PanelIds.Notes);

        Assert.Equal(expected, ws.Root.Split);
        Assert.Equal(PanelIds.Notes, (newIsA ? ws.Root.A : ws.Root.B)!.Panels.Single());
        Assert.Equal(PanelIds.Subtitles, (newIsA ? ws.Root.B : ws.Root.A)!.Panels.Single());
    }

    [Fact]
    public void AddTab_MovesSingletonAndCollapsesEmptiedArea()
    {
        var ws = Ws(AreaPresets.Legacy(1));
        var videoLeaf = AreaTree.FindLeafWith(ws.Root, PanelIds.Video)!;

        AreaTree.AddTab(ws, videoLeaf, PanelIds.Waveform);

        // Waveform's own area is gone; the root is now the former top split.
        Assert.Equal(SplitDirection.LeftRight, ws.Root.Split);
        Assert.Equal([PanelIds.Video, PanelIds.Waveform], videoLeaf.Panels);
        Assert.Equal(1, videoLeaf.Active);
        Assert.Single(AreaTree.Leaves(ws.Root), l => l.Panels.Contains(PanelIds.Waveform));
    }

    [Fact]
    public void Split_AreaWithItself_IsNoOp()
    {
        var ws = Ws(AreaPresets.Legacy(7));
        var before = Json(ws);
        var videoLeaf = AreaTree.FindLeafWith(ws.Root, PanelIds.Video)!;

        AreaTree.Split(ws, videoLeaf, DockSide.Bottom, PanelIds.Video);

        Assert.Equal(before, Json(ws));
    }

    [Fact]
    public void CloseLeaf_RefusesLastArea_AndRemovePanel_RefusesLastPanel()
    {
        var leaf = AreaNode.Leaf(PanelIds.Subtitles);
        var ws = Ws(leaf);

        Assert.False(AreaTree.CloseLeaf(ws, leaf));
        Assert.False(AreaTree.RemovePanel(ws, PanelIds.Subtitles));
        Assert.Equal(PanelIds.Subtitles, ws.Root.Panels.Single());
    }

    [Fact]
    public void Swap_KeepsSizes()
    {
        var ws = Ws(AreaPresets.Legacy(1));
        var video = AreaTree.FindLeafWith(ws.Root, PanelIds.Video)!;
        var wave = AreaTree.FindLeafWith(ws.Root, PanelIds.Waveform)!;

        AreaTree.Swap(video, wave);

        Assert.Equal(SizeMode.FixedSecond, ws.Root.SizeMode);
        Assert.Equal(PanelIds.Video, ws.Root.B!.Panels.Single());
        Assert.Equal(PanelIds.Waveform, ws.Root.A!.B!.Panels.Single());
    }

    [Theory]
    [InlineData(PanelIds.Video)]
    [InlineData(PanelIds.Waveform)]
    [InlineData(PanelIds.TextEdit)]
    public void DetachThenDock_RestoresSameTree(string panel)
    {
        var ws = Ws(AreaPresets.Legacy(1));
        var before = Json(ws.Root);

        var floating = AreaTree.Detach(ws, panel);
        Assert.NotNull(floating);
        Assert.False(AreaTree.Contains(ws.Root, panel));

        AreaTree.Dock(ws, floating);

        Assert.Empty(ws.Floating);
        Assert.Equal(before, Json(ws.Root));
    }

    [Fact]
    public void DetachTab_DocksBackAsTab()
    {
        var ws = Ws(AreaNode.LeftRight(AreaNode.Leaf(PanelIds.Subtitles), AreaNode.Leaf(PanelIds.Styles, PanelIds.Notes)));

        var floating = AreaTree.Detach(ws, PanelIds.Notes)!;
        Assert.Null(floating.AnchorSide);
        AreaTree.Dock(ws, floating);

        Assert.Equal([PanelIds.Styles, PanelIds.Notes], ws.Root.B!.Panels);
    }

    [Fact]
    public void Detach_RefusesLastPanelOfMainWindow()
    {
        var ws = Ws(AreaNode.Leaf(PanelIds.Subtitles));

        Assert.Null(AreaTree.Detach(ws, PanelIds.Subtitles));
    }

    [Fact]
    public void Dock_WhenAnchorIsGone_SplitsRootOnTheRight()
    {
        var ws = Ws(AreaNode.LeftRight(AreaNode.Leaf(PanelIds.Subtitles), AreaNode.Leaf(PanelIds.Notes)));
        var floating = AreaTree.Detach(ws, PanelIds.Subtitles)!; // anchor = notes
        ws.Root = AreaNode.Leaf(PanelIds.Video);

        AreaTree.Dock(ws, floating);

        Assert.Equal(SplitDirection.LeftRight, ws.Root.Split);
        Assert.Equal(PanelIds.Subtitles, ws.Root.B!.Panels.Single());
    }

    [Fact]
    public void Normalize_DropsUnknownAndDuplicates_CollapsesAndClamps()
    {
        var root = AreaNode.LeftRight(
            AreaNode.Leaf("bogus", PanelIds.Subtitles, PanelIds.Subtitles),
            AreaNode.TopBottom(AreaNode.Leaf("bogus"), AreaNode.Leaf(PanelIds.Video), 7));
        root.Ratio = double.NaN;
        var ws = new Workspace
        {
            Root = root,
            Floating = [new FloatingArea { Root = AreaNode.Leaf(PanelIds.Video, PanelIds.Notes) }],
        };

        AreaTree.Normalize(ws, Known, () => AreaPresets.Legacy(1));

        Assert.Equal(0.5, ws.Root.Ratio);
        Assert.Equal([PanelIds.Subtitles], ws.Root.A!.Panels);
        Assert.Equal([PanelIds.Video], ws.Root.B!.Panels);          // empty "bogus" leaf collapsed away
        Assert.Equal([PanelIds.Notes], ws.Floating.Single().Root.Panels); // duplicate video dropped
    }

    [Fact]
    public void Normalize_EmptyTree_FallsBack()
    {
        var ws = new Workspace { Root = AreaNode.Leaf("bogus") };

        AreaTree.Normalize(ws, Known, () => AreaPresets.Legacy(1));

        Assert.True(AreaTree.Contains(ws.Root, PanelIds.Subtitles));
    }

    [Fact]
    public void Legacy1_HasFixedWaveformAndAutoEditBox()
    {
        var root = AreaPresets.Legacy(1);

        Assert.Equal(SplitDirection.TopBottom, root.Split);
        Assert.Equal(SizeMode.FixedSecond, root.SizeMode);
        Assert.Equal(150, root.SecondPx);
        Assert.Equal(PanelIds.Waveform, root.B!.Panels.Single());
        Assert.Equal(SizeMode.AutoSecond, root.A!.A!.SizeMode);
        Assert.Equal(PanelIds.TextEdit, root.A.A.B!.Panels.Single());
        Assert.Equal(PanelIds.Video, root.A.B!.Panels.Single());
    }

    [Fact]
    public void AllPresets_AreAlreadyNormal()
    {
        var roots = Enumerable.Range(1, AreaPresets.LegacyCount).Select(AreaPresets.Legacy)
            .Append(AreaPresets.ForName(AreaPresets.Timing))
            .Append(AreaPresets.ForName(AreaPresets.Typeset))
            .Append(AreaPresets.ForName(AreaPresets.Project));

        foreach (var root in roots)
        {
            var ws = Ws(root);
            var before = Json(ws);
            AreaTree.Normalize(ws, Known, () => throw new InvalidOperationException("fallback used"));
            Assert.Equal(before, Json(ws));
        }
    }

    [Fact]
    public void Defaults_MigratesUndockedVideo()
    {
        var edit = AreaPresets.Defaults(1, videoUndocked: true)[0];

        Assert.False(AreaTree.Contains(edit.Root, PanelIds.Video));
        Assert.False(AreaTree.Contains(edit.Root, PanelIds.Waveform));
        Assert.Equal(2, edit.Floating.Count);
    }

    [Fact]
    public void Workspace_JsonRoundTrip()
    {
        var ws = AreaPresets.Defaults(1, videoUndocked: true)[0];
        ws.Floating[0].X = 100;

        var json = Json(ws);
        var back = JsonSerializer.Deserialize<Workspace>(json)!;

        Assert.Equal(json, Json(back));
        Assert.Contains("\"TopBottom\"", json); // enums stored as names
    }

    // -----------------------------------------------------------------
    // Drag & drop (Move / Float with floating windows that hold trees)
    // -----------------------------------------------------------------

    [Fact]
    public void Move_TabOntoOtherArea_JoinsAsActiveTab()
    {
        var ws = Ws(AreaPresets.Legacy(1));
        var video = AreaTree.FindLeafWith(ws.Root, PanelIds.Video)!;

        Assert.True(AreaTree.Move(ws, [PanelIds.TextEdit], PanelIds.TextEdit, video, DropZone.Tab));

        Assert.Equal([PanelIds.Video, PanelIds.TextEdit], video.Panels);
        Assert.Equal(1, video.Active);
        Assert.Single(AreaTree.Leaves(ws.Root), l => l.Panels.Contains(PanelIds.Subtitles) && l.Panels.Count == 1);
    }

    [Fact]
    public void Move_ToAreaSide_SplitsThatAreaInHalf()
    {
        var ws = Ws(AreaNode.LeftRight(AreaNode.Leaf(PanelIds.Subtitles), AreaNode.Leaf(PanelIds.Video, PanelIds.Notes)));
        var subtitles = ws.Root.A!;

        Assert.True(AreaTree.Move(ws, [PanelIds.Notes], PanelIds.Notes, subtitles, DropZone.Bottom));

        Assert.Equal(SplitDirection.TopBottom, ws.Root.A!.Split);
        Assert.Equal(0.5, ws.Root.A.Ratio);
        Assert.Equal([PanelIds.Notes], ws.Root.A.B!.Panels);
        Assert.Equal([PanelIds.Video], ws.Root.B!.Panels);
    }

    [Fact]
    public void Move_ToWindowEdge_AddsFullHeightColumn()
    {
        var ws = Ws(AreaPresets.Legacy(1));

        Assert.True(AreaTree.Move(ws, [PanelIds.Waveform], PanelIds.Waveform, ws.Root, DropZone.Left));

        Assert.Equal(SplitDirection.LeftRight, ws.Root.Split);
        Assert.Equal(0.3, ws.Root.Ratio);
        Assert.Equal([PanelIds.Waveform], ws.Root.A!.Panels);
    }

    [Fact]
    public void Move_WholeAreaOntoItself_IsNoOp()
    {
        var ws = Ws(AreaPresets.Legacy(1));
        var before = Json(ws);
        var video = AreaTree.FindLeafWith(ws.Root, PanelIds.Video)!;

        Assert.False(AreaTree.Move(ws, video.Panels.ToList(), PanelIds.Video, video, DropZone.Left));
        Assert.False(AreaTree.Move(ws, [PanelIds.Video], PanelIds.Video, video, DropZone.Tab));
        Assert.Equal(before, Json(ws));
    }

    [Fact]
    public void Move_OneTabToSideOfItsOwnArea_SplitsTheArea()
    {
        var leaf = AreaNode.Leaf(PanelIds.Styles, PanelIds.Notes);
        var ws = Ws(AreaNode.LeftRight(AreaNode.Leaf(PanelIds.Subtitles), leaf));

        Assert.True(AreaTree.Move(ws, [PanelIds.Notes], PanelIds.Notes, leaf, DropZone.Right));

        Assert.Equal(SplitDirection.LeftRight, ws.Root.B!.Split);
        Assert.Equal([PanelIds.Styles], ws.Root.B.A!.Panels);
        Assert.Equal([PanelIds.Notes], ws.Root.B.B!.Panels);
    }

    [Fact]
    public void Move_IntoFloatingWindow_SplitsInsideIt_AndEmptyWindowsDisappear()
    {
        var ws = Ws(AreaNode.LeftRight(AreaNode.Leaf(PanelIds.Subtitles), AreaNode.TopBottom(AreaNode.Leaf(PanelIds.Video), AreaNode.Leaf(PanelIds.Waveform))));
        var videoWindow = AreaTree.Detach(ws, PanelIds.Video)!;
        var waveWindow = AreaTree.Detach(ws, PanelIds.Waveform)!;
        Assert.Equal(2, ws.Floating.Count);

        // attach the waveform window under the video window
        Assert.True(AreaTree.Move(ws, [PanelIds.Waveform], PanelIds.Waveform, videoWindow.Root, DropZone.Bottom));

        Assert.DoesNotContain(waveWindow, ws.Floating);
        var root = ws.Floating.Single().Root;
        Assert.Equal(SplitDirection.TopBottom, root.Split);
        Assert.Equal([PanelIds.Video], root.A!.Panels);
        Assert.Equal([PanelIds.Waveform], root.B!.Panels);
    }

    [Fact]
    public void Move_RefusesTakingTheMainWindowsLastPanel()
    {
        var ws = Ws(AreaNode.Leaf(PanelIds.Subtitles));
        ws.Floating.Add(new FloatingArea { Root = AreaNode.Leaf(PanelIds.Notes) });

        Assert.False(AreaTree.Move(ws, [PanelIds.Subtitles], PanelIds.Subtitles, ws.Floating[0].Root, DropZone.Tab));
        Assert.Null(AreaTree.Float(ws, [PanelIds.Subtitles], PanelIds.Subtitles));
    }

    [Fact]
    public void Move_NewPanel_IsSimplyInserted()
    {
        var ws = Ws(AreaNode.Leaf(PanelIds.Subtitles));

        Assert.True(AreaTree.Move(ws, [PanelIds.Notes], PanelIds.Notes, ws.Root, DropZone.Right));

        Assert.Equal([PanelIds.Notes], ws.Root.B!.Panels);
    }

    [Fact]
    public void DockWindowAt_KeepsTheWindowsOwnSplits()
    {
        var ws = Ws(AreaNode.LeftRight(AreaNode.Leaf(PanelIds.Subtitles), AreaNode.Leaf(PanelIds.Video)));
        var window = AreaTree.Detach(ws, PanelIds.Video)!;
        AreaTree.Move(ws, [PanelIds.Waveform], PanelIds.Waveform, window.Root, DropZone.Bottom);

        Assert.True(AreaTree.DockWindowAt(ws, window, ws.Root, DropZone.Left));

        Assert.Empty(ws.Floating);
        Assert.Equal(SplitDirection.LeftRight, ws.Root.Split);
        Assert.Equal(SplitDirection.TopBottom, ws.Root.A!.Split);
        Assert.Equal([PanelIds.Video], ws.Root.A.A!.Panels);
        Assert.Equal([PanelIds.Waveform], ws.Root.A.B!.Panels);
        Assert.Equal([PanelIds.Subtitles], ws.Root.B!.Panels);
    }

    [Fact]
    public void DockWindowAt_TabMergesAllPanels()
    {
        var ws = Ws(AreaNode.LeftRight(AreaNode.Leaf(PanelIds.Subtitles), AreaNode.Leaf(PanelIds.Video)));
        var window = AreaTree.Detach(ws, PanelIds.Video)!;

        Assert.True(AreaTree.DockWindowAt(ws, window, ws.Root, DropZone.Tab));

        Assert.Equal([PanelIds.Subtitles, PanelIds.Video], ws.Root.Panels);
        Assert.Equal(1, ws.Root.Active);
    }

    [Fact]
    public void Dock_FloatingTree_GoesBackAsSubtree()
    {
        var ws = Ws(AreaNode.LeftRight(AreaNode.Leaf(PanelIds.Subtitles), AreaNode.Leaf(PanelIds.Video)));
        var window = AreaTree.Detach(ws, PanelIds.Video)!;
        AreaTree.Move(ws, [PanelIds.Waveform], PanelIds.Waveform, window.Root, DropZone.Bottom);

        AreaTree.Dock(ws, window);

        Assert.Empty(ws.Floating);
        Assert.Equal([PanelIds.Subtitles], ws.Root.A!.Panels);
        Assert.Equal(SplitDirection.TopBottom, ws.Root.B!.Split); // video over waveform, on the right where video was
        Assert.Equal([PanelIds.Video], ws.Root.B.A!.Panels);
        Assert.Equal([PanelIds.Waveform], ws.Root.B.B!.Panels);
    }

    // -----------------------------------------------------------------
    // Toolbars (compact panels)
    // -----------------------------------------------------------------

    [Fact]
    public void AddBar_PutsToolbarAlongTheBottom_SizedToContent_Once()
    {
        var ws = Ws(AreaPresets.Legacy(1));

        AreaTree.AddBar(ws, PanelIds.AssaTools);
        AreaTree.AddBar(ws, PanelIds.AssaTools);

        Assert.Equal(SplitDirection.TopBottom, ws.Root.Split);
        Assert.Equal(SizeMode.AutoSecond, ws.Root.SizeMode);
        Assert.Equal([PanelIds.AssaTools], ws.Root.B!.Panels);
        Assert.Single(AreaTree.Leaves(ws.Root), l => l.Panels.Contains(PanelIds.AssaTools));
    }

    [Theory]
    [InlineData(DropZone.Left, SizeMode.AutoFirst)]
    [InlineData(DropZone.Top, SizeMode.AutoFirst)]
    [InlineData(DropZone.Right, SizeMode.AutoSecond)]
    [InlineData(DropZone.Bottom, SizeMode.AutoSecond)]
    public void Move_Toolbar_SizesToContentOnItsSide(DropZone zone, SizeMode expected)
    {
        var ws = Ws(AreaPresets.Legacy(1));
        AreaTree.AddBar(ws, PanelIds.AssaTools);

        Assert.True(AreaTree.Move(ws, [PanelIds.AssaTools], PanelIds.AssaTools, ws.Root.A!, zone));

        var parent = AreaTree.FindParent(ws.Root, AreaTree.FindLeafWith(ws.Root, PanelIds.AssaTools)!)!;
        Assert.Equal(expected, parent.SizeMode);
    }

    [Fact]
    public void ProjectPreset_EpisodeIndexWithGitHubTab_OverNotes_BesideTheEditingSet()
    {
        var root = AreaPresets.ForName(AreaPresets.Project);
        var left = root.A!;
        Assert.Equal([PanelIds.Project, PanelIds.GitHub], left.A!.Panels);
        Assert.Equal(PanelIds.Project, left.A.ActivePanel);
        Assert.Equal(PanelIds.Notes, left.B!.Panels.Single());
        foreach (var id in new[] { PanelIds.Video, PanelIds.Subtitles, PanelIds.TextEdit, PanelIds.Waveform })
        {
            Assert.True(AreaTree.Contains(root.B!, id), id);
        }

        Assert.Contains(AreaPresets.Defaults(1, videoUndocked: false), w => w.Name == AreaPresets.Project);
    }

    [Fact]
    public void Defaults_HaveTheAssaToolbar_AndAreNormal()
    {
        foreach (var ws in AreaPresets.Defaults(1, videoUndocked: false))
        {
            Assert.True(AreaTree.Contains(ws.Root, PanelIds.AssaTools));
            var before = Json(ws);
            AreaTree.Normalize(ws, Known, () => throw new InvalidOperationException("fallback used"));
            Assert.Equal(before, Json(ws));
        }
    }
}
