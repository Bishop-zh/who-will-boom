using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BombTavern.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BombTavern.EditorTools
{
    public static class LegacyUiPrefabBuilder
    {
        const string Folder = "Assets/Resources/BombTavern/Prefabs/UI/";
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        [MenuItem("工具/炸弹酒馆/17. 补齐既有弹窗预制体", false, 27)]
        public static void CreateMissing()
        {
            UiPrefabBindings.Exporting = true;
            try { ExportRules(); ExportResults(); ExportLobby(); AssetDatabase.SaveAssets(); }
            finally { UiPrefabBindings.Exporting = false; }
        }
        static void Invoke(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
        static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Private).GetValue(target);
        static RectTransform Root(string name) => new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        static void ExportRules()
        {
            if (File.Exists(Folder + "GameSetupRules.prefab")) return;
            var root = Root("GameSetupRules"); var view = root.gameObject.AddComponent<PreGameMenuUgui>();
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.sizeDelta = Vector2.zero;
            typeof(PreGameMenuUgui).GetField("_safe", Private).SetValue(view, root);
            Invoke(view, "BuildGameSetupPanel"); Save(root, view, "GameSetupRules");
        }
        static void ExportResults()
        {
            if (File.Exists(Folder + "GameOver.prefab")) return;
            var host = Root("ExportResults"); var view = host.gameObject.AddComponent<GameHudUgui>();
            Invoke(view, "BuildGameOver", host);
            Save(Get<RectTransform>(view, "_gameOver"), view, "GameOver"); UnityEngine.Object.DestroyImmediate(host.gameObject);
        }
        static void ExportLobby()
        {
            if (File.Exists(Folder + "LobbyPanel.prefab")) return;
            var host = Root("ExportLobby"); var view = host.gameObject.AddComponent<LobbyUI>();
            Invoke(view, "Build"); var canvas = Get<Canvas>(view, "_canvas");
            Save(Get<RectTransform>(view, "_panel"), view, "LobbyPanel");
            UnityEngine.Object.DestroyImmediate(canvas.gameObject); UnityEngine.Object.DestroyImmediate(host.gameObject);
        }
        static void Save(RectTransform root, Component view, string name)
        {
            var bindings = root.gameObject.AddComponent<UiPrefabBindings>(); var entries = new List<UiPrefabBindings.Entry>();
            foreach (var field in view.GetType().GetFields(Private))
            {
                if (field.Name == "_safe" || field.Name == "_canvas") continue;
                var value = field.GetValue(view); UnityEngine.Object[] references;
                if (value is Array array) references = array.Cast<object>().Select(v => v as UnityEngine.Object).ToArray();
                else if (value is UnityEngine.Object reference) references = new[] { reference };
                else continue;
                if (references.Length == 0 || !references.Any(r => r is Component c && (c.transform == root || c.transform.IsChildOf(root)))) continue;
                entries.Add(new UiPrefabBindings.Entry { field = field.Name, references = references });
            }
            bindings.entries = entries.ToArray();
            Bake(root);
            if (view.transform == root) UnityEngine.Object.DestroyImmediate(view);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, Folder + name + ".prefab");
            UnityEngine.Object.DestroyImmediate(root.gameObject);
        }
        static void Bake(RectTransform root)
        {
            var font = Resources.Load<TMP_FontAsset>("BombTavern/UI/Fonts/NotoSansSC-Dynamic");
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true)) { label.font = font; label.fontSharedMaterial = font.material; }
            foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true)) input.fontAsset = font;
            string folder = "Assets/Resources/BombTavern/UI/Generated"; Directory.CreateDirectory(folder);
            var saved = new Dictionary<Sprite, Sprite>();
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                var sprite = image.sprite;
                if (sprite == null || !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(sprite))) continue;
                if (!saved.TryGetValue(sprite, out var asset))
                {
                    var bytes = sprite.texture.EncodeToPNG(); string path = folder + "/Ui_" + Hash128.Compute(Convert.ToBase64String(bytes)) + ".png";
                    File.WriteAllBytes(path, bytes); AssetDatabase.ImportAsset(path);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.textureType = TextureImporterType.Sprite;
                    importer.spriteBorder = sprite.border; importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.SaveAndReimport();
                    asset = AssetDatabase.LoadAssetAtPath<Sprite>(path); saved[sprite] = asset;
                }
                image.sprite = asset;
            }
            foreach(var image in root.GetComponentsInChildren<Image>(true))
                if(image.name=="GameSetupPanel" || image.name=="ResultCard" || image.name=="LobbyPanel") image.sprite=StatisticsPrefabBuilder.CardSprite();
        }
    }
}
