using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LiarsBar.EditorTools
{
    /// <summary>
    /// 骗子酒馆场景构建共享工具箱：材质库、程序化网格、原语助手。
    /// </summary>
    public static class LiarsBarKit
    {
        public const string RootFolder = "Assets/LiarsBar";
        public const string MaterialFolder = RootFolder + "/Materials";
        public const string MeshFolder = RootFolder + "/Meshes";

        static readonly Dictionary<string, Material> MatCache = new Dictionary<string, Material>();

        // ---------------------------------------------------------------- 调色板
        public static class Pal
        {
            public static readonly Color WoodDark   = Hex("3A2614");
            public static readonly Color Wood       = Hex("6E4526");
            public static readonly Color WoodLight  = Hex("94643A");
            public static readonly Color WoodWarm   = Hex("A9743F");
            public static readonly Color Felt        = Hex("1F6B3C");
            public static readonly Color FeltDark    = Hex("164D2B");
            public static readonly Color Brass       = Hex("C9A227");
            public static readonly Color BrassDark   = Hex("8A6E17");
            public static readonly Color Metal       = Hex("4A4A54");
            public static readonly Color Iron        = Hex("2C2C33");
            public static readonly Color BombShell   = Hex("26262E");
            public static readonly Color White       = Hex("F2F0EA");
            public static readonly Color Ink         = Hex("141418");
            public static readonly Color Card        = Hex("EFE7D2");
            public static readonly Color Glass       = Hex("A8DCEA");
            public static readonly Color Fuse        = Hex("8A6B4A");
            public static readonly Color Spark       = Hex("FFB03A");
            public static readonly Color LampGlow    = Hex("FFD79A");
            public static readonly Color FireGlow    = Hex("FF7A2E");
            public static readonly Color BottleGreen = Hex("2F6B3A");
            public static readonly Color BottleAmber = Hex("9A5A18");
            public static readonly Color BottleRed   = Hex("8E2B2B");

            public static readonly Color Red    = Hex("E0433F");
            public static readonly Color Blue   = Hex("3B7DE0");
            public static readonly Color Green  = Hex("3BC46B");
            public static readonly Color Yellow = Hex("E8C33B");
            public static readonly Color Purple = Hex("9257D6");

            static Color Hex(string h)
            {
                Color c;
                ColorUtility.TryParseHtmlString("#" + h, out c);
                return c;
            }
        }

        // ---------------------------------------------------------------- 文件夹
        public static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/LiarsBar"))
                AssetDatabase.CreateFolder("Assets", "LiarsBar");
            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder(RootFolder, "Materials");
            if (!AssetDatabase.IsValidFolder(MeshFolder))
                AssetDatabase.CreateFolder(RootFolder, "Meshes");
        }

        // ---------------------------------------------------------------- 材质
        public static Material Mat(string key, Color color, float smoothness = 0.22f, float metallic = 0f, Color? emission = null)
        {
            if (MatCache.TryGetValue(key, out var cached) && cached != null) return cached;

            string path = MaterialFolder + "/" + key + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = Shader.Find("Standard");
            mat.color = color;
            mat.SetFloat("_Glossiness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            mat.SetInt("_ZWrite", 1);
            mat.DisableKeyword("_ALPHABLEND_ON");

            if (emission.HasValue && emission.Value.maxColorComponent > 0.001f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", emission.Value);
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }

            EditorUtility.SetDirty(mat);
            MatCache[key] = mat;
            return mat;
        }

        // ---------------------------------------------------------------- 网格
        /// <summary>
        /// 按 2D 剖面（x=半径, y=高度）绕 Y 轴旋转生成网格。用于卡通炸弹弹体等回转体。
        /// </summary>
        public static Mesh Revolve(string name, Vector2[] profile, int segments = 28)
        {
            string path = MeshFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;

            int n = profile.Length;
            var verts = new List<Vector3>(segments * n);
            var uvs = new List<Vector2>(segments * n);

            for (int i = 0; i < segments; i++)
            {
                float u = (float)i / (segments - 1);
                float ang = u * Mathf.PI * 2f;
                float cos = Mathf.Cos(ang), sin = Mathf.Sin(ang);
                for (int j = 0; j < n; j++)
                {
                    verts.Add(new Vector3(profile[j].x * cos, profile[j].y, profile[j].x * sin));
                    uvs.Add(new Vector2(u, (float)j / (n - 1)));
                }
            }

            var tris = new List<int>();
            for (int i = 0; i < segments - 1; i++)
            {
                for (int j = 0; j < n - 1; j++)
                {
                    int a = i * n + j;
                    int b = (i + 1) * n + j;
                    int c = (i + 1) * n + j + 1;
                    int d = i * n + j + 1;
                    tris.Add(a); tris.Add(d); tris.Add(c);
                    tris.Add(a); tris.Add(c); tris.Add(b);
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ---------------------------------------------------------------- 原语
        public static GameObject Prim(PrimitiveType type, string name, Transform parent,
            Vector3 pos, Vector3 scale, Material mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        public static GameObject Empty(string name, Transform parent, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go;
        }

        public static GameObject Sphere(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
            => Prim(PrimitiveType.Sphere, name, parent, pos, scale, mat);

        public static GameObject Cube(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
            Vector3? euler = null)
        {
            var go = Prim(PrimitiveType.Cube, name, parent, pos, scale, mat);
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
            return go;
        }

        public static GameObject Cylinder(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
            Vector3? euler = null)
        {
            var go = Prim(PrimitiveType.Cylinder, name, parent, pos, scale, mat);
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
            return go;
        }

        public static GameObject Capsule(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
            Vector3? euler = null)
        {
            var go = Prim(PrimitiveType.Capsule, name, parent, pos, scale, mat);
            if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
            return go;
        }

        public static GameObject MeshObj(Mesh mesh, string name, Transform parent, Vector3 pos, Vector3 scale,
            Material mat, bool collider = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return go;
        }

        public static void FaceTo(GameObject go, Vector3 targetWorld)
        {
            var dir = targetWorld - go.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }
    }
}
