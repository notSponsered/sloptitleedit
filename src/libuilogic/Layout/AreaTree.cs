namespace Nikse.SubtitleEdit.UiLogic.Layout;

/// <summary>
/// Operations on a workspace: the main window's area tree plus one tree per floating window.
/// Every panel exists at most once (the main window holds single control instances), so adding a
/// panel that is shown elsewhere moves it. Mutating calls change trees in place, then collapse
/// emptied areas and drop emptied floating windows.
/// </summary>
public static class AreaTree
{
    // ---------------------------------------------------------------------
    // Queries
    // ---------------------------------------------------------------------

    public static IEnumerable<AreaNode> Leaves(AreaNode? node)
    {
        if (node == null)
        {
            yield break;
        }

        if (node.IsLeaf)
        {
            yield return node;
            yield break;
        }

        foreach (var leaf in Leaves(node.A))
        {
            yield return leaf;
        }

        foreach (var leaf in Leaves(node.B))
        {
            yield return leaf;
        }
    }

    /// <summary>The main tree first, then each floating window's tree.</summary>
    public static IEnumerable<AreaNode> Roots(Workspace ws) => ws.Floating.Select(f => f.Root).Prepend(ws.Root);

    public static List<string> PanelsIn(AreaNode? node) => Leaves(node).SelectMany(l => l.Panels).ToList();

    public static AreaNode? FindLeafWith(AreaNode root, string id) => Leaves(root).FirstOrDefault(l => l.Panels.Contains(id));

    public static AreaNode? FindLeafWith(Workspace ws, string id) => Roots(ws).Select(r => FindLeafWith(r, id)).FirstOrDefault(l => l != null);

    public static bool Contains(AreaNode root, string id) => FindLeafWith(root, id) != null;

    public static bool Contains(Workspace ws, string id) => FindLeafWith(ws, id) != null;

    public static FloatingArea? FindFloating(Workspace ws, string id) => ws.Floating.FirstOrDefault(f => Contains(f.Root, id));

    public static bool ContainsNode(AreaNode root, AreaNode node) =>
        root == node || !root.IsLeaf && (root.A != null && ContainsNode(root.A, node) || root.B != null && ContainsNode(root.B, node));

    /// <summary>The tree (main or floating) that holds <paramref name="node"/>.</summary>
    public static AreaNode? RootOf(Workspace ws, AreaNode node) => Roots(ws).FirstOrDefault(r => ContainsNode(r, node));

    public static FloatingArea? FloatingOf(Workspace ws, AreaNode node) => ws.Floating.FirstOrDefault(f => ContainsNode(f.Root, node));

    public static AreaNode? FindParent(AreaNode node, AreaNode child)
    {
        if (node.IsLeaf)
        {
            return null;
        }

        if (node.A == child || node.B == child)
        {
            return node;
        }

        return (node.A == null ? null : FindParent(node.A, child)) ?? (node.B == null ? null : FindParent(node.B, child));
    }

    public static AreaNode? FindParent(Workspace ws, AreaNode child) => Roots(ws).Select(r => FindParent(r, child)).FirstOrDefault(p => p != null);

    // ---------------------------------------------------------------------
    // Moves — drag & drop and the area menus all end up here
    // ---------------------------------------------------------------------

    /// <summary>
    /// Moves <paramref name="panels"/> (one tab, or every tab of an area) next to/into <paramref name="target"/>,
    /// which may be an area or a whole window's root (docking at the window edge). Returns false for no-ops
    /// (dropping onto itself) and for moves that would leave the main window empty.
    /// </summary>
    public static bool Move(Workspace ws, IReadOnlyList<string> panels, string? active, AreaNode target, DropZone zone)
    {
        var payload = panels.Distinct().ToList();
        var targetRoot = RootOf(ws, target);
        if (payload.Count == 0 || targetRoot == null || zone == DropZone.Tab && !target.IsLeaf)
        {
            return false;
        }

        // Dropping an area (or a window) onto itself changes nothing.
        if (target.IsLeaf && target.Panels.Count > 0 && target.Panels.All(payload.Contains))
        {
            return false;
        }

        var targetRootPanels = PanelsIn(targetRoot);
        if (targetRootPanels.Count > 0 && targetRootPanels.All(payload.Contains))
        {
            return false;
        }

        if (targetRoot != ws.Root && WouldEmptyMain(ws, payload))
        {
            return false;
        }

        RemoveEverywhere(ws, payload);
        var activeIndex = Math.Max(0, active == null ? 0 : payload.IndexOf(active));
        if (zone == DropZone.Tab)
        {
            target.Panels.AddRange(payload);
            target.Active = target.Panels.IndexOf(payload[activeIndex]);
        }
        else
        {
            var side = ToSide(zone);
            var (sizeMode, ratio) = SplitSize(payload, side, target == targetRoot && !target.IsLeaf);
            SplitInPlace(target, side, new AreaNode { Panels = payload, Active = activeIndex }, sizeMode, ratio, 0);
        }

        CollapseAll(ws);
        return true;
    }

