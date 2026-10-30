using System;
using System.Collections.Generic;
using System.Linq;
using LootTheDeep.Gameplay;
using LootTheDeep.Gameplay.Maps;
using LootTheDeep.Inventory;
using LootTheDeep.Inventory.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace LootTheDeep.LevelBuilder
{
    public sealed partial class LevelBuilderWindow : EditorWindow
    {
        private enum Mode { Select, Brush, Rectangle, Erase, Pick, Module, Capture, Prefab, Spawn, Exit, EnemyRegion, PathStart, PathEnd }
        [SerializeField] private Tilemap map;
        [SerializeField] private TilemapPathfinder2D navigation;
        [SerializeField] private PlayerShooter2D player;
        [SerializeField] private RegionContainerManager region;
        [SerializeField] private LevelBuilderLibrary library;
        [SerializeField] private TileBase tile;
        [SerializeField] private LevelTileModule module;
        [SerializeField] private int prefabIndex = -1, category, turns;
        [SerializeField] private bool snap = true, overwrite = true, showGrid = true, showColliders = true, showRanges = true;
        private Mode mode;
        private string search = "", status = "选择素材后在 Scene 视图编辑；选择模式可照常使用 Unity 工具。";
        private bool bindings, helpers = true, navigationDirty, checkStale = true;
        private bool ownsSceneTool, previousToolsHidden;
        private Tool previousTool;
        private double syncAfter;
        private Vector2 libraryScroll, propertyScroll, issueScroll;
        private Object importAsset;
        private EnemyCombat2D[] enemies = Array.Empty<EnemyCombat2D>();
        private SearchableContainer[] boxes = Array.Empty<SearchableContainer>();
        private Collider2D[] blockers = Array.Empty<Collider2D>();
        private List<LayoutIssue> issues = new();
        private readonly List<Vector2> previewPath = new();
        private Vector2 pathStart, pathEnd;
        private bool hasPathStart, hasPathEnd;
        private static readonly string[] Categories = { "地图", "容器", "出生点", "撤离点", "敌人区域" };
        [SerializeField] private int terrainCategory, testSeed = 12345, testSpawn = -1;
        [SerializeField] private bool previewEnemies, showAllCandidates;
        [SerializeField] private bool mapSettings;
        private LevelAssetKind PrefabKind => category == 1 ? LevelAssetKind.Container : LevelAssetKind.Obstacle;
        private LevelPrefabEntry Entry => library != null && prefabIndex >= 0 && prefabIndex < library.prefabs.Count ? library.prefabs[prefabIndex] : null;

        [MenuItem("LootTheDeep/开发者模式/关卡搭建 %#F3")]
        public static void Open()
        {
            var window = GetWindow<LevelBuilderWindow>("关卡编辑器");
            window.minSize = new Vector2(480, 680);
            window.Show();
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.in2DMode = true;
        }

        [MenuItem("LootTheDeep/开发者模式/保存并试玩 %#F11")]
        public static void SaveAndPlay()
        {
            if (!LevelBuilderScene.CanEdit) return;
            Open();
            GetWindow<LevelBuilderWindow>().Save(true);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("关卡编辑器");
            minSize = new Vector2(480, 680);
            if (library == null) library = AssetDatabase.LoadAssetAtPath<LevelBuilderLibrary>(LevelBuilderLibrary.AssetPath);
            if (LevelBuilderScene.CanEdit)
            {
                if (library == null) library = LevelBuilderLibrary.LoadOrCreate();
                Bind();
            }
            SceneView.duringSceneGui += DuringSceneGUI;
            EditorApplication.hierarchyChanged += SceneChanged;
            Undo.undoRedoPerformed += SceneChanged;
            Selection.selectionChanged += Repaint;
            EditorApplication.update += UpdateNavigation;
            EditorSceneManager.activeSceneChangedInEditMode += ActiveSceneChanged;
            EditorApplication.playModeStateChanged += PlayStateChanged;
            ObjectChangeEvents.changesPublished += ChangesPublished;
            EnablePaletteCallbacks();
        }

        private void OnDisable()
        {
            FinishGesture(); ClearPreview();
            ReleaseSceneTool();
            DisablePalette();
            SceneView.duringSceneGui -= DuringSceneGUI;
            EditorApplication.hierarchyChanged -= SceneChanged;
            Undo.undoRedoPerformed -= SceneChanged;
            Selection.selectionChanged -= Repaint;
            EditorApplication.update -= UpdateNavigation;
            EditorSceneManager.activeSceneChangedInEditMode -= ActiveSceneChanged;
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            ObjectChangeEvents.changesPublished -= ChangesPublished;
        }

        private void ActiveSceneChanged(Scene before, Scene after) { SetMode(Mode.Select); Bind(); }
        private void PlayStateChanged(PlayModeStateChange state)
        {
            SetMode(Mode.Select);
            if (state == PlayModeStateChange.ExitingEditMode) ReleasePaletteSession();
            if (state == PlayModeStateChange.EnteredEditMode) { Bind(); SceneChanged(); }
            Repaint();
        }

        private void Bind()
        {
            if (!LevelBuilderScene.CanEdit) return;
            var scene = SceneManager.GetActiveScene();
            var root = MapEditorWorkspace.Current;
            map = root != null ? root.ground : null;
            navigation = root != null ? root.navigation : null;
            if (player == null || player.gameObject.scene != scene) player = LevelBuilderScene.All<PlayerShooter2D>(scene).FirstOrDefault();
            if (region == null || !ValidBinding(region)) region = root != null ? root.GetComponentInChildren<RegionContainerManager>(true) : null;
            RefreshCache();
            BindPaletteTarget();
        }

        private void RefreshCache()
        {
            var scene = SceneManager.GetActiveScene();
            enemies = LevelBuilderScene.All<EnemyCombat2D>(scene).Where(e => e.isActiveAndEnabled).ToArray();
            boxes = LevelBuilderScene.All<SearchableContainer>(scene).Where(b => b.isActiveAndEnabled).ToArray();
            blockers = LevelBuilderScene.Blockers(scene);
        }

        private void SceneChanged()
        {
            if (!LevelBuilderScene.CanEdit) return;
            Bind(); MarkNavigationDirty(); Repaint(); SceneView.RepaintAll();
        }

        private void MarkNavigationDirty()
        {
            navigationDirty = checkStale = true;
            previewPath.Clear();
            syncAfter = EditorApplication.timeSinceStartup + .3;
        }

        private bool ValidBinding(Component component) => component == null ||
            component.gameObject.scene == SceneManager.GetActiveScene() && !EditorUtility.IsPersistent(component) &&
            MapEditorWorkspace.Current != null && component.transform.IsChildOf(MapEditorWorkspace.Current.transform);

        private void UpdateNavigation()
        {
            UpdatePaletteUI();
            if (!LevelBuilderScene.CanEdit || gesture || GUIUtility.hotControl != 0 || !navigationDirty || map == null ||
                EditorApplication.timeSinceStartup < syncAfter) return;
            // Native handles and Tile Palette also reach this path through ObjectChangeEvents below.
            SyncNavigation();
        }

        private void SyncNavigation()
        {
            if (map == null || !ValidBinding(map)) return;
            ClearPreview();
            navigation = LevelBuilderScene.SyncNavigation(map, navigation, true);
            navigationDirty = false;
            RefreshCache();
            if (hasPathStart && hasPathEnd) CalculatePath(false);
        }

        private void SetMode(Mode next)
        {
            StopPaletteTool();
            FinishGesture(); ClearPreview();
            mode = next;
            if (next == Mode.Select) ReleaseSceneTool();
            else if (!ownsSceneTool)
            {
                ownsSceneTool = true;
                previousTool = Tools.current; previousToolsHidden = Tools.hidden;
                Tools.current = Tool.None; Tools.hidden = true;
            }
            SceneView.RepaintAll(); Repaint();
        }

        private void ReleaseSceneTool()
        {
            if (!ownsSceneTool) return;
            if (Tools.current == Tool.None) Tools.current = previousTool;
            Tools.hidden = previousToolsHidden; ownsSceneTool = false;
        }

        private void DrawHeader()
        {
            DrawToolbar();
            if (!LevelBuilderScene.CanEdit)
            {
                EditorGUILayout.HelpBox("点击“打开制作场景”开始制作 Map。退出试玩后自动返回制作场景；原 ExploreScene 保持独立。", MessageType.Info);
                return;
            }
            if (library == null)
            {
                if (GUILayout.Button("建立素材库并导入现有地块、敌人与容器")) library = LevelBuilderLibrary.LoadOrCreate();
                return;
            }
            DrawBindings();
        }

        private void DrawFooter()
        {
            if (!LevelBuilderScene.CanEdit || library == null) return;
            DrawIssues();
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        private void DrawToolbar()
        {
            bool narrow = position.width < 820;
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling))
            {
                if (GUILayout.Button("打开制作场景", EditorStyles.toolbarButton)) MapEditorWorkspace.OpenEditor();
                using (new EditorGUI.DisabledScope(!LevelBuilderScene.CanEdit))
                {
                    if (GUILayout.Button("新建 Map", EditorStyles.toolbarButton)) { SetMode(Mode.Select); MapEditorWorkspace.NewMap(); Bind(); }
                    if (GUILayout.Button("打开 Map", EditorStyles.toolbarButton)) { SetMode(Mode.Select); MapEditorWorkspace.OpenMap(); Bind(); }
                    if (GUILayout.Button("另存为", EditorStyles.toolbarButton)) { SetMode(Mode.Select); MapEditorWorkspace.SaveMap(true); }
                }
            }
            using (new EditorGUI.DisabledScope(!LevelBuilderScene.CanEdit))
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    ToolButton("选择", Mode.Select);
                    if (GUILayout.Button("Tile Palette 绘图", EditorStyles.toolbarButton)) ActivatePaletteTool();
                    ToolButton("框选模块", Mode.Capture);
                    snap = GUILayout.Toggle(snap, "网格吸附", EditorStyles.toolbarButton);
                    GUILayout.FlexibleSpace();
                    if (!narrow) DrawMapActions();
                }
                if (narrow)
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) DrawMapActions();
                using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
                {
                    if (GUILayout.Button("放置出生点", EditorStyles.toolbarButton)) SetMode(Mode.Spawn);
                    if (GUILayout.Button("放置撤离点", EditorStyles.toolbarButton)) SetMode(Mode.Exit);
                    if (GUILayout.Button("绘制敌人区域", EditorStyles.toolbarButton)) SetMode(Mode.EnemyRegion);
                    GUILayout.FlexibleSpace();
                    if (!narrow)
                    {
                        DrawLayoutActions();
                        GUILayout.Label("Scene 操作 · Q/E 旋转模块/障碍", EditorStyles.miniLabel);
                    }
                }
                if (narrow)
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar)) DrawLayoutActions();
            }
        }

        private void DrawMapActions()
        {
            if (GUILayout.Button("检查布局", EditorStyles.toolbarButton)) RunCheck();
            if (GUILayout.Button("保存", EditorStyles.toolbarButton)) Save(false);
            if (GUILayout.Button("保存并试玩", EditorStyles.toolbarButton)) Save(true);
        }

        private void DrawLayoutActions()
        {
            if (GUILayout.Button("吸附所选", EditorStyles.toolbarButton)) EditSelection(0);
            if (GUILayout.Button("复制所选", EditorStyles.toolbarButton)) EditSelection(1);
            if (GUILayout.Button("删除所选", EditorStyles.toolbarButton)) EditSelection(2);
            if (GUILayout.Button("同步导航", EditorStyles.toolbarButton)) { SyncNavigation(); status = "碰撞与导航已同步。"; }
        }

        private void ToolButton(string label, Mode value)
        {
            if (GUILayout.Toggle(mode == value && !PaletteToolActive, label, EditorStyles.toolbarButton) && (mode != value || PaletteToolActive)) SetMode(value);
        }

        private void DrawBindings()
        {
            var root = MapEditorWorkspace.Current;
            if (root != null)
            {
                EditorGUILayout.LabelField("Map · " + root.displayName + (SceneManager.GetActiveScene().isDirty ? "  *" : ""), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(string.IsNullOrEmpty(MapEditorWorkspace.AssetPath) ? "未保存为 Map 预制体" : MapEditorWorkspace.AssetPath, EditorStyles.miniLabel);
                mapSettings = EditorGUILayout.Foldout(mapSettings, "地图设置 · 编号 / 名称 / 自动范围 / 出口数量", true);
                if (mapSettings)
                {
                    EditComponent(root, ("mapId", "地图编号"), ("displayName", "地图名称"), ("activeExtractionCount", "每局启用撤离点数"));
                    var bounds = root.Bounds;
                    EditorGUILayout.LabelField("地图范围（自动）", bounds.size == Vector3Int.zero ? "尚未绘制地形" :
                        $"{bounds.size.x} × {bounds.size.y} 格 · 起点 ({bounds.xMin}, {bounds.yMin})");
                    EditorGUILayout.HelpBox("范围随实际瓦片自动更新，不限制绘制；空白区域不是可通行地面。", MessageType.None);
                }
            }
            bindings = EditorGUILayout.Foldout(bindings, "场景绑定 · " + (map == null ? "未指定地图" : map.name) + " · 搜索区域 " + (region == null ? "未指定" : region.regionName), true);
            if (!bindings) return;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("目标 Tilemap", map, typeof(Tilemap), true);
                EditorGUILayout.ObjectField("导航", navigation, typeof(TilemapPathfinder2D), true);
            }
            var nextRegion = (RegionContainerManager)EditorGUILayout.ObjectField("新容器归属区域", region, typeof(RegionContainerManager), true);
            if (EditorGUI.EndChangeCheck())
            {
                if (ValidBinding(nextRegion)) { region = nextRegion; MarkNavigationDirty(); }
                else status = "容器归属必须是当前 MapRoot 内的搜索区域。";
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("重新识别")) { map = null; navigation = null; player = null; region = null; Bind(); }
                if (GUILayout.Button("新建搜索区域"))
                {
                    int group = LevelBuilderScene.Begin("新建搜索区域");
                    region = LevelBuilderScene.CreateRegion(SceneManager.GetActiveScene());
                    Selection.activeGameObject = region.gameObject;
                    Undo.CollapseUndoOperations(group);
                }
                if (GUILayout.Button("定位地图") && map != null) { Selection.activeGameObject = map.gameObject; SceneView.FrameLastActiveSceneView(); }
            }
        }

        private void DrawLibraryTabs()
        {
            EditorGUILayout.LabelField("素材库", EditorStyles.boldLabel);
            int next = GUILayout.Toolbar(category, Categories);
            if (next != category) { category = next; SetMode(Mode.Select); }
            if (category == 0)
            {
                next = GUILayout.Toolbar(terrainCategory, new[] { "Tile Palette", "模块", "障碍" });
                if (next != terrainCategory) { terrainCategory = next; SetMode(Mode.Select); }
            }
        }

        private void DrawLibrary()
        {
            if (category >= 2)
            {
                string label = category == 2 ? "放置出生候选点" : category == 3 ? "放置撤离候选点" : "拖动矩形创建敌人区域";
                if (GUILayout.Button(label)) SetMode(category == 2 ? Mode.Spawn : category == 3 ? Mode.Exit : Mode.EnemyRegion);
                EditorGUILayout.HelpBox(category == 4 ? "选中区域后配置敌人预制体和数量；仅开局生成，不锁定追击范围。" : "可放多个候选；每局选择结果固定。选中标记可修改属性。", MessageType.Info);
                return;
            }
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            libraryScroll = EditorGUILayout.BeginScrollView(libraryScroll);
            if (category == 0 && terrainCategory == 1)
                foreach (var value in library.modules.Where(m => m != null && Matches(m.name)))
                    if (AssetRow(value.cells.FirstOrDefault(c => c.tile != null).tile, value.name + "  " + value.size.x + "×" + value.size.y, module == value && mode == Mode.Module))
                    { module = value; turns = 0; SetMode(Mode.Module); }
            if (category == 1 || category == 0 && terrainCategory == 2)
                for (int i = 0; i < library.prefabs.Count; i++)
                {
                    var value = library.prefabs[i];
                    if (value.kind != PrefabKind || value.prefab == null || !Matches(value.label + value.prefab.name)) continue;
                    if (AssetRow(value.prefab, value.label, prefabIndex == i && mode == Mode.Prefab))
                    { prefabIndex = i; turns = 0; SetMode(Mode.Prefab); }
                }
            EditorGUILayout.EndScrollView();
            importAsset = EditorGUILayout.ObjectField("添加资源", importAsset, typeof(Object), false);
            using (new EditorGUI.DisabledScope(importAsset == null))
                if (GUILayout.Button("加入当前分类")) ImportAsset();
            if (GUILayout.Button("在 Inspector 编辑素材库")) Selection.activeObject = library;
        }

        private bool Matches(string text) => string.IsNullOrEmpty(search) || text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool AssetRow(Object asset, string label, bool selected)
        {
            using (new EditorGUILayout.HorizontalScope(selected ? EditorStyles.selectionRect : EditorStyles.helpBox))
            {
                var preview = asset == null ? null : AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
                GUILayout.Label(preview, GUILayout.Width(38), GUILayout.Height(38));
                return GUILayout.Button(label, EditorStyles.label, GUILayout.Height(38));
            }
        }

        private void ImportAsset()
        {
            if (category == 0 && terrainCategory == 0 && importAsset is TileBase addedTile)
            { Undo.RecordObject(library, "添加地块素材"); if (!library.tiles.Contains(addedTile)) library.tiles.Add(addedTile); }
            else if (category == 0 && terrainCategory == 1 && importAsset is LevelTileModule addedModule)
            { Undo.RecordObject(library, "添加地图模块"); if (!library.modules.Contains(addedModule)) library.modules.Add(addedModule); }
            else if ((category == 1 || category == 0 && terrainCategory == 2) && importAsset is GameObject prefab && PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                var kind = PrefabKind;
                if (kind == LevelAssetKind.Enemy && prefab.GetComponent<EnemyCombat2D>() == null ||
                    kind == LevelAssetKind.Container && prefab.GetComponent<SearchableContainer>() == null ||
                    kind == LevelAssetKind.Obstacle && (prefab.GetComponentInChildren<Collider2D>() == null ||
                        prefab.GetComponentsInChildren<Rigidbody2D>(true).Any(b => b.bodyType != RigidbodyType2D.Static)))
                { status = "类型不匹配：敌人/容器需要对应根组件；障碍需要静态 2D 碰撞体。"; return; }
                Undo.RecordObject(library, "添加预制体素材");
                library.prefabs.Add(new LevelPrefabEntry { label = prefab.name, prefab = prefab, kind = kind,
                    scale = prefab.transform.localScale, allowRotation = kind == LevelAssetKind.Obstacle });
            }
            else { status = "请选择匹配分类的项目资源。"; return; }
            EditorUtility.SetDirty(library); AssetDatabase.SaveAssets(); importAsset = null;
        }

        private void DrawProperties()
        {
            EditorGUILayout.LabelField("当前工具 · " + (PaletteToolActive ? "原生 Tile Palette" : ModeLabel()), EditorStyles.boldLabel);
            if (PaletteToolActive) EditorGUILayout.HelpBox("在上方调色板选取单格或框选组合，再到 Scene 绘制。笔刷、矩形、擦除、填充及旋转翻转均为 Unity 原生工具。", MessageType.Info);
            if (mode is Mode.Brush or Mode.Rectangle or Mode.Erase or Mode.Pick)
                tile = (TileBase)EditorGUILayout.ObjectField("绘制地块", tile, typeof(TileBase), false);
            if (mode == Mode.Rectangle) EditorGUILayout.LabelField("拖动矩形铺设；按住 Shift 拖动擦除。", EditorStyles.wordWrappedMiniLabel);
            if (mode == Mode.Capture) EditorGUILayout.HelpBox("在 Scene 拖出矩形，松开后保存地图模块。空格子也会记录；模块只包含地形。", MessageType.Info);
            if (mode == Mode.Module && module != null)
            {
                EditorGUILayout.ObjectField("地图模块", module, typeof(LevelTileModule), false);
                DrawRotation();
                overwrite = EditorGUILayout.Toggle("覆盖已有地块（含空格）", overwrite);
                EditorGUILayout.LabelField("范围", module.RotatedSize(turns).ToString());
            }
            if (mode == Mode.Prefab && Entry != null)
            {
                EditorGUILayout.ObjectField("放置素材", Entry.prefab, typeof(GameObject), false);
                EditorGUILayout.LabelField("放置缩放", Entry.scale.ToString("F2"));
                if (Entry.allowRotation && Entry.kind == LevelAssetKind.Obstacle) DrawRotation();
                else EditorGUILayout.LabelField("方向", "沿用预制体方向");
                if (Entry.kind == LevelAssetKind.Container && region == null)
                    EditorGUILayout.HelpBox("请展开场景绑定，指定或新建搜索区域。", MessageType.Warning);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("选中对象 · 实例属性", EditorStyles.boldLabel);
            GameObject selected = Selection.activeGameObject;
            if (selected == null || selected.scene != SceneManager.GetActiveScene() || !ValidBinding(selected.transform))
            { EditorGUILayout.LabelField("在 Scene 或 Hierarchy 选择场景对象。", EditorStyles.wordWrappedMiniLabel); return; }
            var enemy = selected.GetComponentInParent<EnemyCombat2D>();
            var box = selected.GetComponentInParent<SearchableContainer>();
            var selectedRegion = selected.GetComponent<RegionContainerManager>();
            var selectedPlayer = selected.GetComponentInParent<PlayerShooter2D>();
            Transform target = enemy != null ? enemy.transform : box != null ? box.transform :
                selectedPlayer != null ? selectedPlayer.transform : LayoutRoot(selected.transform) ?? selected.transform;
            EditorGUILayout.ObjectField("对象", target.gameObject, typeof(GameObject), true);
            bool editable = IsLayoutObject(target) || player != null && target == player.transform;
            using (new EditorGUI.DisabledScope(!editable))
            {
                EditorGUI.BeginChangeCheck();
                Vector3 positionValue = EditorGUILayout.Vector3Field("世界位置", target.position);
                if (EditorGUI.EndChangeCheck())
                { Undo.RecordObject(target, "修改布局位置"); target.position = snap && map != null ? LevelBuilderScene.Snap(map, positionValue) : positionValue; LevelBuilderScene.Changed(target); MarkNavigationDirty(); }
            }
            var spawnMarker = selected.GetComponent<PlayerSpawnPoint2D>();
            var extraction = selected.GetComponentInParent<ExtractionPoint2D>();
            var enemyRegion = selected.GetComponent<EnemyRegion2D>();
            if (spawnMarker != null) EditComponent(spawnMarker, ("displayName", "出生点名称"));
            else if (extraction != null) EditComponent(extraction, ("displayName", "撤离点名称"), ("interactDistance", "交互距离"), ("holdSeconds", "按住 E 时间"));
            else if (enemyRegion != null)
            {
                EditComponent(enemyRegion, ("displayName", "区域名称"), ("size", "矩形尺寸"), ("enemies", "敌人列表 / 数量"));
                EditorGUILayout.LabelField("敌人预制体可从 Project 中拖入；区域仅控制开局分布。", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                    foreach (var entry in library.prefabs.Where(e => e.kind == LevelAssetKind.Enemy))
                        if (GUILayout.Button("添加" + entry.label))
                        {
                            Undo.RecordObject(enemyRegion, "配置区域敌人");
                            enemyRegion.enemies.Add(new EnemyRegionEntry { prefab = entry.prefab.GetComponent<EnemyCombat2D>() });
                            LevelBuilderScene.Changed(enemyRegion); checkStale = true;
                        }
            }
            else if (enemy != null)
            {
                EditComponent(enemy, ("attackKind", "攻击类型"), ("projectilePrefab", "远程弹丸"), ("moveSpeed", "移动速度"),
                    ("sightRange", "感知距离"), ("attackRange", "攻击距离"), ("damage", "伤害"), ("attackInterval", "攻击间隔"),
                    ("windupSeconds", "攻击前摇"), ("chaseDistance", "追击距离"));
                EditComponent(enemy.GetComponent<CombatHealth2D>(), ("maxHealth", "生命上限"));
            }
            else if (box != null)
            {
                EditComponent(box, ("displayName", "显示名称"), ("type", "容器类型"), ("searchSeconds", "单件搜索时间"),
                    ("columns", "列数"), ("rows", "行数"), ("lootMode", "物资模式"), ("overrideTable", "单箱掉落表"), ("defaultTable", "兜底掉落表"));
                var oldRegion = box.FindNearestRegion();
                var next = (RegionContainerManager)EditorGUILayout.ObjectField("继承区域", oldRegion, typeof(RegionContainerManager), true);
                if (next != oldRegion && next != null && ValidBinding(next))
                { Undo.SetTransformParent(box.transform, next.transform, "调整容器区域"); LevelBuilderScene.Changed(box.transform); checkStale = true; }
                var rule = new LootRuleResolver().Resolve(box);
                EditorGUILayout.LabelField("当前规则来源", rule.Source.ToString());
                if (GUILayout.Button("打开容器详细配置"))
                { var window = GetWindow<RegionContainerWindow>(); window.SetRegion(box.FindNearestRegion()); window.FocusContainer(box); }
            }
            else if (selectedRegion != null)
            {
                EditComponent(selectedRegion, ("regionName", "区域名称"), ("config", "共享区域配置"));
                if (GUILayout.Button("设为新容器归属区域")) region = selectedRegion;
                if (GUILayout.Button("打开区域详细配置")) GetWindow<RegionContainerWindow>().SetRegion(selectedRegion);
            }
            else if (IsLayoutObject(target))
            {
                EditorGUI.BeginChangeCheck();
                float angle = EditorGUILayout.FloatField("Z 旋转", target.eulerAngles.z);
                Vector3 scale = EditorGUILayout.Vector3Field("实例缩放", target.localScale);
                if (EditorGUI.EndChangeCheck())
                { Undo.RecordObject(target, "修改障碍实例"); target.rotation = Quaternion.Euler(0, 0, angle); target.localScale = scale; LevelBuilderScene.Changed(target); MarkNavigationDirty(); }
            }
            EditorGUILayout.LabelField("此处修改场景实例；掉落表与区域配置引用的是共享资源，详细窗口可另存副本。", EditorStyles.wordWrappedMiniLabel);
        }

        private void EditComponent(Component component, params (string field, string label)[] fields)
        {
            if (component == null) return;
            var serialized = new SerializedObject(component);
            serialized.Update();
            foreach (var field in fields) EditorGUILayout.PropertyField(serialized.FindProperty(field.field), new GUIContent(field.label));
            if (serialized.ApplyModifiedProperties()) { LevelBuilderScene.Changed(component); checkStale = true; SceneView.RepaintAll(); }
        }

        private void DrawRotation()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("方向 " + turns * 90 + "°");
                if (GUILayout.Button("↶ Q")) Rotate(-1);
                if (GUILayout.Button("↷ E")) Rotate(1);
            }
        }

        private void DrawHelpers()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("试玩设置", EditorStyles.boldLabel);
            testSeed = EditorGUILayout.IntField("测试种子", testSeed);
            var root = MapEditorWorkspace.Current;
            if (root != null)
            {
                var labels = new[] { "随机出生点" }.Concat(root.Spawns.Select(p => p.name)).ToArray();
                testSpawn = EditorGUILayout.Popup("测试出生点", Mathf.Clamp(testSpawn + 1, 0, labels.Length - 1), labels) - 1;
            }
            previewEnemies = EditorGUILayout.Toggle("Scene 预览敌人位置", previewEnemies);
            showAllCandidates = EditorGUILayout.Toggle("试玩 Gizmos 显示全部出口", showAllCandidates);
            helpers = EditorGUILayout.Foldout(helpers, "Scene 辅助显示与寻路预览", true);
            if (!helpers) return;
            EditorGUI.BeginChangeCheck();
            showGrid = EditorGUILayout.Toggle("局部网格", showGrid);
            showColliders = EditorGUILayout.Toggle("静态碰撞边界", showColliders);
            showRanges = EditorGUILayout.Toggle("敌人 / 搜索距离", showRanges);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();
            EditorGUILayout.LabelField("范围圈只表示距离，不表示无遮挡视野。", EditorStyles.wordWrappedMiniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("拾取路径起点")) SetMode(Mode.PathStart);
                if (GUILayout.Button("拾取路径终点")) SetMode(Mode.PathEnd);
                if (GUILayout.Button("清除路径")) { previewPath.Clear(); hasPathStart = hasPathEnd = false; SceneView.RepaintAll(); }
            }
            if (hasPathStart) EditorGUILayout.LabelField("起点 " + pathStart.ToString("F1") + (hasPathEnd ? " → " + pathEnd.ToString("F1") : ""));
        }

        private void DrawIssues()
        {
            EditorGUILayout.LabelField("布局检查" + (checkStale ? " · 修改后请重新检查" : " · " + issues.Count(i => i.error) + " 个错误"), EditorStyles.boldLabel);
            issueScroll = EditorGUILayout.BeginScrollView(issueScroll, GUILayout.Height(85));
            if (!checkStale && issues.Count == 0) EditorGUILayout.LabelField("检查通过。", EditorStyles.miniLabel);
            foreach (var issue in issues)
                if (GUILayout.Button((issue.error ? "错误 · " : "提示 · ") + issue.message, EditorStyles.wordWrappedMiniLabel) && issue.target != null)
                { Selection.activeObject = issue.target; EditorGUIUtility.PingObject(issue.target); SceneView.FrameLastActiveSceneView(); }
            EditorGUILayout.EndScrollView();
        }

        private void RunCheck()
        {
            FinishGesture(); SyncNavigation();
            issues = MapEditorWorkspace.Current == null ? new List<LayoutIssue> { new(null, "缺少 MapRoot。") } : MapEditorValidation.Check(MapEditorWorkspace.Current, testSeed); checkStale = false;
            status = "布局检查完成：" + issues.Count(i => i.error) + " 个错误，" + issues.Count(i => !i.error) + " 条提示。";
            Repaint();
        }

        private void Save(bool play)
        {
            FinishGesture(); ClearPreview(); SyncNavigation();
            if (play)
            {
                RunCheck();
                if (issues.Any(i => i.error)) { status = "请先处理下方布局错误，再保存并试玩。"; return; }
                if (EditorSettings.enterPlayModeOptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                { status = "试玩需要启用 Scene Reload，避免运行状态留在编辑场景；请在 Project Settings > Editor 中开启。"; return; }
            }
            string path = MapEditorWorkspace.SaveMap(false);
            if (path == null) { status = "Map 尚未保存。"; return; }
            status = "Map 预制体已保存：" + path;
            if (play) { SetMode(Mode.Select); MapEditorWorkspace.Play(path, testSeed, testSpawn, showAllCandidates); }
        }

        private bool IsLayoutObject(Transform value) => value != null &&
            (value.GetComponent<EnemyCombat2D>() != null || value.GetComponent<SearchableContainer>() != null ||
             value.GetComponent<RegionContainerManager>() != null || value.GetComponent<PlayerSpawnPoint2D>() != null ||
             value.GetComponent<ExtractionPoint2D>() != null || value.GetComponent<EnemyRegion2D>() != null ||
             value.parent != null && (value.parent.name == "LevelObstacles" || value.parent.name == "Obstacles"));

        private Transform LayoutRoot(Transform value)
        {
            var enemy = value.GetComponentInParent<EnemyCombat2D>();
            if (enemy != null) return enemy.transform;
            var box = value.GetComponentInParent<SearchableContainer>();
            if (box != null) return box.transform;
            var marker = value.GetComponentInParent<ExtractionPoint2D>();
            if (marker != null) return marker.transform;
            for (Transform ancestor = value; ancestor != null; ancestor = ancestor.parent)
                if (ancestor.parent != null && (ancestor.parent.name == "LevelObstacles" || ancestor.parent.name == "Obstacles")) return ancestor;
            return IsLayoutObject(value) ? value : null;
        }

        private void EditSelection(int action)
        {
            if (map == null) return;
            var roots = Selection.transforms.Where(t => ValidBinding(t)).Select(LayoutRoot)
                .Where(t => t != null).Distinct().ToArray();
            var selected = roots.Where(t => !roots.Any(other => t != other && t.IsChildOf(other))).ToArray();
            if (selected.Length == 0) { status = "请选择容器、出生点、撤离点、敌人区域或障碍。"; return; }
            int group = LevelBuilderScene.Begin(action == 0 ? "吸附所选" : action == 1 ? "复制布局对象" : "删除布局对象");
            if (action == 1)
            {
                Selection.objects = LevelBuilderScene.Duplicate(selected, map).Cast<Object>().ToArray();
                Undo.CollapseUndoOperations(group); SceneChanged(); return;
            }
            foreach (var value in selected)
            {
                if (action == 2) Undo.DestroyObjectImmediate(value.gameObject);
                else if (action == 0)
                { Undo.RecordObject(value, "吸附所选"); value.position = LevelBuilderScene.Snap(map, value.position); LevelBuilderScene.Changed(value); }
            }
            Undo.CollapseUndoOperations(group); SceneChanged();
        }

        private void CalculatePath(bool sync)
        {
            if (sync) SyncNavigation();
            previewPath.Clear();
            bool found = navigation != null && navigation.TryFindPath(pathStart, pathEnd, previewPath);
            status = found ? "路径可达，已显示绕行路线。" : "路径不可达：请检查起终点、通路宽度与墙体。";
            SceneView.RepaintAll(); Repaint();
        }

        private string ModeLabel() => mode switch
        {
            Mode.Select => "选择", Mode.Brush => "笔刷", Mode.Rectangle => "矩形", Mode.Erase => "擦除", Mode.Pick => "吸管",
            Mode.Module => "放置模块", Mode.Capture => "框选并保存模块", Mode.Prefab => "放置对象", Mode.Spawn => "放置出生点",
            Mode.Exit => "放置撤离点", Mode.EnemyRegion => "绘制敌人区域",
            Mode.PathStart => "拾取路径起点", _ => "拾取路径终点"
        };
    }
}
