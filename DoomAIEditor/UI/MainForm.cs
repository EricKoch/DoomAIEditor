using DoomAIEditor.Editor;
using DoomAIEditor.Models;
using DoomAIEditor.Wad;

namespace DoomAIEditor.UI;

public class MainForm : Form
{
    readonly EditorState _state = new();
    MapViewControl _mapView = null!;
    PropertiesPanel _props = null!;
    StatusStrip _status = null!;
    ToolStripStatusLabel _statusMode = null!, _statusCoords = null!, _statusCounts = null!;
    ToolStrip _toolbar = null!;
    Panel _sidebar = null!;
    ListBox _thingList = null!;
    Panel _thingPanel = null!;
    TableLayoutPanel _sideLayout = null!;
    ClaudePanel _claudePanel = null!;

    public MainForm()
    {
        InitUI();
        NewMap();
        UpdateTitle();
    }

    void InitUI()
    {
        Text = "Doom AI Editor";
        Size = new Size(1280, 800);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(35, 35, 35);
        Icon = SystemIcons.Application;

        BuildMenu();
        BuildToolbar();
        BuildStatusBar();
        BuildLayout();

        _state.Changed += () => { UpdateStatus(); UpdateTitle(); _mapView.Invalidate(); _props.Refresh(_state); };
        _mapView.SelectionChanged += (_, _) => _props.Refresh(_state);
    }

    void BuildMenu()
    {
        var menu = new MenuStrip { BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.White };

        var file = new ToolStripMenuItem("File");
        AddMenuItem(file, "New Map", (_, _) => NewMap(), Keys.Control | Keys.N);
        AddMenuItem(file, "Open WAD...", (_, _) => OpenWad(), Keys.Control | Keys.O);
        AddMenuItem(file, "Save WAD", (_, _) => SaveWad(), Keys.Control | Keys.S);
        AddMenuItem(file, "Save WAD As...", (_, _) => SaveWadAs(), Keys.Control | Keys.Shift | Keys.S);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Exit", null, (_, _) => Close());

        var edit = new ToolStripMenuItem("Edit");
        edit.DropDownItems.Add("Delete Selected\tDel", null, (_, _) => _mapView.Focus());
        edit.DropDownItems.Add("Select All\tCtrl+A", null, (_, _) => SelectAll());
        edit.DropDownItems.Add("Clear Selection\tEsc", null, (_, _) => { _state.Selection.Clear(); _mapView.Invalidate(); });

        var view = new ToolStripMenuItem("View");
        AddMenuItem(view, "Fit Map", (_, _) => _mapView.FitView(), Keys.F);
        view.DropDownItems.Add("Reset Zoom", null, (_, _) => { _state.Zoom = 1; _state.ViewOffset = PointF.Empty; _mapView.Invalidate(); });

        var mapMenu = new ToolStripMenuItem("Map");
        mapMenu.DropDownItems.Add("Map Properties...", null, (_, _) => ShowMapProperties());
        mapMenu.DropDownItems.Add("Add Sector...", null, (_, _) => AddSector());
        mapMenu.DropDownItems.Add("Statistics", null, (_, _) => ShowStats());

        menu.Items.AddRange(new[] { file, edit, view, mapMenu });
        Controls.Add(menu);
        MainMenuStrip = menu;
    }

    static void AddMenuItem(ToolStripMenuItem parent, string text, EventHandler handler, Keys shortcut, string? displayString = null)
    {
        var item = new ToolStripMenuItem(text, null, handler);
        // ShortcutKeys requires a modifier; bare keys (F, Home) use display string only
        bool hasModifier = (shortcut & (Keys.Control | Keys.Alt | Keys.Shift)) != 0;
        if (hasModifier)
            item.ShortcutKeys = shortcut;
        else
            item.ShortcutKeyDisplayString = displayString ?? shortcut.ToString();
        parent.DropDownItems.Add(item);
    }

