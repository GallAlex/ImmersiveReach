#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using UnityEditor;

public class MeshCombiner : Editor
{
    [MenuItem("GameObject/Combine Meshes")]
    public static void CombineMeshes()
    {
        GameObject go = GameObject.Find("Skeleton");

        Quaternion oldRotation = go.transform.rotation;
        Vector3 oldPosition = go.transform.position;

        go.transform.rotation = Quaternion.identity;
        go.transform.position = Vector3.zero;


        MeshFilter[] meshFilters = go.GetComponentsInChildren<MeshFilter>();
        Mesh finalMesh = new Mesh();
        finalMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Use UInt32 index format
        CombineInstance[] combine = new CombineInstance[meshFilters.Length];

        for (int i = 0; i < meshFilters.Length; ++i)
        {
            if (meshFilters[i].transform == go.transform)
                continue;
            
            combine[i].subMeshIndex = 0;
            combine[i].mesh = meshFilters[i].sharedMesh;
            combine[i].transform = meshFilters[i].transform.localToWorldMatrix;
        }
        
        finalMesh.CombineMeshes(combine);

        // check if go has a MeshFilter component if not add one
        if (go.GetComponent<MeshFilter>() == null)
            go.AddComponent<MeshFilter>();
        go.GetComponent<MeshFilter>().sharedMesh = finalMesh;

        go.transform.rotation = oldRotation;
        go.transform.position = oldPosition;

        string name = go.name;
        AssetDatabase.CreateAsset(finalMesh, $"Assets/_CrepuscularRays/Resources/Meshes/{name}.asset");
        Debug.Log($"Mesh {name} created");

        // add the mesh created in the scene
        GameObject newGo = new GameObject(name);
        newGo.AddComponent<MeshFilter>().sharedMesh = finalMesh;
        newGo.AddComponent<MeshRenderer>().sharedMaterial = go.transform.GetChild(0).GetComponent<MeshRenderer>().sharedMaterial;
        newGo.transform.position = go.transform.position;
        newGo.transform.rotation = go.transform.rotation;
        newGo.tag = go.tag;
        newGo.layer = go.layer;
        newGo.AddComponent<MeshCollider>().sharedMesh = finalMesh;

        // disable the original go
        go.SetActive(false);
    }
}

#endif