using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Logging;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace GK2Mods.DataDump
{
    /// <summary>
    /// Loads WGO prefabs far from the scene and, for each: writes its hierarchy (meshes, sizes, textures),
    /// exports the textures as PNG and renders it with an orthographic camera at the game's 53.13° pitch.
    /// Output: BepInEx/dumps/studio/&lt;id&gt;/.
    /// </summary>
    internal class Studio
    {
        // "<wgo>" renders a game object; "<wgo>@<model>[@v]" applies a Storage Network model (vertical variant with @v).
        internal static readonly string[] DefaultIds =
        {
            "chest_good", "chest_good@central_storage", "chest_good@central_storage@v",
            "chest", "chest@zone_relay", "chest@zone_relay@v", "chest_rough", "chest_rough@connector", "chest_rough@connector@v",
        };

        private const float FixedOrthoSize = 0.75f;

        private static readonly Vector3 StagePosition = new Vector3(5000f, 0f, 5000f);
        private const float Pitch = 53.130104f;  // MainCamera

        private readonly ManualLogSource log;
        private readonly string root;

        internal Studio(ManualLogSource log, string dumpDir)
        {
            this.log = log;
            root = Path.Combine(dumpDir, "studio");
        }

        internal IEnumerator Run(IEnumerable<string> ids)
        {
            Directory.CreateDirectory(root);
            foreach (string spec in ids)
            {
                GameObject instance = null;
                string[] parts = spec.Split('@');
                string id = parts[0];
                string model = parts.Length > 1 ? parts[1] : null;
                bool vertical = parts.Length > 2 && parts[2] == "v";
                string dir = Path.Combine(root, spec.Replace('@', '_'));
                try
                {
                    string assetId = GameBalance.Me.GetDataOrNull<WGODef>(id)?.ResolveAssetId(id) ?? id;
                    var prefab = Addressables.LoadAssetAsync<GameObject>(WgoPartPool.GetAddressableKey(assetId)).WaitForCompletion();
                    if (prefab == null)
                    {
                        log.LogWarning($"Studio: {id} not found");
                        continue;
                    }
                    Directory.CreateDirectory(dir);
                    instance = UnityEngine.Object.Instantiate(prefab, StagePosition, Quaternion.identity);
                    instance.SetActive(true);
                }
                catch (Exception e)
                {
                    log.LogWarning($"Studio: {id} failed to load: {e.Message}");
                    continue;
                }

                // Let Awake/Start/OnEnable set up materials and property blocks.
                yield return null;
                yield return null;

                try
                {
                    if (vertical)
                    {
                        instance.transform.Find("Horizontal")?.gameObject.SetActive(false);
                        instance.transform.Find("Vertical")?.gameObject.SetActive(true);
                    }
                    if (model != null)
                    {
                        Type models = AppDomain.CurrentDomain.GetAssemblies()
                            .Select(a => a.GetType("GK2Mods.StorageNetwork.Models")).FirstOrDefault(t => t != null);
                        object ok = models?.GetMethod("Apply", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                            ?.Invoke(null, new object[] { instance, model });
                        log.LogInfo($"Studio: {spec} model applied: {ok ?? "Models type not found"}");
                    }
                }
                catch (Exception e)
                {
                    log.LogWarning($"Studio: {spec} model failed: {e}");
                }
                yield return null;

                try
                {
                    File.WriteAllText(Path.Combine(dir, "hierarchy.json"),
                        JsonConvert.SerializeObject(Describe(instance.transform, dir), Formatting.Indented));
                    Render(instance, Path.Combine(dir, "render.png"));
                    log.LogInfo($"Studio: {spec} done");
                }
                catch (Exception e)
                {
                    log.LogWarning($"Studio: {id} failed: {e}");
                }
                finally
                {
                    UnityEngine.Object.Destroy(instance);
                }
                yield return null;
            }
            log.LogInfo("Studio finished");
        }

        private object Describe(Transform t, string dir)
        {
            var info = new Dictionary<string, object>
            {
                ["name"] = t.name,
                ["active"] = t.gameObject.activeSelf,
                ["pos"] = V(t.localPosition),
                ["rot"] = V(t.localEulerAngles),
                ["scale"] = V(t.localScale),
            };
            var components = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null)
                {
                    continue;
                }
                components.Add(c.GetType().Name);
                switch (c)
                {
                    case MeshFilter mf when mf.sharedMesh != null:
                        ExportObj(mf.sharedMesh, Path.Combine(dir, "mesh_" + mf.sharedMesh.name.Replace(' ', '_') + ".obj"));
                        info["mesh"] = new
                        {
                            mf.sharedMesh.name,
                            verts = mf.sharedMesh.vertexCount,
                            subMeshes = mf.sharedMesh.subMeshCount,
                            bounds = new { center = V(mf.sharedMesh.bounds.center), size = V(mf.sharedMesh.bounds.size) },
                        };
                        break;
                    case Renderer r:
                        info["materials"] = r.sharedMaterials.Select(m => m == null ? null : $"{m.name} [{m.shader?.name}]").ToList();
                        info["materialProps"] = r.sharedMaterials.Where(m => m != null).Select(DescribeMaterial).ToList();
                        info["worldBounds"] = new { center = V(r.bounds.center - StagePosition), size = V(r.bounds.size) };
                        if (r is SpriteRenderer sr && sr.sprite != null)
                        {
                            info["sprite"] = sr.sprite.name;
                        }
                        break;
                    case Object3DMesh mesh:
                        info["subMeshTextures"] = mesh.subMeshes.Select(s => new
                        {
                            main = Export(s.texture, dir),
                            second = Export(s.texture2, dir),
                            normal = Export(s.normalMap, dir),
                            lut = Export(s.lutTexture, dir),
                            s.materialIndex,
                        }).ToList();
                        break;
                }
            }
            info["components"] = components;
            info["children"] = Enumerable.Range(0, t.childCount).Select(i => Describe(t.GetChild(i), dir)).ToList();
            return info;
        }

        private static object DescribeMaterial(Material m)
        {
            var props = new Dictionary<string, object>();
            Shader shader = m.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        props[name] = m.GetFloat(name);
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        props[name] = m.GetColor(name).ToString();
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        props[name] = m.GetTexture(name)?.name;
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
                        props[name] = m.GetVector(name).ToString();
                        break;
                }
            }
            return new { m.name, shader = shader.name, keywords = m.shaderKeywords, m.renderQueue, props };
        }

        private static void ExportObj(Mesh mesh, string file)
        {
            if (File.Exists(file) || !mesh.isReadable)
            {
                return;
            }
            var sb = new System.Text.StringBuilder();
            foreach (Vector3 v in mesh.vertices) sb.AppendLine(FormattableString.Invariant($"v {v.x} {v.y} {v.z}"));
            foreach (Vector2 uv in mesh.uv) sb.AppendLine(FormattableString.Invariant($"vt {uv.x} {uv.y}"));
            foreach (Vector3 n in mesh.normals) sb.AppendLine(FormattableString.Invariant($"vn {n.x} {n.y} {n.z}"));
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                sb.AppendLine($"g submesh{s}");
                int[] t = mesh.GetTriangles(s);
                for (int i = 0; i < t.Length; i += 3)
                {
                    sb.AppendLine($"f {t[i] + 1}/{t[i] + 1}/{t[i] + 1} {t[i + 1] + 1}/{t[i + 1] + 1}/{t[i + 1] + 1} {t[i + 2] + 1}/{t[i + 2] + 1}/{t[i + 2] + 1}");
                }
            }
            File.WriteAllText(file, sb.ToString());
        }

        private static float[] V(Vector3 v) => new[] { (float)Math.Round(v.x, 4), (float)Math.Round(v.y, 4), (float)Math.Round(v.z, 4) };

        private static string Export(Texture2D texture, string dir)
        {
            if (texture == null)
            {
                return null;
            }
            string file = Path.Combine(dir, "tex_" + texture.name + ".png");
            if (!File.Exists(file))
            {
                RenderTexture rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                RenderTexture previous = RenderTexture.active;
                try
                {
                    Graphics.Blit(texture, rt);
                    RenderTexture.active = rt;
                    var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                    copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                    copy.Apply();
                    File.WriteAllBytes(file, copy.EncodeToPNG());
                    UnityEngine.Object.Destroy(copy);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }
            return $"{texture.name} {texture.width}x{texture.height} {texture.filterMode}";
        }

        private static void Render(GameObject target, string file)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy).ToArray();
            if (renderers.Length == 0)
            {
                return;
            }
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }

            var cameraObject = new GameObject("GK2DataDump Studio Camera");
            var lightObject = new GameObject("GK2DataDump Studio Light");
            try
            {
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.16f, 0.15f, 0.18f, 1f);
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 200f;
                cameraObject.transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
                cameraObject.transform.position = bounds.center - cameraObject.transform.forward * 50f;
                // Same scale for every render, so models compare 1:1; centred on the object's base.
                camera.orthographicSize = FixedOrthoSize;
                cameraObject.transform.position = target.transform.position + new Vector3(0f, 0.35f, -0.1f) - cameraObject.transform.forward * 50f;

                var rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;
                var image = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(file, image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
                camera.targetTexture = null;
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
            finally
            {
                UnityEngine.Object.Destroy(cameraObject);
                UnityEngine.Object.Destroy(lightObject);
            }
        }
    }
}
