using UnityEditor;
using UnityEngine;

namespace LootTheDeep.Weapons.EditorTools
{
    public sealed class GunConfigWindow : EditorWindow
    {
        public const string DefaultTablePath = "Assets/Data/Weapons/GunConfigs.asset";
        private GunConfigTableSO table;
        private Vector2 scroll;

        private static readonly string[] Fields =
        {
            "id", "displayName", "magazineCapacity", "roundsPerMinute", "reloadSeconds",
            "spreadDegrees", "baseDamage", "projectileSpeed", "projectilePrefab"
        };
        private static readonly string[] Labels =
        {
            "武器 ID", "名称", "弹容量", "射速（发/分）", "换弹（秒）",
            "分散半角（度）", "基础伤害", "子弹速度", "子弹预制体"
        };
        private static readonly float[] Widths = { 165, 150, 65, 100, 85, 105, 75, 75, 190 };

        [MenuItem("LootTheDeep/武器/枪械配置表")]
        public static void Open()
        {
            var window = GetWindow<GunConfigWindow>("枪械配置表");
            window.minSize = new Vector2(760f, 260f);
        }

        private void OnEnable()
        {
            if (table == null) table = AssetDatabase.LoadAssetAtPath<GunConfigTableSO>(DefaultTablePath);
        }

        private void OnGUI()
        {
            table = (GunConfigTableSO)EditorGUILayout.ObjectField("配置表", table, typeof(GunConfigTableSO), false);
            if (table == null) return;

            EditorGUILayout.LabelField("分散半角为每发相对瞄准方向的最大随机偏角；0 表示无分散。", EditorStyles.wordWrappedLabel);
            var serialized = new SerializedObject(table);
            serialized.Update();
            SerializedProperty guns = serialized.FindProperty("guns");
            int removeIndex = -1;
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < Labels.Length; i++) GUILayout.Label(Labels[i], EditorStyles.boldLabel, GUILayout.Width(Widths[i]));
            EditorGUILayout.EndHorizontal();
            for (int row = 0; row < guns.arraySize; row++)
            {
                EditorGUILayout.BeginHorizontal();
                var gun = guns.GetArrayElementAtIndex(row);
                for (int column = 0; column < Fields.Length; column++)
                    EditorGUILayout.PropertyField(gun.FindPropertyRelative(Fields[column]), GUIContent.none, GUILayout.Width(Widths[column]));
                if (GUILayout.Button("移除", GUILayout.Width(48))) removeIndex = row;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (removeIndex >= 0) guns.DeleteArrayElementAtIndex(removeIndex);
            serialized.ApplyModifiedProperties();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("添加枪械"))
            {
                Undo.RecordObject(table, "添加枪械配置");
                table.guns.Add(new GunConfig { id = "gun_" + System.Guid.NewGuid().ToString("N").Substring(0, 8), displayName = "新枪械" });
                EditorUtility.SetDirty(table);
            }
            if (GUILayout.Button("保存配置"))
            {
                if (table.Validate().Count == 0)
                {
                    AssetDatabase.SaveAssetIfDirty(table);
                    ShowNotification(new GUIContent("配置已保存"));
                }
                else ShowNotification(new GUIContent("请先修正下方配置错误"));
            }
            EditorGUILayout.EndHorizontal();
            foreach (string error in table.Validate()) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
