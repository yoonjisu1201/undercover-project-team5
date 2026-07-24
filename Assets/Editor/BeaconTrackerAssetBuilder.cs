using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

internal static class BeaconTrackerAssetBuilder
{
    private const string RootFolder = "Assets/Prefabs/Props/BeaconTracker";
    private const string PrefabPath = RootFolder + "/BeaconTracker.prefab";
    private const string GeneratedMarker = RootFolder + "/.generated";

    [InitializeOnLoadMethod]
    private static void BuildOnFirstImport()
    {
        if (!AssetDatabase.IsValidFolder(RootFolder) || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            EditorApplication.delayCall += Build;
        }
    }

    [MenuItem("Tools/Undercover/Create Beacon Tracker")]
    private static void Build()
    {
        EnsureFolders();

        Material body = CreateMaterial("M_BeaconTracker_Body", new Color(0.22f, 0.32f, 0.48f), 0.24f, 0f);
        Material bevel = CreateMaterial("M_BeaconTracker_Bevel", new Color(0.11f, 0.16f, 0.25f), 0.2f, 0f);
        Material face = CreateMaterial("M_BeaconTracker_Face", new Color(0.16f, 0.22f, 0.33f), 0.18f, 0f);
        Material cyan = CreateMaterial("M_BeaconTracker_Cyan", new Color(0.05f, 0.75f, 1f), 0.25f, 0f, new Color(0.05f, 0.8f, 1f) * 5f);
        Material yellow = CreateMaterial("M_BeaconTracker_Yellow", new Color(1f, 0.55f, 0.05f), 0.22f, 0f, new Color(1f, 0.38f, 0.02f) * 4f);
        Material adhesive = CreateMaterial("M_BeaconTracker_Adhesive", new Color(0.72f, 0.71f, 0.67f), 0.08f, 0f);

        GameObject root = new GameObject("BeaconTracker");
        root.transform.localScale = Vector3.one;

        AddCylinder(root.transform, "BackPlate", 0.041f, 0.008f, -0.004f, bevel, 48);
        AddCylinder(root.transform, "MainBody", 0.04f, 0.014f, 0.004f, body, 48);
        AddCylinder(root.transform, "TopFace", 0.0325f, 0.005f, 0.013f, face, 48);
        AddTorus(root.transform, "StatusRing", 0.027f, 0.0015f, 0.0162f, cyan);
        AddCylinder(root.transform, "ButtonBezel", 0.0145f, 0.0035f, 0.016f, bevel, 40);

        GameObject button = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        button.name = "CenterLight";
        button.transform.SetParent(root.transform, false);
        button.transform.localPosition = new Vector3(0f, 0f, 0.0202f);
        button.transform.localScale = new Vector3(0.025f, 0.025f, 0.008f);
        Object.DestroyImmediate(button.GetComponent<Collider>());
        button.GetComponent<Renderer>().sharedMaterial = yellow;

        AddCylinder(root.transform, "AdhesivePad", 0.033f, 0.0018f, -0.0088f, adhesive, 48);
        AddCylinder(root.transform, "AdhesiveEdge", 0.035f, 0.0015f, -0.0075f, bevel, 48);

        GameObject notch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        notch.name = "AlignmentNotch";
        notch.transform.SetParent(root.transform, false);
        notch.transform.localPosition = new Vector3(0f, -0.035f, 0.012f);
        notch.transform.localScale = new Vector3(0.012f, 0.004f, 0.004f);
        Object.DestroyImmediate(notch.GetComponent<Collider>());
        notch.GetComponent<Renderer>().sharedMaterial = bevel;

        CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
        collider.direction = 2;
        collider.radius = 0.04f;
        collider.height = 0.03f;
        collider.center = new Vector3(0f, 0f, 0.005f);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);

        GameObject existing = GameObject.Find("BeaconTracker_Preview");
        if (existing == null && prefab != null)
        {
            existing = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            existing.name = "BeaconTracker_Preview";
            existing.transform.SetPositionAndRotation(new Vector3(0f, 1.25f, 0f), Quaternion.Euler(0f, 0f, 0f));
            Undo.RegisterCreatedObjectUndo(existing, "Create Beacon Tracker preview");
            EditorSceneManager.MarkSceneDirty(existing.scene);
        }

