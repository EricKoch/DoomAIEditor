using Anthropic;
using Anthropic.Models.Messages;
using DoomAIEditor.Editor;
using DoomAIEditor.Models;
using System.Text;
using System.Text.Json;

namespace DoomAIEditor.UI;

public class ClaudePanel : Panel
{
    readonly EditorState _state;
    readonly RichTextBox _display;
    readonly TextBox _input;
    readonly Button _sendBtn;
    readonly List<MessageParam> _history = new();
    AnthropicClient? _client;
    bool _isStreaming;
    readonly IReadOnlyList<ToolUnion> _tools;

    static readonly Color ColorUser      = Color.FromArgb(160, 210, 255);
    static readonly Color ColorAssistant = Color.FromArgb(180, 255, 190);
    static readonly Color ColorError     = Color.FromArgb(255, 100, 100);
    static readonly Color ColorTool      = Color.FromArgb(255, 210, 80);

    // ── Schema helpers ─────────────────────────────────────────────────────

    static InputSchema BuildSchema(string json)
    {
        var doc = JsonDocument.Parse(json);
        return InputSchema.FromRawUnchecked(
            doc.RootElement.EnumerateObject().GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.Last().Value));
    }

    static InputSchema EmptyInputSchema() => BuildSchema("""{"type":"object","properties":{}}""");

    // ── Constructor ────────────────────────────────────────────────────────

    public ClaudePanel(EditorState state)
    {
        _state = state;
        _tools = BuildTools();

        var header = new Label
        {
            Dock = DockStyle.Top, Height = 24,
            Text = "Claude AI Assistant",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.White,
            BackColor = Color.FromArgb(30, 40, 90),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Padding = new Padding(6, 0, 0, 0)
        };

        _display = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(22, 22, 28),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            Font = new Font("Consolas", 8.5f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            WordWrap = true
        };

        var inputRow = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 68,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(45, 45, 45),
            Padding = new Padding(4)
        };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        inputRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _input = new TextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(35, 35, 35),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f),
            Multiline = true
        };

        _sendBtn = new Button
        {
            Dock = DockStyle.Fill,
            Text = "Send",
            BackColor = Color.FromArgb(0, 100, 200),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _sendBtn.FlatAppearance.BorderSize = 0;

        inputRow.Controls.Add(_input, 0, 0);
        inputRow.Controls.Add(_sendBtn, 1, 0);

        Controls.Add(_display);
        Controls.Add(inputRow);
        Controls.Add(header);

        _sendBtn.Click += (_, _) => _ = SendAsync();
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && e.Control)
            {
                e.SuppressKeyPress = true;
                _ = SendAsync();
            }
        };

        try { _client = new AnthropicClient(); }
        catch { /* API key not set */ }
    }

    // ── Tool definitions ──────────────────────────────────────────────────

    IReadOnlyList<ToolUnion> BuildTools() => new ToolUnion[]
    {
        new Tool
        {
            Name = "add_room",
            Description = "Creates a rectangular room in the Doom map. Adds 4 vertices, 4 linedefs with one-sided walls, 4 sidedefs, and 1 sector. This is the primary way to create map geometry. Rooms can be placed anywhere; use coordinates on the 64-unit grid.",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "X":              {"type":"integer","description":"Left edge X coordinate in Doom units"},
                "Y":              {"type":"integer","description":"Bottom edge Y coordinate in Doom units"},
                "Width":          {"type":"integer","description":"Width in Doom units (positive)"},
                "Height":         {"type":"integer","description":"Height in Doom units (positive)"},
                "FloorHeight":    {"type":"integer","description":"Floor height in Doom units"},
                "CeilingHeight":  {"type":"integer","description":"Ceiling height in Doom units"},
                "FloorTexture":   {"type":"string", "description":"Floor texture name, max 8 chars (e.g. FLOOR4_8)"},
                "CeilingTexture": {"type":"string", "description":"Ceiling texture name, max 8 chars (e.g. CEIL3_5)"},
                "WallTexture":    {"type":"string", "description":"Wall texture name, max 8 chars (e.g. STARTAN3)"},
                "LightLevel":     {"type":"integer","description":"Light level 0-255 (160 is normal, 255 is bright)"}
              }
            }
            """)
        },
        new Tool
        {
            Name = "add_thing",
            Description = "Places a Doom thing (monster, item, player start, key, powerup) at the specified coordinates. A Player 1 Start (type 1) is required for the map to be playable. Things should be placed inside rooms.",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "X":     {"type":"integer","description":"X coordinate"},
                "Y":     {"type":"integer","description":"Y coordinate"},
                "Type":  {"type":"integer","description":"Doom thing type number. Common: 1=Player1Start, 3004=Zombie, 9=Shotgun Guy, 65=Heavy Weapon, 3001=Imp, 3002=Demon, 3003=Baron, 3005=Cacodemon, 16=Cyberdemon, 2001=Shotgun, 2002=Chaingun, 2011=Stimpak, 2012=Medikit, 2019=Blue Armor"},
                "Angle": {"type":"integer","description":"Facing angle: 0=east, 90=north, 180=west, 270=south"}
              }
            }
            """)
        },
        new Tool
        {
            Name = "modify_sector",
            Description = "Modifies properties of an existing sector: floor/ceiling heights, textures, light level, or special type. Use get_map_state first to find sector indices.",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "SectorIndex":    {"type":"integer","description":"Index of the sector to modify (0-based)"},
                "FloorHeight":    {"type":"integer","description":"New floor height (omit to keep current)"},
                "CeilingHeight":  {"type":"integer","description":"New ceiling height (omit to keep current)"},
                "FloorTexture":   {"type":"string", "description":"New floor texture name (omit to keep current)"},
                "CeilingTexture": {"type":"string", "description":"New ceiling texture name (omit to keep current)"},
                "LightLevel":     {"type":"integer","description":"New light level 0-255 (omit to keep current)"},
                "Special":        {"type":"integer","description":"Sector special type (0=normal, 9=secret, 11=exit, etc.) (omit to keep current)"},
                "Tag":            {"type":"integer","description":"Sector tag for triggers (omit to keep current)"}
              },
              "required": ["SectorIndex"]
            }
            """)
        },
        new Tool
        {
            Name = "modify_thing",
            Description = "Changes an existing thing's position, type, or facing angle.",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "ThingIndex": {"type":"integer","description":"Index of the thing to modify (0-based)"},
                "X":          {"type":"integer","description":"New X coordinate (omit to keep current)"},
                "Y":          {"type":"integer","description":"New Y coordinate (omit to keep current)"},
                "Type":       {"type":"integer","description":"New thing type number (omit to keep current)"},
                "Angle":      {"type":"integer","description":"New angle in degrees (omit to keep current)"}
              },
              "required": ["ThingIndex"]
            }
            """)
        },
        new Tool
        {
            Name = "modify_linedef",
            Description = "Modifies an existing linedef's flags, action special, tag, or sidedef textures. Use get_map_state to find linedef indices first.",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "LinedefIndex":   {"type":"integer","description":"Index of the linedef to modify (0-based)"},
                "Impassable":     {"type":"boolean","description":"Blocks player and monsters (omit to keep current)"},
                "BlockMonsters":  {"type":"boolean","description":"Blocks monsters only (omit to keep current)"},
                "TwoSided":       {"type":"boolean","description":"Has back sidedef, allows see-through (omit to keep current)"},
                "UpperUnpegged":  {"type":"boolean","description":"Upper texture anchored to ceiling (omit to keep current)"},
                "LowerUnpegged":  {"type":"boolean","description":"Lower texture anchored to floor (omit to keep current)"},
                "Secret":         {"type":"boolean","description":"Shows as one-sided on automap (omit to keep current)"},
                "BlockSound":     {"type":"boolean","description":"Blocks sound propagation (omit to keep current)"},
                "NeverOnAutomap": {"type":"boolean","description":"Never shown on automap (omit to keep current)"},
                "AlwaysOnAutomap":{"type":"boolean","description":"Always shown on automap (omit to keep current)"},
                "Special":        {"type":"integer","description":"Action special number (omit to keep current)"},
                "Tag":            {"type":"integer","description":"Sector tag for the action (omit to keep current)"},
                "FrontMiddle":    {"type":"string", "description":"Front sidedef middle texture (omit to keep current)"},
                "FrontUpper":     {"type":"string", "description":"Front sidedef upper texture (omit to keep current)"},
                "FrontLower":     {"type":"string", "description":"Front sidedef lower texture (omit to keep current)"},
                "BackMiddle":     {"type":"string", "description":"Back sidedef middle texture (omit to keep current)"},
                "BackUpper":      {"type":"string", "description":"Back sidedef upper texture (omit to keep current)"},
                "BackLower":      {"type":"string", "description":"Back sidedef lower texture (omit to keep current)"}
              },
              "required": ["LinedefIndex"]
            }
            """)
        },
        new Tool
        {
            Name = "delete_linedef",
            Description = "Deletes a linedef and its associated sidedefs. Adjusts sidedef indices in remaining linedefs. Does not remove vertices. Use get_map_state to find linedef indices.",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "LinedefIndex": {"type":"integer","description":"Index of the linedef to delete (0-based)"}
              },
              "required": ["LinedefIndex"]
            }
            """)
        },
        new Tool
        {
            Name = "get_map_state",
            Description = "Returns the current state of the map: all linedefs (with flags and sidedef info), sectors, and things. Use this before making targeted modifications.",
            InputSchema = EmptyInputSchema()
        },
        new Tool
        {
            Name = "get_raw_map_data",
            Description = "Returns the complete raw map data as JSON: all vertices (x,y), linedefs (startVertex, endVertex, flags, special, tag, frontSidedef, backSidedef), sidedefs (xOffset, yOffset, upperTexture, lowerTexture, middleTexture, sector), sectors (floorHeight, ceilingHeight, floorTexture, ceilingTexture, lightLevel, special, tag), and things (x, y, angle, type, flags). Use this to read the full map before building with set_raw_map_data.",
            InputSchema = EmptyInputSchema()
        },
        new Tool
        {
            Name = "set_raw_map_data",
            Description = "Replaces map data directly. Provide any combination of the five arrays (vertices, linedefs, sidedefs, sectors, things) — only the arrays you include are replaced; omitted arrays are unchanged. Vertex and sidedef indices in linedefs/sidedefs must be consistent. Flags: 0x01=Impassable 0x02=BlockMonsters 0x04=TwoSided 0x08=UpperUnpegged 0x10=LowerUnpegged 0x20=Secret 0x40=BlockSound 0x80=NeverOnAutomap 0x100=AlwaysOnAutomap.",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "vertices": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "x": {"type":"integer"},
                      "y": {"type":"integer"}
                    },
                    "required": ["x","y"]
                  }
                },
                "linedefs": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "startVertex":  {"type":"integer"},
                      "endVertex":    {"type":"integer"},
                      "flags":        {"type":"integer"},
                      "special":      {"type":"integer"},
                      "tag":          {"type":"integer"},
                      "frontSidedef": {"type":"integer"},
                      "backSidedef":  {"type":"integer"}
                    },
                    "required": ["startVertex","endVertex"]
                  }
                },
                "sidedefs": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "xOffset":       {"type":"integer"},
                      "yOffset":       {"type":"integer"},
                      "upperTexture":  {"type":"string"},
                      "lowerTexture":  {"type":"string"},
                      "middleTexture": {"type":"string"},
                      "sector":        {"type":"integer"}
                    },
                    "required": ["sector"]
                  }
                },
                "sectors": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "floorHeight":    {"type":"integer"},
                      "ceilingHeight":  {"type":"integer"},
                      "floorTexture":   {"type":"string"},
                      "ceilingTexture": {"type":"string"},
                      "lightLevel":     {"type":"integer"},
                      "special":        {"type":"integer"},
                      "tag":            {"type":"integer"}
                    }
                  }
                },
                "things": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "x":     {"type":"integer"},
                      "y":     {"type":"integer"},
                      "angle": {"type":"integer"},
                      "type":  {"type":"integer"},
                      "flags": {"type":"integer"}
                    },
                    "required": ["x","y","type"]
                  }
                }
              }
            }
            """)
        },
        new Tool
        {
            Name = "clear_map",
            Description = "Removes all geometry (vertices, linedefs, sidedefs, sectors) and all things from the map, leaving it completely empty. Use this before building a new map from scratch.",
            InputSchema = EmptyInputSchema()
        },
        new Tool
        {
            Name = "add_corridor",
            Description = "Creates a corridor (narrow rectangular room) with corridor-appropriate defaults. Use small Width or Height (64–128) for one dimension to make a hallway. Place it so its ends touch adjacent rooms, then the rooms will visually connect (geometry only — no two-sided linking).",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "X":              {"type":"integer","description":"Left edge X coordinate"},
                "Y":              {"type":"integer","description":"Bottom edge Y coordinate"},
                "Width":          {"type":"integer","description":"East-west size in Doom units (64–128 for a corridor width)"},
                "Height":         {"type":"integer","description":"North-south size in Doom units (64–128 for a corridor width)"},
                "FloorHeight":    {"type":"integer","description":"Floor height (default 0)"},
                "CeilingHeight":  {"type":"integer","description":"Ceiling height (default 128)"},
                "FloorTexture":   {"type":"string", "description":"Floor texture (default FLAT1)"},
                "CeilingTexture": {"type":"string", "description":"Ceiling texture (default CEIL3_5)"},
                "WallTexture":    {"type":"string", "description":"Wall texture (default STARTAN3)"},
                "LightLevel":     {"type":"integer","description":"Light level 0-255 (default 144)"}
              },
              "required": ["X","Y","Width","Height"]
            }
            """)
        },
        new Tool
        {
            Name = "add_door",
            Description = "Creates a functional Doom door sector (Special 1 = DR Open/Wait/Close). The door starts closed (ceiling=floor). IMPORTANT: Rooms must have a GAP between them equal to the door Depth — the door sector fills that gap. Example for 'ns': room A north wall at Y=256, room B south wall at Y=272, place door at Y=256 Depth=16. Example for 'ew': room A east wall at X=256, room B west wall at X=272, place door at X=256 Width=16. Width is ALWAYS east-west (X); Depth is ALWAYS north-south (Y). 'ns'=rooms are north+south, Width=opening (e.g. 128), Depth=thickness (e.g. 16). 'ew'=rooms are east+west, Width=thickness (e.g. 16), Depth=opening (e.g. 128).",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "X":              {"type":"integer","description":"Left/bottom corner of door sector"},
                "Y":              {"type":"integer","description":"Bottom corner of door sector"},
                "Width":          {"type":"integer","description":"Size along the door's opening axis (match corridor width, e.g. 128)"},
                "Depth":          {"type":"integer","description":"Thickness of door sector perpendicular to the opening (16–32 typical)"},
                "Orientation":    {"type":"string", "description":"'ns' (door spans X, rooms north+south) or 'ew' (door spans Y, rooms east+west). Default 'ns'"},
                "FrontSectorIdx": {"type":"integer","description":"Sector index of the room on the south (ns) or west (ew) side"},
                "BackSectorIdx":  {"type":"integer","description":"Sector index of the room on the north (ns) or east (ew) side"},
                "FloorHeight":    {"type":"integer","description":"Door floor height — should match adjacent rooms (default 0)"},
                "DoorTexture":    {"type":"string", "description":"Texture on the door face, max 8 chars (default BIGDOOR2)"},
                "LightLevel":     {"type":"integer","description":"Light level 0-255 (default 160)"}
              },
              "required": ["X","Y","Width","Depth","FrontSectorIdx","BackSectorIdx"]
            }
            """)
        },
        new Tool
        {
            Name = "add_arena",
            Description = "Creates a large combat room and fills it with a grid of enemies. Optionally places a Player 1 Start in the center. Good for boss fights or intense encounters.",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "X":              {"type":"integer","description":"Left edge X coordinate"},
                "Y":              {"type":"integer","description":"Bottom edge Y coordinate"},
                "Width":          {"type":"integer","description":"Width in Doom units (256–1024 recommended)"},
                "Height":         {"type":"integer","description":"Height in Doom units (256–1024 recommended)"},
                "EnemyType":      {"type":"integer","description":"Thing type for enemies: 3001=Imp, 3002=Demon, 3003=Baron, 3004=Zombie, 3005=Cacodemon, 16=Cyberdemon (default 3001)"},
                "EnemyCount":     {"type":"integer","description":"Number of enemies to place, 1–16 (default 4)"},
                "AddPlayerStart": {"type":"boolean","description":"Add a Player 1 Start in the center (default false)"},
                "FloorHeight":    {"type":"integer","description":"Floor height (default 0)"},
                "CeilingHeight":  {"type":"integer","description":"Ceiling height (default 192)"},
                "FloorTexture":   {"type":"string", "description":"Floor texture (default FLOOR6_1)"},
                "CeilingTexture": {"type":"string", "description":"Ceiling texture (default CEIL5_1)"},
                "WallTexture":    {"type":"string", "description":"Wall texture (default STONE2)"},
                "LightLevel":     {"type":"integer","description":"Light level 0-255 (default 200)"}
              },
              "required": ["X","Y","Width","Height"]
            }
            """)
        },
        new Tool
        {
            Name = "apply_template",
            Description = "Places a pre-designed multi-room layout at the given origin. Templates: 'dungeon_start' = entrance room + 2 side rooms + north corridor; 'cross' = central hub + 4 arms; 'L_shape' = two rooms at a right angle; 'boss_room' = antechamber + large arena with Baron of Hell. All rooms are separate sectors (geometry only, no two-sided linking). Scale multiplies all dimensions.",
            InputSchema = BuildSchema("""
            {
              "type": "object",
              "properties": {
                "Template":       {"type":"string", "description":"'dungeon_start', 'cross', 'L_shape', or 'boss_room'"},
                "X":              {"type":"integer","description":"Origin X (left edge of bounding box)"},
                "Y":              {"type":"integer","description":"Origin Y (bottom edge of bounding box)"},
                "Scale":          {"type":"integer","description":"Size multiplier: 1=normal (~512 units), 2=double (default 1)"},
                "WallTexture":    {"type":"string", "description":"Wall texture for all rooms (default STARTAN3)"},
                "FloorTexture":   {"type":"string", "description":"Floor texture for all rooms (default FLOOR4_8)"},
                "CeilingTexture": {"type":"string", "description":"Ceiling texture for all rooms (default CEIL3_5)"},
                "LightLevel":     {"type":"integer","description":"Base light level 0-255 (default 160)"},
                "AddEnemies":     {"type":"boolean","description":"Populate with enemies (default true)"},
                "AddPlayerStart": {"type":"boolean","description":"Add Player 1 Start in starting room (default true)"}
              },
              "required": ["Template","X","Y"]
            }
            """)
        }
    };

    // ── System prompt ─────────────────────────────────────────────────────

    string BuildSystemPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert Doom map editor assistant with direct tools to build and modify Doom maps.");
        sb.AppendLine("When the user asks you to create, build, or place something in the map, use your tools to do it directly — don't just give instructions.");
        sb.AppendLine("When describing what you built, be brief: a one-line summary is enough.");
        sb.AppendLine();
        sb.AppendLine("Tool summary:");
        sb.AppendLine("  add_room(X,Y,Width,Height,...) — creates a rectangular sector with walls");
        sb.AppendLine("  add_corridor(X,Y,Width,Height,...) — narrow room, corridor defaults (FLAT1 floor, light=144)");
        sb.AppendLine("  add_door(X,Y,Width,Depth,Orientation,FrontSectorIdx,BackSectorIdx,...) — DR door in the GAP between two rooms; rooms must be separated by exactly Depth (ns) or Width (ew) units; Width=EW, Depth=NS always");
        sb.AppendLine("  add_arena(X,Y,Width,Height,EnemyType,EnemyCount,...) — large room pre-filled with monsters in a grid");
        sb.AppendLine("  apply_template(Template,X,Y,Scale,...) — multi-room layout: 'dungeon_start','cross','L_shape','boss_room'");
        sb.AppendLine("  add_thing(X,Y,Type,Angle) — places a monster/item/player start");
        sb.AppendLine("  modify_sector(SectorIndex,...) — changes heights/textures/light");
        sb.AppendLine("  modify_thing(ThingIndex,...) — moves or changes a thing");
        sb.AppendLine("  modify_linedef(LinedefIndex,...) — changes flags/special/tag/textures on a linedef");
        sb.AppendLine("  delete_linedef(LinedefIndex) — removes a linedef and its sidedefs");
        sb.AppendLine("  get_map_state() — shows all linedefs, sectors, and things");
        sb.AppendLine("  get_raw_map_data() — returns full raw JSON of all vertices/linedefs/sidedefs/sectors/things");
        sb.AppendLine("  set_raw_map_data({vertices,linedefs,sidedefs,sectors,things}) — directly writes raw arrays; omitted arrays unchanged");
        sb.AppendLine("  clear_map() — wipes the map clean");
        sb.AppendLine();
        sb.AppendLine("Coordinate system: X=east, Y=north. Use 64-unit grid. Player 1 Start (type 1) is required to play.");
        sb.AppendLine("Typical room sizes: 128–512 units. Ceiling height 128 is standard.");
        sb.AppendLine("Room and door placement rules:");
        sb.AppendLine("  - Rooms connected by a door MUST have a gap between them. The door sector fills that gap.");
        sb.AppendLine("  - The gap size must equal the door Depth. Example: room A north wall at Y=256, door Depth=16, room B south wall at Y=272. Place door at Y=256 with Depth=16.");
        sb.AppendLine("  - Rooms with NO connection must also have a gap of at least 64 units between their walls.");
        sb.AppendLine("  - Never place rooms so their walls touch or overlap.");
        sb.AppendLine();

        string fileName = _state.WadPath != null ? Path.GetFileName(_state.WadPath) : "(unsaved)";
        sb.AppendLine($"File: {fileName}  Map: {_state.Map.Name}");
        sb.AppendLine($"Counts — Vertices: {_state.Map.Vertices.Count}, Linedefs: {_state.Map.Linedefs.Count}, Sidedefs: {_state.Map.Sidedefs.Count}, Sectors: {_state.Map.Sectors.Count}, Things: {_state.Map.Things.Count}");

        if (_state.Selection.Linedefs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Selected linedefs ({_state.Selection.Linedefs.Count}):");
            foreach (int idx in _state.Selection.Linedefs.Take(30))
            {
                if (idx < 0 || idx >= _state.Map.Linedefs.Count) continue;
                var ld = _state.Map.Linedefs[idx];
                sb.AppendLine($"  LD{idx}: {VertexStr(ld.StartVertex)}→{VertexStr(ld.EndVertex)}  Flags=0x{ld.Flags:X4}  Special={ld.Special}  Tag={ld.Tag}");
                AppendSidedef(sb, "    Front", ld.FrontSidedef);
                AppendSidedef(sb, "    Back ", ld.BackSidedef);
            }
        }

        if (_state.Selection.Sectors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Selected sectors ({_state.Selection.Sectors.Count}):");
            foreach (int idx in _state.Selection.Sectors.Take(30))
            {
                if (idx < 0 || idx >= _state.Map.Sectors.Count) continue;
                var s = _state.Map.Sectors[idx];
                sb.AppendLine($"  Sector {idx}: Floor={s.FloorHeight}  Ceil={s.CeilingHeight}  Light={s.LightLevel}");
                sb.AppendLine($"    FloorTex={s.FloorTexture}  CeilTex={s.CeilingTexture}  Special={s.Special}  Tag={s.Tag}");
            }
        }

        return sb.ToString();
    }

    string VertexStr(int idx)
    {
        if (idx < 0 || idx >= _state.Map.Vertices.Count) return "?";
        var v = _state.Map.Vertices[idx];
        return $"({v.X},{v.Y})";
    }

    void AppendSidedef(StringBuilder sb, string label, int sidedefIdx)
    {
        if (sidedefIdx < 0 || sidedefIdx >= _state.Map.Sidedefs.Count) return;
        var sd = _state.Map.Sidedefs[sidedefIdx];
        sb.AppendLine($"{label}: Sector={sd.Sector}  Mid={sd.MiddleTexture}  Upper={sd.UpperTexture}  Lower={sd.LowerTexture}");
    }

    // ── Send / agentic loop ───────────────────────────────────────────────

    async Task SendAsync()
    {
        string userText = _input.Text.Trim();
        if (string.IsNullOrEmpty(userText) || _isStreaming) return;

        if (_client == null)
        {
            AppendColored("[Error] ANTHROPIC_API_KEY environment variable is not set.\n", ColorError);
            return;
        }

        _input.Clear();
        _isStreaming = true;
        _sendBtn.Enabled = false;

        AppendColored($"You: {userText}\n", ColorUser);

        int historyCountBefore = _history.Count;
        _history.Add(new MessageParam { Role = Role.User, Content = userText });

        try
        {
            string systemPrompt = BuildSystemPrompt();
            const int maxIterations = 8;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                var parameters = new MessageCreateParams
                {
                    Model = "claude-opus-4-7",
                    MaxTokens = 4096,
                    System = new List<TextBlockParam>
                    {
                        new() { Text = systemPrompt, CacheControl = new CacheControlEphemeral() }
                    },
                    Tools = _tools,
                    Messages = _history.ToList()
                };

                AppendColored("Claude: ", ColorAssistant);

                var assistantText  = new StringBuilder();
                var toolCalls      = new List<ToolCallAccum>();
                ToolCallAccum? activeTool = null;
                string stopReason  = "end_turn";

                await foreach (var ev in _client.Messages.CreateStreaming(parameters))
                {
                    if (ev.TryPickContentBlockStart(out var blockStart) &&
                        blockStart.ContentBlock.TryPickToolUse(out var tu))
                    {
                        activeTool = new ToolCallAccum(tu.ID, tu.Name);
                    }

                    if (ev.TryPickContentBlockDelta(out var delta))
                    {
                        if (delta.Delta.TryPickText(out var textDelta))
                        {
                            assistantText.Append(textDelta.Text);
                            AppendColored(textDelta.Text, ColorAssistant);
                        }
                        else if (delta.Delta.TryPickInputJson(out var jsonDelta) && activeTool != null)
                        {
                            activeTool.InputJson.Append(jsonDelta.PartialJson);
                        }
                    }

                    if (ev.TryPickContentBlockStop(out _) && activeTool != null)
                    {
                        toolCalls.Add(activeTool);
                        activeTool = null;
                    }

                    if (ev.TryPickDelta(out var msgDelta) && msgDelta.Delta.StopReason is { } sr)
                        stopReason = (string)sr;
                }

                AppendColored("\n", ColorAssistant);

                // Build assistant history entry (text + tool_use blocks)
                var assistantContent = new List<ContentBlockParam>();
                if (assistantText.Length > 0)
                    assistantContent.Add(new TextBlockParam { Text = assistantText.ToString() });
                foreach (var tc in toolCalls)
                    assistantContent.Add(new ToolUseBlockParam
                    {
                        ID    = tc.Id,
                        Name  = tc.Name,
                        Input = JsonDocument.Parse(
                                    tc.InputJson.Length > 0 ? tc.InputJson.ToString() : "{}")
                                .RootElement.EnumerateObject()
                                .GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.Last().Value)
                    });

                _history.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });

                if (stopReason != "tool_use" || toolCalls.Count == 0)
                    break;

                // Execute tools and collect results
                var resultContent = new List<ContentBlockParam>();
                foreach (var tc in toolCalls)
                {
                    AppendColored($"[Tool: {tc.Name}]\n", ColorTool);
                    string toolResult;
                    bool isError = false;
                    try
                    {
                        var inputEl = JsonDocument.Parse(
                            tc.InputJson.Length > 0 ? tc.InputJson.ToString() : "{}").RootElement;
                        toolResult = ExecuteTool(tc.Name, inputEl);
                        AppendColored($"[OK: {toolResult}]\n", ColorTool);
                    }
                    catch (Exception ex)
                    {
                        toolResult = $"Error: {ex.Message}";
                        isError = true;
                        AppendColored($"[Error: {ex.Message}]\n", ColorError);
                    }

                    resultContent.Add(new ToolResultBlockParam
                    {
                        ToolUseID = tc.Id,
                        Content   = toolResult,
                        IsError   = isError ? true : null
                    });
                }

                _history.Add(new MessageParam { Role = Role.User, Content = resultContent });
            }
        }
        catch (Exception ex)
        {
            while (_history.Count > historyCountBefore)
                _history.RemoveAt(_history.Count - 1);
            AppendColored($"\n[Error] {ex.Message}\n", ColorError);
        }
        finally
        {
            _isStreaming = false;
            _sendBtn.Enabled = true;
        }
    }

    sealed class ToolCallAccum(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public StringBuilder InputJson { get; } = new();
    }

    // ── Tool execution ────────────────────────────────────────────────────

    string ExecuteTool(string name, JsonElement input) => name switch
    {
        "add_room"          => DoAddRoom(input),
        "add_thing"         => DoAddThing(input),
        "modify_sector"     => DoModifySector(input),
        "modify_thing"      => DoModifyThing(input),
        "modify_linedef"    => DoModifyLinedef(input),
        "delete_linedef"    => DoDeleteLinedef(input),
        "get_map_state"     => DoGetMapState(),
        "get_raw_map_data"  => DoGetRawMapData(),
        "set_raw_map_data"  => DoSetRawMapData(input),
        "clear_map"         => DoClearMap(),
        "add_corridor"      => DoAddCorridor(input),
        "add_door"          => DoAddDoor(input),
        "add_arena"         => DoAddArena(input),
        "apply_template"    => DoApplyTemplate(input),
        _                   => throw new ArgumentException($"Unknown tool: {name}")
    };

    string DoAddRoom(JsonElement input)
    {
        int x        = GetInt(input, "X");
        int y        = GetInt(input, "Y");
        int width    = GetInt(input, "Width", 256);
        int height   = GetInt(input, "Height", 256);
        int floorH   = GetInt(input, "FloorHeight", 0);
        int ceilH    = GetInt(input, "CeilingHeight", 128);
        string flTex = Truncate8(GetStr(input, "FloorTexture", "FLOOR4_8"));
        string ceTex = Truncate8(GetStr(input, "CeilingTexture", "CEIL3_5"));
        string wTex  = Truncate8(GetStr(input, "WallTexture", "STARTAN3"));
        int light    = GetInt(input, "LightLevel", 160);
        return AddRoomCore(x, y, width, height, floorH, ceilH, flTex, ceTex, wTex, light);
    }

    string AddRoomCore(int x, int y, int width, int height, int floorH, int ceilH,
                       string flTex, string ceTex, string wTex, int light)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width and Height must be > 0");

        var map   = _state.Map;
        int sBase = map.Sectors.Count;

        // Reuse existing vertices at shared corners rather than always creating new ones
        int v0 = GetOrCreateVertex(x,         y);
        int v1 = GetOrCreateVertex(x,         y + height);
        int v2 = GetOrCreateVertex(x + width, y + height);
        int v3 = GetOrCreateVertex(x + width, y);

        map.Sectors.Add(new DoomSector
        {
            FloorHeight    = (short)floorH,
            CeilingHeight  = (short)ceilH,
            FloorTexture   = flTex,
            CeilingTexture = ceTex,
            LightLevel     = (short)Math.Clamp(light, 0, 255)
        });

        // Create each wall only if that exact linedef doesn't already exist
        int skipped = 0;
        int[] sv = { v0, v1, v2, v3 };
        int[] ev = { v1, v2, v3, v0 };
        for (int i = 0; i < 4; i++)
        {
            if (LinedefExists(sv[i], ev[i]))
            {
                skipped++;
                continue;
            }
            int sdIdx = map.Sidedefs.Count;
            map.Sidedefs.Add(new DoomSidedef { MiddleTexture = wTex, Sector = sBase });
            map.Linedefs.Add(new DoomLinedef
            {
                StartVertex  = sv[i],
                EndVertex    = ev[i],
                Flags        = 1,
                FrontSidedef = sdIdx,
                BackSidedef  = -1
            });
        }

        _state.NotifyChanged();
        string result = $"Sector {sBase} created at ({x},{y}) size {width}x{height}";
        if (skipped > 0)
            result += $" ({skipped} wall(s) skipped — already existed at that position)";
        return result;
    }

    // Returns true if a linedef already connects v1↔v2 in either direction.
    bool LinedefExists(int v1, int v2) =>
        _state.Map.Linedefs.Any(ld =>
            (ld.StartVertex == v1 && ld.EndVertex == v2) ||
            (ld.StartVertex == v2 && ld.EndVertex == v1));

    void AddThingRaw(int x, int y, int type, int angle) =>
        _state.Map.Things.Add(new DoomThing
        {
            X = (short)x, Y = (short)y,
            Type = (short)type, Angle = (short)angle,
            Flags = 7
        });

    string DoAddThing(JsonElement input)
    {
        int x     = GetInt(input, "X");
        int y     = GetInt(input, "Y");
        int type  = GetInt(input, "Type", 1);
        int angle = GetInt(input, "Angle", 90);

        _state.Map.Things.Add(new DoomThing
        {
            X     = (short)x,
            Y     = (short)y,
            Type  = (short)type,
            Angle = (short)angle,
            Flags = 7
        });

        _state.NotifyChanged();
        string typeName = DoomThing.TypeNames.TryGetValue(type, out var n) ? n : $"Type #{type}";
        return $"Placed {typeName} (idx {_state.Map.Things.Count - 1}) at ({x},{y})";
    }

    string DoModifySector(JsonElement input)
    {
        int idx = GetInt(input, "SectorIndex");
        var map = _state.Map;
        if (idx < 0 || idx >= map.Sectors.Count)
            throw new ArgumentException($"Sector {idx} not found (map has {map.Sectors.Count})");

        var s       = map.Sectors[idx];
        var changes = new List<string>();

        if (TryGetInt(input, "FloorHeight",    out int fh)) { s.FloorHeight    = (short)fh;                       changes.Add($"floor={fh}"); }
        if (TryGetInt(input, "CeilingHeight",  out int ch)) { s.CeilingHeight  = (short)ch;                       changes.Add($"ceil={ch}"); }
        if (TryGetStr(input, "FloorTexture",   out string ft)) { s.FloorTexture   = Truncate8(ft);                changes.Add($"flTex={ft}"); }
        if (TryGetStr(input, "CeilingTexture", out string ct)) { s.CeilingTexture = Truncate8(ct);                changes.Add($"ceTex={ct}"); }
        if (TryGetInt(input, "LightLevel",     out int ll)) { s.LightLevel     = (short)Math.Clamp(ll, 0, 255);   changes.Add($"light={ll}"); }
        if (TryGetInt(input, "Special",        out int sp)) { s.Special        = (short)sp;                       changes.Add($"special={sp}"); }
        if (TryGetInt(input, "Tag",            out int tg)) { s.Tag            = (short)tg;                       changes.Add($"tag={tg}"); }

        _state.NotifyChanged();
        return changes.Count > 0
            ? $"Sector {idx} updated: {string.Join(", ", changes)}"
            : $"Sector {idx}: nothing changed";
    }

    string DoModifyThing(JsonElement input)
    {
        int idx = GetInt(input, "ThingIndex");
        var map = _state.Map;
        if (idx < 0 || idx >= map.Things.Count)
            throw new ArgumentException($"Thing {idx} not found (map has {map.Things.Count})");

        var t       = map.Things[idx];
        var changes = new List<string>();

        if (TryGetInt(input, "X",     out int x))  { t.X     = (short)x;  changes.Add($"x={x}"); }
        if (TryGetInt(input, "Y",     out int y))  { t.Y     = (short)y;  changes.Add($"y={y}"); }
        if (TryGetInt(input, "Type",  out int tp)) { t.Type  = (short)tp; changes.Add($"type={tp}"); }
        if (TryGetInt(input, "Angle", out int ang)){ t.Angle = (short)ang; changes.Add($"angle={ang}"); }

        _state.NotifyChanged();
        return changes.Count > 0
            ? $"Thing {idx} ({t.TypeName}): {string.Join(", ", changes)}"
            : $"Thing {idx}: nothing changed";
    }

    string DoModifyLinedef(JsonElement input)
    {
        int idx = GetInt(input, "LinedefIndex");
        var map = _state.Map;
        if (idx < 0 || idx >= map.Linedefs.Count)
            throw new ArgumentException($"Linedef {idx} not found (map has {map.Linedefs.Count})");

        var ld      = map.Linedefs[idx];
        var changes = new List<string>();

        void SetFlag(string key, short bit)
        {
            if (!TryGetBool(input, key, out bool v)) return;
            if (v) ld.Flags |= bit; else ld.Flags &= (short)~bit;
            changes.Add($"{key}={v}");
        }

        SetFlag("Impassable",      0x0001);
        SetFlag("BlockMonsters",   0x0002);
        SetFlag("TwoSided",        0x0004);
        SetFlag("UpperUnpegged",   0x0008);
        SetFlag("LowerUnpegged",   0x0010);
        SetFlag("Secret",          0x0020);
        SetFlag("BlockSound",      0x0040);
        SetFlag("NeverOnAutomap",  0x0080);
        SetFlag("AlwaysOnAutomap", 0x0100);

        if (TryGetInt(input, "Special", out int sp)) { ld.Special = (short)sp; changes.Add($"special={sp}"); }
        if (TryGetInt(input, "Tag",     out int tg)) { ld.Tag     = (short)tg; changes.Add($"tag={tg}"); }

        DoomSidedef? front = ld.FrontSidedef >= 0 && ld.FrontSidedef < map.Sidedefs.Count ? map.Sidedefs[ld.FrontSidedef] : null;
        DoomSidedef? back  = ld.BackSidedef  >= 0 && ld.BackSidedef  < map.Sidedefs.Count ? map.Sidedefs[ld.BackSidedef]  : null;

        if (front != null)
        {
            if (TryGetStr(input, "FrontMiddle", out string fm)) { front.MiddleTexture = Truncate8(fm); changes.Add($"frontMid={fm}"); }
            if (TryGetStr(input, "FrontUpper",  out string fu)) { front.UpperTexture  = Truncate8(fu); changes.Add($"frontUp={fu}"); }
            if (TryGetStr(input, "FrontLower",  out string fl)) { front.LowerTexture  = Truncate8(fl); changes.Add($"frontLo={fl}"); }
        }
        if (back != null)
        {
            if (TryGetStr(input, "BackMiddle", out string bm)) { back.MiddleTexture = Truncate8(bm); changes.Add($"backMid={bm}"); }
            if (TryGetStr(input, "BackUpper",  out string bu)) { back.UpperTexture  = Truncate8(bu); changes.Add($"backUp={bu}"); }
            if (TryGetStr(input, "BackLower",  out string bl)) { back.LowerTexture  = Truncate8(bl); changes.Add($"backLo={bl}"); }
        }

        _state.NotifyChanged();
        return changes.Count > 0
            ? $"Linedef {idx} updated: {string.Join(", ", changes)}"
            : $"Linedef {idx}: nothing changed";
    }

    string DoDeleteLinedef(JsonElement input)
    {
        int idx = GetInt(input, "LinedefIndex");
        var map = _state.Map;
        if (idx < 0 || idx >= map.Linedefs.Count)
            throw new ArgumentException($"Linedef {idx} not found (map has {map.Linedefs.Count})");

        var ld = map.Linedefs[idx];

        // Collect sidedef indices to remove (descending so removals don't shift each other)
        var sdToRemove = new List<int>();
        if (ld.FrontSidedef >= 0 && ld.FrontSidedef < map.Sidedefs.Count) sdToRemove.Add(ld.FrontSidedef);
        if (ld.BackSidedef  >= 0 && ld.BackSidedef  < map.Sidedefs.Count && ld.BackSidedef != ld.FrontSidedef) sdToRemove.Add(ld.BackSidedef);
        sdToRemove.Sort((a, b) => b.CompareTo(a)); // descending

        map.Linedefs.RemoveAt(idx);

        foreach (int sdIdx in sdToRemove)
        {
            map.Sidedefs.RemoveAt(sdIdx);
            // Adjust sidedef references in all remaining linedefs
            foreach (var l in map.Linedefs)
            {
                if (l.FrontSidedef > sdIdx) l.FrontSidedef--;
                if (l.BackSidedef  > sdIdx) l.BackSidedef--;
            }
        }

        _state.Selection.Linedefs.Remove(idx);
        _state.NotifyChanged();
        return $"Linedef {idx} deleted (removed {sdToRemove.Count} sidedef(s))";
    }

    string DoGetRawMapData()
    {
        var map = _state.Map;
        using var stream = new System.IO.MemoryStream();
        using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        w.WriteStartObject();

        w.WriteStartArray("vertices");
        foreach (var v in map.Vertices)
        { w.WriteStartObject(); w.WriteNumber("x", v.X); w.WriteNumber("y", v.Y); w.WriteEndObject(); }
        w.WriteEndArray();

        w.WriteStartArray("linedefs");
        foreach (var l in map.Linedefs)
        {
            w.WriteStartObject();
            w.WriteNumber("startVertex",  l.StartVertex);
            w.WriteNumber("endVertex",    l.EndVertex);
            w.WriteNumber("flags",        l.Flags);
            w.WriteNumber("special",      l.Special);
            w.WriteNumber("tag",          l.Tag);
            w.WriteNumber("frontSidedef", l.FrontSidedef);
            w.WriteNumber("backSidedef",  l.BackSidedef);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("sidedefs");
        foreach (var s in map.Sidedefs)
        {
            w.WriteStartObject();
            w.WriteNumber("xOffset",       s.XOffset);
            w.WriteNumber("yOffset",       s.YOffset);
            w.WriteString("upperTexture",  s.UpperTexture);
            w.WriteString("lowerTexture",  s.LowerTexture);
            w.WriteString("middleTexture", s.MiddleTexture);
            w.WriteNumber("sector",        s.Sector);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("sectors");
        foreach (var s in map.Sectors)
        {
            w.WriteStartObject();
            w.WriteNumber("floorHeight",    s.FloorHeight);
            w.WriteNumber("ceilingHeight",  s.CeilingHeight);
            w.WriteString("floorTexture",   s.FloorTexture);
            w.WriteString("ceilingTexture", s.CeilingTexture);
            w.WriteNumber("lightLevel",     s.LightLevel);
            w.WriteNumber("special",        s.Special);
            w.WriteNumber("tag",            s.Tag);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("things");
        foreach (var t in map.Things)
        {
            w.WriteStartObject();
            w.WriteNumber("x",     t.X);
            w.WriteNumber("y",     t.Y);
            w.WriteNumber("angle", t.Angle);
            w.WriteNumber("type",  t.Type);
            w.WriteNumber("flags", t.Flags);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteEndObject();
        w.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    string DoSetRawMapData(JsonElement input)
    {
        var map     = _state.Map;
        var updated = new List<string>();

        if (input.TryGetProperty("vertices", out var vArr) && vArr.ValueKind == JsonValueKind.Array)
        {
            map.Vertices.Clear();
            foreach (var v in vArr.EnumerateArray())
                map.Vertices.Add(new DoomVertex(
                    (short)ElInt(v, "x"), (short)ElInt(v, "y")));
            updated.Add($"{map.Vertices.Count} vertices");
        }

        if (input.TryGetProperty("sectors", out var secArr) && secArr.ValueKind == JsonValueKind.Array)
        {
            map.Sectors.Clear();
            foreach (var s in secArr.EnumerateArray())
                map.Sectors.Add(new DoomSector
                {
                    FloorHeight    = (short)ElInt(s, "floorHeight",   0),
                    CeilingHeight  = (short)ElInt(s, "ceilingHeight", 128),
                    FloorTexture   = Truncate8(ElStr(s, "floorTexture",   "FLOOR4_8")),
                    CeilingTexture = Truncate8(ElStr(s, "ceilingTexture", "CEIL3_5")),
                    LightLevel     = (short)Math.Clamp(ElInt(s, "lightLevel", 160), 0, 255),
                    Special        = (short)ElInt(s, "special", 0),
                    Tag            = (short)ElInt(s, "tag",     0),
                });
            updated.Add($"{map.Sectors.Count} sectors");
        }

        if (input.TryGetProperty("sidedefs", out var sdArr) && sdArr.ValueKind == JsonValueKind.Array)
        {
            map.Sidedefs.Clear();
            foreach (var s in sdArr.EnumerateArray())
                map.Sidedefs.Add(new DoomSidedef
                {
                    XOffset       = (short)ElInt(s, "xOffset", 0),
                    YOffset       = (short)ElInt(s, "yOffset", 0),
                    UpperTexture  = Truncate8(ElStr(s, "upperTexture",  "-")),
                    LowerTexture  = Truncate8(ElStr(s, "lowerTexture",  "-")),
                    MiddleTexture = Truncate8(ElStr(s, "middleTexture", "-")),
                    Sector        = ElInt(s, "sector", 0),
                });
            updated.Add($"{map.Sidedefs.Count} sidedefs");
        }

        if (input.TryGetProperty("linedefs", out var ldArr) && ldArr.ValueKind == JsonValueKind.Array)
        {
            map.Linedefs.Clear();
            foreach (var l in ldArr.EnumerateArray())
                map.Linedefs.Add(new DoomLinedef
                {
                    StartVertex  = ElInt(l, "startVertex",  0),
                    EndVertex    = ElInt(l, "endVertex",    0),
                    Flags        = (short)ElInt(l, "flags",   1),
                    Special      = (short)ElInt(l, "special", 0),
                    Tag          = (short)ElInt(l, "tag",     0),
                    FrontSidedef = ElInt(l, "frontSidedef", -1),
                    BackSidedef  = ElInt(l, "backSidedef",  -1),
                });
            updated.Add($"{map.Linedefs.Count} linedefs");
        }

        if (input.TryGetProperty("things", out var thArr) && thArr.ValueKind == JsonValueKind.Array)
        {
            map.Things.Clear();
            foreach (var t in thArr.EnumerateArray())
                map.Things.Add(new DoomThing
                {
                    X     = (short)ElInt(t, "x",     0),
                    Y     = (short)ElInt(t, "y",     0),
                    Angle = (short)ElInt(t, "angle", 90),
                    Type  = (short)ElInt(t, "type",  1),
                    Flags = (short)ElInt(t, "flags", 7),
                });
            updated.Add($"{map.Things.Count} things");
        }

        if (updated.Count == 0) return "No arrays provided — map unchanged";
        _state.Selection.Clear();
        _state.NotifyChanged();
        return $"Map updated: {string.Join(", ", updated)}";
    }

    // Read a property from a JsonElement by exact name, return default if missing/wrong type.
    static int ElInt(JsonElement el, string key, int def = 0)
    {
        if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int v)) return v;
        return def;
    }

    static string ElStr(JsonElement el, string key, string def = "")
    {
        if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String) return p.GetString() ?? def;
        return def;
    }

    string DoGetMapState()
    {
        var map = _state.Map;
        var sb  = new StringBuilder();
        sb.AppendLine($"Map: {map.Name}  File: {(_state.WadPath != null ? Path.GetFileName(_state.WadPath) : "(unsaved)")}");
        sb.AppendLine($"Vertices={map.Vertices.Count}  Linedefs={map.Linedefs.Count}  Sidedefs={map.Sidedefs.Count}  Sectors={map.Sectors.Count}  Things={map.Things.Count}");

        if (map.Linedefs.Count > 0)
        {
            sb.AppendLine("Linedefs:");
            for (int i = 0; i < map.Linedefs.Count; i++)
            {
                var ld = map.Linedefs[i];
                string v1 = VertexStr(ld.StartVertex), v2 = VertexStr(ld.EndVertex);
                string flags = DescribeFlags(ld.Flags);
                sb.Append($"  [{i}] {v1}→{v2}  Flags=0x{ld.Flags:X4}({flags})  Special={ld.Special}  Tag={ld.Tag}");
                string frontSec = SidedefSector(map, ld.FrontSidedef);
                string backSec  = SidedefSector(map, ld.BackSidedef);
                sb.AppendLine($"  FrontSector={frontSec}  BackSector={backSec}");
            }
        }

        if (map.Sectors.Count > 0)
        {
            sb.AppendLine("Sectors:");
            for (int i = 0; i < map.Sectors.Count; i++)
            {
                var s = map.Sectors[i];
                sb.AppendLine($"  [{i}] floor={s.FloorHeight} ceil={s.CeilingHeight} light={s.LightLevel} flTex={s.FloorTexture} ceTex={s.CeilingTexture} special={s.Special} tag={s.Tag}");
            }
        }

        if (map.Things.Count > 0)
        {
            sb.AppendLine("Things:");
            for (int i = 0; i < map.Things.Count; i++)
            {
                var t = map.Things[i];
                sb.AppendLine($"  [{i}] {t.TypeName} (type {t.Type}) at ({t.X},{t.Y}) angle={t.Angle}");
            }
        }

        return sb.ToString();
    }

    static string DescribeFlags(short flags)
    {
        var parts = new List<string>();
        if ((flags & 0x0001) != 0) parts.Add("Impassable");
        if ((flags & 0x0002) != 0) parts.Add("BlockMon");
        if ((flags & 0x0004) != 0) parts.Add("TwoSided");
        if ((flags & 0x0008) != 0) parts.Add("UpperUnpeg");
        if ((flags & 0x0010) != 0) parts.Add("LowerUnpeg");
        if ((flags & 0x0020) != 0) parts.Add("Secret");
        if ((flags & 0x0040) != 0) parts.Add("BlockSound");
        if ((flags & 0x0080) != 0) parts.Add("NoAutomap");
        if ((flags & 0x0100) != 0) parts.Add("AlwaysAutomap");
        return parts.Count > 0 ? string.Join("|", parts) : "none";
    }

    static string SidedefSector(DoomMap map, int sdIdx)
    {
        if (sdIdx < 0 || sdIdx >= map.Sidedefs.Count) return "none";
        return map.Sidedefs[sdIdx].Sector.ToString();
    }

    string DoAddCorridor(JsonElement input)
    {
        int x       = GetInt(input, "X");
        int y       = GetInt(input, "Y");
        int width   = GetInt(input, "Width", 128);
        int height  = GetInt(input, "Height", 128);
        int floorH  = GetInt(input, "FloorHeight", 0);
        int ceilH   = GetInt(input, "CeilingHeight", 128);
        string flTex = Truncate8(GetStr(input, "FloorTexture", "FLAT1"));
        string ceTex = Truncate8(GetStr(input, "CeilingTexture", "CEIL3_5"));
        string wTex  = Truncate8(GetStr(input, "WallTexture", "STARTAN3"));
        int light   = GetInt(input, "LightLevel", 144);
        return AddRoomCore(x, y, width, height, floorH, ceilH, flTex, ceTex, wTex, light);
    }

    // ── Door wall-splitting helpers ───────────────────────────────────────

    int GetOrCreateVertex(int x, int y)
    {
        var map = _state.Map;
        for (int i = 0; i < map.Vertices.Count; i++)
            if (map.Vertices[i].X == x && map.Vertices[i].Y == y) return i;
        map.Vertices.Add(new DoomVertex((short)x, (short)y));
        return map.Vertices.Count - 1;
    }

    void RemoveLinedefAt(int ldIdx)
    {
        var map = _state.Map;
        var ld = map.Linedefs[ldIdx];
        var sdToRemove = new List<int>();
        if (ld.FrontSidedef >= 0 && ld.FrontSidedef < map.Sidedefs.Count)
            sdToRemove.Add(ld.FrontSidedef);
        if (ld.BackSidedef >= 0 && ld.BackSidedef < map.Sidedefs.Count && ld.BackSidedef != ld.FrontSidedef)
            sdToRemove.Add(ld.BackSidedef);
        sdToRemove.Sort((a, b) => b.CompareTo(a));
        map.Linedefs.RemoveAt(ldIdx);
        foreach (int sdIdx in sdToRemove)
        {
            map.Sidedefs.RemoveAt(sdIdx);
            foreach (var l in map.Linedefs)
            {
                if (l.FrontSidedef > sdIdx) l.FrontSidedef--;
                if (l.BackSidedef  > sdIdx) l.BackSidedef--;
            }
        }
    }

    int AddSideDef(int sector, string upper, string middle, string lower)
    {
        _state.Map.Sidedefs.Add(new DoomSidedef { Sector = sector, UpperTexture = upper, MiddleTexture = middle, LowerTexture = lower });
        return _state.Map.Sidedefs.Count - 1;
    }

    void AddLD1(int sv, int ev, int frontSd, short flags = 1) =>
        _state.Map.Linedefs.Add(new DoomLinedef { StartVertex = sv, EndVertex = ev, Flags = flags, FrontSidedef = frontSd, BackSidedef = -1 });

    void AddLD2Door(int sv, int ev, int frontSd, int backSd) =>
        _state.Map.Linedefs.Add(new DoomLinedef { StartVertex = sv, EndVertex = ev, Flags = 0x04, Special = 1, FrontSidedef = frontSd, BackSidedef = backSd });

    // Find one-sided linedefs at Y=wallY belonging to adjSector that overlap [spanX1,spanX2].
    // Split each: keep non-overlapping pieces as solid walls, convert overlapping portion to
    // a two-sided DR door face connecting adjSector ↔ doorSector.
    // Returns true if at least one wall was found and replaced.
    bool SplitHorizontalWall(int wallY, int spanX1, int spanX2, int adjSector, int doorSector, string doorTex)
    {
        var map = _state.Map;
        var cands = new List<(int idx, int ldX1, int ldX2, bool rightward, string wallTex)>();
        for (int i = 0; i < map.Linedefs.Count; i++)
        {
            var ld = map.Linedefs[i];
            if (ld.BackSidedef >= 0 || ld.FrontSidedef < 0 || ld.FrontSidedef >= map.Sidedefs.Count) continue;
            if (map.Sidedefs[ld.FrontSidedef].Sector != adjSector) continue;
            var sv = map.Vertices[ld.StartVertex];
            var ev = map.Vertices[ld.EndVertex];
            if (sv.Y != wallY || ev.Y != wallY) continue;
            int lx1 = Math.Min(sv.X, ev.X), lx2 = Math.Max(sv.X, ev.X);
            if (Math.Max(lx1, spanX1) >= Math.Min(lx2, spanX2)) continue;
            cands.Add((i, lx1, lx2, sv.X < ev.X, map.Sidedefs[ld.FrontSidedef].MiddleTexture));
        }
        if (cands.Count == 0) return false;

        // Process highest index first so removals don't shift lower indices
        cands.Sort((a, b) => b.idx.CompareTo(a.idx));
        foreach (var (idx, ldX1, ldX2, rightward, wallTex) in cands)
        {
            int ovX1 = Math.Max(ldX1, spanX1), ovX2 = Math.Min(ldX2, spanX2);
            RemoveLinedefAt(idx);
            // Vertex indices are stable (RemoveLinedefAt only touches linedefs+sidedefs)
            int vX1 = GetOrCreateVertex(ldX1, wallY), vOv1 = GetOrCreateVertex(ovX1, wallY),
                vOv2 = GetOrCreateVertex(ovX2, wallY), vX2 = GetOrCreateVertex(ldX2, wallY);
            if (rightward)
            {
                if (ldX1 < ovX1) AddLD1(vX1, vOv1, AddSideDef(adjSector, "-", wallTex, "-"));
                AddLD2Door(vOv1, vOv2, AddSideDef(adjSector, doorTex, "-", "-"), AddSideDef(doorSector, "-", "-", "-"));
                if (ovX2 < ldX2) AddLD1(vOv2, vX2, AddSideDef(adjSector, "-", wallTex, "-"));
            }
            else
            {
                if (ldX2 > ovX2) AddLD1(vX2, vOv2, AddSideDef(adjSector, "-", wallTex, "-"));
                AddLD2Door(vOv2, vOv1, AddSideDef(adjSector, doorTex, "-", "-"), AddSideDef(doorSector, "-", "-", "-"));
                if (ldX1 < ovX1) AddLD1(vOv1, vX1, AddSideDef(adjSector, "-", wallTex, "-"));
            }
        }
        return true;
    }

    // Same as above but for vertical walls at X=wallX spanning [spanY1,spanY2].
    bool SplitVerticalWall(int wallX, int spanY1, int spanY2, int adjSector, int doorSector, string doorTex)
    {
        var map = _state.Map;
        var cands = new List<(int idx, int ldY1, int ldY2, bool upward, string wallTex)>();
        for (int i = 0; i < map.Linedefs.Count; i++)
        {
            var ld = map.Linedefs[i];
            if (ld.BackSidedef >= 0 || ld.FrontSidedef < 0 || ld.FrontSidedef >= map.Sidedefs.Count) continue;
            if (map.Sidedefs[ld.FrontSidedef].Sector != adjSector) continue;
            var sv = map.Vertices[ld.StartVertex];
            var ev = map.Vertices[ld.EndVertex];
            if (sv.X != wallX || ev.X != wallX) continue;
            int ly1 = Math.Min(sv.Y, ev.Y), ly2 = Math.Max(sv.Y, ev.Y);
            if (Math.Max(ly1, spanY1) >= Math.Min(ly2, spanY2)) continue;
            cands.Add((i, ly1, ly2, sv.Y < ev.Y, map.Sidedefs[ld.FrontSidedef].MiddleTexture));
        }
        if (cands.Count == 0) return false;

        cands.Sort((a, b) => b.idx.CompareTo(a.idx));
        foreach (var (idx, ldY1, ldY2, upward, wallTex) in cands)
        {
            int ovY1 = Math.Max(ldY1, spanY1), ovY2 = Math.Min(ldY2, spanY2);
            RemoveLinedefAt(idx);
            int vY1 = GetOrCreateVertex(wallX, ldY1), vOv1 = GetOrCreateVertex(wallX, ovY1),
                vOv2 = GetOrCreateVertex(wallX, ovY2), vY2 = GetOrCreateVertex(wallX, ldY2);
            if (upward)
            {
                if (ldY1 < ovY1) AddLD1(vY1, vOv1, AddSideDef(adjSector, "-", wallTex, "-"));
                AddLD2Door(vOv1, vOv2, AddSideDef(adjSector, doorTex, "-", "-"), AddSideDef(doorSector, "-", "-", "-"));
                if (ovY2 < ldY2) AddLD1(vOv2, vY2, AddSideDef(adjSector, "-", wallTex, "-"));
            }
            else
            {
                if (ldY2 > ovY2) AddLD1(vY2, vOv2, AddSideDef(adjSector, "-", wallTex, "-"));
                AddLD2Door(vOv2, vOv1, AddSideDef(adjSector, doorTex, "-", "-"), AddSideDef(doorSector, "-", "-", "-"));
                if (ldY1 < ovY1) AddLD1(vOv1, vY1, AddSideDef(adjSector, "-", wallTex, "-"));
            }
        }
        return true;
    }

    // ── add_door ──────────────────────────────────────────────────────────

    string DoAddDoor(JsonElement input)
    {
        int x              = GetInt(input, "X");
        int y              = GetInt(input, "Y");
        int width          = GetInt(input, "Width", 128);  // EW (X) size always
        int depth          = GetInt(input, "Depth", 16);   // NS (Y) size always
        string orientation = GetStr(input, "Orientation", "ns");
        int frontSector    = GetInt(input, "FrontSectorIdx", -1);
        int backSector     = GetInt(input, "BackSectorIdx",  -1);
        int floorH         = GetInt(input, "FloorHeight", 0);
        string doorTex     = Truncate8(GetStr(input, "DoorTexture", "BIGDOOR2"));
        int light          = GetInt(input, "LightLevel", 160);

        var map = _state.Map;
        if (frontSector < 0 || frontSector >= map.Sectors.Count)
            throw new ArgumentException($"FrontSectorIdx {frontSector} invalid (map has {map.Sectors.Count} sectors)");
        if (backSector < 0 || backSector >= map.Sectors.Count)
            throw new ArgumentException($"BackSectorIdx {backSector} invalid (map has {map.Sectors.Count} sectors)");
        if (width <= 0 || depth <= 0)
            throw new ArgumentException("Width and Depth must be > 0");

        int sBase = map.Sectors.Count;
        map.Sectors.Add(new DoomSector
        {
            FloorHeight    = (short)floorH,
            CeilingHeight  = (short)floorH, // closed door: ceiling starts at floor
            FloorTexture   = "CEIL5_1",
            CeilingTexture = "CEIL5_1",
            LightLevel     = (short)Math.Clamp(light, 0, 255)
        });

        // Corner vertices — Width=EW(X), Depth=NS(Y) always
        int vBL = GetOrCreateVertex(x,         y);
        int vBR = GetOrCreateVertex(x + width, y);
        int vTR = GetOrCreateVertex(x + width, y + depth);
        int vTL = GetOrCreateVertex(x,         y + depth);

        string splitMsg = "";

        if (orientation == "ew")
        {
            // Rooms west (frontSector) and east (backSector).
            // Width = EW door thickness (small); Depth = NS opening height (large).

            // West face: split room's east wall at X=x, or create fresh
            bool westSplit = SplitVerticalWall(x, y, y + depth, frontSector, sBase, doorTex);
            if (!westSplit)
            {
                // V3→V0 going south: right=west=frontSector
                AddLD2Door(vTL, vBL, AddSideDef(frontSector, doorTex, "-", "-"), AddSideDef(sBase, "-", "-", "-"));
                splitMsg += " (west face: fresh)";
            }
            else splitMsg += " (west face: wall split)";

            // East face: split room's west wall at X=x+width, or create fresh
            bool eastSplit = SplitVerticalWall(x + width, y, y + depth, backSector, sBase, doorTex);
            if (!eastSplit)
            {
                // V1→V2 going north: right=east=backSector
                AddLD2Door(vBR, vTR, AddSideDef(backSector, doorTex, "-", "-"), AddSideDef(sBase, "-", "-", "-"));
                splitMsg += " (east face: fresh)";
            }
            else splitMsg += " (east face: wall split)";

            // North side wall: V3→V2 east, right=south=door interior
            AddLD1(vTL, vTR, AddSideDef(sBase, "-", "DOORTRAK", "-"), 0x11);
            // South side wall: V1→V0 west, right=north=door interior
            AddLD1(vBR, vBL, AddSideDef(sBase, "-", "DOORTRAK", "-"), 0x11);
        }
        else // "ns": rooms south (frontSector) and north (backSector)
        {
            // Width = EW opening width (large); Depth = NS door thickness (small).

            // South face: split room's north wall at Y=y, or create fresh
            bool southSplit = SplitHorizontalWall(y, x, x + width, frontSector, sBase, doorTex);
            if (!southSplit)
            {
                // V0→V1 going east: right=south=frontSector
                AddLD2Door(vBL, vBR, AddSideDef(frontSector, doorTex, "-", "-"), AddSideDef(sBase, "-", "-", "-"));
                splitMsg += " (south face: fresh)";
            }
            else splitMsg += " (south face: wall split)";

            // North face: split room's south wall at Y=y+depth, or create fresh
            bool northSplit = SplitHorizontalWall(y + depth, x, x + width, backSector, sBase, doorTex);
            if (!northSplit)
            {
                // V2→V3 going west: right=north=backSector
                AddLD2Door(vTR, vTL, AddSideDef(backSector, doorTex, "-", "-"), AddSideDef(sBase, "-", "-", "-"));
                splitMsg += " (north face: fresh)";
            }
            else splitMsg += " (north face: wall split)";

            // East side wall: V2→V1 south, right=west=door interior
            AddLD1(vTR, vBR, AddSideDef(sBase, "-", "DOORTRAK", "-"), 0x11);
            // West side wall: V0→V3 north, right=east=door interior
            AddLD1(vBL, vTL, AddSideDef(sBase, "-", "DOORTRAK", "-"), 0x11);
        }

        _state.NotifyChanged();
        return $"Door sector {sBase} at ({x},{y}) W={width} D={depth} orient={orientation}, connects sectors {frontSector}↔{backSector}.{splitMsg}";
    }

    string DoAddArena(JsonElement input)
    {
        int x          = GetInt(input, "X");
        int y          = GetInt(input, "Y");
        int width      = GetInt(input, "Width", 512);
        int height     = GetInt(input, "Height", 512);
        int enemyType  = GetInt(input, "EnemyType", 3001);
        int enemyCount = Math.Clamp(GetInt(input, "EnemyCount", 4), 1, 16);
        bool addPlayer = TryGetBool(input, "AddPlayerStart", out bool ap) && ap;
        int floorH     = GetInt(input, "FloorHeight", 0);
        int ceilH      = GetInt(input, "CeilingHeight", 192);
        string flTex   = Truncate8(GetStr(input, "FloorTexture", "FLOOR6_1"));
        string ceTex   = Truncate8(GetStr(input, "CeilingTexture", "CEIL5_1"));
        string wTex    = Truncate8(GetStr(input, "WallTexture", "STONE2"));
        int light      = GetInt(input, "LightLevel", 200);

        string roomResult = AddRoomCore(x, y, width, height, floorH, ceilH, flTex, ceTex, wTex, light);

        int margin = 64;
        int innerW = Math.Max(1, width  - margin * 2);
        int innerH = Math.Max(1, height - margin * 2);
        int cols   = (int)Math.Ceiling(Math.Sqrt(enemyCount));
        int rows   = (int)Math.Ceiling((double)enemyCount / cols);

        int placed = 0;
        for (int r = 0; r < rows && placed < enemyCount; r++)
        {
            for (int c = 0; c < cols && placed < enemyCount; c++)
            {
                int ex = x + margin + (cols > 1 ? c * innerW / (cols - 1) : innerW / 2);
                int ey = y + margin + (rows > 1 ? r * innerH / (rows - 1) : innerH / 2);
                AddThingRaw(ex, ey, enemyType, 270);
                placed++;
            }
        }

        if (addPlayer)
            AddThingRaw(x + width / 2, y + height / 2, 1, 90);

        _state.NotifyChanged();
        string enemyName = DoomThing.TypeNames.TryGetValue(enemyType, out var n) ? n : $"Type {enemyType}";
        return $"{roomResult}. Placed {enemyCount}× {enemyName}" + (addPlayer ? " + Player 1 Start" : "");
    }

    string DoApplyTemplate(JsonElement input)
    {
        string template  = GetStr(input, "Template", "dungeon_start").ToLowerInvariant();
        int x            = GetInt(input, "X");
        int y            = GetInt(input, "Y");
        int scale        = Math.Max(1, GetInt(input, "Scale", 1));
        string wTex      = Truncate8(GetStr(input, "WallTexture",    "STARTAN3"));
        string flTex     = Truncate8(GetStr(input, "FloorTexture",   "FLOOR4_8"));
        string ceTex     = Truncate8(GetStr(input, "CeilingTexture", "CEIL3_5"));
        int light        = GetInt(input, "LightLevel", 160);
        bool addEnemies  = !TryGetBool(input, "AddEnemies",     out bool ae) || ae;
        bool addPlayer   = !TryGetBool(input, "AddPlayerStart", out bool ap) || ap;

        int S(int v) => v * scale;

        switch (template)
        {
            case "dungeon_start":
            {
                // Entrance room
                AddRoomCore(x,            y, S(256), S(256), 0, 128, flTex, ceTex, wTex, light);
                // Left side room (dimmer)
                AddRoomCore(x - S(192), y + S(32), S(192), S(192), 0, 128, flTex, ceTex, wTex, light - 20);
                // Right side room (dimmer)
                AddRoomCore(x + S(256), y + S(32), S(192), S(192), 0, 128, flTex, ceTex, wTex, light - 20);
                // North corridor (dark)
                AddRoomCore(x + S(96),  y + S(256), S(64), S(128), 0, 128, "FLAT1", ceTex, wTex, light - 40);

                if (addPlayer)  AddThingRaw(x + S(128), y + S(128), 1,    90);
                if (addEnemies) { AddThingRaw(x - S(96),  y + S(128), 3004, 270); AddThingRaw(x + S(352), y + S(128), 3004, 90); }
                _state.NotifyChanged();
                return $"Template 'dungeon_start' at ({x},{y}): entrance + 2 side rooms + north corridor";
            }
            case "cross":
            {
                AddRoomCore(x + S(128), y + S(128), S(256), S(256), 0, 128, flTex, ceTex, wTex, light);      // hub
                AddRoomCore(x + S(192), y + S(384), S(128), S(192), 0, 128, flTex, ceTex, wTex, light - 20); // north
                AddRoomCore(x + S(192), y - S(192), S(128), S(192), 0, 128, flTex, ceTex, wTex, light - 20); // south
                AddRoomCore(x + S(384), y + S(192), S(192), S(128), 0, 128, flTex, ceTex, wTex, light - 20); // east
                AddRoomCore(x - S(192), y + S(192), S(192), S(128), 0, 128, flTex, ceTex, wTex, light - 20); // west

                if (addPlayer)  AddThingRaw(x + S(256), y + S(256), 1, 90);
                if (addEnemies)
                {
                    AddThingRaw(x + S(256), y + S(480), 3001, 270);
                    AddThingRaw(x + S(256), y - S(96),  3001, 90);
                    AddThingRaw(x + S(480), y + S(256), 3001, 180);
                    AddThingRaw(x - S(96),  y + S(256), 3001, 0);
                }
                _state.NotifyChanged();
                return $"Template 'cross' at ({x},{y}): central hub + 4 arms";
            }
            case "l_shape":
            {
                AddRoomCore(x,          y, S(384), S(192), 0, 128, flTex, ceTex, wTex, light);           // horizontal leg
                AddRoomCore(x + S(256), y + S(192), S(128), S(256), 0, 128, flTex, ceTex, wTex, light);  // vertical leg

                if (addPlayer)  AddThingRaw(x + S(96),  y + S(96),  1,    0);
                if (addEnemies) AddThingRaw(x + S(320), y + S(320), 3001, 180);
                _state.NotifyChanged();
                return $"Template 'L_shape' at ({x},{y}): two rooms at right angle";
            }
            case "boss_room":
            {
                AddRoomCore(x + S(192), y, S(128), S(192), 0, 128, flTex, ceTex, wTex, light);                   // antechamber
                AddRoomCore(x, y + S(192), S(512), S(512), 0, 192, "FLOOR6_2", "CEIL5_2", "STONE2", 220);        // boss arena

                if (addPlayer)  AddThingRaw(x + S(256), y + S(96), 1, 90);
                if (addEnemies)
                {
                    AddThingRaw(x + S(256), y + S(448), 3003, 270); // Baron of Hell (boss)
                    AddThingRaw(x + S(64),  y + S(320), 3001, 0);
                    AddThingRaw(x + S(448), y + S(320), 3001, 180);
                    AddThingRaw(x + S(64),  y + S(576), 3001, 0);
                    AddThingRaw(x + S(448), y + S(576), 3001, 180);
                }
                _state.NotifyChanged();
                return $"Template 'boss_room' at ({x},{y}): antechamber + boss arena with Baron of Hell + 4 Imps";
            }
            default:
                throw new ArgumentException($"Unknown template '{template}'. Valid: dungeon_start, cross, L_shape, boss_room");
        }
    }

    string DoClearMap()
    {
        var map = _state.Map;
        map.Vertices.Clear();
        map.Linedefs.Clear();
        map.Sidedefs.Clear();
        map.Sectors.Clear();
        map.Things.Clear();
        _state.Selection.Clear();
        _state.NotifyChanged();
        return "Map cleared";
    }

    // ── JSON helpers ──────────────────────────────────────────────────────

    // Try both PascalCase ("Width") and camelCase ("width") since the LLM
    // may use either depending on how the schema is serialized.

    static int GetInt(JsonElement el, string key, int def = 0)
    {
        if (TryGetInt(el, key, out int v)) return v;
        return def;
    }

    static bool TryGetInt(JsonElement el, string key, out int value)
    {
        if (el.TryGetProperty(key, out var p) && p.ValueKind != JsonValueKind.Null && p.TryGetInt32(out value)) return true;
        string alt = char.ToLowerInvariant(key[0]) + key[1..];
        if (el.TryGetProperty(alt, out p) && p.ValueKind != JsonValueKind.Null && p.TryGetInt32(out value)) return true;
        value = 0;
        return false;
    }

    static string GetStr(JsonElement el, string key, string def = "")
    {
        TryGetStr(el, key, out string v);
        return v.Length > 0 ? v : def;
    }

    static bool TryGetStr(JsonElement el, string key, out string value)
    {
        if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String) { value = p.GetString() ?? ""; return value.Length > 0; }
        string alt = char.ToLowerInvariant(key[0]) + key[1..];
        if (el.TryGetProperty(alt, out p) && p.ValueKind == JsonValueKind.String) { value = p.GetString() ?? ""; return value.Length > 0; }
        value = "";
        return false;
    }

    static bool TryGetBool(JsonElement el, string key, out bool value)
    {
        if (el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.True)  { value = true;  return true; }
        if (el.TryGetProperty(key, out     p) && p.ValueKind == JsonValueKind.False) { value = false; return true; }
        string alt = char.ToLowerInvariant(key[0]) + key[1..];
        if (el.TryGetProperty(alt, out p) && p.ValueKind == JsonValueKind.True)  { value = true;  return true; }
        if (el.TryGetProperty(alt, out p) && p.ValueKind == JsonValueKind.False) { value = false; return true; }
        value = false;
        return false;
    }

    static string Truncate8(string s) => s.Length > 8 ? s[..8] : s;

    // ── Display ───────────────────────────────────────────────────────────

    void AppendColored(string text, Color color)
    {
        if (InvokeRequired) { Invoke(() => AppendColored(text, color)); return; }
        int start = _display.TextLength;
        _display.AppendText(text);
        _display.Select(start, text.Length);
        _display.SelectionColor = color;
        _display.SelectionLength = 0;
        _display.ScrollToCaret();
    }
}