    /// <summary>
    /// How a new piece is sized: toolbars (compact panels) take what their content needs, panels take
    /// half of an area, or a 30% column/row at a window edge.
    /// </summary>
    private static (SizeMode Mode, double Ratio) SplitSize(IReadOnlyCollection<string> added, DockSide side, bool windowEdge)
    {
        if (added.Count > 0 && added.All(PanelIds.IsCompact))
        {
            return (side is DockSide.Left or DockSide.Top ? SizeMode.AutoFirst : SizeMode.AutoSecond, 0.5);
        }

        return (SizeMode.Ratio, !windowEdge ? 0.5 : side is DockSide.Left or DockSide.Top ? 0.3 : 0.7);
    }

    /// <summary>Adds a toolbar (compact panel) along the bottom of the main window; no-op if it is already shown.</summary>
    public static void AddBar(Workspace ws, string id)
    {
        if (Contains(ws, id))
        {
            return;
        }

        SplitInPlace(ws.Root, DockSide.Bottom, AreaNode.Leaf(id), SizeMode.AutoSecond, 0.5, 0);
    }

    /// <summary>Moves <paramref name="panels"/> into a new floating window. Null when it would empty the main window.</summary>
    public static FloatingArea? Float(Workspace ws, IReadOnlyList<string> panels, string? active)
    {
        var payload = panels.Distinct().ToList();
        if (payload.Count == 0 || WouldEmptyMain(ws, payload))
        {
            return null;
        }

        var floating = new FloatingArea();
        var source = FindLeafWith(ws, payload[0]);
        if (source != null)
        {
            SetAnchor(ws, floating, source, payload);
        }

        RemoveEverywhere(ws, payload);
        floating.Root = new AreaNode { Panels = payload, Active = Math.Max(0, active == null ? 0 : payload.IndexOf(active)) };
        CollapseAll(ws);
        ws.Floating.Add(floating);
        return floating;
    }

    /// <summary>
    /// Docks a whole floating window (keeping its own splits) at <paramref name="target"/>; a Tab drop
    /// merges all its panels into the target area as tabs.
    /// </summary>
    public static bool DockWindowAt(Workspace ws, FloatingArea floating, AreaNode target, DropZone zone)
    {
        var targetRoot = RootOf(ws, target);
        if (targetRoot == null || targetRoot == floating.Root || zone == DropZone.Tab && !target.IsLeaf)
        {
            return false;
        }

        ws.Floating.Remove(floating);
        if (zone == DropZone.Tab)
        {
            var panels = PanelsIn(floating.Root);
            var active = Leaves(floating.Root).First().ActivePanel ?? panels[0];
            target.Panels.AddRange(panels);
            target.Active = target.Panels.IndexOf(active);
        }
        else
        {
            var side = ToSide(zone);
            var (sizeMode, ratio) = SplitSize(PanelsIn(floating.Root), side, target == targetRoot && !target.IsLeaf);
            SplitInPlace(target, side, floating.Root, sizeMode, ratio, 0);
        }

        CollapseAll(ws);
        return true;
    }

    /// <summary>Puts <paramref name="id"/> next to <paramref name="target"/> (an area or a window root) on <paramref name="side"/>.</summary>
    public static void Split(Workspace ws, AreaNode target, DockSide side, string id) => Move(ws, [id], id, target, ToZone(side));

    public static void AddTab(Workspace ws, AreaNode target, string id)
    {
        if (target.Panels.Contains(id))
        {
            target.Active = target.Panels.IndexOf(id);
            return;
        }

        Move(ws, [id], id, target, DropZone.Tab);
    }

    /// <summary>One panel into a floating window (Window menu / "Open in new window").</summary>
    public static FloatingArea? Detach(Workspace ws, string id) => Float(ws, [id], id);

    /// <summary>A whole area (all its tabs) into a floating window.</summary>
    public static FloatingArea? DetachLeaf(Workspace ws, AreaNode leaf) => Float(ws, leaf.Panels.ToList(), leaf.ActivePanel);

    /// <summary>Closes one tab. Refused when it is the last panel in the main window.</summary>
    public static bool RemovePanel(Workspace ws, string id)
    {
        if (WouldEmptyMain(ws, [id]))
        {
            return false;
        }

        RemoveEverywhere(ws, [id]);
        CollapseAll(ws);
        return true;
    }

