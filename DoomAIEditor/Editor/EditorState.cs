using DoomAIEditor.Models;

namespace DoomAIEditor.Editor;

public enum EditMode { Select, DrawVertex, DrawLinedef, DrawSector, PlaceThing }

public class Selection
{
    public HashSet<int> Vertices { get; } = new();
    public HashSet<int> Linedefs { get; } = new();
    public HashSet<int> Things { get; } = new();
    public HashSet<int> Sectors { get; } = new();

    public bool IsEmpty => Vertices.Count == 0 && Linedefs.Count == 0 && Things.Count == 0 && Sectors.Count == 0;

    public void Clear()
    {
        Vertices.Clear(); Linedefs.Clear(); Things.Clear(); Sectors.Clear();
    }
}

public class EditorState
{
    public DoomMap Map { get; set; } = new();
    public EditMode Mode { get; set; } = EditMode.Select;
    public Selection Selection { get; } = new();
    public int SelectedThingType { get; set; } = 1;
    public string? WadPath { get; set; }
    public bool IsDirty { get; set; }

    public PointF ViewOffset { get; set; } = new(0, 0);
    public float Zoom { get; set; } = 1.0f;

    public bool SnapToGrid { get; set; } = true;
    public int GridSize { get; set; } = 64;

    public PointF? DrawStart { get; set; }
    public PointF? DrawEnd { get; set; }
    public List<int> DrawVertices { get; } = new();

    public event Action? Changed;
    public void NotifyChanged() { IsDirty = true; Changed?.Invoke(); }

    public PointF WorldToScreen(PointF worldPt, Size clientSize)
    {
        float cx = clientSize.Width / 2f + ViewOffset.X;
        float cy = clientSize.Height / 2f + ViewOffset.Y;
        return new PointF(cx + worldPt.X * Zoom, cy - worldPt.Y * Zoom);
    }

    public PointF ScreenToWorld(PointF screenPt, Size clientSize)
    {
        float cx = clientSize.Width / 2f + ViewOffset.X;
        float cy = clientSize.Height / 2f + ViewOffset.Y;
        return new PointF((screenPt.X - cx) / Zoom, -(screenPt.Y - cy) / Zoom);
    }

    public PointF Snap(PointF world)
    {
        if (!SnapToGrid) return world;
        return new PointF(
            MathF.Round(world.X / GridSize) * GridSize,
            MathF.Round(world.Y / GridSize) * GridSize);
    }

    public int FindNearestVertex(PointF worldPt, float thresholdWorld = 16f)
    {
        float minDist = thresholdWorld * thresholdWorld;
        int best = -1;
        for (int i = 0; i < Map.Vertices.Count; i++)
        {
            float dx = Map.Vertices[i].X - worldPt.X;
            float dy = Map.Vertices[i].Y - worldPt.Y;
            float dist = dx * dx + dy * dy;
            if (dist < minDist) { minDist = dist; best = i; }
        }
        return best;
    }

    public int FindNearestLinedef(PointF worldPt, float threshold = 12f)
    {
        float minDist = threshold;
        int best = -1;
        for (int i = 0; i < Map.Linedefs.Count; i++)
        {
            var ld = Map.Linedefs[i];
            if (ld.StartVertex < 0 || ld.StartVertex >= Map.Vertices.Count) continue;
            if (ld.EndVertex < 0 || ld.EndVertex >= Map.Vertices.Count) continue;
            var v1 = Map.Vertices[ld.StartVertex];
            var v2 = Map.Vertices[ld.EndVertex];
            float dist = PointToSegmentDistance(worldPt, new PointF(v1.X, v1.Y), new PointF(v2.X, v2.Y));
            if (dist < minDist) { minDist = dist; best = i; }
        }
        return best;
    }

    public int FindNearestThing(PointF worldPt, float threshold = 20f)
    {
        float minDist = threshold * threshold;
        int best = -1;
        for (int i = 0; i < Map.Things.Count; i++)
        {
            float dx = Map.Things[i].X - worldPt.X;
            float dy = Map.Things[i].Y - worldPt.Y;
            float dist = dx * dx + dy * dy;
            if (dist < minDist) { minDist = dist; best = i; }
        }
        return best;
    }

    public int FindSectorAt(PointF worldPt)
    {
        for (int s = 0; s < Map.Sectors.Count; s++)
            if (IsPointInSector(worldPt, s)) return s;
        return -1;
    }

    bool IsPointInSector(PointF p, int sectorIdx)
    {
        int crossings = 0;
        foreach (var ld in Map.Linedefs)
        {
            bool hasFront = ld.FrontSidedef >= 0 && ld.FrontSidedef < Map.Sidedefs.Count
                            && Map.Sidedefs[ld.FrontSidedef].Sector == sectorIdx;
            bool hasBack  = ld.BackSidedef  >= 0 && ld.BackSidedef  < Map.Sidedefs.Count
                            && Map.Sidedefs[ld.BackSidedef].Sector == sectorIdx;
            if (!hasFront && !hasBack) continue;
            if (ld.StartVertex < 0 || ld.StartVertex >= Map.Vertices.Count) continue;
            if (ld.EndVertex   < 0 || ld.EndVertex   >= Map.Vertices.Count) continue;

            float ax = Map.Vertices[ld.StartVertex].X, ay = Map.Vertices[ld.StartVertex].Y;
            float bx = Map.Vertices[ld.EndVertex].X,   by = Map.Vertices[ld.EndVertex].Y;
            if ((ay > p.Y) != (by > p.Y))
            {
                float ix = ax + (p.Y - ay) / (by - ay) * (bx - ax);
                if (p.X < ix) crossings++;
            }
        }
        return (crossings & 1) == 1;
    }

    static float PointToSegmentDistance(PointF p, PointF a, PointF b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y;
        float lenSq = dx * dx + dy * dy;
        if (lenSq == 0) return MathF.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
        float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        float projX = a.X + t * dx, projY = a.Y + t * dy;
        return MathF.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
    }
}
