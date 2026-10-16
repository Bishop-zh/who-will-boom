using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BombTavern.Runtime
{
    /// <summary>保存既有视图字段的预制体引用；只在实例化时绑定，不参与每帧布局。</summary>
    public sealed class UiPrefabBindings : MonoBehaviour
    {
        [Serializable] public class Entry { public string field; public UnityEngine.Object[] references; }
        public Entry[] entries;
        public static bool Exporting;
        public void Apply(object target)
        {
            foreach (var entry in entries)
            {
                var field = target.GetType().GetField(entry.field, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) throw new MissingFieldException(target.GetType().Name, entry.field);
                if (!field.FieldType.IsArray) { field.SetValue(target, entry.references[0]); continue; }
                var array = (Array)field.GetValue(target);
                if (array == null || array.Length != entry.references.Length || array.Rank > 2)
                    throw new InvalidOperationException("预制体绑定数组与视图不一致：" + entry.field);
                for (int i = 0; i < entry.references.Length; i++)
                    if (array.Rank == 1) array.SetValue(entry.references[i], i);
                    else array.SetValue(entry.references[i], i / array.GetLength(1), i % array.GetLength(1));
            }
        }
        public static RectTransform Load(string name, Transform parent, object target)
        {
            if (Exporting) return null;
            var prefab = Resources.Load<GameObject>("BombTavern/Prefabs/UI/" + name);
            if (prefab == null) return null;
            var instance = Instantiate(prefab, parent, false);
            instance.GetComponent<UiPrefabBindings>().Apply(target);
            return (RectTransform)instance.transform;
        }
        public static Button ButtonWithText(Transform root, string text)
        {
            foreach (var button in root.GetComponentsInChildren<Button>(true))
                if (button.name == "Btn_" + text || button.GetComponentInChildren<TMP_Text>(true)?.text == text) return button;
            throw new InvalidOperationException("预制体缺少按钮：" + text);
        }
    }
}
