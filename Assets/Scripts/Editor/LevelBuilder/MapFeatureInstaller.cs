using System;
using System.IO;
using LootTheDeep.Gameplay.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LootTheDeep.LevelBuilder
{
    public static class MapFeatureInstaller
    {
        [MenuItem("LootTheDeep/地图/安装地图界面")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请退出运行模式后安装地图界面。");
            var rig = PrefabUtility.LoadPrefabContents(MapEditorWorkspace.RigPath);
            try
            {
                var view = rig.GetComponentInChildren<MapView2D>(true);
                if (view == null) view = CreateView(rig.transform);
                var symbol = view.transform.Find("MapWindow/Panel/Viewport/MapContent/Exits/ExitTemplate/Symbol").GetComponent<Text>();
                symbol.fontSize = 16;
                symbol.verticalOverflow = VerticalWrapMode.Overflow;
                PrefabUtility.SaveAsPrefabAsset(rig, MapEditorWorkspace.RigPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(rig); }
            string message = $"MAP_FEATURE_INSTALLED: dataPath={Application.dataPath}; product={Application.productName}; scene={SceneManager.GetActiveScene().path}";
            File.WriteAllText("Temp/MapFeatureInstall.txt", message);
            Debug.Log(message);
        }

        internal static MapView2D CreateView(Transform parent)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/NotoSansCJKsc-Regular.otf");
            var root = new GameObject("MapUI", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(parent, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            canvas.pixelPerfect = true;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;

            var window = Rect(root.transform, "MapWindow", Vector2.zero, Vector2.one);
            var backdrop = window.gameObject.AddComponent<Image>();
            backdrop.color = new Color(.015f, .025f, .035f, .96f);
            // Blocks pointer events from reaching any underlying inventory UI.
            backdrop.raycastTarget = true;
            var panel = Rect(window, "Panel", new Vector2(.08f, .08f), new Vector2(.92f, .92f));
            var background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(.09f, .12f, .13f);
            background.raycastTarget = false;
            var title = Label(panel, "Title", font, "地图", new Vector2(.04f, .9f), new Vector2(.96f, .98f), 28);
            Label(panel, "Hint", font, "M / Esc  关闭地图", new Vector2(.04f, .02f), new Vector2(.96f, .09f), 21);
            var viewport = Rect(panel, "Viewport", new Vector2(.04f, .12f), new Vector2(.96f, .88f));
            var content = Rect(viewport, "MapContent", Vector2.zero, Vector2.one);
            var aspect = content.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            var image = Rect(content, "Terrain", Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
            var exits = Rect(content, "Exits", Vector2.zero, Vector2.one);
            var marker = Rect(exits, "ExitTemplate", new Vector2(.5f, .5f), new Vector2(.5f, .5f));
            marker.sizeDelta = new Vector2(26, 26);
            var markerImage = marker.gameObject.AddComponent<Image>();
            markerImage.color = new Color(.3f, 1f, .53f);
            markerImage.raycastTarget = false;
            var symbol = Label(marker, "Symbol", font, "出", Vector2.zero, Vector2.one, 16);
            symbol.verticalOverflow = VerticalWrapMode.Overflow;
            symbol.color = new Color(.025f, .12f, .06f);
            marker.gameObject.SetActive(false);
            var view = root.AddComponent<MapView2D>();
            view.Configure(window.gameObject, image, aspect, marker, title);
            window.gameObject.SetActive(false);
            return view;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Text Label(Transform parent, string name, Font font, string text,
            Vector2 min, Vector2 max, int size)
        {
            var label = Rect(parent, name, min, max).gameObject.AddComponent<Text>();
            label.font = font; label.text = text; label.fontSize = size;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(.88f, .94f, .9f);
            label.raycastTarget = false;
            return label;
        }
    }
}