    void BuildToolbar()
    {
        _toolbar = new ToolStrip { BackColor = Color.FromArgb(50, 50, 50), GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(4, 2, 0, 2) };

        AddModeButton("Select", EditMode.Select, "S — Select and move elements", Keys.S);
        AddModeButton("Vertex", EditMode.DrawVertex, "V — Place vertices", Keys.V);
        AddModeButton("Linedef", EditMode.DrawLinedef, "L — Draw linedefs (walls)", Keys.L);
        AddModeButton("Sector", EditMode.DrawSector, "C — Draw closed sectors (rooms)", Keys.C);
        AddModeButton("Thing", EditMode.PlaceThing, "T — Place things (enemies, items, etc.)", Keys.T);

        _toolbar.Items.Add(new ToolStripSeparator());

        var snapBtn = new ToolStripButton("Snap") { CheckOnClick = true, Checked = true, ToolTipText = "G — Toggle grid snap", BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
        snapBtn.CheckedChanged += (_, _) => _state.SnapToGrid = snapBtn.Checked;
        _toolbar.Items.Add(snapBtn);

        var gridSizes = new ToolStripComboBox { ToolTipText = "Grid size" };
        gridSizes.Items.AddRange(new object[] { "8", "16", "32", "64", "128", "256" });
        gridSizes.SelectedItem = "64";
        gridSizes.SelectedIndexChanged += (_, _) => { if (int.TryParse(gridSizes.Text, out int g)) _state.GridSize = g; };
        _toolbar.Items.Add(new ToolStripLabel("Grid: ") { ForeColor = Color.White });
        _toolbar.Items.Add(gridSizes);

        _toolbar.Items.Add(new ToolStripSeparator());
        var fitBtn = new ToolStripButton("Fit View") { ToolTipText = "F — Fit map in view", BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
        fitBtn.Click += (_, _) => _mapView.FitView();
        _toolbar.Items.Add(fitBtn);

        _toolbar.Items.Add(new ToolStripSeparator());
        var newSectorBtn = new ToolStripButton("New Sector") { ToolTipText = "Create a new blank sector and select it", BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White };
        newSectorBtn.Click += (_, _) => AddSector();
        _toolbar.Items.Add(newSectorBtn);

        Controls.Add(_toolbar);
    }

    void AddModeButton(string label, EditMode mode, string tooltip, Keys key)
    {
        var btn = new ToolStripButton(label) { Tag = mode, CheckOnClick = false, ToolTipText = tooltip, BackColor = Color.FromArgb(60, 60, 60), ForeColor = Color.White, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
        btn.Click += (_, _) => SetMode(mode);
        _toolbar.Items.Add(btn);
    }

    void SetMode(EditMode mode)
    {
        _state.Mode = mode;
        _state.DrawStart = null;
        _state.DrawVertices.Clear();
        foreach (ToolStripItem item in _toolbar.Items)
            if (item is ToolStripButton btn && btn.Tag is EditMode m)
                btn.BackColor = m == mode ? Color.FromArgb(0, 120, 200) : Color.FromArgb(60, 60, 60);
        _statusMode.Text = $"Mode: {mode}";

        bool thingMode = mode == EditMode.PlaceThing;
        _thingPanel.Visible = thingMode;
        _sideLayout.RowStyles[0] = thingMode
            ? new RowStyle(SizeType.Percent, 50)
            : new RowStyle(SizeType.Absolute, 0);
        _sideLayout.RowStyles[1] = thingMode
            ? new RowStyle(SizeType.Percent, 50)
            : new RowStyle(SizeType.Percent, 100);

        _mapView.Focus();
    }

    void BuildStatusBar()
    {
        _status = new StatusStrip { BackColor = Color.FromArgb(40, 40, 40) };
        _statusMode = new ToolStripStatusLabel("Mode: Select") { ForeColor = Color.White };
        _statusCoords = new ToolStripStatusLabel("(0, 0)") { ForeColor = Color.LightGray, Spring = true, TextAlign = ContentAlignment.MiddleRight };
        _statusCounts = new ToolStripStatusLabel("V:0 L:0 S:0 T:0") { ForeColor = Color.LightGray };
        _status.Items.AddRange(new ToolStripItem[] { _statusMode, _statusCoords, _statusCounts });
        Controls.Add(_status);
    }

    void BuildLayout()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            BackColor = Color.FromArgb(35, 35, 35),
            SplitterWidth = 4
        };
        // Defer min sizes and splitter position until the form has a real client size
        Load += (_, _) =>
        {
            split.Panel1MinSize = 400;
            split.Panel2MinSize = 180;
            split.SplitterDistance = Math.Max(400, split.Width - 260);
        };

        _mapView = new MapViewControl(_state) { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(_mapView);

        _sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 40, 40), Padding = new Padding(0, 48, 0, 0) };
        _sideLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        _sideLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _sideLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _props = new PropertiesPanel(_state) { Dock = DockStyle.Fill };
        _sideLayout.Controls.Add(_props, 0, 1);

        _thingPanel = BuildThingPanel();
        _thingPanel.Visible = false;
        _sideLayout.Controls.Add(_thingPanel, 0, 0);

        _sidebar.Controls.Add(_sideLayout);

        _claudePanel = new ClaudePanel(_state) { Dock = DockStyle.Fill };

        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BackColor = Color.FromArgb(35, 35, 35),
            SplitterWidth = 4
        };
        Load += (_, _) =>
        {
            rightSplit.Panel1MinSize = 120;
            rightSplit.Panel2MinSize = 180;
            rightSplit.SplitterDistance = Math.Max(120, rightSplit.Height - 280);
        };
        rightSplit.Panel1.Controls.Add(_sidebar);
        rightSplit.Panel2.Controls.Add(_claudePanel);

