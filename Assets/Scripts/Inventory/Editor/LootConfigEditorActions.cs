using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LootTheDeep.Inventory.EditorTools
{
    public sealed class LootConfigEditorActions
    {
        public void BatchSetOverride(IEnumerable<SearchableContainer> selected, LootTableSO table)
        {
            if (table == null) return;
            ApplyOverrides(selected, table, "批量指定掉落表");
        }
        public void BatchClearOverrides(IEnumerable<SearchableContainer> selected)
        {
            ApplyOverrides(selected, null, "恢复区域继承");
        }
        private void ApplyOverrides(IEnumerable<SearchableContainer> selected, LootTableSO table, string label)
        {
            var targets = selected.Where(c => c != null).Distinct().ToArray();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);
            foreach (var container in targets)
            {
                var serialized = new SerializedObject(container);
                serialized.FindProperty("lootMode").enumValueIndex = (int)ContainerLootMode.RandomTable;
                serialized.FindProperty("overrideTable").objectReferenceValue = table;
                serialized.ApplyModifiedProperties();
            }
            Undo.CollapseUndoOperations(group);
        }
        public void SetRegionDefault(RegionLootConfigSO config, ContainerType type, LootTableSO table)
        {
            if (config == null) return;
            Undo.RecordObject(config, "修改区域类型默认表");
            config.bindings ??= new List<ContainerLootBinding>();
            config.bindings.RemoveAll(b => b != null && b.type == type);
            if (table != null) config.bindings.Add(new ContainerLootBinding { type = type, table = table });
            EditorUtility.SetDirty(config);
        }
        public LootTableSO CloneAndAssign(SearchableContainer container, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var current = new LootRuleResolver().Resolve(container);
            if (current.Table == null) return null;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("复制并指定独立掉落表");
            var clone = Object.Instantiate(current.Table);
            clone.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(clone, AssetDatabase.GenerateUniqueAssetPath(path));
            Undo.RegisterCreatedObjectUndo(clone, "创建独立掉落表");
            var serialized = new SerializedObject(container);
            serialized.FindProperty("lootMode").enumValueIndex = (int)ContainerLootMode.RandomTable;
            serialized.FindProperty("overrideTable").objectReferenceValue = clone;
            serialized.ApplyModifiedProperties();
            Undo.CollapseUndoOperations(group);
            AssetDatabase.SaveAssets();
            return clone;
        }
    }
}
