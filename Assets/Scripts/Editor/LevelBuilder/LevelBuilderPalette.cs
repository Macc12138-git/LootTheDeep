using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.UIElements;

namespace LootTheDeep.LevelBuilder
{
    public sealed partial class LevelBuilderWindow
    {
        private VisualElement editorBody, paletteHost, libraryHost;
        private TilePaletteElement paletteElement;
        private Label paletteTarget;
        [NonSerialized] private bool paletteSession, changingPaletteTool;
        private GameObject previousPaintTarget, boundPaintTarget;
        [NonSerialized] private string paletteError;

        private static bool PaletteToolActive => ToolManager.activeToolType != null &&
            typeof(TilemapEditorTool).IsAssignableFrom(ToolManager.activeToolType);
        private bool ShowPalette => category == 0 && terrainCategory == 0;

        [MenuItem("LootTheDeep/关卡编辑器/Tile Palette 地形绘制")]
        private static void OpenPalette()
        {
            Open();
            GetWindow<LevelBuilderWindow>().ActivatePaletteTool();
        }

        public void CreateGUI()
        {
            RemovePaletteElement();
            var root = rootVisualElement;
            root.Clear();
            root.Add(new IMGUIContainer(DrawHeader) { style = { flexShrink = 0 } });
            editorBody = new TwoPaneSplitView(0, 380, TwoPaneSplitViewOrientation.Vertical)
                { style = { flexGrow = 1, minHeight = 0 } };
            var materials = new ScrollView(ScrollViewMode.Vertical)
                { name = "map-materials-pane", style = { minWidth = 0, minHeight = 240 } };
            materials.contentContainer.style.flexGrow = 1;
            materials.contentContainer.style.minHeight = 350;
            materials.Add(new IMGUIContainer(DrawLibraryTabs) { style = { flexShrink = 0 } });
            paletteHost = new VisualElement { name = "map-tile-palette", style = { flexGrow = 1, minHeight = 0 } };
            libraryHost = new IMGUIContainer(DrawLibrary) { style = { flexGrow = 1 } };
            materials.Add(paletteHost);
            materials.Add(libraryHost);
            editorBody.Add(materials);
            editorBody.Add(new IMGUIContainer(() =>
            {
                propertyScroll = EditorGUILayout.BeginScrollView(propertyScroll);
                DrawProperties(); DrawHelpers();
                EditorGUILayout.EndScrollView();
            }) { name = "map-properties-pane", style = { flexGrow = 1, minWidth = 0, minHeight = 100 } });
            root.Add(editorBody);
            root.Add(new IMGUIContainer(DrawFooter) { style = { flexShrink = 0 } });
            UpdatePaletteUI();
        }

        private void EnablePaletteCallbacks()
        {
            ToolManager.activeToolChanged += PaletteToolChanged;
            GridPaintingState.scenePaintTargetChanged += PaintTargetChanged;
            GridPaintingState.scenePaintTargetEdited += PaletteEdited;
        }

        private void DisablePalette()
        {
            ToolManager.activeToolChanged -= PaletteToolChanged;
            GridPaintingState.scenePaintTargetChanged -= PaintTargetChanged;
            GridPaintingState.scenePaintTargetEdited -= PaletteEdited;
            ReleasePaletteSession();
        }

        private void UpdatePaletteUI()
        {
            if (editorBody == null) return;
            bool canEdit = LevelBuilderScene.CanEdit && library != null;
            editorBody.style.display = canEdit ? DisplayStyle.Flex : DisplayStyle.None;
            if (!canEdit || map == null || !ValidBinding(map) || rootVisualElement.panel == null)
            {
                ReleasePaletteSession();
                return;
            }
            // Keep the painting service alive across categories so brush selections are not lost.
            if (!paletteSession && string.IsNullOrEmpty(paletteError))
            {
                previousPaintTarget = GridPaintingState.scenePaintTarget;
                bool hadPalette = GridPaintingState.palette != null;
                if (!NativePaletteSession.TryRegister(this, out paletteError))
                {
                    paletteHost.Clear();
                    paletteHost.Add(new HelpBox(paletteError, HelpBoxMessageType.Error));
                }
                else
                {
                    paletteSession = true;
                    if (!hadPalette)
                    {
                        var preferred = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Map/Tile/MapBase.prefab");
                        if (preferred != null && GridPaintingState.palettes.Contains(preferred)) GridPaintingState.palette = preferred;
                    }
                }
            }
            paletteHost.style.display = ShowPalette ? DisplayStyle.Flex : DisplayStyle.None;
            libraryHost.style.display = ShowPalette ? DisplayStyle.None : DisplayStyle.Flex;
            if (ShowPalette && paletteSession)
            {
                if (paletteElement == null) AddPaletteElement();
                BindPaletteTarget();
            }
            else RemovePaletteElement();
        }

