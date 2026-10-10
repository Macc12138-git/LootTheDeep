using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LootTheDeep.Inventory.EditorTools
{
    public sealed class RegionContainerWindow : EditorWindow
    {
        [SerializeField] private RegionContainerManager region;
        private readonly LootRuleResolver resolver = new();
        private readonly LootConfigEditorActions actions = new();
        private readonly LootPreviewService preview = new();
        private readonly HashSet<SearchableContainer> selected = new();
        private List<SearchableContainer> all = new();
        private List<SearchableContainer> filtered = new();
        private SearchableContainer inspected;
        private string search = "";
        private int typeFilter, sourceFilter, stateFilter, markerMode;
        private Vector2 listScroll, detailScroll;
        private LootTableSO batchTable;
        private ContainerType regionType;
        private LootTableSO regionTable;
        private bool showRegionDefaults;
        private bool showMarkers = true;
        private int sampleCount = 1000, previewSeed = 12345;
        private string simulation;
        private UnityEditor.Editor tableEditor;
        private double nextRefresh;
        private readonly string[] types = { "储物柜", "维修箱", "医疗柜", "设备箱" };
        private readonly string[] sources = { "缺少配置", "单独配置", "区域默认", "预制体默认", "固定原型" };

        public void SetRegion(RegionContainerManager value)
        {
            region = value;
            selected.Clear(); inspected = null; simulation = null;
            RefreshRows();
            Repaint();
        }
        private void OnEnable()
        {
            titleContent = new GUIContent("区域容器爆率控制");
            minSize = new Vector2(1080, 540);
            EditorApplication.hierarchyChanged += RefreshRows;
            Undo.undoRedoPerformed += RefreshRows;
            SceneView.duringSceneGui += DrawSceneMarkers;
            EditorApplication.update += TickRefresh;
        }
        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= RefreshRows;
            Undo.undoRedoPerformed -= RefreshRows;
            SceneView.duringSceneGui -= DrawSceneMarkers;
            EditorApplication.update -= TickRefresh;
            if (tableEditor != null) DestroyImmediate(tableEditor);
        }
        private void TickRefresh()
        {
            if (EditorApplication.timeSinceStartup < nextRefresh) return;
            nextRefresh = EditorApplication.timeSinceStartup + 1;
            // Also reflects edits made in another Inspector without touching runtime contents.
            RefreshRows();
        }
        public void RefreshRows()
        {
            if (region != null)
            {
                // Read-only discovery: window refresh never triggers runtime registration/initialization.
                all = region.GetComponentsInChildren<SearchableContainer>(true)
                    .Where(c => c.FindNearestRegion() == region).ToList();
            }
            else all.Clear();
            selected.RemoveWhere(c => c == null || !all.Contains(c));
            if (inspected != null && !all.Contains(inspected)) inspected = null;
            ApplyFilter();
            Repaint();
            SceneView.RepaintAll();
        }
        private void ApplyFilter()
        {
            filtered = all.Where(MatchesFilter).ToList();
        }
        private bool MatchesFilter(SearchableContainer c)
        {
            if (typeFilter > 0 && (int)c.Type != typeFilter - 1) return false;
            var rule = resolver.Resolve(c);
            if (sourceFilter > 0 && (int)rule.Source != sourceFilter - 1) return false;
            bool valid = resolver.ValidateContainer(c).Count == 0;
            if (stateFilter == 1 && !valid || stateFilter == 2 && valid) return false;
            return string.IsNullOrEmpty(search) ||
                (c.name + c.displayName + HierarchyPath(c.transform)).IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private void OnGUI()
        {
            var next = (RegionContainerManager)EditorGUILayout.ObjectField("区域", region, typeof(RegionContainerManager), true);
            if (next != region) SetRegion(next);
            if (region == null) { EditorGUILayout.HelpBox("选择场景区域，或在区域对象上添加 RegionContainerManager 后打开本窗口。", MessageType.Info); return; }
            EditorGUILayout.LabelField(region.regionName, EditorStyles.boldLabel);
            DrawToolbar();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                DrawRegionDefaults();
                DrawBatchActions();
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorGUILayout.HelpBox("运行中只读查看；当前表是配置预览，不代表已经生成的容器内容。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * .57f))) DrawList();
                using (new EditorGUILayout.VerticalScope()) DrawDetails();
            }
        }
        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                search = EditorGUILayout.TextField(search, GUILayout.MinWidth(90));
                typeFilter = EditorGUILayout.Popup(typeFilter, new[] { "全部类型" }.Concat(types).ToArray(), GUILayout.Width(95));
                sourceFilter = EditorGUILayout.Popup(sourceFilter, new[] { "全部来源" }.Concat(sources).ToArray(), GUILayout.Width(110));
                stateFilter = EditorGUILayout.Popup(stateFilter, new[] { "全部状态", "配置有效", "配置错误" }, GUILayout.Width(95));
                if (EditorGUI.EndChangeCheck()) { ApplyFilter(); SceneView.RepaintAll(); }
                if (GUILayout.Button("刷新", GUILayout.Width(55))) RefreshRows();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"共 {all.Count} · 筛选 {filtered.Count} · 已选 {selected.Count}（隐藏 {selected.Count(c => !filtered.Contains(c))}）");
                if (GUILayout.Button("选择筛选结果")) { foreach (var c in filtered) selected.Add(c); }
                if (GUILayout.Button("清空选择")) selected.Clear();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                showMarkers = EditorGUILayout.ToggleLeft("场景标记", showMarkers, GUILayout.Width(85));
                markerMode = EditorGUILayout.Popup(markerMode, new[] { "显示区域全部", "仅筛选结果", "仅已选容器" }, GUILayout.Width(130));
            }
        }
        private void DrawRegionDefaults()
        {
            showRegionDefaults = EditorGUILayout.Foldout(showRegionDefaults, "区域类型默认规则（影响继承它的容器）", true);
            if (!showRegionDefaults) return;
            var config = (RegionLootConfigSO)EditorGUILayout.ObjectField("区域配置", region.config, typeof(RegionLootConfigSO), false);
            if (config != region.config)
            {
                var serialized = new SerializedObject(region);
                serialized.FindProperty("config").objectReferenceValue = config;
                serialized.ApplyModifiedProperties();
                RefreshRows();
            }
            if (region.config == null)
            {
                if (GUILayout.Button("创建区域配置 SO"))
                {
                    string path = EditorUtility.SaveFilePanelInProject("创建区域配置", "RegionLootConfig", "asset", "选择保存位置");
                    if (!string.IsNullOrEmpty(path))
                    {
                        var created = CreateInstance<RegionLootConfigSO>();
                        AssetDatabase.CreateAsset(created, AssetDatabase.GenerateUniqueAssetPath(path));
                        Undo.RegisterCreatedObjectUndo(created, "创建区域配置");
                        var serialized = new SerializedObject(region);
                        serialized.FindProperty("config").objectReferenceValue = created;
                        serialized.ApplyModifiedProperties();
                    }
                }
                return;
            }
            EditorGUILayout.HelpBox("该区域配置 SO 可能被其他区域共享；修改会影响所有引用者。", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                regionType = (ContainerType)EditorGUILayout.Popup((int)regionType, types, GUILayout.Width(90));
                regionTable = (LootTableSO)EditorGUILayout.ObjectField(regionTable, typeof(LootTableSO), false);
                if (GUILayout.Button("设置", GUILayout.Width(55)) && regionTable != null)
                { actions.SetRegionDefault(region.config, regionType, regionTable); RefreshRows(); }
                if (GUILayout.Button("移除映射", GUILayout.Width(80)))
                { actions.SetRegionDefault(region.config, regionType, null); RefreshRows(); }
            }
            if (region.config.bindings != null)
                foreach (var binding in region.config.bindings)
                    if (binding != null)
                        EditorGUILayout.LabelField(types[(int)binding.type], binding.table == null ? "缺少表" : binding.table.name);
            foreach (string error in region.config.Validate()) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
        private void DrawBatchActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                batchTable = (LootTableSO)EditorGUILayout.ObjectField("选中容器指定表", batchTable, typeof(LootTableSO), false);
                using (new EditorGUI.DisabledScope(selected.Count == 0))
                {
                    using (new EditorGUI.DisabledScope(batchTable == null))
                        if (GUILayout.Button("批量指定")) { actions.BatchSetOverride(selected, batchTable); Changed(); }
                    if (GUILayout.Button("恢复继承")) { actions.BatchClearOverrides(selected); Changed(); }
                }
            }
        }
        private void DrawList()
        {
            EditorGUILayout.LabelField("选择 / 名称 / 类型 / 世界位置 / 来源 / 每份稀有及以上", EditorStyles.miniBoldLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            foreach (var c in filtered)
            {
                var rule = resolver.Resolve(c);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool value = EditorGUILayout.Toggle(selected.Contains(c), GUILayout.Width(18));
                        if (value) selected.Add(c); else selected.Remove(c);
                        if (GUILayout.Button(c.displayName, inspected == c ? EditorStyles.boldLabel : EditorStyles.label))
                        {
                            inspected = c; simulation = null;
                            if (Event.current.clickCount == 2) FocusContainer(c);
                        }
                        GUILayout.Label(types[(int)c.Type], GUILayout.Width(55));
                        if (GUILayout.Button("定位", GUILayout.Width(45))) FocusContainer(c);
                    }
                    EditorGUILayout.LabelField($"{c.transform.position:F1} | {sources[(int)rule.Source]} | {(rule.Table == null ? "—" : rule.Table.name)}", EditorStyles.miniLabel);
                    var errors = resolver.ValidateContainer(c);
                    if (errors.Count > 0) EditorGUILayout.LabelField("错误：" + errors[0], EditorStyles.wordWrappedMiniLabel);
                    else EditorGUILayout.LabelField(Summary(rule), EditorStyles.wordWrappedMiniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }
        private string Summary(ResolvedLootRule rule)
        {
            if (rule.Table == null) return "固定原型内容，不参与随机抽取";
            var parts = new List<string>();
            foreach (var group in rule.Table.groups)
            {
                double total = group.qualityWeights.Sum(w => (double)w);
                parts.Add($"{group.name} {group.minRolls}—{group.maxRolls}份；每份稀有+ {(group.qualityWeights[2] + group.qualityWeights[3]) / total:P1}");
            }
            return string.Join("\n", parts);
        }
        private void DrawDetails()
        {
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            if (inspected == null) { EditorGUILayout.LabelField("点击容器名称查看详情"); EditorGUILayout.EndScrollView(); return; }
            EditorGUILayout.LabelField(inspected.displayName, EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(HierarchyPath(inspected.transform), EditorStyles.wordWrappedLabel, GUILayout.Height(36));
            EditorGUILayout.LabelField("世界坐标", inspected.transform.position.ToString("F2"));
            EditorGUILayout.LabelField("格子容量", $"{inspected.Columns} × {inspected.Rows}");
            var rule = resolver.Resolve(inspected);
            EditorGUILayout.LabelField("生效来源", sources[(int)rule.Source]);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("场景定位")) FocusContainer(inspected);
                if (GUILayout.Button("选中对象")) Selection.activeGameObject = inspected.gameObject;
                if (GUILayout.Button("定位配置来源")) EditorGUIUtility.PingObject(rule.SourceObject);
            }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                var serialized = new SerializedObject(inspected);
                serialized.Update();
                EditorGUILayout.PropertyField(serialized.FindProperty("type"), new GUIContent("容器类型"));
                EditorGUILayout.PropertyField(serialized.FindProperty("lootMode"), new GUIContent("物资模式"));
                EditorGUILayout.PropertyField(serialized.FindProperty("defaultTable"), new GUIContent("容器默认表"));
                EditorGUILayout.PropertyField(serialized.FindProperty("overrideTable"), new GUIContent("单独配置表"));
                if (serialized.ApplyModifiedProperties()) Changed();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("恢复继承")) { actions.BatchClearOverrides(new[] { inspected }); Changed(); }
                    using (new EditorGUI.DisabledScope(rule.Table == null))
                        if (GUILayout.Button("创建独立副本"))
                        {
                            string path = EditorUtility.SaveFilePanelInProject("复制掉落表", inspected.name + "_Loot", "asset", "指定副本位置");
                            actions.CloneAndAssign(inspected, path); Changed();
                        }
                }
            }
            rule = resolver.Resolve(inspected);
            foreach (var error in resolver.ValidateContainer(inspected)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (!string.IsNullOrEmpty(inspected.InitializationError)) EditorGUILayout.HelpBox(inspected.InitializationError, MessageType.Error);
            if (rule.Table != null)
            {
                int uses = all.Count(c => resolver.Resolve(c).Table == rule.Table);
                EditorGUILayout.HelpBox($"编辑共享规则：当前区域扫描范围内 {uses} 个容器生效引用此表；其他区域可能也引用它。运行时已生成物资不会随配置变化。", MessageType.Info);
                UnityEditor.Editor.CreateCachedEditor(rule.Table, null, ref tableEditor);
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    EditorGUI.BeginChangeCheck();
                    tableEditor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck()) Changed();
                }
                EditorGUILayout.LabelField("理论概率", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(preview.CalculatePerRollProbabilities(rule.Table), EditorStyles.wordWrappedLabel);
                sampleCount = EditorGUILayout.IntSlider("模拟样本数", sampleCount, 1, 10000);
                previewSeed = EditorGUILayout.IntField("预览种子", previewSeed);
                if (GUILayout.Button("模拟生成（不改变真实容器）"))
                    simulation = preview.Simulate(rule.Table, inspected.Columns, inspected.Rows, sampleCount, previewSeed);
                if (!string.IsNullOrEmpty(simulation))
                    EditorGUILayout.HelpBox("上次点击模拟的快照：\n" + simulation, MessageType.Info);
            }
            EditorGUILayout.EndScrollView();
        }
        private void Changed() { simulation = null; RefreshRows(); }
        private string HierarchyPath(Transform value)
        {
            string path = value.name;
            while (value.parent != null) { value = value.parent; path = value.name + "/" + path; }
            return path;
        }
        public void FocusContainer(SearchableContainer container)
        {
            inspected = container;
            Selection.activeGameObject = container.gameObject;
            SceneView.lastActiveSceneView?.Frame(new Bounds(container.transform.position, Vector3.one * 4), false);
            SceneView.RepaintAll();
        }
        private void DrawSceneMarkers(SceneView sceneView)
        {
            if (!showMarkers || region == null) return;
            IEnumerable<SearchableContainer> visible = markerMode == 1 ? filtered : markerMode == 2 ? selected : all;
            Color previous = Handles.color;
            foreach (var c in visible)
            {
                if (c == null) continue;
                Handles.color = selected.Contains(c) || inspected == c ? Color.yellow : Color.cyan;
                float size = HandleUtility.GetHandleSize(c.transform.position) * .12f;
                if (Handles.Button(c.transform.position, Quaternion.identity, size, size, Handles.CubeHandleCap))
                { inspected = c; selected.Add(c); simulation = null; Repaint(); }
                Handles.Label(c.transform.position + Vector3.up * size * 1.5f,
                    $"{types[(int)c.Type]} · {c.displayName}{(c.OverrideTable != null ? " ★" : "")}");
                if (selected.Contains(c) || inspected == c)
                {
                    var renderer = c.GetComponent<SpriteRenderer>();
                    var bounds = renderer != null ? renderer.bounds : new Bounds(c.transform.position, Vector3.one);
                    Handles.DrawWireCube(bounds.center, bounds.size);
                }
            }
            Handles.color = previous;
        }
    }
}
