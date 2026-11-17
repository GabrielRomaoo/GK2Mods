using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GK2Mods.FactoryPlanner
{
    /// <summary>
    /// Custom world models (made by tools/models/planner_models.py): a quad mesh + pixel-art atlas, drawn with
    /// a copy of the game's own "Custom/3D Object" material so lighting, dithering and shadows match.
    /// On a tagged object the template chest's meshes stop rendering (colliders, interaction and the
    /// ground shadow stay) and the model is added under each orientation (horizontal / vertical).
    /// Nothing is saved: without the mod the object is the template chest again.
    /// </summary>
    internal static class Models
    {
        private const string ModelObjectName = "GK2FP_Model";

        private static readonly Dictionary<string, string> ModelByTag = new Dictionary<string, string>
        {
            [Content.DraftingTag] = "planning_board",
            [Content.ArchitectTag] = "architect_table",
        };

        private static readonly Dictionary<string, (Mesh mesh, Texture2D atlas)> cache = new Dictionary<string, (Mesh, Texture2D)>();
        private static readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();

        private static readonly MethodInfo LoadImage = AccessTools.Method(typeof(ImageConversion), nameof(ImageConversion.LoadImage),
            new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });

        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int UseNormalMap = Shader.PropertyToID("_UseNormalMap");

        /// <summary>Applies the tag's model to a WGO part, or removes a model left from a previous (pooled) use.</summary>
        internal static void Refresh(WgoPart part, string tag)
        {
            if (part == null)
            {
                return;
            }
            string model = tag != null && ModelByTag.TryGetValue(tag, out string m) ? m : null;
            ModelMarker marker = part.GetComponent<ModelMarker>();
            if (marker != null && marker.Model == model)
            {
                return;
            }
            if (marker != null)
            {
                marker.Remove();
                UnityEngine.Object.Destroy(marker);
            }
            if (model != null)
            {
                Apply(part.gameObject, model);
            }
        }

        /// <summary>Applies a model to a WGO part (also callable by reflection from a preview tool).</summary>
        internal static bool Apply(GameObject part, string model)
        {
            var marker = part.AddComponent<ModelMarker>();
            marker.Model = model;

            // One model per orientation root ("Horizontal" / "Vertical"); each owns its own chest meshes.
            foreach (Object3DMesh chestMesh in part.GetComponentsInChildren<Object3DMesh>(true))
            {
                var renderer = chestMesh.GetComponent<MeshRenderer>();
                var filter = chestMesh.GetComponent<MeshFilter>();
                if (renderer == null || filter == null || filter.sharedMesh == null)
                {
                    continue;
                }
                // The vertical variant is the chest turned a quarter. The console keeps facing the camera
                // and uses a narrower model that fits that footprint.
                Bounds b = filter.sharedMesh.bounds;
                string variant = b.size.z > b.size.x ? model + "_v" : model;
                if (!TryLoad(variant, out Mesh mesh, out Texture2D atlas))
                {
                    continue;
                }
                renderer.forceRenderingOff = true;
                marker.Hidden.Add(renderer);

                var go = new GameObject(ModelObjectName);
                go.layer = chestMesh.gameObject.layer;
                go.transform.SetParent(chestMesh.transform, false);
                go.transform.localPosition = new Vector3(b.center.x, 0f, b.center.z);

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var ours = go.AddComponent<MeshRenderer>();
                ours.sharedMaterial = MaterialFor(renderer.sharedMaterial);
                ours.shadowCastingMode = renderer.shadowCastingMode;
                ours.receiveShadows = renderer.receiveShadows;
                var block = new MaterialPropertyBlock();
                block.SetTexture(MainTex, atlas);
                block.SetFloat(UseNormalMap, 0f);
                ours.SetPropertyBlock(block);
                go.AddComponent<SelectionTintFollower>().Init(renderer, ours, atlas);
                marker.Added.Add(go);
            }
            return marker.Added.Count > 0;
        }

        private static Material MaterialFor(Material template)
        {
            if (!materials.TryGetValue(template, out Material material))
            {
                material = new Material(template) { name = template.name + " (GK2FP)" };
                material.DisableKeyword("USE_NORMAL_MAP");
                materials[template] = material;
            }
            return material;
        }

        private static bool TryLoad(string model, out Mesh mesh, out Texture2D atlas)
        {
            if (cache.TryGetValue(model, out var loaded))
            {
                (mesh, atlas) = loaded;
                return mesh != null;
            }
            mesh = null;
            atlas = null;
            try
            {
                string json = ReadText(model + ".json");
                byte[] png = ReadBytes(model + ".png");
                if (json == null || png == null)
                {
                    Plugin.Log.LogError($"Model {model}: embedded files missing.");
                    cache[model] = (null, null);
                    return false;
                }
                atlas = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "gk2fp_" + model,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                LoadImage.Invoke(null, new object[] { atlas, png, false });
                UnityEngine.Object.DontDestroyOnLoad(atlas);
                mesh = BuildMesh(model, JObject.Parse(json));
                UnityEngine.Object.DontDestroyOnLoad(mesh);
                cache[model] = (mesh, atlas);
                Plugin.Log.LogInfo($"Model {model}: {mesh.vertexCount} vertices, atlas {atlas.width}x{atlas.height}");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Model {model} failed to load: {e}");
                cache[model] = (null, null);
                return false;
            }
        }

        private static Mesh BuildMesh(string name, JObject data)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            foreach (JToken quad in data["quads"])
            {
                int start = vertices.Count;
                for (int i = 0; i < 4; i++)
                {
                    JToken v = quad["v"][i];
                    JToken uv = quad["uv"][i];
                    vertices.Add(new Vector3((float)v[0], (float)v[1], (float)v[2]));
                    uvs.Add(new Vector2((float)uv[0], (float)uv[1]));
                }
                // corners are bottom-left, bottom-right, top-right, top-left seen from the front
                triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
            var mesh = new Mesh { name = "gk2fp_" + name };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Stream Open(string file)
        {
            Assembly asm = typeof(Models).Assembly;
            foreach (string resource in asm.GetManifestResourceNames())
            {
                if (resource.EndsWith(".Models." + file, StringComparison.Ordinal))
                {
                    return asm.GetManifestResourceStream(resource);
                }
            }
            return null;
        }

        private static string ReadText(string file)
        {
            using (Stream s = Open(file))
            {
                return s == null ? null : new StreamReader(s).ReadToEnd();
            }
        }

        private static byte[] ReadBytes(string file)
        {
            using (Stream s = Open(file))
            {
                if (s == null)
                {
                    return null;
                }
                using (var memory = new MemoryStream())
                {
                    s.CopyTo(memory);
                    return memory.ToArray();
                }
            }
        }
    }

    /// <summary>Remembers what Models.Apply changed on a WGO part, so a pooled part can be restored.</summary>
    internal class ModelMarker : MonoBehaviour
    {
        internal string Model;
        internal readonly List<MeshRenderer> Hidden = new List<MeshRenderer>();
        internal readonly List<GameObject> Added = new List<GameObject>();

        internal void Remove()
        {
            // The Storage Network mod hides the same pooled chest meshes for its own models: leave them hidden
            // when its marker is on this part now.
            bool otherModel = false;
            foreach (MonoBehaviour b in GetComponents<MonoBehaviour>())
            {
                if (b != null && b != this && b.GetType().Name == nameof(ModelMarker))
                {
                    otherModel = true;
                }
            }
            foreach (MeshRenderer r in Hidden)
            {
                if (r != null && !otherModel)
                {
                    r.forceRenderingOff = false;
                }
            }
            foreach (GameObject go in Added)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }
            Hidden.Clear();
            Added.Clear();
        }
    }

    /// <summary>Copies the hover highlight the game puts on the (hidden) chest mesh onto the model.</summary>
    internal class SelectionTintFollower : MonoBehaviour
    {
        private static readonly int TintAmount = Shader.PropertyToID("_SelectionTintAmount");
        private static readonly int TintColor = Shader.PropertyToID("_SelectionTintColor");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int UseNormalMap = Shader.PropertyToID("_UseNormalMap");

        private MeshRenderer source, target;
        private Texture2D atlas;
        private MaterialPropertyBlock read, write;
        private float lastAmount = -1f;

        internal void Init(MeshRenderer source, MeshRenderer target, Texture2D atlas)
        {
            this.source = source;
            this.target = target;
            this.atlas = atlas;
            read = new MaterialPropertyBlock();
            write = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            if (source == null || target == null)
            {
                return;
            }
            source.GetPropertyBlock(read);
            float amount = read.GetFloat(TintAmount);
            if (Mathf.Approximately(amount, lastAmount))
            {
                return;
            }
            lastAmount = amount;
            write.Clear();
            write.SetTexture(MainTex, atlas);
            write.SetFloat(UseNormalMap, 0f);
            write.SetFloat(TintAmount, amount);
            Color color = read.GetColor(TintColor);
            if (color.a > 0f)
            {
                write.SetColor(TintColor, color);
            }
            target.SetPropertyBlock(write);
        }
    }

    /// <summary>Runs after the Storage Network mod's twin patch, so its marker is already in place.</summary>
    [HarmonyPatch(typeof(WgoPart), nameof(WgoPart.ReInitFromPool))]
    [HarmonyPriority(Priority.Low)]
    internal static class WgoPart_ReInitFromPool
    {
        private static void Postfix(WgoPart __instance, Wgo newWgo)
        {
            try
            {
                WgoData data = newWgo?.Data;
                // A construction site ("..._place") keeps the game's scaffold look.
                bool built = data != null && !data.id.EndsWith("_place", StringComparison.Ordinal);
                Models.Refresh(__instance, built ? data.CustomTag : null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Model refresh failed: {e}");
            }
        }
    }
}
