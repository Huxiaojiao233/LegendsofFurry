using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ContinentalFog
{
    [ExecuteAlways, DisallowMultipleComponent]
    [AddComponentMenu("Visuals/Continental Fog System")]
    public sealed class FogSystem : MonoBehaviour
    {
        [Header("地图范围 / Map bounds (local XZ)")]
        public Vector2 mapSize = new Vector2(40, 40);
        public Vector3 mapCenter;
        [Tooltip("可选：只指定地面网格根节点，不要包含角色、特效或天空。运行时每秒检测范围变化。")]
        public Transform boundsTarget;
        public bool autoFitBounds = true;
        [Min(0)] public float boundsPadding;

        [Header("远景大气 / Atmosphere")]
        public bool atmosphere = true;
        public Color fogColor = new Color(.53f, .64f, .70f, 1);
        public FogMode atmosphereMode = FogMode.Linear;
        [Min(0)] public float atmosphereStart = 28;
        [Min(.1f)] public float atmosphereEnd = 100;
        [Range(0, .1f)] public float density = .012f;

        [Header("四周雾墙 / Layered walls")]
        [Range(1, 8)] public int wallLayers = 4;
        [Min(.1f)] public float wallHeight = 12;
        [Min(0)] public float wallBelowGround = 3;
        [Range(0, 25)] public float outwardTilt = 10;
        [Min(.05f)] public float layerSpacing = 1.4f;
        [Range(0, 1)] public float wallOpacity = .55f;
        [Range(.2f, 4)] public float verticalFalloff = 1.3f;
        [Tooltip("外侧观看的近侧雾墙透明度倍率。俯视地图建议 0.08–0.2，步行视角可设为 1。")]
        [Range(0, 1)] public float nearSideOpacity = .12f;

        [Header("贴地雾 / Ground mist")]
        [Min(.1f)] public float innerWidth = 2;
        [Min(.1f)] public float edgeWidth = 12;
        [Range(1, 5)] public int groundLayers = 3;
        [Min(.01f)] public float groundLift = .12f;
        [Min(.01f)] public float groundLayerSpacing = .22f;
        [Range(0, 1)] public float groundOpacity = .32f;
        [Tooltip("紧贴地表的远处雾底，填充地图之外的天空空隙。无碰撞，不生成可行走地面。")]
        public bool horizonVeil = true;
        [Min(10)] public float horizonExtent = 250;

        [Header("纹理与柔化 / Noise and softness")]
        [Min(.001f)] public float noiseScale = .18f;
        public Vector2 speed = new Vector2(.025f, .01f);
        [Range(0, 1)] public float noiseStrength = .25f;
        [Min(.01f)] public float depthSoftness = .6f;
        [Min(0)] public float cameraFadeDistance = .5f;
        [Tooltip("URP 未提供深度纹理时自动回退为普通透明雾。")]
        public bool softIntersections = true;
        public Material fogMaterial;

        [NonSerialized] Transform generated;
        readonly List<Mesh> meshes = new List<Mesh>();
        readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
        readonly List<int> kinds = new List<int>();
        readonly List<float> phases = new List<float>();
        Material instanceMaterial;
        MaterialPropertyBlock properties;
        bool rebuild = true;
        double nextBoundsCheck;
        static FogSystem atmosphereOwner;
        bool captured;
        bool oldFog;
        Color oldColor;
        FogMode oldMode;
        float oldDensity, oldStart, oldEnd;

        void OnEnable()
        {
            rebuild = true; nextBoundsCheck = 0;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
        }
        void OnValidate() { rebuild = true; }
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            ReleaseAtmosphere(); ClearGenerated();
        }
        void OnDestroy() { ReleaseAtmosphere(); ClearGenerated(); }

        void Update()
        {
            if (autoFitBounds && boundsTarget && Time.realtimeSinceStartupAsDouble >= nextBoundsCheck)
            {
                nextBoundsCheck = Time.realtimeSinceStartupAsDouble + 1;
                FitBounds();
            }
            if (rebuild) Rebuild();
            UpdateMaterials();
            UpdateAtmosphere();
        }

        bool IsSceneInstance()
        {
            if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return false;
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(this) ||
                UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null) return false;
#endif
            return true;
        }

        void UpdateAtmosphere()
        {
            if (!atmosphere || !IsSceneInstance()) { ReleaseAtmosphere(); return; }
            if (atmosphereOwner && atmosphereOwner != this) return;
            if (!captured)
            {
                atmosphereOwner = this;
                oldFog = RenderSettings.fog; oldColor = RenderSettings.fogColor;
                oldMode = RenderSettings.fogMode; oldDensity = RenderSettings.fogDensity;
                oldStart = RenderSettings.fogStartDistance; oldEnd = RenderSettings.fogEndDistance;
                captured = true;
            }
            RenderSettings.fog = true; RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = atmosphereMode; RenderSettings.fogDensity = density;
            RenderSettings.fogStartDistance = atmosphereStart;
            RenderSettings.fogEndDistance = Mathf.Max(atmosphereStart + .1f, atmosphereEnd);
        }

        void ReleaseAtmosphere()
        {
            if (!captured) return;
            if (atmosphereOwner == this)
            {
                RenderSettings.fog = oldFog; RenderSettings.fogColor = oldColor;
                RenderSettings.fogMode = oldMode; RenderSettings.fogDensity = oldDensity;
                RenderSettings.fogStartDistance = oldStart; RenderSettings.fogEndDistance = oldEnd;
                atmosphereOwner = null;
            }
            captured = false;
        }

        [ContextMenu("Fit to ground renderers / 适配地面范围")]
        public void FitBounds()
        {
            if (!boundsTarget || boundsTarget == transform || boundsTarget.IsChildOf(transform)) return;
            bool found = false;
            Bounds local = default;
            foreach (var renderer in boundsTarget.GetComponentsInChildren<MeshRenderer>())
            {
                if (!renderer.enabled || renderer.transform.IsChildOf(transform)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh) continue;
                var b = filter.sharedMesh.bounds;
                var matrix = transform.worldToLocalMatrix * renderer.localToWorldMatrix;
                for (int i = 0; i < 8; ++i)
                {
                    var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!found) { local = new Bounds(p, Vector3.zero); found = true; }
                    else local.Encapsulate(p);
                }
            }
            if (!found) return; // Runtime-generated maps retain their authored fallback until ready.
            var size = new Vector2(Mathf.Max(1, local.size.x + boundsPadding * 2), Mathf.Max(1, local.size.z + boundsPadding * 2));
            var center = new Vector3(local.center.x, local.max.y, local.center.z);
            if ((size - mapSize).sqrMagnitude < .0001f && (center - mapCenter).sqrMagnitude < .0001f) return;
            mapSize = size; mapCenter = center; rebuild = true;
        }

        [ContextMenu("Rebuild fog / 重建雾层")]
        public void Rebuild()
        {
            rebuild = false;
            ClearGenerated();
            if (!isActiveAndEnabled) return;
            var shader = fogMaterial ? fogMaterial.shader : Shader.Find("ContinentalFog/Soft Boundary");
            if (!shader) return;
            instanceMaterial = fogMaterial ? new Material(fogMaterial) : new Material(shader);
            instanceMaterial.hideFlags = HideFlags.HideAndDontSave;
            var root = new GameObject("Generated Fog Layers (automatic)");
            root.hideFlags = HideFlags.DontSave;
            generated = root.transform; generated.SetParent(transform, false);
            for (int layer = 0; layer < Mathf.Clamp(wallLayers, 1, 8); layer++)
                BuildRing("FogWall", layer, layer * Mathf.Max(.05f, layerSpacing), true, 0);
            for (int layer = 0; layer < Mathf.Clamp(groundLayers, 1, 5); layer++)
                BuildRing("GroundMist", layer, edgeWidth, false, 1);
            if (horizonVeil) BuildRing("HorizonVeil", 0, Mathf.Max(horizonExtent, edgeWidth * 3), false, 2);
            UpdateMaterials();
        }

        Vector3 Corner(int corner, Vector2 half, float y)
        {
            return mapCenter + new Vector3((corner == 0 || corner == 3) ? -half.x : half.x,
                y, corner < 2 ? -half.y : half.y);
        }

        void BuildRing(string label, int layer, float extent, bool wall, int kind)
        {
            var half = new Vector2(Mathf.Max(1, mapSize.x), Mathf.Max(1, mapSize.y)) * .5f;
            var bottom = wall ? half + Vector2.one * extent : Vector2.Max(Vector2.one * .01f, half - Vector2.one * innerWidth);
            float height = Mathf.Max(.1f, wallHeight) * (1 + layer * .055f);
            var top = wall ? half + Vector2.one * (extent + Mathf.Tan(outwardTilt * Mathf.Deg2Rad) * height) : half + Vector2.one * extent;
            float lowY = wall ? -wallBelowGround : (kind == 2 ? groundLift * .25f : groundLift + layer * groundLayerSpacing);
            float highY = wall ? height : lowY;
            string[] sides = { "South", "East", "North", "West" };
            // Shared diagonal normals feather the near-side cutaway continuously through corners.
            var normals = new[] { new Vector3(-1,0,-1).normalized, new Vector3(1,0,-1).normalized,
                new Vector3(1,0,1).normalized, new Vector3(-1,0,1).normalized };
            for (int side = 0; side < 4; side++)
            {
                int end = (side + 1) % 4;
                var vertices = new[] { Corner(side, bottom, lowY), Corner(end, bottom, lowY), Corner(end, top, highY), Corner(side, top, highY) };
                var mesh = new Mesh { name = label + " mesh", hideFlags = HideFlags.HideAndDontSave };
                mesh.vertices = vertices;
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                mesh.normals = new[] { normals[side], normals[end], normals[end], normals[side] };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateBounds();
                meshes.Add(mesh);
                var go = new GameObject($"{label}_{sides[side]}_{layer + 1}");
                go.hideFlags = HideFlags.DontSave; go.layer = gameObject.layer;
                go.transform.SetParent(generated, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = instanceMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderers.Add(renderer); kinds.Add(kind); phases.Add(layer * 3.71f);
            }
        }

        void BeginCamera(ScriptableRenderContext context, Camera camera) { UpdateMaterials(camera); }

        public void UpdateMaterials(Camera camera = null)
        {
            if (!instanceMaterial) return;
            properties ??= new MaterialPropertyBlock();
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool depth = softIntersections && pipeline && pipeline.supportsCameraDepthTexture;
            if (camera && camera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData))
                depth = softIntersections && cameraData.requiresDepthTexture;
            for (int i = 0; i < renderers.Count; i++)
            {
                if (!renderers[i]) continue;
                properties.Clear();
                properties.SetColor("_FogColor", fogColor);
                properties.SetVector("_Map", new Vector4(mapSize.x * .5f, mapSize.y * .5f, innerWidth, edgeWidth));
                properties.SetVector("_Center", mapCenter);
                properties.SetVector("_Noise", new Vector4(noiseScale, noiseStrength, speed.x, speed.y));
                properties.SetVector("_Shape", new Vector4(kinds[i], kinds[i] == 0 ? wallOpacity : groundOpacity, verticalFalloff, phases[i]));
                properties.SetVector("_Soft", new Vector4(depth ? 1 : 0, depthSoftness, cameraFadeDistance, nearSideOpacity));
                renderers[i].SetPropertyBlock(properties);
            }
        }

        static void Dispose(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        void ClearGenerated()
        {
            if (generated) { generated.gameObject.SetActive(false); Dispose(generated.gameObject); }
            generated = null;
            foreach (var mesh in meshes) Dispose(mesh);
            meshes.Clear(); renderers.Clear(); kinds.Clear(); phases.Clear();
            Dispose(instanceMaterial); instanceMaterial = null;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(.4f, .8f, 1, 1);
            Gizmos.DrawWireCube(mapCenter, new Vector3(mapSize.x, .04f, mapSize.y));
            Gizmos.color = new Color(.4f, .8f, 1, .3f);
            Gizmos.DrawWireCube(mapCenter + Vector3.up * wallHeight * .5f,
                new Vector3(mapSize.x + edgeWidth * 2, wallHeight, mapSize.y + edgeWidth * 2));
        }
    }
}
