using DoomAIEditor.Editor;
using DoomAIEditor.Models;
using System.Drawing.Design;
using System.Windows.Forms.Design;

namespace DoomAIEditor.UI;

public class PropertiesPanel : Panel
{
    readonly EditorState _state;
    readonly PropertyGrid _grid;
    readonly Label _label;

    public PropertiesPanel(EditorState state)
    {
        _state = state;
        _label = new Label { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, BackColor = Color.FromArgb(50, 50, 50), Padding = new Padding(4, 0, 0, 0), Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
        _grid = new PropertyGrid { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 40, 40), LineColor = Color.FromArgb(60, 60, 60), CategoryForeColor = Color.LightGray, ViewForeColor = Color.White, ViewBackColor = Color.FromArgb(45, 45, 45), HelpForeColor = Color.LightGray, HelpBackColor = Color.FromArgb(35, 35, 35) };
        Controls.Add(_grid);
        Controls.Add(_label);
    }

    public void Refresh(EditorState state)
    {
        if (state.Selection.Vertices.Count == 1)
        {
            var v = state.Map.Vertices[state.Selection.Vertices.First()];
            _label.Text = "Vertex";
            _grid.SelectedObject = new VertexWrapper(v);
        }
        else if (state.Selection.Linedefs.Count == 1)
        {
            int idx = state.Selection.Linedefs.First();
            _label.Text = "Linedef";
            _grid.SelectedObject = new LinedefWrapper(state.Map, idx);
        }
        else if (state.Selection.Things.Count == 1)
        {
            _label.Text = "Thing";
            _grid.SelectedObject = new ThingWrapper(state.Map.Things[state.Selection.Things.First()]);
        }
        else if (state.Selection.Sectors.Count == 1)
        {
            _label.Text = "Sector";
            _grid.SelectedObject = new SectorWrapper(state.Map.Sectors[state.Selection.Sectors.First()]);
        }
        else
        {
            _label.Text = state.Selection.IsEmpty ? "No Selection" : "Multiple Selected";
            _grid.SelectedObject = null;
        }
        _grid.Refresh();
    }
}

class VertexWrapper(DoomVertex v)
{
    [System.ComponentModel.Category("Position")]
    public short X { get => v.X; set => v.X = value; }
    [System.ComponentModel.Category("Position")]
    public short Y { get => v.Y; set => v.Y = value; }
}

class LinedefWrapper(DoomMap map, int idx)
{
    DoomLinedef L => map.Linedefs[idx];
    [System.ComponentModel.Category("Geometry")]
    public int StartVertex { get => L.StartVertex; set => L.StartVertex = value; }
    [System.ComponentModel.Category("Geometry")]
    public int EndVertex { get => L.EndVertex; set => L.EndVertex = value; }
    [System.ComponentModel.Category("Flags")]
    [System.ComponentModel.Editor(typeof(FlagsChecklist), typeof(UITypeEditor))]
    public LinedefFlags Flags { get => (LinedefFlags)L.Flags; set => L.Flags = (short)value; }
    [System.ComponentModel.Category("Action")]
    public short Special { get => L.Special; set => L.Special = value; }
    [System.ComponentModel.Category("Action")]
    public short Tag { get => L.Tag; set => L.Tag = value; }
    [System.ComponentModel.Category("Sidedefs")]
    public int FrontSidedef { get => L.FrontSidedef; set => L.FrontSidedef = value; }
    [System.ComponentModel.Category("Sidedefs")]
    public int BackSidedef { get => L.BackSidedef; set => L.BackSidedef = value; }

