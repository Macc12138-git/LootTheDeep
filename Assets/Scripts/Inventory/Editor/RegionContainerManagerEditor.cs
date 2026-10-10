using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LootTheDeep.Inventory.EditorTools
{
    [CustomEditor(typeof(RegionContainerManager))]
    public sealed class RegionContainerManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var region = (RegionContainerManager)target;
            EditorGUILayout.LabelField("区域内容器", region.GetComponentsInChildren<SearchableContainer>(true)
                .Count(c => c.FindNearestRegion() == region).ToString());
            if (GUILayout.Button("区域容器爆率控制"))
            {
                var window = EditorWindow.GetWindow<RegionContainerWindow>();
                window.SetRegion(region);
                window.Show();
            }
            if (GUILayout.Button("刷新容器列表")) region.RefreshContainers();
        }
    }
}
