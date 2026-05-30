using DoomAIEditor.Editor;
using DoomAIEditor.Models;

namespace DoomAIEditor.UI;

public class MapViewControl : Control
{
    readonly EditorState _state;
    PointF? _lastPan;
    PointF? _hoverWorld;
    bool _isPanning;
    int _draggedVertex = -1;
    int _draggedLinedef = -1;
    PointF _dragLineOrigin;
    int _lineStartIdx = -1;
    int _draggedThing = -1;

    static readonly Color BgColor = Color.FromArgb(30, 30, 30);
    static readonly Color GridColor = Color.FromArgb(55, 55, 55);
    static readonly Color GridMajorColor = Color.FromArgb(75, 75, 75);
    static readonly Color VertexColor = Color.FromArgb(0, 220, 120);
    static readonly Color VertexSelColor = Color.FromArgb(255, 200, 0);
    static readonly Color LinedefColor = Color.FromArgb(180, 180, 180);
    static readonly Color LinedefTwoSidedColor = Color.FromArgb(100, 130, 200);
    static readonly Color LinedefSelColor = Color.FromArgb(255, 200, 0);
    static readonly Color LinedefSectorSelColor = Color.FromArgb(80, 160, 255);
    static readonly Color ThingColor = Color.FromArgb(220, 100, 100);
    static readonly Color ThingSelColor = Color.FromArgb(255, 200, 0);
    static readonly Color DrawColor = Color.FromArgb(255, 255, 0);

    public MapViewControl(EditorState state)
    {
        _state = state;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = BgColor;
        Cursor = Cursors.Cross;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        DrawGrid(g);
        DrawLinedefs(g);
        DrawSectorSideHighlights(g);
        DrawVertices(g);
        DrawThings(g);
        DrawPreview(g);
        DrawCursor(g);
    }

    void DrawGrid(Graphics g)
    {
        float zoom = _state.Zoom;
        int gs = _state.GridSize;
        float pixelStep = gs * zoom;
        if (pixelStep < 4) return;

        float cx = Width / 2f + _state.ViewOffset.X;
        float cy = Height / 2f + _state.ViewOffset.Y;

        float startX = cx % pixelStep;
        float startY = cy % pixelStep;

        using var gridPen = new Pen(GridColor, 1);
        using var majorPen = new Pen(GridMajorColor, 1);

        int majorEvery = 8;
        float worldLeft = _state.ScreenToWorld(new PointF(0, 0), Size).X;
        float worldTop = _state.ScreenToWorld(new PointF(0, 0), Size).Y;

        for (float x = startX; x < Width; x += pixelStep)
        {
            float wx = (x - cx) / zoom;
            bool major = (Math.Abs(wx / gs) % majorEvery) < 0.5f;
            g.DrawLine(major ? majorPen : gridPen, x, 0, x, Height);
        }
        for (float y = startY; y < Height; y += pixelStep)
        {
            float wy = -(y - cy) / zoom;
            bool major = (Math.Abs(wy / gs) % majorEvery) < 0.5f;
            g.DrawLine(major ? majorPen : gridPen, 0, y, Width, y);
        }

        // Origin axes
        var origin = _state.WorldToScreen(PointF.Empty, Size);
        using var axisPen = new Pen(Color.FromArgb(80, 100, 80), 1);
        if (origin.X >= 0 && origin.X <= Width) g.DrawLine(axisPen, origin.X, 0, origin.X, Height);
        if (origin.Y >= 0 && origin.Y <= Height) g.DrawLine(axisPen, 0, origin.Y, Width, origin.Y);
    }