    [System.ComponentModel.Category("Front Sidedef")]
    public string FrontMiddle { get => Sidedef(L.FrontSidedef)?.MiddleTexture ?? "-"; set { var s = Sidedef(L.FrontSidedef); if (s != null) s.MiddleTexture = value; } }
    [System.ComponentModel.Category("Front Sidedef")]
    public string FrontUpper { get => Sidedef(L.FrontSidedef)?.UpperTexture ?? "-"; set { var s = Sidedef(L.FrontSidedef); if (s != null) s.UpperTexture = value; } }
    [System.ComponentModel.Category("Front Sidedef")]
    public string FrontLower { get => Sidedef(L.FrontSidedef)?.LowerTexture ?? "-"; set { var s = Sidedef(L.FrontSidedef); if (s != null) s.LowerTexture = value; } }
    [System.ComponentModel.Category("Front Sidedef")]
    public int FrontSector { get => Sidedef(L.FrontSidedef)?.Sector ?? -1; set { var s = Sidedef(L.FrontSidedef); if (s != null) s.Sector = value; } }

    DoomSidedef? Sidedef(int i) => (i >= 0 && i < map.Sidedefs.Count) ? map.Sidedefs[i] : null;
}

class ThingWrapper(DoomThing t)
{
    [System.ComponentModel.Category("Position")]
    public short X { get => t.X; set => t.X = value; }
    [System.ComponentModel.Category("Position")]
    public short Y { get => t.Y; set => t.Y = value; }
    [System.ComponentModel.Category("Thing")]
    public short Type { get => t.Type; set => t.Type = value; }
    [System.ComponentModel.Category("Thing")]
    [System.ComponentModel.ReadOnly(true)]
    public string TypeName => t.TypeName;
    [System.ComponentModel.Category("Thing")]
    public short Angle { get => t.Angle; set => t.Angle = value; }
    [System.ComponentModel.Category("Thing")]
    public short Flags { get => t.Flags; set => t.Flags = value; }
}

class SectorWrapper(DoomSector s)
{
    [System.ComponentModel.Category("Heights")]
    public short FloorHeight { get => s.FloorHeight; set => s.FloorHeight = value; }
    [System.ComponentModel.Category("Heights")]
    public short CeilingHeight { get => s.CeilingHeight; set => s.CeilingHeight = value; }
    [System.ComponentModel.Category("Textures")]
    public string FloorTexture { get => s.FloorTexture; set => s.FloorTexture = value; }
    [System.ComponentModel.Category("Textures")]
    public string CeilingTexture { get => s.CeilingTexture; set => s.CeilingTexture = value; }
    [System.ComponentModel.Category("Lighting")]
    public short LightLevel { get => s.LightLevel; set => s.LightLevel = value; }
    [System.ComponentModel.Category("Action")]
    public short Special { get => s.Special; set => s.Special = value; }
    [System.ComponentModel.Category("Action")]
    public short Tag { get => s.Tag; set => s.Tag = value; }
}

// Dropdown checklist editor for [Flags] enums used in the PropertyGrid.
class FlagsChecklist : UITypeEditor
{
    public override UITypeEditorEditStyle GetEditStyle(System.ComponentModel.ITypeDescriptorContext? ctx)
        => UITypeEditorEditStyle.DropDown;

    public override object? EditValue(
        System.ComponentModel.ITypeDescriptorContext? ctx,
        IServiceProvider provider,
        object? value)
    {
        if (provider.GetService(typeof(IWindowsFormsEditorService)) is not IWindowsFormsEditorService svc)
            return value;
        if (value is not Enum current) return value;

        var enumType = value.GetType();
        // Only single-bit values (skip None = 0 and combined aliases)
        var bits = Enum.GetValues(enumType)
                       .Cast<Enum>()
                       .Where(e => { var v = Convert.ToInt64(e); return v > 0 && (v & (v - 1)) == 0; })
                       .ToArray();

        var list = new CheckedListBox
        {
            CheckOnClick  = true,
            BorderStyle   = BorderStyle.None,
            BackColor     = Color.FromArgb(45, 45, 45),
            ForeColor     = Color.White,
            Font          = new Font("Segoe UI", 8.5f),
        };

        foreach (var bit in bits)
            list.Items.Add(bit, current.HasFlag(bit));

        list.Height = list.ItemHeight * bits.Length + 2;

        svc.DropDownControl(list);

        long result = 0;
        for (int i = 0; i < list.Items.Count; i++)
            if (list.GetItemChecked(i))
                result |= Convert.ToInt64(bits[i]);

        return Enum.ToObject(enumType, result);
    }
}