        private void AddPaletteElement()
        {
            paletteHost.Clear();
            paletteTarget = new Label { style = { whiteSpace = WhiteSpace.Normal, marginLeft = 5, marginBottom = 4 } };
            paletteHost.Add(paletteTarget);
            paletteHost.Add(new Button(ActivatePaletteTool) { text = "在 Scene 绘制当前 Map" });
            var toolbar = new GridPaintingToolbar(this) { style = { flexShrink = 0, minHeight = 30 } };
            paletteHost.Add(toolbar);
            paletteElement = new TilePaletteElement { style = { flexGrow = 1, minHeight = 180 } };
            paletteHost.Add(paletteElement);
            // Attach creates the native clipboard. Lock only after that creation, never before.
            paletteElement.schedule.Execute(() =>
            {
                if (paletteElement != null && paletteElement.panel != null) paletteElement.clipboardUnlocked = false;
            });
            var brushes = new TilePaletteBrushesPopup("笔刷") { style = { flexShrink = 0 } };
            paletteHost.Add(brushes);
            var foldout = new Foldout { text = "原生笔刷设置", value = false };
            foldout.Add(new TilePaletteBrushInspectorElement { style = { height = 140 } });
            paletteHost.Add(foldout);
        }

        private void RemovePaletteElement()
        {
            if (paletteElement == null) return;
            paletteHost?.Clear(); // Detach releases native preview textures and event subscriptions.
            paletteElement = null;
            paletteTarget = null;
        }

        private void ReleasePaletteSession()
        {
            if (!paletteSession) { RemovePaletteElement(); return; }
            StopPaletteTool();
            paletteSession = false;
            RemovePaletteElement();
            if (GridPaintingState.scenePaintTarget == boundPaintTarget)
                GridPaintingState.scenePaintTarget = previousPaintTarget;
            boundPaintTarget = previousPaintTarget = null;
            NativePaletteSession.Unregister(this);
        }

        private void BindPaletteTarget()
        {
            if (!paletteSession || !ShowPalette || !LevelBuilderScene.CanEdit || map == null || !ValidBinding(map)) return;
            boundPaintTarget = map.gameObject;
            if (GridPaintingState.scenePaintTarget != boundPaintTarget) GridPaintingState.scenePaintTarget = boundPaintTarget;
            if (paletteTarget != null) paletteTarget.text = "绘制目标：" + MapEditorWorkspace.Current.name + " / Terrain / " + map.name;
        }

        private void PaintTargetChanged(GameObject target) => BindPaletteTarget();

        private void PaletteEdited(GameObject target)
        {
            if (target != null && map != null && target == map.gameObject && LevelBuilderScene.CanEdit)
            { LevelBuilderScene.Changed(map); MarkNavigationDirty(); Repaint(); }
        }

        private void ActivatePaletteTool()
        {
            if (!LevelBuilderScene.CanEdit || map == null) return;
            SetMode(Mode.Select);
            category = terrainCategory = 0;
            UpdatePaletteUI();
            if (!paletteSession) return;
            Selection.activeGameObject = map.gameObject;
            BindPaletteTarget();
            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
            status = "Tile Palette：上方取样或框选组合，Scene 绘制；原生工具条支持矩形、擦除、填充、旋转与翻转。";
        }

        private void StopPaletteTool()
        {
            if (!PaletteToolActive || !paletteSession) return;
            changingPaletteTool = true;
            try
            {
                // Initialize the brush outside a tool-change callback. Its lazy toolbar rebuild
                // may otherwise try to select a Tilemap tool recursively during deactivation.
                _ = GridPaintingState.gridBrush;
                // A previous persistent tool can itself be another Tilemap tool.
                Tools.current = Tool.Move;
            }
            finally { changingPaletteTool = false; }
        }

        private void PaletteToolChanged()
        {
            if (changingPaletteTool || !paletteSession || !LevelBuilderScene.CanEdit) return;
            if (PaletteToolActive)
            {
                FinishGesture();
                // Tool activation precedes the first native preview repaint.
                if (mode != Mode.Select && map != null) map.ClearAllEditorPreviewTiles();
                ReleaseSceneTool();
                mode = Mode.Select;
                category = terrainCategory = 0;
                BindPaletteTarget();
            }
            Repaint(); SceneView.RepaintAll();
        }
    }

    // Unity 6000.3 exposes the Palette UI but not its painter-interest lifecycle.
    // Isolate the two internal calls; do not copy package code or create a hidden Palette window.
    internal static class NativePaletteSession
    {
        private static readonly MethodInfo Register = typeof(GridPaintingState).GetMethod("RegisterPainterInterest", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo UnregisterMethod = typeof(GridPaintingState).GetMethod("UnregisterPainterInterest", BindingFlags.Static | BindingFlags.NonPublic);

        internal static bool TryRegister(object owner, out string error)
        {
            if (Register == null || UnregisterMethod == null)
            { error = "当前 Tilemap 包的绘制生命周期接口已变化，无法启用内嵌 Tile Palette。"; return false; }
            Register.Invoke(null, new[] { owner });
            error = null;
            return true;
        }

        internal static void Unregister(object owner) => UnregisterMethod.Invoke(null, new[] { owner });
    }
}
