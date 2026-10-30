using System.Linq;
using LootTheDeep.Gameplay;
using LootTheDeep.Gameplay.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace LootTheDeep.LevelBuilder
{
    public sealed partial class LevelBuilderWindow
    {
        private bool gesture, hovering;
        private int gestureGroup = -1, gestureControl;
        private Vector3Int firstCell, previousCell, hoverCell;
        private Vector3 hoverWorld;
        private readonly Color cursorColor = new(.3f, .9f, 1f, .8f);

        private void ChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!LevelBuilderScene.CanEdit) return;
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectOrComponentProperties) continue;
                stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var change);
                var component = EditorUtility.EntityIdToObject(change.instanceId) as Component;
                if (component == null || component.gameObject.scene != UnityEngine.SceneManagement.SceneManager.GetActiveScene() || component == navigation) continue;
                checkStale = true;
                if (component is Transform or Tilemap or Collider2D or Rigidbody2D) MarkNavigationDirty();
            }
        }

        private void DuringSceneGUI(SceneView view)
        {
            if (!LevelBuilderScene.CanEdit || map == null || !ValidBinding(map)) { ClearPreview(); ReleaseSceneTool(); return; }
            // The native painter owns Scene input AND editor-preview tiles while a Tilemap tool is active.
            if (PaletteToolActive)
            {
                if (Event.current.type == EventType.Repaint) DrawHelpers(view);
                return;
            }
            if (mode == Mode.Select) ReleaseSceneTool();
            Event e = Event.current;
            int control = GUIUtility.GetControlID("LootTheDeep.LevelBuilder".GetHashCode(), FocusType.Passive);
            bool active = mode != Mode.Select;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && active)
            {
                if (gesture)
                {
                    gesture = false;
                    if (GUIUtility.hotControl == gestureControl) GUIUtility.hotControl = 0;
                    if (gestureGroup >= 0) Undo.RevertAllDownToGroup(gestureGroup);
                    gestureGroup = -1;
                }
                SetMode(Mode.Select); e.Use(); return;
            }
            if (e.type == EventType.KeyDown && active && !e.alt && !e.control && !e.command &&
                (e.keyCode == KeyCode.Q || e.keyCode == KeyCode.E))
            { Rotate(e.keyCode == KeyCode.Q ? -1 : 1); e.Use(); }

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var plane = new Plane(map.transform.forward, map.transform.position);
            hovering = plane.Raycast(ray, out float enter) && view.position.Contains(GUIUtility.GUIToScreenPoint(e.mousePosition));
            if (hovering)
            {
                hoverWorld = ray.GetPoint(enter);
                hoverCell = map.WorldToCell(hoverWorld); hoverCell.z = 0;
            }
            if (e.type == EventType.MouseLeaveWindow && !gesture) { hovering = false; ClearPreview(); }
            if (e.type == EventType.Repaint)
            {
                ClearPreview();
                DrawHelpers(view);
                if (active && hovering && !e.alt) DrawCursor();
            }
            // Default selection, transform handles, Alt orbit, middle pan and right navigation stay native.
            if (!active || e.alt || Tools.viewToolActive) return;
            if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (e.type == EventType.MouseMove) { view.Repaint(); Repaint(); }
            if (e.type == EventType.MouseDown && e.button == 0 && hovering && HandleUtility.nearestControl == control)
            {
                if ((mode is Mode.Brush or Mode.Rectangle) && tile == null && !e.shift)
                { status = "请先选择一个地块。"; Repaint(); e.Use(); return; }
                gesture = true; firstCell = previousCell = hoverCell;
                gestureControl = control; GUIUtility.hotControl = control;
                gestureGroup = LevelBuilderScene.Begin("关卡搭建 · " + ModeLabel());
                ClearPreview();
                if (mode is Mode.Brush or Mode.Erase)
                {
                    Undo.RegisterCompleteObjectUndo(map, "关卡搭建 · 连续笔刷");
                    PaintLine(firstCell, firstCell, mode == Mode.Erase || e.shift ? null : tile);
                }
                e.Use();
            }
            if (e.type == EventType.MouseDrag && e.button == 0 && gesture && hovering)
            {
                if (mode is Mode.Brush or Mode.Erase)
                    PaintLine(previousCell, hoverCell, mode == Mode.Erase || e.shift ? null : tile);
                previousCell = hoverCell; view.Repaint(); e.Use();
            }
            if (e.type == EventType.MouseUp && e.button == 0 && gesture)
            {
                if (mode == Mode.Rectangle)
                {
                    BoundsInt area = LevelBuilderScene.Rectangle(firstCell, hoverCell);
                    if (AllowedArea(area)) LevelBuilderScene.Paint(map, area, e.shift ? null : tile);
                }
                else if (mode == Mode.Capture) CaptureModule(LevelBuilderScene.Rectangle(firstCell, hoverCell));
                else if (mode == Mode.Module && module != null)
                {
                    if ((module.sourceCellSize - map.layoutGrid.cellSize).sqrMagnitude > .0001f)
                        status = "模块与目标 Grid 的格子尺寸不同，请先统一尺寸。";
                    else LevelBuilderScene.PlaceModule(map, module, hoverCell, turns, overwrite);
                }
                else if (mode == Mode.Prefab && Entry != null)
                {
                    if (Entry.kind == LevelAssetKind.Container && region == null) status = "请在场景绑定中先指定搜索区域。";
                    else
                    {
                        var instance = LevelBuilderScene.Place(map.gameObject.scene, Entry, region, PlacementPosition(),
                            Entry.kind == LevelAssetKind.Obstacle && Entry.allowRotation ? turns : 0);
                        Selection.activeGameObject = instance;
                    }
                }
                else if (mode is Mode.Spawn or Mode.Exit or Mode.EnemyRegion && MapEditorWorkspace.Current != null)
                {
                    var root = MapEditorWorkspace.Current;
                    var type = mode == Mode.Spawn ? typeof(PlayerSpawnPoint2D) : mode == Mode.Exit ? typeof(ExtractionPoint2D) : typeof(EnemyRegion2D);
                    Vector3 center = PlacementPosition();
                    var area = LevelBuilderScene.Rectangle(firstCell, hoverCell);
                    if (mode == Mode.EnemyRegion) center = (map.CellToWorld(area.min) + map.CellToWorld(area.max)) / 2;
                    var marker = MapEditorWorkspace.Marker(root, type, center);
                    Undo.RegisterCreatedObjectUndo(marker, "放置地图标记");
                    if (mode == Mode.EnemyRegion)
                    {
                        var enemyArea = marker.GetComponent<EnemyRegion2D>();
                        enemyArea.size = map.CellToWorld(area.max) - map.CellToWorld(area.min);
                        var defaultEnemy = library.prefabs.FirstOrDefault(p => p.kind == LevelAssetKind.Enemy);
                        if (defaultEnemy != null) enemyArea.enemies.Add(new EnemyRegionEntry { prefab = defaultEnemy.prefab.GetComponent<EnemyCombat2D>() });
                    }
                    LevelBuilderScene.Changed(marker.transform); Selection.activeGameObject = marker;
                }
                else if (mode == Mode.Pick)
                {
                    tile = map.GetTile(hoverCell);
                    status = tile != null ? "已吸取 " + tile.name : "该格为空。";
                    mode = Mode.Brush;
                }
                else if (mode == Mode.PathStart || mode == Mode.PathEnd)
                {
                    if (mode == Mode.PathStart) { pathStart = PlacementPosition(); hasPathStart = true; }
                    else { pathEnd = PlacementPosition(); hasPathEnd = true; }
                    mode = Mode.Select;
                }
                FinishGesture();
                if (hasPathStart && hasPathEnd) CalculatePath(true);
                view.Repaint(); Repaint(); e.Use();
            }
        }

        private void PaintLine(Vector3Int a, Vector3Int b, TileBase value)
        {
            int x = a.x, y = a.y, dx = Mathf.Abs(b.x - x), dy = -Mathf.Abs(b.y - y);
            int sx = x < b.x ? 1 : -1, sy = y < b.y ? 1 : -1, error = dx + dy;
            while (true)
            {
                LevelBuilderScene.Paint(map, new BoundsInt(new Vector3Int(x, y, 0), Vector3Int.one), value, false);
                if (x == b.x && y == b.y) break;
                int twice = error * 2;
                if (twice >= dy) { error += dy; x += sx; }
                if (twice <= dx) { error += dx; y += sy; }
            }
        }

        private bool AllowedArea(BoundsInt area)
        {
            if ((long)area.size.x * area.size.y <= 16384) return true;
            status = "单次矩形或模块限制为 16384 格，请分段处理。";
            return false;
        }

        private void FinishGesture()
        {
            if (!gesture) return;
            gesture = false;
            if (GUIUtility.hotControl == gestureControl) GUIUtility.hotControl = 0;
            MarkNavigationDirty();
            if (LevelBuilderScene.CanEdit && map != null) SyncNavigation();
            Undo.FlushUndoRecordObjects();
            if (gestureGroup >= 0) Undo.CollapseUndoOperations(gestureGroup);
            gestureGroup = -1;
        }

        private Vector3 PlacementPosition() => snap ? map.GetCellCenterWorld(hoverCell) : hoverWorld;

        private void CaptureModule(BoundsInt area)
        {
            if (!AllowedArea(area)) return;
            // Release the Scene gesture before showing a native save dialog.
            if (GUIUtility.hotControl == gestureControl) GUIUtility.hotControl = 0;
            string path = EditorUtility.SaveFilePanelInProject("保存地形模块", "地图模块", "asset", "保存范围内地块（含空格），不包含敌人与容器。", LevelBuilderLibrary.Folder);
            if (string.IsNullOrEmpty(path)) { status = "已取消模块保存。"; return; }
            if (!path.StartsWith("Assets/Editor/", System.StringComparison.Ordinal))
            { status = "请将开发素材保存在 Assets/Editor 下，避免进入成品。"; return; }
            var captured = LevelTileModule.Capture(map, area);
            AssetDatabase.CreateAsset(captured, AssetDatabase.GenerateUniqueAssetPath(path));
            Undo.RecordObject(library, "加入新地图模块");
            library.modules.Add(captured); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            module = captured; category = 0; terrainCategory = 1; turns = 0; mode = Mode.Module;
            status = "模块已保存。Q/E 旋转，点击放置；覆盖模式会同时写入模块中的空格。";
        }

        private void Rotate(int direction)
        {
            if (mode != Mode.Module && !(mode == Mode.Prefab && Entry != null && Entry.allowRotation && Entry.kind == LevelAssetKind.Obstacle)) return;
            turns = (turns + direction + 4) % 4; ClearPreview(); SceneView.RepaintAll(); Repaint();
        }

        private void ClearPreview()
        {
            if (map != null && !PaletteToolActive) map.ClearAllEditorPreviewTiles();
        }

        private void DrawCursor()
        {
            ClearPreview();
            BoundsInt area = gesture && mode is Mode.Rectangle or Mode.Capture or Mode.EnemyRegion ? LevelBuilderScene.Rectangle(firstCell, hoverCell) : new BoundsInt(hoverCell, Vector3Int.one);
            if (mode == Mode.Module && module != null)
            {
                Vector2Int size = module.RotatedSize(turns);
                area = new BoundsInt(hoverCell, new Vector3Int(size.x, size.y, 1));
                foreach (var cell in module.cells)
                {
                    Vector3Int p = hoverCell + module.RotateCell(cell.position, turns);
                    if (!overwrite && map.HasTile(p)) continue;
                    map.SetEditorPreviewTile(p, cell.tile);
                    map.SetEditorPreviewColor(p, new Color(cell.color.r, cell.color.g, cell.color.b, .55f));
                    map.SetEditorPreviewTransformMatrix(p, Matrix4x4.Rotate(Quaternion.Euler(0, 0, turns * 90)) * cell.transform);
                }
            }
            else if (mode is Mode.Brush or Mode.Rectangle && tile != null && !Event.current.shift)
            {
                // Keep previews bounded even while dragging a very large rectangle.
                if ((long)area.size.x * area.size.y <= 16384)
                    foreach (var p in area.allPositionsWithin)
                    { map.SetEditorPreviewTile(p, tile); map.SetEditorPreviewColor(p, new Color(1, 1, 1, .5f)); }
            }
            Color color = mode == Mode.Erase || Event.current.shift ? new Color(1f, .35f, .3f, .9f) : cursorColor;
            DrawArea(area, color);
            if (mode == Mode.Prefab && Entry != null)
            {
                var renderer = Entry.prefab.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(r => r.sprite != null && r.gameObject.activeSelf);
                if (renderer != null) DrawSpriteGhost(renderer.sprite, PlacementPosition(), Entry.scale, Entry.allowRotation ? turns * 90 : 0);
            }
            if (mode is Mode.Spawn or Mode.Exit or Mode.PathStart or Mode.PathEnd)
                Handles.DrawWireDisc(PlacementPosition(), Vector3.forward, navigation != null ? navigation.AgentRadius : .6f);
            Handles.Label(map.GetCellCenterWorld(hoverCell) + Vector3.up * .7f, ModeLabel() + "  " + hoverCell.x + ", " + hoverCell.y);
        }

        private void DrawArea(BoundsInt area, Color color)
        {
            Vector3 a = map.CellToWorld(area.min), b = map.CellToWorld(new Vector3Int(area.xMax, area.yMin, 0));
            Vector3 c = map.CellToWorld(area.max), d = map.CellToWorld(new Vector3Int(area.xMin, area.yMax, 0));
            Handles.DrawSolidRectangleWithOutline(new[] { a, b, c, d }, new Color(color.r, color.g, color.b, .08f), color);
        }

        private static void DrawSpriteGhost(Sprite sprite, Vector3 center, Vector3 scale, float angle)
        {
            Vector3 size = Vector3.Scale(sprite.bounds.size, scale);
            Vector2 a = HandleUtility.WorldToGUIPoint(center + new Vector3(-size.x, size.y) * .5f);
            Vector2 b = HandleUtility.WorldToGUIPoint(center + new Vector3(size.x, -size.y) * .5f);
            Rect rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            Rect uv = sprite.textureRect;
            uv = new Rect(uv.x / sprite.texture.width, uv.y / sprite.texture.height, uv.width / sprite.texture.width, uv.height / sprite.texture.height);
            Handles.BeginGUI();
            Matrix4x4 oldMatrix = GUI.matrix; Color oldColor = GUI.color;
            GUIUtility.RotateAroundPivot(-angle, rect.center); GUI.color = new Color(1, 1, 1, .5f);
            GUI.DrawTextureWithTexCoords(rect, sprite.texture, uv);
            GUI.color = oldColor; GUI.matrix = oldMatrix;
            Handles.EndGUI();
        }

        private void DrawHelpers(SceneView view)
        {
            Color previousColor = Handles.color;
            if (showGrid && hovering)
            {
                Handles.color = new Color(.55f, .7f, .8f, .22f);
                for (int offset = -12; offset <= 12; offset++)
                {
                    Handles.DrawLine(map.CellToWorld(hoverCell + new Vector3Int(offset, -12)), map.CellToWorld(hoverCell + new Vector3Int(offset, 12)));
                    Handles.DrawLine(map.CellToWorld(hoverCell + new Vector3Int(-12, offset)), map.CellToWorld(hoverCell + new Vector3Int(12, offset)));
                }
            }
            if (showColliders)
            {
                Handles.color = new Color(.2f, 1f, .55f, .65f);
                foreach (var collider in blockers)
                {
                    if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                    Matrix4x4 oldMatrix = Handles.matrix; Handles.matrix = collider.transform.localToWorldMatrix;
                    if (collider is CompositeCollider2D composite)
                    {
                        for (int path = 0; path < composite.pathCount; path++)
                        {
                            var points = new Vector2[composite.GetPathPointCount(path)]; composite.GetPath(path, points);
                            DrawColliderPath(points, composite.offset, true);
                        }
                    }
                    else if (collider is PolygonCollider2D polygon)
                        for (int path = 0; path < polygon.pathCount; path++) DrawColliderPath(polygon.GetPath(path), polygon.offset, true);
                    else if (collider is BoxCollider2D box)
                    {
                        Vector2 h = box.size * .5f;
                        DrawColliderPath(new[] { new Vector2(-h.x, -h.y), new Vector2(h.x, -h.y), h, new Vector2(-h.x, h.y) }, box.offset, true);
                    }
                    else if (collider is CircleCollider2D circle) Handles.DrawWireDisc(circle.offset, Vector3.forward, circle.radius);
                    else if (collider is EdgeCollider2D edge) DrawColliderPath(edge.points, edge.offset, false);
                    Handles.matrix = oldMatrix;
                }
            }
            if (showRanges)
            {
                foreach (var enemy in enemies)
                {
                    if (enemy == null) continue;
                    var serialized = new SerializedObject(enemy);
                    Handles.color = new Color(1f, .68f, .15f, .25f);
                    Handles.DrawWireDisc(enemy.transform.position, Vector3.forward, serialized.FindProperty("sightRange").floatValue);
                    Handles.color = new Color(1f, .3f, .25f, .4f);
                    Handles.DrawWireDisc(enemy.transform.position, Vector3.forward, serialized.FindProperty("attackRange").floatValue);
                }
                var inventory = LevelBuilderScene.All<LootTheDeep.Inventory.InventorySession>(map.gameObject.scene).FirstOrDefault();
                Handles.color = new Color(.3f, .85f, 1f, .4f);
                foreach (var box in boxes)
                    if (box != null) Handles.DrawWireDisc(box.transform.position, Vector3.forward, inventory != null ? inventory.interactDistance : 1.8f);
            }
            if (player != null)
            {
                Handles.color = Color.green;
                Handles.DrawWireDisc(player.transform.position, Vector3.forward, navigation != null ? navigation.AgentRadius : .6f);
                Handles.Label(player.transform.position + Vector3.up, "玩家出生点");
            }
            var root = MapEditorWorkspace.Current;
            if (root != null)
            {
                var bounds = root.Bounds;
                if (bounds.size != Vector3Int.zero) DrawArea(bounds, new Color(.7f, .7f, 1f, .65f));
                foreach (var spawn in root.Spawns) Handles.Label(spawn.transform.position + Vector3.up, "出生 · " + spawn.name);
                foreach (var exit in root.Exits) Handles.Label(exit.transform.position + Vector3.up * 1.7f, "撤离 · " + exit.name);
                foreach (var area in root.All<EnemyRegion2D>()) Handles.Label(area.transform.position, area.displayName);
                if (previewEnemies && !navigationDirty && root.Spawns.Length > 0)
                {
                    int index = testSpawn < 0 ? new System.Random(testSeed).Next(root.Spawns.Length) : Mathf.Clamp(testSpawn, 0, root.Spawns.Length - 1);
                    if (MapSpawnPlanner.PlanEnemies(root, root.Spawns[index].transform.position, testSeed, out var planned, out _))
                    {
                        Handles.color = new Color(1, .45f, .15f);
                        foreach (var p in planned)
                        { Handles.DrawWireDisc(p.position, Vector3.forward, navigation.AgentRadius); Handles.Label(p.position, p.prefab.name); }
                    }
                }
            }
            Handles.color = Color.cyan;
            if (hasPathStart) Handles.DrawWireDisc(pathStart, Vector3.forward, .25f);
            if (hasPathEnd) Handles.DrawWireDisc(pathEnd, Vector3.forward, .25f);
            if (previewPath.Count > 0) Handles.DrawAAPolyLine(4f, new[] { (Vector3)pathStart }.Concat(previewPath.Select(p => (Vector3)p)).ToArray());
            Handles.color = previousColor;
        }

        private static void DrawColliderPath(Vector2[] points, Vector2 offset, bool loop)
        {
            if (points.Length < 2) return;
            var vertices = new Vector3[points.Length + (loop ? 1 : 0)];
            for (int i = 0; i < points.Length; i++) vertices[i] = points[i] + offset;
            if (loop) vertices[points.Length] = vertices[0];
            Handles.DrawPolyLine(vertices);
        }
    }
}