    /// <summary>Closes a whole area. Refused for the main window's last area.</summary>
    public static bool CloseLeaf(Workspace ws, AreaNode leaf)
    {
        var root = RootOf(ws, leaf);
        if (root == null || root == ws.Root && WouldEmptyMain(ws, leaf.Panels))
        {
            return false;
        }

        leaf.Panels.Clear();
        CollapseAll(ws);
        return true;
    }

    public static void Swap(AreaNode a, AreaNode b)
    {
        (a.Panels, b.Panels) = (b.Panels, a.Panels);
        (a.Active, b.Active) = (b.Active, a.Active);
    }

    /// <summary>Puts a floating window's areas back where they were detached from (or on the right if that place is gone).</summary>
    public static void Dock(Workspace ws, FloatingArea floating)
    {
        ws.Floating.Remove(floating);
        var root = floating.Root;
        foreach (var leaf in Leaves(root))
        {
            leaf.Panels.RemoveAll(p => Contains(ws, p));
        }

        root = Collapse(root);
        if (IsEmpty(root))
        {
            return;
        }

        var anchors = floating.AnchorPanels.Where(p => Contains(ws, p)).ToList();
        var anchorRoot = anchors.Count == 0 ? null : Roots(ws).First(r => Contains(r, anchors[0]));
        if (anchorRoot == null)
        {
            SplitInPlace(ws.Root, DockSide.Right, root, SizeMode.Ratio, 0.75, 0);
        }
        else if (floating.AnchorSide == null && root.IsLeaf)
        {
            var tabLeaf = FindLeafWith(anchorRoot, anchors[0])!;
            var active = root.ActivePanel;
            tabLeaf.Panels.AddRange(root.Panels);
            tabLeaf.Active = active == null ? tabLeaf.Active : tabLeaf.Panels.IndexOf(active);
        }
        else
        {
            var inRoot = anchors.Where(a => Contains(anchorRoot, a)).ToHashSet();
            var target = CommonAncestor(anchorRoot, inRoot) ?? anchorRoot;
            SplitInPlace(target, floating.AnchorSide ?? DockSide.Right, root, floating.DockSizeMode, floating.DockRatio, floating.DockSecondPx);
        }

        CollapseAll(ws);
    }

    // ---------------------------------------------------------------------
    // Repair
    // ---------------------------------------------------------------------

    /// <summary>
    /// Repairs a workspace loaded from settings: unknown/duplicate panels dropped, broken splits
    /// collapsed, sizes clamped, empty floating windows removed. Falls back to <paramref name="fallback"/>
    /// if the main window ends up empty.
    /// </summary>
    public static void Normalize(Workspace ws, ICollection<string> knownPanels, Func<AreaNode> fallback)
    {
        var seen = new HashSet<string>();
        ws.Root = Collapse(NormalizeNode(ws.Root, knownPanels, seen));
        ws.Floating ??= [];
        ws.Floating.RemoveAll(f => f == null);
        foreach (var f in ws.Floating)
        {
            f.Root = Collapse(NormalizeNode(f.Root, knownPanels, seen));
            f.AnchorPanels ??= [];
            f.DockRatio = ClampRatio(f.DockRatio);
            f.DockSecondPx = ClampPx(f.DockSecondPx);
        }

        ws.Floating.RemoveAll(f => IsEmpty(f.Root));
        ws.Name ??= string.Empty;

        if (IsEmpty(ws.Root))
        {
            ws.Root = fallback();
            ws.Floating.Clear();
            Normalize(ws, knownPanels, () => new AreaNode());
        }
    }

