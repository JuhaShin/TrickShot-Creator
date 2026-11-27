using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PlacementShapeBaker
{
    // 가장 긴 변을 나누는 셀 수. 값이 클수록 표면을 세밀하게 따라간다.
    private const int Resolution = 24;
    private const string FryingPanPath = "Assets/PreFebs/FryingPanPreFeb.prefab";
    private const int FryingPanResolution = 48;
    private const string Folder = "Assets/Resources/PlacementShapes";

    static PlacementShapeBaker()
    {
        EditorApplication.delayCall += EnsureDefaults;
    }

    private static void EnsureDefaults()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        Bake(FryingPanPath, false);
        Bake("Assets/PreFebs/PotPrefeb.prefab", false);
    }

    [MenuItem("Tools/TrickShot/Rebuild Selected Placement Shapes")]
    private static void RebuildSelected()
    {
        foreach (Object item in Selection.objects)
        {
            if (item is GameObject goal && goal.CompareTag("Goal"))
            {
                foreach (MeshCollider body in goal.GetComponentsInChildren<MeshCollider>())
                    if (body.enabled && !body.isTrigger && body.sharedMesh != null)
                        BakeGoalMesh(body.sharedMesh);
                continue;
            }
            string path = AssetDatabase.GetAssetPath(item);
            if (path.EndsWith(".prefab"))
                Bake(path, true);
        }
    }

    internal static void BakeGoalMesh(Mesh mesh)
    {
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long id)) return;
        string path = $"{Folder}/Goal_{guid}_{id}.asset";
        var parts = new List<Bounds>();
        BakeMesh(mesh, Matrix4x4.identity, parts);
        if (parts.Count == 0) return;
        PlacementShape shape = AssetDatabase.LoadAssetAtPath<PlacementShape>(path);
        if (shape == null)
        {
            shape = ScriptableObject.CreateInstance<PlacementShape>();
            AssetDatabase.CreateAsset(shape, path);
        }
        shape.sourceMesh = mesh;
        shape.parts = parts.ToArray();
        EditorUtility.SetDirty(shape);
        AssetDatabase.SaveAssets();
    }

    private static void Bake(string path, bool rebuild)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            return;
        if (!AssetDatabase.IsValidFolder(Folder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateFolder("Assets/Resources", "PlacementShapes");
        }
        string output = Folder + "/" + prefab.name + ".asset";
        PlacementShape shape = AssetDatabase.LoadAssetAtPath<PlacementShape>(output);
        if (shape != null && !rebuild)
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        List<Bounds> parts = new List<Bounds>();
        try
        {
            foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>())
            {
                if (!collider.enabled || collider.isTrigger || collider.sharedMesh == null)
                    continue;
                Matrix4x4 matrix = root.transform.worldToLocalMatrix * collider.transform.localToWorldMatrix;
                BakeMesh(collider.sharedMesh, matrix, parts, path == FryingPanPath ? FryingPanResolution : Resolution);
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        if (parts.Count == 0)
        {
            Debug.LogWarning("설치용 MeshCollider를 찾지 못했습니다: " + path);
            return;
        }
        if (shape == null)
        {
            shape = ScriptableObject.CreateInstance<PlacementShape>();
            AssetDatabase.CreateAsset(shape, output);
        }
        shape.sourcePrefab = prefab;
        shape.parts = parts.ToArray();
        EditorUtility.SetDirty(shape);
        AssetDatabase.SaveAssets();
    }

    private static void BakeMesh(Mesh mesh, Matrix4x4 matrix, List<Bounds> parts, int resolution = Resolution)
    {
        using (Mesh.MeshDataArray data = MeshUtility.AcquireReadOnlyMeshData(mesh))

        {
            NativeArray<Vector3> vertices = new NativeArray<Vector3>(data[0].vertexCount, Allocator.Temp);
            try
            {
                data[0].GetVertices(vertices);
                if (vertices.Length == 0)
                    return;
                Bounds bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[0]), Vector3.zero);
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
                    bounds.Encapsulate(vertices[i]);
                }
    
                float cell = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) / resolution;
                if (cell < 0.00001f)
                    return;
                Vector3 origin = bounds.min - Vector3.one * (cell * 0.5f);
                HashSet<Vector3Int> occupied = new HashSet<Vector3Int>();
                for (int sub = 0; sub < data[0].subMeshCount; sub++)
                {
                    var descriptor = data[0].GetSubMesh(sub);
                    if (descriptor.topology != MeshTopology.Triangles)
                        continue;
                    using (NativeArray<int> indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                    {
                        data[0].GetIndices(indices, sub);
                        for (int i = 0; i + 2 < indices.Length; i += 3)
                        {
                            Vector3 a = vertices[indices[i]];
                            Vector3 b = vertices[indices[i + 1]];
                            Vector3 c = vertices[indices[i + 2]];
                            Vector3Int min = Vector3Int.FloorToInt((Vector3.Min(a, Vector3.Min(b, c)) - origin) / cell);
                            Vector3Int max = Vector3Int.FloorToInt((Vector3.Max(a, Vector3.Max(b, c)) - origin) / cell);
                            for (int x = min.x; x <= max.x; x++)
                            for (int y = min.y; y <= max.y; y++)
                            for (int z = min.z; z <= max.z; z++)
                            {
                                Vector3Int key = new Vector3Int(x, y, z);
                                Vector3 center = origin + ((Vector3)key + Vector3.one * 0.5f) * cell;
                                if (TouchesCell(a - center, b - center, c - center, cell * 0.5f))
                                    occupied.Add(key);
                            }
                        }
                    }
                }
    
                // 옆으로 이어진 셀을 합쳐 설치 판정용 콜라이더 수를 줄인다.
                while (occupied.Count > 0)
                {
                    Vector3Int first = default;
                    foreach (Vector3Int key in occupied) { first = key; break; }
                    Vector3Int min = first;
                    Vector3Int max = first;
                    while (occupied.Contains(min + Vector3Int.left)) min += Vector3Int.left;
                    while (occupied.Contains(max + Vector3Int.right)) max += Vector3Int.right;
                    for (int x = min.x; x <= max.x; x++)
                        occupied.Remove(new Vector3Int(x, first.y, first.z));
                    Vector3 size = new Vector3(max.x - min.x + 1, 1, 1) * cell;
                    Vector3 center = origin + new Vector3((min.x + max.x + 1) * 0.5f, first.y + 0.5f, first.z + 0.5f) * cell;
                    parts.Add(new Bounds(center, size));
                }
            }
            finally
            {
                // 중간에 return하거나 오류가 나도 할당한 메모리를 정리한다.
                vertices.Dispose();
            }
        }
    }

    // 삼각형과 작은 셀이 실제로 닿는지 분리축으로 검사한다. 빈 셀은 생성하지 않는다.
    private static bool TouchesCell(Vector3 a, Vector3 b, Vector3 c, float half)
    {
        Vector3[] edges = { b - a, c - b, a - c };
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        foreach (Vector3 axis in axes)
            if (Separated(a, b, c, axis, half)) return false;
        if (Separated(a, b, c, Vector3.Cross(edges[0], edges[1]), half)) return false;
        foreach (Vector3 edge in edges)
            foreach (Vector3 axis in axes)
                if (Separated(a, b, c, Vector3.Cross(edge, axis), half)) return false;
        return true;
    }

    private static bool Separated(Vector3 a, Vector3 b, Vector3 c, Vector3 axis, float half)
    {
        float radius = half * (Mathf.Abs(axis.x) + Mathf.Abs(axis.y) + Mathf.Abs(axis.z));
        float x = Vector3.Dot(a, axis), y = Vector3.Dot(b, axis), z = Vector3.Dot(c, axis);
        return Mathf.Min(x, Mathf.Min(y, z)) > radius || Mathf.Max(x, Mathf.Max(y, z)) < -radius;
    }
}