    void DrawLinedefs(Graphics g)
    {
        using var pen = new Pen(LinedefColor, 1.5f);
        using var tsPen = new Pen(LinedefTwoSidedColor, 1.5f);
        using var selPen = new Pen(LinedefSelColor, 2.5f);

        for (int i = 0; i < _state.Map.Linedefs.Count; i++)
        {
            var ld = _state.Map.Linedefs[i];
            if (ld.StartVertex < 0 || ld.StartVertex >= _state.Map.Vertices.Count) continue;
            if (ld.EndVertex < 0 || ld.EndVertex >= _state.Map.Vertices.Count) continue;

            var v1 = _state.Map.Vertices[ld.StartVertex];
            var v2 = _state.Map.Vertices[ld.EndVertex];
            var p1 = _state.WorldToScreen(new PointF(v1.X, v1.Y), Size);
            var p2 = _state.WorldToScreen(new PointF(v2.X, v2.Y), Size);

            bool selected = _state.Selection.Linedefs.Contains(i);
            var usePen = selected ? selPen : (ld.IsTwoSided ? tsPen : pen);
            g.DrawLine(usePen, p1, p2);

            // Direction tick
            if (_state.Zoom > 0.3f)
            {
                float mx = (p1.X + p2.X) / 2;
                float my = (p1.Y + p2.Y) / 2;
                float dx = p2.X - p1.X, dy = p2.Y - p1.Y;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len > 10)
                {
                    float nx = -dy / len * 6, ny = dx / len * 6;
                    g.DrawLine(usePen, mx, my, mx + nx, my + ny);
                }
            }
        }
    }

    void DrawVertices(Graphics g)
    {
        float r = Math.Max(2f, 3f * _state.Zoom);
        using var brush = new SolidBrush(VertexColor);
        using var selBrush = new SolidBrush(VertexSelColor);

        for (int i = 0; i < _state.Map.Vertices.Count; i++)
        {
            var v = _state.Map.Vertices[i];
            var sp = _state.WorldToScreen(new PointF(v.X, v.Y), Size);
            bool sel = _state.Selection.Vertices.Contains(i);
            g.FillRectangle(sel ? selBrush : brush, sp.X - r, sp.Y - r, r * 2, r * 2);
        }
    }

    void DrawThings(Graphics g)
    {
        float r = Math.Max(4f, 8f * _state.Zoom);
        using var brush = new SolidBrush(Color.FromArgb(120, ThingColor));
        using var selBrush = new SolidBrush(Color.FromArgb(120, ThingSelColor));
        using var pen = new Pen(ThingColor, 1.5f);
        using var selPen = new Pen(ThingSelColor, 2f);

        for (int i = 0; i < _state.Map.Things.Count; i++)
        {
            var t = _state.Map.Things[i];
            var sp = _state.WorldToScreen(new PointF(t.X, t.Y), Size);
            bool sel = _state.Selection.Things.Contains(i);

            g.FillEllipse(sel ? selBrush : brush, sp.X - r, sp.Y - r, r * 2, r * 2);
            g.DrawEllipse(sel ? selPen : pen, sp.X - r, sp.Y - r, r * 2, r * 2);

            // Direction arrow
            float rad = t.Angle * MathF.PI / 180f;
            float ax = sp.X + MathF.Cos(rad) * r;
            float ay = sp.Y - MathF.Sin(rad) * r;
            g.DrawLine(sel ? selPen : pen, sp.X, sp.Y, ax, ay);
        }
    }

    void DrawPreview(Graphics g)
    {
        if (_hoverWorld == null) return;
        var snap = _state.Snap(_hoverWorld.Value);
        var snapScreen = _state.WorldToScreen(snap, Size);

        if (_state.Mode == EditMode.DrawLinedef && _state.DrawStart != null)
        {
            using var pen = new Pen(DrawColor, 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            var startScreen = _state.WorldToScreen(_state.DrawStart.Value, Size);
            g.DrawLine(pen, startScreen, snapScreen);
        }

        if (_state.Mode == EditMode.DrawSector && _state.DrawVertices.Count > 0)
        {
            using var pen = new Pen(DrawColor, 2f);
            var pts = _state.DrawVertices
                .Select(idx => _state.WorldToScreen(new PointF(_state.Map.Vertices[idx].X, _state.Map.Vertices[idx].Y), Size))
                .Append(snapScreen).ToArray();
            if (pts.Length >= 2)
                g.DrawLines(pen, pts);
        }
    }

    void DrawCursor(Graphics g)
    {
        if (_hoverWorld == null || _isPanning) return;
        var snap = _state.Snap(_hoverWorld.Value);
        var sp = _state.WorldToScreen(snap, Size);

        using var brush = new SolidBrush(Color.FromArgb(180, Color.Yellow));
        g.FillEllipse(brush, sp.X - 3, sp.Y - 3, 6, 6);

        if (_state.Zoom >= 0.5f)
        {
            using var font = new Font("Consolas", 8);
            using var textBrush = new SolidBrush(Color.FromArgb(200, Color.White));
            g.DrawString($"({(int)snap.X}, {(int)snap.Y})", font, textBrush, sp.X + 6, sp.Y + 2);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var worldPt = _state.ScreenToWorld(e.Location, Size);
        var snapped = _state.Snap(worldPt);

        if (e.Button == MouseButtons.Middle ||
            (e.Button == MouseButtons.Right && _state.Mode != EditMode.Select && _state.Mode != EditMode.DrawVertex && _state.Mode != EditMode.DrawLinedef && _state.Mode != EditMode.DrawSector && _state.Mode != EditMode.PlaceThing))
        {
            _isPanning = true;
            _lastPan = e.Location;
            Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button == MouseButtons.Right && _state.Mode == EditMode.DrawVertex)
        {
            int nearVert = _state.FindNearestVertex(worldPt, 12f / _state.Zoom);
            if (nearVert >= 0)
            {
                _draggedVertex = nearVert;
                Cursor = Cursors.SizeAll;
            }
            else
            {
                PlaceVertex(snapped);
                Invalidate();
            }
            return;
        }

        if (e.Button == MouseButtons.Right && _state.Mode == EditMode.DrawLinedef)
        {
            int nearVert = _state.FindNearestVertex(worldPt, 12f / _state.Zoom);
            if (nearVert >= 0)
            {
                _lineStartIdx = nearVert;
                _state.DrawStart = new PointF(_state.Map.Vertices[nearVert].X, _state.Map.Vertices[nearVert].Y);
            }
            else
            {
                int nearLine = _state.FindNearestLinedef(worldPt, 10f / _state.Zoom);
                if (nearLine >= 0)
                {
                    _draggedLinedef = nearLine;
                    _dragLineOrigin = worldPt;
                    Cursor = Cursors.SizeAll;
                }
                else if (_lineStartIdx >= 0)
                {
                    _lineStartIdx = -1;
                    _state.DrawStart = null;
                }
            }
            Invalidate();
            return;
        }

        if (e.Button == MouseButtons.Right && _state.Mode == EditMode.DrawSector)
        {
            HandleSectorModeRightClick(worldPt);
            Invalidate();
            return;
        }

        if (e.Button == MouseButtons.Right && _state.Mode == EditMode.PlaceThing)
        {
            int nearThing = _state.FindNearestThing(worldPt, 16f / _state.Zoom);
            if (nearThing >= 0)
            {
                _draggedThing = nearThing;
                Cursor = Cursors.SizeAll;
            }
            else
            {
                PlaceThing(snapped);
                Invalidate();
            }
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            _state.Selection.Clear();
            _state.DrawStart = null;
            _state.DrawVertices.Clear();
            Invalidate();
            return;
        }

        if (e.Button == MouseButtons.Left)
            HandleLeftClick(worldPt, snapped, e);
    }

    void HandleLeftClick(PointF worldPt, PointF snapped, MouseEventArgs e)
    {
        switch (_state.Mode)
        {
            case EditMode.Select:
                HandleSelect(worldPt, (Control.ModifierKeys & Keys.Shift) != 0);
                break;
            case EditMode.DrawVertex:
                HandleVertexModeSelect(worldPt);
                break;
            case EditMode.DrawLinedef:
                HandleLinedefModeLeftClick(worldPt);
                break;
            case EditMode.DrawSector:
                HandleSectorModeLeftClick(worldPt, snapped);
                break;
            case EditMode.PlaceThing:
                HandleThingModeSelect(worldPt);
                break;
        }
        Invalidate();
    }

    void HandleSelect(PointF worldPt, bool additive)
    {
        if (!additive) _state.Selection.Clear();

        int nearVert = _state.FindNearestVertex(worldPt, 12f / _state.Zoom);
        if (nearVert >= 0) { _state.Selection.Vertices.Add(nearVert); SelectionChanged?.Invoke(this, EventArgs.Empty); return; }

        int nearThing = _state.FindNearestThing(worldPt, 16f / _state.Zoom);
        if (nearThing >= 0) { _state.Selection.Things.Add(nearThing); SelectionChanged?.Invoke(this, EventArgs.Empty); return; }

        int nearLine = _state.FindNearestLinedef(worldPt, 10f / _state.Zoom);
        if (nearLine >= 0) { _state.Selection.Linedefs.Add(nearLine); SelectionChanged?.Invoke(this, EventArgs.Empty); return; }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void HandleVertexModeSelect(PointF worldPt)
    {
        _state.Selection.Clear();
        int nearVert = _state.FindNearestVertex(worldPt, 12f / _state.Zoom);
        if (nearVert >= 0)
            _state.Selection.Vertices.Add(nearVert);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void HandleLinedefModeLeftClick(PointF worldPt)
    {
        if (_lineStartIdx >= 0)
        {
            int nearVert = _state.FindNearestVertex(worldPt, 12f / _state.Zoom);
            if (nearVert >= 0 && nearVert != _lineStartIdx && !LinedefExists(_lineStartIdx, nearVert))
            {
                if (_state.Map.Sectors.Count == 0)
                    _state.Map.Sectors.Add(new DoomSector());
                var ld = new DoomLinedef { StartVertex = _lineStartIdx, EndVertex = nearVert, Flags = 1 };
                ld.FrontSidedef = _state.Map.Sidedefs.Count;
                _state.Map.Sidedefs.Add(new DoomSidedef { Sector = 0, MiddleTexture = "STARTAN2" });
                _state.Map.Linedefs.Add(ld);
                _state.NotifyChanged();
            }
            _lineStartIdx = -1;
            _state.DrawStart = null;
        }
        else
        {
            _state.Selection.Clear();
            int nearLine = _state.FindNearestLinedef(worldPt, 10f / _state.Zoom);
            if (nearLine >= 0)
                _state.Selection.Linedefs.Add(nearLine);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    void PlaceVertex(PointF snapped)
    {
        _state.Map.Vertices.Add(new DoomVertex((short)snapped.X, (short)snapped.Y));
        _state.NotifyChanged();
    }

    void HandleDrawLinedef(PointF snapped)
    {
        if (_state.DrawStart == null)
        {
            int near = _state.FindNearestVertex(snapped, 16f / _state.Zoom);
            _state.DrawStart = near >= 0 ? new PointF(_state.Map.Vertices[near].X, _state.Map.Vertices[near].Y) : snapped;
        }
        else
        {
            int startIdx = FindOrCreateVertex(_state.DrawStart.Value);
            int endIdx = FindOrCreateVertex(snapped);

            if (startIdx != endIdx && !LinedefExists(startIdx, endIdx))
            {
                var ld = new DoomLinedef { StartVertex = startIdx, EndVertex = endIdx, Flags = 1 };
                if (_state.Map.Sectors.Count == 0)
                    _state.Map.Sectors.Add(new DoomSector());
                ld.FrontSidedef = _state.Map.Sidedefs.Count;
                _state.Map.Sidedefs.Add(new DoomSidedef { Sector = 0, MiddleTexture = "STARTAN2" });
                _state.Map.Linedefs.Add(ld);
            }
            _state.DrawStart = snapped;
            _state.NotifyChanged();
        }
    }

    void HandleSectorModeLeftClick(PointF worldPt, PointF snapped)
    {
        _state.Selection.Clear();
        int sectorIdx = _state.FindSectorAt(worldPt);
        if (sectorIdx >= 0)
            _state.Selection.Sectors.Add(sectorIdx);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void HandleSectorModeRightClick(PointF worldPt)
    {
        if (_state.Selection.Sectors.Count == 0) return;
        int selectedSector = _state.Selection.Sectors.First();

        int nearLine = _state.FindNearestLinedef(worldPt, 20f / _state.Zoom);
        if (nearLine < 0) return;

        var ld = _state.Map.Linedefs[nearLine];
        if (ld.StartVertex < 0 || ld.StartVertex >= _state.Map.Vertices.Count) return;
        if (ld.EndVertex   < 0 || ld.EndVertex   >= _state.Map.Vertices.Count) return;

        float ax = _state.Map.Vertices[ld.StartVertex].X, ay = _state.Map.Vertices[ld.StartVertex].Y;
        float bx = _state.Map.Vertices[ld.EndVertex].X,   by = _state.Map.Vertices[ld.EndVertex].Y;
        float cross = (bx - ax) * (worldPt.Y - ay) - (by - ay) * (worldPt.X - ax);

        if (cross < 0)
        {
            // Front sidedef side
            if (ld.FrontSidedef >= 0 && ld.FrontSidedef < _state.Map.Sidedefs.Count)
                _state.Map.Sidedefs[ld.FrontSidedef].Sector = selectedSector;
        }
        else
        {
            // Back sidedef side
            if (ld.BackSidedef >= 0 && ld.BackSidedef < _state.Map.Sidedefs.Count)
                _state.Map.Sidedefs[ld.BackSidedef].Sector = selectedSector;
        }
        _state.NotifyChanged();
    }

    void HandleDrawSector(PointF snapped)
    {
        int near = _state.FindNearestVertex(snapped, 16f / _state.Zoom);
        int vertIdx;
        if (near >= 0)
        {
            vertIdx = near;
            snapped = new PointF(_state.Map.Vertices[near].X, _state.Map.Vertices[near].Y);
        }
        else
        {
            vertIdx = _state.Map.Vertices.Count;
            _state.Map.Vertices.Add(new DoomVertex((short)snapped.X, (short)snapped.Y));
        }

        if (_state.DrawVertices.Count > 0 && vertIdx == _state.DrawVertices[0])
        {
            CloseSector();
            return;
        }

        if (_state.DrawVertices.Count > 0)
        {
            int prevIdx = _state.DrawVertices[^1];
            if (!LinedefExists(prevIdx, vertIdx))
            {
                if (_state.Map.Sectors.Count == 0 || _state.DrawVertices.Count == 0)
                    _state.Map.Sectors.Add(new DoomSector());
                int sectorIdx = _state.Map.Sectors.Count - 1;
                var ld = new DoomLinedef { StartVertex = prevIdx, EndVertex = vertIdx, Flags = 1 };
                ld.FrontSidedef = _state.Map.Sidedefs.Count;
                _state.Map.Sidedefs.Add(new DoomSidedef { Sector = sectorIdx, MiddleTexture = "STARTAN2" });
                _state.Map.Linedefs.Add(ld);
            }
        }
        else
        {
            _state.Map.Sectors.Add(new DoomSector());
        }

        _state.DrawVertices.Add(vertIdx);
        _state.NotifyChanged();
    }

    void CloseSector()
    {
        if (_state.DrawVertices.Count >= 3)
        {
            int first = _state.DrawVertices[0];
            int last = _state.DrawVertices[^1];
            int sectorIdx = _state.Map.Sectors.Count - 1;
            if (!LinedefExists(last, first))
            {
                var ld = new DoomLinedef { StartVertex = last, EndVertex = first, Flags = 1 };
                ld.FrontSidedef = _state.Map.Sidedefs.Count;
                _state.Map.Sidedefs.Add(new DoomSidedef { Sector = sectorIdx, MiddleTexture = "STARTAN2" });
                _state.Map.Linedefs.Add(ld);
            }
        }
        _state.DrawVertices.Clear();
        _state.NotifyChanged();
    }

    void HandleThingModeSelect(PointF worldPt)
    {
        _state.Selection.Clear();
        int nearThing = _state.FindNearestThing(worldPt, 16f / _state.Zoom);
        if (nearThing >= 0)
            _state.Selection.Things.Add(nearThing);
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void PlaceThing(PointF snapped)
    {
        _state.Map.Things.Add(new DoomThing
        {
            X = (short)snapped.X, Y = (short)snapped.Y,
            Type = (short)_state.SelectedThingType,
            Angle = 0, Flags = 7
        });
        _state.NotifyChanged();
    }

    int FindOrCreateVertex(PointF pt)
    {
        for (int i = 0; i < _state.Map.Vertices.Count; i++)
        {
            var v = _state.Map.Vertices[i];
            if (Math.Abs(v.X - pt.X) < 1 && Math.Abs(v.Y - pt.Y) < 1) return i;
        }
        int idx = _state.Map.Vertices.Count;
        _state.Map.Vertices.Add(new DoomVertex((short)pt.X, (short)pt.Y));
        return idx;
    }

    void DrawSectorSideHighlights(Graphics g)
    {
        if (_state.Selection.Sectors.Count == 0) return;

        using var pen = new Pen(LinedefSectorSelColor, 2f);
        const float offset = 5f;

        foreach (var ld in _state.Map.Linedefs)
        {
            if (ld.StartVertex < 0 || ld.StartVertex >= _state.Map.Vertices.Count) continue;
            if (ld.EndVertex   < 0 || ld.EndVertex   >= _state.Map.Vertices.Count) continue;

            var p1 = _state.WorldToScreen(new PointF(_state.Map.Vertices[ld.StartVertex].X, _state.Map.Vertices[ld.StartVertex].Y), Size);
            var p2 = _state.WorldToScreen(new PointF(_state.Map.Vertices[ld.EndVertex].X,   _state.Map.Vertices[ld.EndVertex].Y),   Size);

            float sdx = p2.X - p1.X, sdy = p2.Y - p1.Y;
            float len = MathF.Sqrt(sdx * sdx + sdy * sdy);
            if (len < 1) continue;

            // Unit direction along line; right/left normals in screen space (Y-down)
            float nx = sdx / len, ny = sdy / len;
            float rightX = ny, rightY = -nx;
            float leftX  = -ny, leftY  =  nx;

            // Front sidedef → left side of line
            if (ld.FrontSidedef >= 0 && ld.FrontSidedef < _state.Map.Sidedefs.Count
                && _state.Selection.Sectors.Contains(_state.Map.Sidedefs[ld.FrontSidedef].Sector))
            {
                g.DrawLine(pen,
                    p1.X + leftX * offset, p1.Y + leftY * offset,
                    p2.X + leftX * offset, p2.Y + leftY * offset);
            }

            // Back sidedef → right side of line
            if (ld.BackSidedef >= 0 && ld.BackSidedef < _state.Map.Sidedefs.Count
                && _state.Selection.Sectors.Contains(_state.Map.Sidedefs[ld.BackSidedef].Sector))
            {
                g.DrawLine(pen,
                    p1.X + rightX * offset, p1.Y + rightY * offset,
                    p2.X + rightX * offset, p2.Y + rightY * offset);
            }
        }
    }

    bool LinedefExists(int a, int b) =>
        _state.Map.Linedefs.Any(l => (l.StartVertex == a && l.EndVertex == b) || (l.StartVertex == b && l.EndVertex == a));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        _hoverWorld = _state.ScreenToWorld(e.Location, Size);

        if (_isPanning && _lastPan.HasValue)
        {
            float dx = e.X - _lastPan.Value.X;
            float dy = e.Y - _lastPan.Value.Y;
            _state.ViewOffset = new PointF(_state.ViewOffset.X + dx, _state.ViewOffset.Y + dy);
            _lastPan = e.Location;
        }

        if (_draggedVertex >= 0 && _hoverWorld.HasValue)
        {
            var snap = _state.Snap(_hoverWorld.Value);
            var v = _state.Map.Vertices[_draggedVertex];
            v.X = (short)snap.X;
            v.Y = (short)snap.Y;
            _state.NotifyChanged();
        }

        if (_draggedLinedef >= 0 && _hoverWorld.HasValue)
        {
            float dx = _hoverWorld.Value.X - _dragLineOrigin.X;
            float dy = _hoverWorld.Value.Y - _dragLineOrigin.Y;
            int idx = (int)Math.Round(dx);
            int idy = (int)Math.Round(dy);
            if (idx != 0 || idy != 0)
            {
                var ld = _state.Map.Linedefs[_draggedLinedef];
                var v1 = _state.Map.Vertices[ld.StartVertex];
                var v2 = _state.Map.Vertices[ld.EndVertex];
                v1.X += (short)idx; v1.Y += (short)idy;
                v2.X += (short)idx; v2.Y += (short)idy;
                _dragLineOrigin = new PointF(_dragLineOrigin.X + idx, _dragLineOrigin.Y + idy);
                _state.NotifyChanged();
            }
        }

        if (_draggedThing >= 0 && _hoverWorld.HasValue)
        {
            var snap = _state.Snap(_hoverWorld.Value);
            var t = _state.Map.Things[_draggedThing];
            t.X = (short)snap.X;
            t.Y = (short)snap.Y;
            _state.NotifyChanged();
        }

        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_isPanning) { _isPanning = false; Cursor = Cursors.Cross; }
        _lastPan = null;
        if (_draggedVertex >= 0) { _draggedVertex = -1; Cursor = Cursors.Cross; }
        if (_draggedLinedef >= 0) { _draggedLinedef = -1; Cursor = Cursors.Cross; }
        if (_draggedThing >= 0) { _draggedThing = -1; Cursor = Cursors.Cross; }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var worldBefore = _state.ScreenToWorld(e.Location, Size);
        float factor = e.Delta > 0 ? 1.15f : 1f / 1.15f;
        _state.Zoom = Math.Clamp(_state.Zoom * factor, 0.05f, 16f);
        var screenAfter = _state.WorldToScreen(worldBefore, Size);
        _state.ViewOffset = new PointF(
            _state.ViewOffset.X + (e.X - screenAfter.X),
            _state.ViewOffset.Y + (e.Y - screenAfter.Y));
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete)
        {
            DeleteSelected();
            e.Handled = true;
        }
        if (e.KeyCode == Keys.Escape)
        {
            _lineStartIdx = -1;
            _state.DrawStart = null;
            _state.DrawVertices.Clear();
            _state.Selection.Clear();
            Invalidate();
        }
    }

    void DeleteSelected()
    {
        var vertIdx = _state.Selection.Vertices.OrderByDescending(i => i).ToList();
        var thingIdx = _state.Selection.Things.OrderByDescending(i => i).ToList();
        var lineIdx = _state.Selection.Linedefs.OrderByDescending(i => i).ToList();

        foreach (int i in lineIdx) _state.Map.Linedefs.RemoveAt(i);
        foreach (int i in thingIdx) _state.Map.Things.RemoveAt(i);
        foreach (int i in vertIdx)
        {
            _state.Map.Linedefs.RemoveAll(l => l.StartVertex == i || l.EndVertex == i);
            _state.Map.Vertices.RemoveAt(i);
            foreach (var ld in _state.Map.Linedefs)
            {
                if (ld.StartVertex > i) ld.StartVertex--;
                if (ld.EndVertex > i) ld.EndVertex--;
            }
        }

        _state.Selection.Clear();
        _state.NotifyChanged();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FitView()
    {
        var bounds = _state.Map.GetBounds();
        if (bounds.Width == 0 || bounds.Height == 0) return;
        float zoomX = Width / bounds.Width;
        float zoomY = Height / bounds.Height;
        _state.Zoom = Math.Min(zoomX, zoomY) * 0.9f;
        float cx = bounds.X + bounds.Width / 2;
        float cy = bounds.Y + bounds.Height / 2;
        _state.ViewOffset = new PointF(-cx * _state.Zoom, cy * _state.Zoom);
        Invalidate();
    }

    public event EventHandler? SelectionChanged;

    protected override bool IsInputKey(Keys keyData) =>
        keyData == Keys.Delete || keyData == Keys.Escape || base.IsInputKey(keyData);
}