        Selection.activeGameObject = existing != null ? existing : prefab;
        SceneView.lastActiveSceneView?.FrameSelected();
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("Beacon Tracker prefab created at " + PrefabPath);
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets/Prefabs", "Props");
        EnsureFolder("Assets/Prefabs/Props", "BeaconTracker");
        if (!AssetDatabase.IsValidFolder(RootFolder + "/Materials"))
        {
            AssetDatabase.CreateFolder(RootFolder, "Materials");
        }
        if (!AssetDatabase.IsValidFolder(RootFolder + "/Meshes"))
        {
            AssetDatabase.CreateFolder(RootFolder, "Meshes");
        }
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static Material CreateMaterial(string name, Color color, float smoothness, float metallic, Color? emission = null)
    {
        string path = RootFolder + "/Materials/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null)
        {
            material = new Material(shader != null ? shader : Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetColor("_EmissionColor", emission.Value);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void AddCylinder(Transform parent, string name, float radius, float depth, float z, Material material, int sides)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = new Vector3(0f, 0f, z);
        MeshFilter filter = child.AddComponent<MeshFilter>();
        filter.sharedMesh = CreateCylinderMesh(name + "_Mesh", radius, depth, sides);
        child.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void AddTorus(Transform parent, string name, float majorRadius, float tubeRadius, float z, Material material)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = new Vector3(0f, 0f, z);
        MeshFilter filter = child.AddComponent<MeshFilter>();
        filter.sharedMesh = CreateTorusMesh(name + "_Mesh", majorRadius, tubeRadius, 64, 10);
        child.AddComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static Mesh CreateCylinderMesh(string name, float radius, float depth, int sides)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        float half = depth * 0.5f;

        for (int i = 0; i <= sides; i++)
        {
            float angle = i * Mathf.PI * 2f / sides;
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;
            Vector3 normal = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            vertices.Add(new Vector3(x, y, -half));
            vertices.Add(new Vector3(x, y, half));
            normals.Add(normal);
            normals.Add(normal);
            uvs.Add(new Vector2((float)i / sides, 0f));
            uvs.Add(new Vector2((float)i / sides, 1f));
            if (i < sides)
            {
                int v = i * 2;
                triangles.Add(v); triangles.Add(v + 3); triangles.Add(v + 1);
                triangles.Add(v); triangles.Add(v + 2); triangles.Add(v + 3);
            }
        }

        int frontCenter = vertices.Count;
        vertices.Add(new Vector3(0f, 0f, half)); normals.Add(Vector3.forward); uvs.Add(new Vector2(0.5f, 0.5f));
        int backCenter = vertices.Count;
        vertices.Add(new Vector3(0f, 0f, -half)); normals.Add(Vector3.back); uvs.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < sides; i++)
        {
            int a = i * 2;
            int next = ((i + 1) % sides) * 2;
            triangles.Add(frontCenter); triangles.Add(a + 1); triangles.Add(next + 1);
            triangles.Add(backCenter); triangles.Add(next); triangles.Add(a);
        }

        Mesh mesh = new Mesh { name = name };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return SaveMesh(mesh);
    }

    private static Mesh CreateTorusMesh(string name, float majorRadius, float tubeRadius, int majorSegments, int minorSegments)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();
        for (int i = 0; i <= majorSegments; i++)
        {
            float a = i * Mathf.PI * 2f / majorSegments;
            Vector3 radial = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            for (int j = 0; j <= minorSegments; j++)
            {
                float b = j * Mathf.PI * 2f / minorSegments;
                Vector3 normal = radial * Mathf.Cos(b) + Vector3.forward * Mathf.Sin(b);
                vertices.Add(radial * (majorRadius + tubeRadius * Mathf.Cos(b)) + Vector3.forward * (tubeRadius * Mathf.Sin(b)));
                normals.Add(normal);
                uvs.Add(new Vector2((float)i / majorSegments, (float)j / minorSegments));
                if (i < majorSegments && j < minorSegments)
                {
                    int row = minorSegments + 1;
                    int v = i * row + j;
                    triangles.Add(v); triangles.Add(v + row + 1); triangles.Add(v + 1);
                    triangles.Add(v); triangles.Add(v + row); triangles.Add(v + row + 1);
                }
            }
        }
        Mesh mesh = new Mesh { name = name };
        mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return SaveMesh(mesh);
    }

    private static Mesh SaveMesh(Mesh mesh)
    {
        string path = RootFolder + "/Meshes/" + mesh.name + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }
}
