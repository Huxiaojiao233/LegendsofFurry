#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using LegendsOfFurry.Content.Contracts;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class WorldTerrainInstanceRendererTests
{
    private readonly List<Object> owned = new List<Object>();

    [TearDown]
    public void TearDown()
    {
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            if (owned[i] != null)
                Object.DestroyImmediate(owned[i]);
        }

        owned.Clear();
    }

    [Test]
    public void BakedTerrainPrefabsSharePersistentMeshesAndEnableInstancing()
    {
        for (int i = 0; i < TerrainPrefabSetup.Entries.Length; i++)
        {
            string path = $"{TerrainPrefabSetup.TerrainPrefabFolder}/{TerrainPrefabSetup.Entries[i].fileName}";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);

            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(filters.Length, Is.GreaterThan(0), path);
            for (int f = 0; f < filters.Length; f++)
            {
                MeshFilter filter = filters[f];
                if (filter.sharedMesh == null) continue;
                string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                Assert.That(meshPath, Does.StartWith(TerrainPrefabBaker.MeshFolder), path);
                Assert.That(filter.GetComponent<UnityEngine.ProBuilder.ProBuilderMesh>(), Is.Null, path);
                Assert.That(filter.GetComponent<TerrainBatchSource>(), Is.Not.Null, path);
                MeshCollider collider = filter.GetComponent<MeshCollider>();
                if (collider != null)
                    Assert.That(collider.sharedMesh, Is.SameAs(filter.sharedMesh));
            }

            MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Material material = renderers[r].sharedMaterial;
                if (material == null) continue;
                Assert.That(material.enableInstancing, Is.True, material.name);
            }
        }

        GameObject first = AssetDatabase.LoadAssetAtPath<GameObject>(
            $"{TerrainPrefabSetup.TerrainPrefabFolder}/Terrain_Grass.prefab");
        GameObject a = Track(Object.Instantiate(first));
        GameObject b = Track(Object.Instantiate(first));
        Mesh meshA = a.GetComponentInChildren<MeshFilter>().sharedMesh;
        Mesh meshB = b.GetComponentInChildren<MeshFilter>().sharedMesh;
        Assert.That(meshA, Is.SameAs(meshB));
        Assert.That(meshA, Is.Not.Null);
    }

    [Test]
    public void GroupsByStageMeshAndMaterial()
    {
        RequireInstancing();
        Mesh meshA = CreateCubeMesh("BatchMeshA");
        Mesh meshB = CreateCubeMesh("BatchMeshB");
        Material material = CreateInstancedMaterial();
        GameObject root = CreateRoot(out WorldTerrainInstanceRenderer instancer);
        CreateBatchedCell(root.transform, "start", 0, 0, meshA, material);
        CreateBatchedCell(root.transform, "start", 1, 0, meshA, material);
        CreateBatchedCell(root.transform, "fight-1", 10, 0, meshA, material);
        CreateBatchedCell(root.transform, "fight-1", 11, 0, meshB, material);

        instancer.Rebuild();
        instancer.RefreshFrame(null, false);

        Assert.That(instancer.FallbackRendererCount, Is.EqualTo(0));
        Assert.That(instancer.BatchGroupCount, Is.EqualTo(3));
        Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(4));
        Assert.That(instancer.SubmittedBatchCount, Is.EqualTo(3));
    }

    [Test]
    public void HiddenOrInactiveCellsAreFilteredOut()
    {
        RequireInstancing();
        Mesh mesh = CreateCubeMesh("FilterMesh");
        Material material = CreateInstancedMaterial();
        GameObject root = CreateRoot(out WorldTerrainInstanceRenderer instancer);
        BoardCell visible = CreateBatchedCell(root.transform, "start", 0, 0, mesh, material);
        BoardCell fogHidden = CreateBatchedCell(root.transform, "start", 1, 0, mesh, material);
        CreateBatchedCell(root.transform, "start", 2, 0, mesh, material).gameObject.SetActive(false);
        CreateBatchedCell(root.transform, "hidden-stage", 10, 0, mesh, material).gameObject.SetActive(false);
        GameObject decoration = GameObject.CreatePrimitive(PrimitiveType.Cube);
        decoration.name = "Tree";
        decoration.transform.SetParent(visible.transform, false);

        fogHidden.SetTerrainVisible(false);
        instancer.Rebuild();
        visible.SetTerrainVisible(true);
        instancer.RefreshFrame(null, false);

        Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(1));
        Assert.That(instancer.SubmittedBatchCount, Is.EqualTo(1));
        Assert.That(visible.GetComponentInChildren<TerrainBatchSource>().GetComponent<MeshRenderer>().enabled,
            Is.False);
        Assert.That(decoration.GetComponent<MeshRenderer>().enabled, Is.True);
        Assert.That(fogHidden.TerrainVisible, Is.False);
    }

    [Test]
    public void MatricesFollowTerrainTransforms()
    {
        RequireInstancing();
        Mesh mesh = CreateCubeMesh("MatrixMesh");
        Material material = CreateInstancedMaterial();
        GameObject root = CreateRoot(out WorldTerrainInstanceRenderer instancer);
        BoardCell cell = CreateBatchedCell(root.transform, "start", 0, 0, mesh, material);
        instancer.Rebuild();

        cell.transform.localPosition = new Vector3(4f, 1.5f, -2f);
        cell.transform.localScale = new Vector3(1f, 0.12f, 1f);
        instancer.RefreshFrame(null, false);

        Assert.That(instancer.TryGetInstanceMatrix(0, 0, out Matrix4x4 matrix), Is.True);
        Assert.That(matrix, Is.EqualTo(cell.GetComponentInChildren<TerrainBatchSource>().transform.localToWorldMatrix));
    }

    [Test]
    public void UnsupportedMaterialsFallBackToMeshRenderer()
    {
        Mesh mesh = CreateCubeMesh("FallbackMesh");
        Material material = CreateInstancedMaterial();
        material.enableInstancing = false;
        GameObject root = CreateRoot(out WorldTerrainInstanceRenderer instancer);
        BoardCell cell = CreateBatchedCell(root.transform, "start", 0, 0, mesh, material);
        MeshRenderer renderer = cell.GetComponentInChildren<MeshRenderer>();

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("回退到 MeshRenderer"));
        instancer.Rebuild();
        instancer.RefreshFrame(null, false);

        Assert.That(instancer.FallbackRendererCount, Is.EqualTo(1));
        Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(0));
        Assert.That(instancer.SubmittedBatchCount, Is.EqualTo(0));
        Assert.That(cell.GetComponentInChildren<TerrainBatchSource>().Instanced, Is.False);
        Assert.That(renderer.enabled, Is.True);

        cell.SetTerrainVisible(false);
        Assert.That(renderer.enabled, Is.False);
        cell.SetTerrainVisible(true);
        Assert.That(renderer.enabled, Is.True);
    }

    [Test]
    public void FrustumCullsWholeStageChunk()
    {
        RequireInstancing();
        Mesh mesh = CreateCubeMesh("CullMesh");
        Material material = CreateInstancedMaterial();
        GameObject root = CreateRoot(out WorldTerrainInstanceRenderer instancer);
        CreateBatchedCell(root.transform, "start", 0, 0, mesh, material);
        instancer.Rebuild();

        Camera camera = Track(new GameObject("CullCamera")).AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 8f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 80f;
        camera.transform.SetPositionAndRotation(new Vector3(0f, 20f, 0f), Quaternion.Euler(90f, 0f, 0f));
        instancer.RefreshFrame(camera, false);
        Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(1));
        Assert.That(instancer.SubmittedBatchCount, Is.EqualTo(1));

        camera.transform.position = new Vector3(1000f, 20f, 1000f);
        instancer.RefreshFrame(camera, false);
        Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(0));
        Assert.That(instancer.SubmittedBatchCount, Is.EqualTo(0));
    }

    [Test]
    public void RegisterAndUnregisterChunksIncrementally()
    {
        RequireInstancing();
        Mesh mesh = CreateCubeMesh("IncrementalMesh");
        Material material = CreateInstancedMaterial();
        GameObject root = CreateRoot(out WorldTerrainInstanceRenderer instancer);
        Transform chunkA = new GameObject("ChunkA").transform;
        chunkA.SetParent(root.transform, false);
        Transform chunkB = new GameObject("ChunkB").transform;
        chunkB.SetParent(root.transform, false);
        CreateBatchedCell(chunkA, "start", 0, 0, mesh, material);
        CreateBatchedCell(chunkA, "start", 1, 0, mesh, material);
        CreateBatchedCell(chunkB, "fight-1", 10, 0, mesh, material);

        instancer.RegisterChunk(chunkA);
        instancer.RefreshFrame(null, false);
        Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(2));
        Assert.That(instancer.BatchGroupCount, Is.EqualTo(1));

        instancer.RegisterChunk(chunkB);
        instancer.RefreshFrame(null, false);
        Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(3));
        Assert.That(instancer.BatchGroupCount, Is.EqualTo(2));

        instancer.UnregisterChunk(chunkA);
        instancer.RefreshFrame(null, false);
        Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(1));
        Assert.That(instancer.BatchGroupCount, Is.EqualTo(1));
    }

    [Test]
    [Timeout(180000)]
    public void FullBlankWorldBatchesTwentyFiveHundredTiles()
    {
        RequireInstancing();
        BoardGenerator.TerrainPrefabBinding[] bindings = LoadTerrainBindings();
        GameObject fallback = bindings[0].prefab;
        Assert.That(fallback, Is.Not.Null);

        GameObject root = Track(new GameObject("FullWorldBoard"));
        root.SetActive(false);
        Transform grid = new GameObject("Grid").transform;
        grid.SetParent(root.transform, false);
        BoardGenerator board = root.AddComponent<BoardGenerator>();
        board.BindTerrainVisuals(fallback, bindings, grid);
        WorldDefinition world = WorldMapIO.CreateBlank("instancing-map", "合批地图", 5, 5, 10);
        board.BuildWorldBoard(world);
        root.SetActive(true);

        WorldTerrainInstanceRenderer instancer = board.GetComponent<WorldTerrainInstanceRenderer>();
        Assert.That(instancer, Is.Not.Null);
        instancer.RefreshFrame(null, false);

        BoardCell[] cells = board.GetComponentsInChildren<BoardCell>(true);
        Assert.That(cells.Length, Is.EqualTo(2500));
        Assert.That(instancer.FallbackRendererCount, Is.EqualTo(0));
        Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(2500));
        Assert.That(instancer.SubmittedBatchCount, Is.EqualTo(25));
        Assert.That(instancer.SubmittedBatchCount, Is.LessThan(500));

        Mesh shared = cells[0].GetComponentInChildren<MeshFilter>().sharedMesh;
        Assert.That(shared, Is.Not.Null);
        for (int i = 1; i < cells.Length; i++)
            Assert.That(cells[i].GetComponentInChildren<MeshFilter>().sharedMesh, Is.SameAs(shared));
    }

    [UnityTest]
    public IEnumerator PlayModeKeepsFallbackZeroAndDoesNotAllocateWhenSteady()
    {
        yield return new EnterPlayMode();
        GameObject root = null;
        Mesh mesh = null;
        Material material = null;
        System.Exception caught = null;
        try
        {
            if (!SystemInfo.supportsInstancing)
                Assert.Ignore("当前平台不支持 GPU Instancing。");
            mesh = new Mesh { name = "PlayMesh" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
            material = new Material(shader) { enableInstancing = true };
            root = new GameObject("PlayInstancer");
            WorldTerrainInstanceRenderer instancer = root.AddComponent<WorldTerrainInstanceRenderer>();
            CreatePlayCell(root.transform, mesh, material, "start", 0, 0);
            CreatePlayCell(root.transform, mesh, material, "start", 1, 0);
            instancer.Rebuild();
            instancer.RefreshFrame(null, false);
            Assert.That(instancer.FallbackRendererCount, Is.EqualTo(0));
            Assert.That(instancer.VisibleInstanceCount, Is.EqualTo(2));
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            instancer.RefreshFrame(null, false);
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
        }
        catch (System.Exception exception)
        {
            caught = exception;
        }

        if (root != null) Object.DestroyImmediate(root);
        if (material != null) Object.DestroyImmediate(material);
        if (mesh != null) Object.DestroyImmediate(mesh);
        yield return new ExitPlayMode();
        if (caught != null) throw caught;
    }

    private GameObject CreateRoot(out WorldTerrainInstanceRenderer instancer)
    {
        GameObject root = Track(new GameObject("InstancerRoot"));
        instancer = root.AddComponent<WorldTerrainInstanceRenderer>();
        return root;
    }

    private BoardCell CreateBatchedCell(Transform parent, string stageId, int x, int z, Mesh mesh, Material material)
    {
        GameObject cellObject = new GameObject($"Cell_{stageId}_{x}_{z}");
        cellObject.transform.SetParent(parent, false);
        cellObject.transform.localPosition = new Vector3(x, 0f, z);
        BoardCell cell = cellObject.AddComponent<BoardCell>();
        cell.Initialize(x, z, stageId, 0, WorldTerrainCatalog.Grass);
        GameObject cube = new GameObject("Cube");
        cube.transform.SetParent(cellObject.transform, false);
        MeshFilter filter = cube.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = cube.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        cube.AddComponent<MeshCollider>().sharedMesh = mesh;
        cube.AddComponent<TerrainBatchSource>();
        return cell;
    }

    private static BoardCell CreatePlayCell(Transform parent, Mesh mesh, Material material, string stageId, int x,
        int z)
    {
        GameObject cellObject = new GameObject($"Cell_{stageId}_{x}_{z}");
        cellObject.transform.SetParent(parent, false);
        cellObject.transform.localPosition = new Vector3(x, 0f, z);
        BoardCell cell = cellObject.AddComponent<BoardCell>();
        cell.Initialize(x, z, stageId, 0, WorldTerrainCatalog.Grass);
        GameObject cube = new GameObject("Cube");
        cube.transform.SetParent(cellObject.transform, false);
        cube.AddComponent<MeshFilter>().sharedMesh = mesh;
        cube.AddComponent<MeshRenderer>().sharedMaterial = material;
        cube.AddComponent<TerrainBatchSource>();
        return cell;
    }

    private Mesh CreateCubeMesh(string meshName)
    {
        GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh source = primitive.GetComponent<MeshFilter>().sharedMesh;
        Mesh copy = Track(Object.Instantiate(source));
        copy.name = meshName;
        Object.DestroyImmediate(primitive);
        return copy;
    }

    private static void RequireInstancing()
    {
        if (!SystemInfo.supportsInstancing)
            Assert.Ignore("当前平台不支持 GPU Instancing。");
    }

    private Material CreateInstancedMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                        Shader.Find("Sprites/Default") ??
                        Shader.Find("Standard");
        Assert.That(shader, Is.Not.Null);
        Material material = new Material(shader) { name = "InstancedTerrain" };
        material.enableInstancing = true;
        return Track(material);
    }

    private static BoardGenerator.TerrainPrefabBinding[] LoadTerrainBindings()
    {
        var bindings = new BoardGenerator.TerrainPrefabBinding[TerrainPrefabSetup.Entries.Length];
        for (int i = 0; i < TerrainPrefabSetup.Entries.Length; i++)
        {
            string path = $"{TerrainPrefabSetup.TerrainPrefabFolder}/{TerrainPrefabSetup.Entries[i].fileName}";
            bindings[i] = new BoardGenerator.TerrainPrefabBinding
            {
                terrainId = TerrainPrefabSetup.Entries[i].terrainId,
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path)
            };
        }

        return bindings;
    }

    private T Track<T>(T obj) where T : Object
    {
        if (obj != null) owned.Add(obj);
        return obj;
    }
}
#endif