    private static AreaNode NormalizeNode(AreaNode? node, ICollection<string> known, HashSet<string> seen)
    {
        if (node == null)
        {
            return new AreaNode();
        }

        if (!Enum.IsDefined(node.Split) || !Enum.IsDefined(node.SizeMode))
        {
            node.Split = SplitDirection.None;
            node.SizeMode = SizeMode.Ratio;
        }

        if (node.IsLeaf)
        {
            node.A = null;
            node.B = null;
            node.Panels = (node.Panels ?? []).Where(p => p != null && known.Contains(p) && seen.Add(p)).ToList();
            node.Active = node.Panels.Count == 0 ? 0 : Math.Clamp(node.Active, 0, node.Panels.Count - 1);
            return node;
        }

        node.Panels = [];
        node.Ratio = ClampRatio(node.Ratio);
        node.SecondPx = ClampPx(node.SecondPx);
        node.A = NormalizeNode(node.A, known, seen);
        node.B = NormalizeNode(node.B, known, seen);
        return node;
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    public static AreaNode Collapse(AreaNode node)
    {
        if (node.IsLeaf)
        {
            return node;
        }

        node.A = node.A == null ? null : Collapse(node.A);
        node.B = node.B == null ? null : Collapse(node.B);
        var aEmpty = IsEmpty(node.A);
        var bEmpty = IsEmpty(node.B);
        if (aEmpty && bEmpty)
        {
            return new AreaNode();
        }

        if (aEmpty)
        {
            return node.B!;
        }

        return bEmpty ? node.A! : node;
    }

    public static DockSide ToSide(DropZone zone) => zone switch
    {
        DropZone.Left => DockSide.Left,
        DropZone.Top => DockSide.Top,
        DropZone.Bottom => DockSide.Bottom,
        _ => DockSide.Right,
    };

    public static DropZone ToZone(DockSide side) => side switch
    {
        DockSide.Left => DropZone.Left,
        DockSide.Top => DropZone.Top,
        DockSide.Bottom => DropZone.Bottom,
        _ => DropZone.Right,
    };

    private static bool WouldEmptyMain(Workspace ws, IEnumerable<string> leaving)
    {
        var main = PanelsIn(ws.Root);
        return main.Count > 0 && main.All(leaving.Contains);
    }

    private static void CollapseAll(Workspace ws)
    {
        ws.Root = Collapse(ws.Root);
        foreach (var f in ws.Floating)
        {
            f.Root = Collapse(f.Root);
        }

        ws.Floating.RemoveAll(f => IsEmpty(f.Root));
    }

    private static double ClampRatio(double ratio) => double.IsFinite(ratio) ? Math.Clamp(ratio, 0.05, 0.95) : 0.5;

    private static double ClampPx(double px) => double.IsFinite(px) ? Math.Clamp(px, 0, 10000) : 0;

    private static bool IsEmpty(AreaNode? node) => node == null || node.IsLeaf && node.Panels.Count == 0;

    /// <summary>Removes the panels wherever they are, without collapsing (callers collapse once at the end).</summary>
    private static void RemoveEverywhere(Workspace ws, IReadOnlyCollection<string> ids)
    {
        foreach (var leaf in Roots(ws).SelectMany(Leaves).ToList())
        {
            foreach (var id in ids)
            {
                var i = leaf.Panels.IndexOf(id);
                if (i < 0)
                {
                    continue;
                }

                leaf.Panels.RemoveAt(i);
                if (leaf.Active > i || leaf.Active >= leaf.Panels.Count)
                {
                    leaf.Active = Math.Max(0, leaf.Active - 1);
                }
            }
        }
    }

    /// <summary>Remembers where a payload came from, so closing its floating window can put it back.</summary>
    private static void SetAnchor(Workspace ws, FloatingArea floating, AreaNode source, List<string> payload)
    {
        var rest = source.Panels.Where(p => !payload.Contains(p)).ToList();
        if (rest.Count > 0)
        {
            floating.AnchorPanels = rest; // it was a tab next to these
            floating.AnchorSide = null;
            return;
        }

        var parent = FindParent(ws, source);
        if (parent == null)
        {
            return; // a window's only area: no anchor, docks on the right of the main window
        }

        var isA = parent.A == source;
        floating.AnchorPanels = PanelsIn(isA ? parent.B : parent.A);
        floating.AnchorSide = parent.Split == SplitDirection.LeftRight
            ? (isA ? DockSide.Left : DockSide.Right)
            : (isA ? DockSide.Top : DockSide.Bottom);
        floating.DockSizeMode = parent.SizeMode;
        floating.DockRatio = parent.Ratio;
        floating.DockSecondPx = parent.SecondPx;
    }

    /// <summary>Turns <paramref name="node"/> into a split of (its old content, <paramref name="added"/>).</summary>
    private static void SplitInPlace(AreaNode node, DockSide side, AreaNode added, SizeMode sizeMode, double ratio, double secondPx)
    {
        var old = new AreaNode
        {
            Split = node.Split, A = node.A, B = node.B, SizeMode = node.SizeMode, Ratio = node.Ratio,
            SecondPx = node.SecondPx, Panels = node.Panels, Active = node.Active,
        };

        node.Split = side is DockSide.Left or DockSide.Right ? SplitDirection.LeftRight : SplitDirection.TopBottom;
        (node.A, node.B) = side is DockSide.Left or DockSide.Top ? (added, old) : (old, added);
        node.Panels = [];
        node.Active = 0;
        node.SizeMode = sizeMode;
        node.Ratio = ratio;
        node.SecondPx = secondPx;
    }

    /// <summary>Smallest subtree that contains all <paramref name="ids"/>.</summary>
    private static AreaNode? CommonAncestor(AreaNode node, ISet<string> ids)
    {
        if (!ids.All(id => Contains(node, id)))
        {
            return null;
        }

        if (!node.IsLeaf)
        {
            return CommonAncestor(node.A!, ids) ?? CommonAncestor(node.B!, ids) ?? node;
        }

        return node;
    }
}