        split.Panel2.Controls.Add(rightSplit);

        Controls.Add(split);
    }

    Panel BuildThingPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(40, 40, 40) };
        var header = new Label { Dock = DockStyle.Top, Height = 22, Text = "Thing Types", TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.White, BackColor = Color.FromArgb(50, 50, 50), Font = new Font("Segoe UI", 9f, FontStyle.Bold), Padding = new Padding(4, 0, 0, 0) };
        _thingList = new ListBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 35, 35), ForeColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 8.5f) };
        foreach (var (id, name) in DoomThing.TypeNames.OrderBy(kv => kv.Key))
            _thingList.Items.Add(new ThingTypeItem(id, name));
        _thingList.SelectedIndex = 0;
        _thingList.SelectedIndexChanged += (_, _) =>
        {
            if (_thingList.SelectedItem is ThingTypeItem item)
                _state.SelectedThingType = item.Id;
        };
        panel.Controls.Add(_thingList);
        panel.Controls.Add(header);
        return panel;
    }

    void NewMap()
    {
        if (_state.IsDirty && !ConfirmDiscard()) return;
        _state.Map = new DoomMap();
        _state.Map.Things.Add(new DoomThing { X = 0, Y = 0, Type = 1, Angle = 90, Flags = 7 });
        _state.WadPath = null;
        _state.IsDirty = false;
        _state.Selection.Clear();
        _state.ViewOffset = PointF.Empty;
        _state.Zoom = 1f;
        _mapView?.Invalidate();
        UpdateStatus();
        UpdateTitle();
    }

    void OpenWad()
    {
        if (_state.IsDirty && !ConfirmDiscard()) return;
        using var dlg = new OpenFileDialog { Filter = "WAD Files (*.wad)|*.wad|All Files (*.*)|*.*", Title = "Open WAD" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        try
        {
            var wad = WadFile.Load(dlg.FileName);
            var maps = MapSerializer.FindMapNames(wad);
            if (maps.Count == 0) { MessageBox.Show("No maps found in WAD.", "Open WAD", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            string mapName = maps[0];
            if (maps.Count > 1)
            {
                using var picker = new Form { Text = "Select Map", Size = new Size(260, 200), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
                var lb = new ListBox { Dock = DockStyle.Fill };
                maps.ForEach(m => lb.Items.Add(m));
                lb.SelectedIndex = 0;
                var ok = new Button { Text = "OK", Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
                picker.Controls.AddRange(new Control[] { lb, ok });
                picker.AcceptButton = ok;
                if (picker.ShowDialog(this) == DialogResult.OK && lb.SelectedItem != null)
                    mapName = lb.SelectedItem.ToString()!;
            }

            var map = MapSerializer.ReadFromWad(wad, mapName);
            if (map == null) { MessageBox.Show("Failed to read map.", "Error"); return; }
            _state.Map = map;
            _state.WadPath = dlg.FileName;
            _state.IsDirty = false;
            _state.Selection.Clear();
            _mapView.FitView();
            UpdateStatus();
            UpdateTitle();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening WAD:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void SaveWad()
    {
        if (_state.WadPath == null) { SaveWadAs(); return; }
        DoSave(_state.WadPath);
    }

    void SaveWadAs()
    {
        using var dlg = new SaveFileDialog { Filter = "WAD Files (*.wad)|*.wad|All Files (*.*)|*.*", Title = "Save WAD", FileName = _state.WadPath ?? $"{_state.Map.Name}.wad" };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        DoSave(dlg.FileName);
    }

    void DoSave(string path)
    {
        try
        {
            WadFile wad;
            if (File.Exists(path) && path == _state.WadPath)
                wad = WadFile.Load(path);
            else
                wad = new WadFile();

            MapSerializer.WriteToWad(wad, _state.Map);
            wad.Save(path);
            _state.WadPath = path;
            _state.IsDirty = false;
            UpdateTitle();
            _statusMode.Text = "Saved.";
            Task.Delay(2000).ContinueWith(_ => BeginInvoke(() => _statusMode.Text = $"Mode: {_state.Mode}"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving WAD:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    void SelectAll()
    {
        _state.Selection.Clear();
        for (int i = 0; i < _state.Map.Vertices.Count; i++) _state.Selection.Vertices.Add(i);
        for (int i = 0; i < _state.Map.Linedefs.Count; i++) _state.Selection.Linedefs.Add(i);
        for (int i = 0; i < _state.Map.Things.Count; i++) _state.Selection.Things.Add(i);
        _mapView.Invalidate();
        _props.Refresh(_state);
    }

    void ShowMapProperties()
    {
        using var dlg = new Form { Text = "Map Properties", Size = new Size(300, 160), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false };
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(10) };
        table.Controls.Add(new Label { Text = "Map Name:", TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill }, 0, 0);
        var nameBox = new TextBox { Text = _state.Map.Name, Dock = DockStyle.Fill };
        table.Controls.Add(nameBox, 1, 0);
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom };
        dlg.Controls.AddRange(new Control[] { table, ok });
        dlg.AcceptButton = ok;
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _state.Map.Name = nameBox.Text.ToUpperInvariant().Trim();
            _state.NotifyChanged();
        }
    }

    void AddSector()
    {
        int idx = _state.Map.Sectors.Count;
        _state.Map.Sectors.Add(new DoomSector());
        _state.Selection.Clear();
        _state.Selection.Sectors.Add(idx);
        _state.NotifyChanged();
        SetMode(EditMode.DrawSector);
        _mapView.Invalidate();
        _props.Refresh(_state);
    }

    void ShowStats()
    {
        var m = _state.Map;
        MessageBox.Show($"Map: {m.Name}\n\nVertices:  {m.Vertices.Count}\nLinedefs:  {m.Linedefs.Count}\nSidedefs:  {m.Sidedefs.Count}\nSectors:   {m.Sectors.Count}\nThings:    {m.Things.Count}", "Map Statistics");
    }

    void UpdateTitle()
    {
        string dirty = _state.IsDirty ? "*" : "";
        string file = _state.WadPath != null ? Path.GetFileName(_state.WadPath) : "Untitled";
        Text = $"{dirty}{_state.Map.Name} — {file} — Doom AI Editor";
    }

    void UpdateStatus()
    {
        var m = _state.Map;
        _statusCounts.Text = $"V:{m.Vertices.Count}  L:{m.Linedefs.Count}  S:{m.Sectors.Count}  T:{m.Things.Count}";
    }

    bool ConfirmDiscard() =>
        MessageBox.Show("Unsaved changes will be lost. Continue?", "Unsaved Changes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.S when !e.Control: SetMode(EditMode.Select); break;
            case Keys.V when !e.Control: SetMode(EditMode.DrawVertex); break;
            case Keys.L: SetMode(EditMode.DrawLinedef); break;
            case Keys.C when !e.Control: SetMode(EditMode.DrawSector); break;
            case Keys.T when !e.Control: SetMode(EditMode.PlaceThing); break;
            case Keys.F: _mapView.FitView(); break;
            case Keys.G: _state.SnapToGrid = !_state.SnapToGrid; break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_state.IsDirty && !ConfirmDiscard()) e.Cancel = true;
        base.OnFormClosing(e);
    }
}

record ThingTypeItem(int Id, string Name)
{
    public override string ToString() => $"{Id,5}  {Name}";
}
