using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BombTavern.Core;
using BombTavern.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BombTavern.EditorTools
{
    // Explicit background validation only; never saves or regenerates the user's scene/UI.
    public static class BlastMarksValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks;
        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Blast mark validation failed: " + message);
            checks++;
        }
        static object Field(TabletScreen screen, string name) => typeof(TabletScreen).GetField(name, Private).GetValue(screen);
        static void UpdateMarks(TabletScreen screen) => typeof(TabletScreen).GetMethod("UpdateBlastMarks", Private).Invoke(screen, null);
        public static void ValidateAndBuild()
        {
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/BombTavern_Play.unity");
                var director = UnityEngine.Object.FindObjectOfType<GameDirector>();
                Check(director != null, "real scene director exists");
                var camera = Camera.main;
                Check(camera != null, "real scene main camera exists");
                var fixture = new GameObject("BlastValidationFixture");
                fixture.transform.position = new Vector3(0, 100, 0);
                var screen = fixture.AddComponent<TabletScreen>();
                foreach (var sceneScreen in UnityEngine.Object.FindObjectsOfType<TabletScreen>())
                {
                    if (sceneScreen == screen) continue;
                    screen.tileMaterial = sceneScreen.tileMaterial;
                    screen.slabMaterial = sceneScreen.slabMaterial;
                    break;
                }
                var lamp = new GameObject("HealthLamps");
                lamp.transform.SetParent(fixture.transform, false);
                var board = new RiskBoard(4, 2);
                for (int i = 0; i < 4; i++) board.MarkBlasted(i, 0, (byte)i);
                screen.BindShared(director, board);
                UpdateMarks(screen);
                var marks = (IDictionary)Field(screen, "_blastMarks");
                Check(marks.Count == 4, "only four blasted cells create marks");
                var block = new MaterialPropertyBlock();
                for (int i = 0; i < 4; i++)
                {
                    var mark = (Transform)marks[i];
                    Check(mark.childCount == 4, "X has two pulsing bars and two seat-color cores");
                    Check(mark.GetComponentsInChildren<Collider>().Length == 0, "marks cannot intercept clicks");
                    screen.TryGetCellWorldPosition(i, 0, out var cell);
                    Check(Mathf.Abs(Vector3.Distance(mark.position, cell) - .026f) < .0001f, "view offset height");
                    Check(Vector3.Dot(mark.forward, (mark.position - camera.transform.position).normalized) > .999f, "camera-facing billboard");
                    var renderers = mark.GetComponentsInChildren<Renderer>();
                    for (int part = 0; part < renderers.Length; part++)
                    {
                        var renderer = renderers[part];
                        renderer.GetPropertyBlock(block);
                        var expected = director.ColorOfSeat(i);
                        Check(Vector4.Distance(block.GetColor("_Color"), expected) < .0001f, "seat color");
                        var emission = block.GetColor("_EmissionColor");
                        float glow = 2.4f + 1.8f * Mathf.Abs(Mathf.Sin(Time.time * 3.5f + i * .7f));
                        Check(Vector4.Distance(emission, expected * (part < 2 ? glow : .6f)) < .001f, "pulse/core emission formula");
                    }
                }
                var tile = fixture.GetComponentInChildren<CellTile>();
                tile.tileRenderer.GetPropertyBlock(block);
                Check(Vector4.Distance(block.GetColor("_EmissionColor"), new Color(.85f, .16f, .08f) * 2.2f) < .001f, "stronger blasted tile emission");
                var reset = board.Get(0, 0); reset.blasted = false; board.Set(0, 0, reset);
                UpdateMarks(screen);
                Check(!((Transform)marks[0]).gameObject.activeSelf, "restored cell hides mark");
                typeof(TabletScreen).GetField("showBlastMarks", Private).SetValue(screen, false);
                UpdateMarks(screen);
                Check(!((Transform)Field(screen, "_blastRoot")).gameObject.activeSelf, "private toggle hides all marks");
                foreach (var name in new[] { "showBlastMarks", "blastMarkScale", "blastMarkFloatHeight", "blastMarkPulseSpeed" })
                {
                    var field = typeof(TabletScreen).GetField(name, Private);
                    Check(field.IsPrivate && !Attribute.IsDefined(field, typeof(SerializeField)), "new config not serialized: " + name);
                }
                typeof(TabletScreen).GetField("showBlastMarks", Private).SetValue(screen, true);
                board.MarkBlasted(0, 0, 0);
                UpdateMarks(screen);
                camera.transform.position = fixture.transform.position + new Vector3(0, .45f, -.22f);
                camera.transform.LookAt(fixture.transform.position);
                UpdateMarks(screen);
                Directory.CreateDirectory("Build/BlastMarks_2026-10-10");
                var rt = new RenderTexture(1200, 800, 24);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
                File.WriteAllBytes("Build/BlastMarks_2026-10-10/blast-marks-four-seats.png", image.EncodeToPNG());
                // Render exact waveform limits without changing production timing or the saved scene.
                foreach (float limit in new[] { 2.4f, 4.2f })
                {
                    for (int seat = 0; seat < 4; seat++)
                    {
                        var mark = (Transform)marks[seat];
                        for (int part = 0; part < 2; part++)
                        {
                            var renderer = mark.GetChild(part).GetComponent<Renderer>();
                            renderer.GetPropertyBlock(block);
                            block.SetColor("_EmissionColor", director.ColorOfSeat(seat) * limit);
                            renderer.SetPropertyBlock(block);
                        }
                    }
                    camera.Render(); image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
                    File.WriteAllBytes("Build/BlastMarks_2026-10-10/blast-marks-pulse-" + (limit < 3f ? "low" : "high") + ".png", image.EncodeToPNG());
                }
                camera.targetTexture = null; RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt);
                screen.BindShared(director, new RiskBoard(3, 3));
                Check(((IDictionary)Field(screen, "_blastMarks")).Count == 0, "new board clears marks");
                Check(((IDictionary)Field(screen, "_blastMarkRenderers")).Count == 0, "new board clears renderers");
                Check(((IDictionary)Field(screen, "_blastMarkSeat")).Count == 0, "new board clears seats");
                Check((Transform)Field(screen, "_blastRoot") == null, "new board destroys old root");
                Check(lamp != null, "user-owned health lamps survive cleanup");
                UnityEngine.Object.DestroyImmediate(fixture);
                var hudFixture = new GameObject("HudValidationFixture", typeof(RectTransform), typeof(Canvas));
                var hud = hudFixture.AddComponent<GameHudUgui>();
                typeof(GameHudUgui).GetMethod("BuildHoverCard", Private).Invoke(hud, new object[] { hudFixture.GetComponent<RectTransform>() });
                var title = (TMPro.TMP_Text)typeof(GameHudUgui).GetField("_hoverTitle", Private).GetValue(hud);
                Check(!title.richText, "player nicknames are literal text");
                Check(title.enableAutoSizing && title.overflowMode == TMPro.TextOverflowModes.Ellipsis, "long nickname fits existing card without layout edits");
                title.transform.parent.gameObject.SetActive(true);
                foreach (var nickname in new[] { "一二三四五六七八九十一二三四五六", "<size=200>hi" })
                {
                    title.text = "(0,0)  已被 " + nickname + " 引爆";
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)title.transform.parent);
                    title.ForceMeshUpdate();
                    foreach (var character in title.textInfo.characterInfo)
                        if (character.isVisible) Check(character.topRight.x <= title.rectTransform.rect.xMax + .1f, "nickname rendered inside card");
                }
                UnityEngine.Object.DestroyImmediate(hudFixture);
                Debug.Log("[BlastMarksValidation] PASS " + checks + " checks; scene changes discarded before build");
                // Reload instead of saving camera/fixture changes.
                EditorSceneManager.OpenScene("Assets/Scenes/BombTavern_Play.unity");
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-btValidationOnly") >= 0) EditorApplication.Exit(0);
                else PlayerBuilder.BuildWindows();
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
