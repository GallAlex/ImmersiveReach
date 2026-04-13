#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class ConvertMaterialsToHDRP : MonoBehaviour
{
    [MenuItem("Tools/Convert Materials to HDRP")]
    static void ConvertMaterials()
    {
        // Find all materials in the project
        string[] guids = AssetDatabase.FindAssets("t:Material");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            // Skip materials in immutable packages
            if (path.StartsWith("Packages/"))
            {
                Debug.LogWarning("Skipping material in immutable package: " + path);
                continue;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (material.shader.name == "Standard (Specular setup)" || material.shader.name == "Standard")
            {
                material.shader = Shader.Find("HDRP/Lit");
                Debug.Log("Converted material: " + material.name);
            }
            else
            {
                Debug.LogWarning("Material " + material.name + " uses a custom shader: " + material.shader.name);
            }
        }

        // Save the changes
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Copy and Convert Scene Materials to HDRP")]
    static void CopyAndConvertSceneMaterials()
    {
        // Create a folder to store copied materials
        string folderPath = "Assets/_CrepuscularRays/Materials";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets/_CrepuscularRays", "Materials");
        }

        // Dictionary to store original to copied material mapping
        Dictionary<Material, Material> materialMapping = new Dictionary<Material, Material>();

        // Get all renderers in the active scene
        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (Renderer renderer in renderers)
        {
            Material[] sharedMaterials = renderer.sharedMaterials;
            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                Material originalMaterial = sharedMaterials[i];
                if (originalMaterial != null)
                {
                    string materialPath = AssetDatabase.GetAssetPath(originalMaterial);
                    Debug.Log("Material path: " + materialPath);

                    // Check if the material is in the "Shared Assets" directory or is a built-in material
                    if (materialPath.Contains("Shared Assets") || materialPath == "Resources/unity_builtin_extra")
                    {
                        string newPath = folderPath + "/" + originalMaterial.name + ".mat";

                        // Check if the material already exists
                        Material copiedMaterial;
                        if (!materialMapping.TryGetValue(originalMaterial, out copiedMaterial))
                        {
                            copiedMaterial = AssetDatabase.LoadAssetAtPath<Material>(newPath);
                            if (copiedMaterial == null)
                            {
                                // Create a copy of the material
                                copiedMaterial = new Material(originalMaterial);
                                AssetDatabase.CreateAsset(copiedMaterial, newPath);

                                // Change the shader to HDRP/Lit
                                copiedMaterial.shader = Shader.Find("HDRP/Lit");
                                Debug.Log("Copied and converted material: " + copiedMaterial.name);
                            }
                            else
                            {
                                Debug.Log("Material already exists: " + copiedMaterial.name);
                            }

                            // Add to the mapping dictionary
                            materialMapping[originalMaterial] = copiedMaterial;
                        }
                        else if (copiedMaterial != null)
                        {
                            Debug.Log("Using existing copied material: " + copiedMaterial.name);
                        }

                        // Assign the copied material back to the renderer
                        sharedMaterials[i] = copiedMaterial;
                    }
                }
            }
            renderer.sharedMaterials = sharedMaterials;
        }

        // Ensure all renderers are updated with the new materials
        foreach (Renderer renderer in renderers)
        {
            Material[] sharedMaterials = renderer.sharedMaterials;
            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                Material originalMaterial = sharedMaterials[i];
                if (originalMaterial != null && materialMapping.ContainsKey(originalMaterial))
                {
                    sharedMaterials[i] = materialMapping[originalMaterial];
                }
            }
            renderer.sharedMaterials = sharedMaterials;
        }

        Debug.Log("Copied and converted materials for all renderers in the scene.");

        // Save the changes
        AssetDatabase.SaveAssets();
    }
}

#endif