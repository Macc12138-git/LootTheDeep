using UnityEngine;
using UnityEngine.UI;

namespace LootTheDeep.Gameplay.Maps
{
    public sealed class MapView2D : MonoBehaviour
    {
        [SerializeField] private GameObject window;
        [SerializeField] private RawImage terrainImage;
        [SerializeField] private AspectRatioFitter aspect;
        [SerializeField] private RectTransform exitTemplate;
        [SerializeField] private Text title;
        private Texture2D texture;

        public bool IsReady => texture != null;
        public bool IsOpen => window != null && window.activeSelf;
        public Rect WorldBounds { get; private set; }

        public void Configure(GameObject panel, RawImage image, AspectRatioFitter fitter,
            RectTransform marker, Text label)
        { window = panel; terrainImage = image; aspect = fitter; exitTemplate = marker; title = label; }

        public void Initialize(MapRoot map)
        {
            ReleaseTexture();
            // The template's siblings are the exit markers owned by this view, not map objects.
            for (int i = exitTemplate.parent.childCount - 1; i >= 0; i--)
            {
                var child = exitTemplate.parent.GetChild(i);
                if (child != exitTemplate) Release(child.gameObject);
            }
            texture = MapTextureBuilder.Build(map, out Rect bounds);
            WorldBounds = bounds;
            terrainImage.texture = texture;
            aspect.aspectRatio = bounds.width / bounds.height;
            title.text = "地图 · " + map.displayName;
            foreach (var exit in map.Exits)
            {
                var marker = Instantiate(exitTemplate, exitTemplate.parent);
                marker.name = "ActiveExit";
                Vector2 uv = new((exit.transform.position.x - bounds.xMin) / bounds.width,
                    (exit.transform.position.y - bounds.yMin) / bounds.height);
                marker.anchorMin = marker.anchorMax = uv;
                marker.anchoredPosition = Vector2.zero;
                marker.gameObject.SetActive(true);
            }
            Show(false);
        }

        public void Show(bool visible) => window.SetActive(visible);

        private void ReleaseTexture()
        {
            if (terrainImage != null) terrainImage.texture = null;
            if (texture != null) Release(texture);
            texture = null;
        }

        private static void Release(Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private void OnDestroy() => ReleaseTexture();
    }
}
