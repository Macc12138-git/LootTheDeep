using System;
using System.IO;
using System.Linq;
using LootTheDeep.Gameplay.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace LootTheDeep.LevelBuilder
{
    public sealed partial class LevelBuilderWindow
    {
        [MenuItem("LootTheDeep/关卡编辑器/验证上下布局")]
        private static void ValidateVerticalLayout()
        {
            if (!LevelBuilderScene.CanEdit) throw new InvalidOperationException("请在 MapEditorScene 编辑模式验证。");
            var previousPaletteTool = PaletteToolActive ? UnityEditor.EditorTools.ToolManager.activeToolType : null;
            var window = CreateInstance<LevelBuilderWindow>();
            window.titleContent = new GUIContent("上下布局验证（临时）");
            window.ShowUtility();
            window.position = new Rect(100, 100, 480, 820);
            window.rootVisualElement.schedule.Execute(() =>
            {
                try
                {
                    var split = window.editorBody as TwoPaneSplitView;
                    var materials = window.editorBody.Q<ScrollView>("map-materials-pane");
                    var properties = window.editorBody.Q<IMGUIContainer>("map-properties-pane");
                    if (split == null || split.orientation != TwoPaneSplitViewOrientation.Vertical || materials == null || properties == null)
                        throw new InvalidOperationException("MAP_VERTICAL_LAYOUT_FAILED: 必须为上下分区。");
                    Rect upper = materials.worldBound, lower = properties.worldBound;
                    if (lower.yMin < upper.yMax - 1 || Mathf.Abs(lower.xMin - upper.xMin) > 1 || Mathf.Abs(lower.width - upper.width) > 1)
                        throw new InvalidOperationException("MAP_VERTICAL_LAYOUT_FAILED: 两个分区未垂直对齐。");
                    if (upper.width > 480 || lower.width > 480 || lower.height < 99 || window.paletteElement == null || window.paletteElement.resolvedStyle.height < 179)
                        throw new InvalidOperationException("MAP_VERTICAL_LAYOUT_FAILED: 窄窗口中分区或调色板尺寸不正确。");
                    Debug.Log($"MAP_VERTICAL_LAYOUT_CHECKS_PASSED: width={window.position.width}; materials={upper}; properties={lower}; paletteHeight={window.paletteElement.resolvedStyle.height}");
                }
                catch (Exception exception) { Debug.LogException(exception); }
                finally
                {
                    window.Close();
                    if (previousPaletteTool != null) TilemapEditorTool.SetActiveEditorTool(previousPaletteTool);
                }
            }).StartingIn(200);
        }

        [MenuItem("LootTheDeep/关卡编辑器/验证 Tile Palette 集成")]
        private static void ValidatePalette()
        {
            if (!LevelBuilderScene.CanEdit) throw new InvalidOperationException("请在 MapEditorScene 编辑模式验证。");
            OpenPalette();
            var window = GetWindow<LevelBuilderWindow>();
            // Let UI Toolkit attach/layout the native clipboard before inspecting it.
            window.rootVisualElement.schedule.Execute(() =>
            {
                try { window.RunPaletteChecks(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }).StartingIn(100);
        }

        private void RunPaletteChecks()
        {
            int checks = 0;
            void Check(bool passed, string label)
            {
                if (!passed) throw new InvalidOperationException("TILE_PALETTE_CHECK_FAILED: " + label);
                checks++;
            }
            string Hash(string path) => Hash128.Compute(File.ReadAllText(path)).ToString();
            const string palettePath = "Assets/Map/Tile/MapBase.prefab";
            const string sharedPath = "Assets/Map/Prefabs/Map/MapBase.prefab";
            string paletteHash = Hash(palettePath), sharedHash = Hash(sharedPath), demoHash = Hash(MapEditorWorkspace.DemoPath);
            var originalScene = SceneManager.GetActiveScene();
            var selected = Selection.objects;
            var brush = CreateInstance<GridBrush>();
            var brushEditor = (GridBrushEditorBase)UnityEditor.Editor.CreateEditor(brush);
            Scene scratch = default;
            string savePath = AssetDatabase.GenerateUniqueAssetPath("Assets/Maps/__PaletteValidation.prefab");
            Vector3Int previewCell = new(map.cellBounds.xMax + 5, map.cellBounds.yMax + 5, 0);
            try
            {
                Debug.Log($"TILE_PALETTE_IDENTITY: dataPath={Application.dataPath}; product={Application.productName}; scene={originalScene.path}");
                Check(Application.dataPath == Path.GetFullPath("Assets") && originalScene.path == MapEditorWorkspace.EditorPath, "工程与制作场景正确");
                Check(paletteSession && GridPaintingState.isEditing && GridPaintingState.gridBrush != null,
                    $"内嵌控件独立激活原生绘制服务 session={paletteSession}, editing={GridPaintingState.isEditing}, brush={GridPaintingState.gridBrush}, valid={ValidBinding(map)}, panel={rootVisualElement.panel != null}, error={paletteError}");
                Check(paletteElement != null && paletteElement.panel != null && paletteElement.resolvedStyle.height > 80, "原生 Palette 已附加并有可见绘制区域");
                Check(editorBody is TwoPaneSplitView split && split.orientation == TwoPaneSplitViewOrientation.Vertical, "素材库与属性区采用上下布局");
                Check(paletteHost.Q<GridPaintingToolbar>() != null && paletteHost.Q<TilePaletteBrushesPopup>() != null, "原生工具条和笔刷选择器已嵌入");
                Check(PaletteToolActive && GridPaintingState.scenePaintTarget == map.gameObject && !ownsSceneTool, "原生工具绘制当前 Map Ground，无自制工具抢输入");
                scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(originalScene);
                ActivatePaletteTool();
                var root = MapEditorWorkspace.CreateMap(scratch, "PaletteValidation");
                root.transform.position = new Vector3(3000, 3000);
                GridPaintingState.scenePaintTarget = root.ground.gameObject;
                Check(GridPaintingState.scenePaintTarget == map.gameObject, "阻止当前编辑面板误切到其他地图目标");
                var floor = library.tiles.OfType<Tile>().First(t => t.colliderType == Tile.ColliderType.None);
                var wall = library.tiles.OfType<Tile>().First(t => t.colliderType != Tile.ColliderType.None);
                map.SetEditorPreviewTile(previewCell, floor);
                ClearPreview();
                Check(map.GetEditorPreviewTile(previewCell) == floor, "原生绘制期间不清除原生笔刷预览");
                category = 1; SetMode(Mode.Prefab); UpdatePaletteUI();
                Check(!PaletteToolActive && mode == Mode.Prefab && ownsSceneTool && paletteElement == null, "容器工具退出原生绘制并释放调色板控件");
                Check(map.GetEditorPreviewTile(previewCell) == null, "切回布局工具清除上一个工具预览");
                TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool)); UpdatePaletteUI();
                Check(PaletteToolActive && mode == Mode.Select && !ownsSceneTool && ShowPalette && paletteElement != null, "原生工具快捷切换自动结束布局工具并返回调色板");
                SetMode(Mode.Select);
                Check(!PaletteToolActive && !ownsSceneTool, "普通选择模式释放两套绘制工具");

                var tilemap = root.ground;
                var grid = tilemap.layoutGrid;
                var area = new BoundsInt(-16, -12, 0, 32, 24, 1);
                tilemap.SetTile(Vector3Int.zero, floor);
                tilemap.SetTile(Vector3Int.right, wall);
                brush.Pick(grid, tilemap.gameObject, new BoundsInt(0, 0, 0, 2, 1, 1), Vector3Int.zero);
                Check(brush.size == new Vector3Int(2, 1, 1) && brush.cells.Any(c => c.tile == wall), "原生框选拾取组合瓦片");
                var dest = new Vector3Int(3, 3, 0);
                int group = LevelBuilderScene.Begin("原生笔刷验证");
                brushEditor.RegisterUndo(tilemap.gameObject, GridBrushBase.Tool.Paint);
                brush.Paint(grid, tilemap.gameObject, dest);
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(group);
                Check(tilemap.GetTile(dest) == floor && tilemap.GetTile(dest + Vector3Int.right) == wall, "原生组合笔刷绘制");
                Undo.PerformUndo(); Check(!tilemap.HasTile(dest) && !tilemap.HasTile(dest + Vector3Int.right), "原生绘制整笔撤销");
                Undo.PerformRedo(); Check(tilemap.GetTile(dest + Vector3Int.right) == wall, "原生绘制重做");
                brush.Rotate(GridBrushBase.RotationDirection.Clockwise, GridLayout.CellLayout.Rectangle);
                Check(brush.size == new Vector3Int(1, 2, 1), "原生组合旋转");
                brush.Flip(GridBrushBase.FlipAxis.X, GridLayout.CellLayout.Rectangle);
                var rotatedTarget = new Vector3Int(6, 6, 0);
                brush.Paint(grid, tilemap.gameObject, rotatedTarget);
                var rotatedBounds = new BoundsInt(rotatedTarget - brush.pivot, brush.size);
                Check(tilemap.HasTile(rotatedBounds.min) && tilemap.HasTile(rotatedBounds.min + Vector3Int.up), "旋转翻转后的组合按原生 pivot 可绘制");
                brush.Pick(grid, tilemap.gameObject, new BoundsInt(Vector3Int.zero, Vector3Int.one), Vector3Int.zero);
                brush.BoxFill(grid, tilemap.gameObject, new BoundsInt(8, 8, 0, 3, 2, 1));
                Check(tilemap.GetTile(new Vector3Int(10, 9, 0)) == floor, "原生矩形铺设");
                brush.BoxErase(grid, tilemap.gameObject, new BoundsInt(8, 8, 0, 3, 2, 1));
                brush.Erase(grid, tilemap.gameObject, dest);
                Check(!tilemap.HasTile(new Vector3Int(10, 9, 0)) && !tilemap.HasTile(dest), "原生矩形与笔刷擦除");
                // Fill a walkable map then paint a wall through the same native brush path.
                brush.BoxFill(grid, tilemap.gameObject, area);
                brush.Pick(grid, tilemap.gameObject, new BoundsInt(Vector3Int.zero, Vector3Int.one), Vector3Int.zero);
                // Use a known wall sample after filling over the original sample.
                tilemap.SetTile(Vector3Int.right, wall);
                brush.Pick(grid, tilemap.gameObject, new BoundsInt(Vector3Int.right, Vector3Int.one), Vector3Int.right);
                brush.BoxFill(grid, tilemap.gameObject, new BoundsInt(2, -5, 0, 1, 10, 1));
                root.RebuildNavigation();
                Vector2 wallPosition = tilemap.GetCellCenterWorld(new Vector3Int(2, 2, 0));
                Check(!root.navigation.CanTraverse(wallPosition, wallPosition), "原生墙体进入现有碰撞与导航");
                navigationDirty = checkStale = false;
                ActivatePaletteTool(); PaletteEdited(map.gameObject);
                Check(navigationDirty && checkStale, "原生绘制完成回调使导航与检查过期");
                navigationDirty = checkStale = false;
                SceneChanged();
                Check(navigationDirty && checkStale && GridPaintingState.scenePaintTarget == map.gameObject, "撤销重做/层级刷新保持目标并触发导航更新");
                Check(PrefabUtility.SaveAsPrefabAsset(root.gameObject, savePath) != null, "原生绘制地图可导出预制体");
                var saved = AssetDatabase.LoadAssetAtPath<MapRoot>(savePath);
                var reopened = ((GameObject)PrefabUtility.InstantiatePrefab(saved.gameObject, scratch)).GetComponent<MapRoot>();
                Check(reopened.ground.GetTile(new Vector3Int(2, 2, 0)) == wall && reopened.ground.GetTile(new Vector3Int(10, 9, 0)) == floor, "重开 Map 保留原生绘制结果");
                ReleasePaletteSession();
                Check(!paletteSession && paletteElement == null && !PaletteToolActive, "退出内嵌会话释放工具与控件");
                UpdatePaletteUI(); ActivatePaletteTool();
                Check(paletteSession && GridPaintingState.isEditing && GridPaintingState.scenePaintTarget == map.gameObject, "再次进入可恢复绘制");
                Check(Hash(palettePath) == paletteHash && Hash(sharedPath) == sharedHash && Hash(MapEditorWorkspace.DemoPath) == demoHash, "未污染共享调色板、旧地图与示例 Map 资产");
                Debug.Log($"TILE_PALETTE_CHECKS_PASSED: {checks} checks; native UI, service lifetime, exclusive input, previews, brush pick/paint/rotate/flip/box/erase, undo/redo, navigation, prefab persistence.");
            }
            finally
            {
                map.ClearAllEditorPreviewTiles();
                DestroyImmediate(brushEditor); DestroyImmediate(brush);
                if (scratch.IsValid() && scratch.isLoaded) EditorSceneManager.CloseScene(scratch, true);
                AssetDatabase.DeleteAsset(savePath);
                Selection.objects = selected;
                Bind(); ActivatePaletteTool();
                SceneView.RepaintAll(); Repaint();
            }
        }
    }
}
