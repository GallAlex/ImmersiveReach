using System;
using System.Runtime.InteropServices;
using UnityEngine;
using System.Diagnostics;
using GLTFast.Export;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;
using Unity.Collections;
using System.Threading.Tasks;

// UNUSED
/*
public class OptixRaycastingWrapper
{
    public Transform rayStart;
    public int instrumentSamplingPoints;
    public int resolution = 256;
    public LayerMask layerMask;
    public GameObject objectStudied;
    public int vertexNeighborhoodRadius = 0;
    public bool normalizeColorsWithMaxHit = true;
    public ColoringMethod coloringMethod;
    public bool debug = false;
    public bool colorEveryObject = false;
    public InstrumentSamplingMethod instrumentSamplingMethod;
    public CastingAreaMethod castingAreaMethod;
    public float coneAngle;
    public int ppu;
    public bool displayOnlyObjectStudied = false;
    public int accessibilityColoringValues = 3;
    public bool drawIsoLines = true;
    public float sigmaGaussianSmoothing = 1.0f;
    public bool continuousRaycasting = false;
    public float maxHitDistance = 100.0f;


    public const string OPTIX_LIB = "optixRaycasting";

    // Import the functions from the DLL
    [DllImport(OPTIX_LIB)]
    private static extern IntPtr initializeOptix(String gltfScenePath, int displayWidth, int displayHeight, int instrumentSamplingPoints, IntPtr instrumentVolumeSample);

    [DllImport(OPTIX_LIB)]
    private static extern void cleanupOptix(IntPtr optixState);

    [DllImport(OPTIX_LIB)]
    private static extern IntPtr performRaycasting(IntPtr optixState, ref Float3 rayStartPosition, ref Quaternion rayStartRotation, ref Float3 planePosition, ref Quaternion planeRotation, ref Float3 planeScale);

    [DllImport(OPTIX_LIB)]
    private static extern void earlyCleanup(IntPtr optixState, IntPtr raycastingOutput);

    private Stopwatch stopwatch;
    private IntPtr optixState;
    private bool optixInitializationDone = false;
    private Float3 rayStartPosition;
    private Vector3 previousRayStartPosition;
    private Float3[] sampledRayLaunchPoints;
    private Bounds instrumentBounds;
    private Vector3 instrumentOrigin;
    private CastingArea castingArea; // Surface used to determine the rays direction
    private NativeArray<OptixHit> optixHits;
    private RaycastObject raycastObject;
    private List<RaycastObject> raycastObjects;
    private Bounds aggregateBounds; // New variable to store the aggregate bounds of all objects
    private GameObject[] layerObjects;
    private Transform cameraTransform;
    private bool deleteObjectInitalColor = true;
    private bool raycastResultsAvailable = false;

    public void SetDisplayOnlyObjectStudied(bool value)
    {
        displayOnlyObjectStudied = value;
        ObjectHelper.UpdateDisplayOnlyObjectStudied(displayOnlyObjectStudied, layerObjects);
    }

    public void SetAccessibilityColoringValues(int value)
    {
        accessibilityColoringValues = value;
        TextureColoringHelper.SetColorPalette(accessibilityColoringValues);
        UpdateObjectsColoring();
    }

    public void SetDrawIsoLines(bool value)
    {
        drawIsoLines = value;
        TextureColoringHelper.SetDrawIsoLines(drawIsoLines);
        UpdateObjectsColoring();
    }

    public void SetSigmaGaussianSmoothing(float value)
    {
        sigmaGaussianSmoothing = value;
        TextureColoringHelper.SetGaussianKernel(sigmaGaussianSmoothing);
        UpdateObjectsColoring();
    }

    public void SetColoringMethod(ColoringMethod value)
    {
        coloringMethod = value;
        PerformRaycasting();
    }

    public void SetVertexNeighborhoodRadius(int value)
    {
        vertexNeighborhoodRadius = value;
        if (ColoringMethod.VertexNeighborhood == coloringMethod)
        {
            ComputeObjectsColoring();
        }
    }

    public void SetNormalizeColorsWithMaxHit(bool value)
    {
        normalizeColorsWithMaxHit = value;
        ComputeObjectsColoring();
    }

    public async Task Initialize()
    {
        try
        {
            stopwatch = new Stopwatch();
            stopwatch.Start();
            previousRayStartPosition = rayStart.position;
            cameraTransform = Camera.main.transform;
            TextureColoringHelper.SetGaussianKernel(sigmaGaussianSmoothing);
            TextureColoringHelper.SetColorPalette(accessibilityColoringValues);
            TextureColoringHelper.SetDrawIsoLines(drawIsoLines);

            String gltfScenePath = "./Temp/ExportedScene.gltf";

            // Export the scene to glTF
            layerObjects = ObjectHelper.FindGameObjectsWithLayer(layerMask);
            var export = new GameObjectExport();
            export.AddScene(layerObjects);
            await export.SaveToFileAndDispose(gltfScenePath);
            String fullPath = Path.GetFullPath(gltfScenePath);

            // Sample the instrument volume
            // Get the renderer of the child of the first active child of rayStart
            Transform firstActiveChild = null;
            foreach (Transform child in rayStart)
            {
                if (child.gameObject.activeSelf)
                {
                    firstActiveChild = child;
                    break;
                }
            }

            raycastObjects = new List<RaycastObject>();
            aggregateBounds = ObjectHelper.GetAggregatedBounds(layerObjects);
            foreach (GameObject obj in layerObjects)
            {
                if (obj == objectStudied)
                {
                    raycastObjects.Add(new RaycastObject(obj, ppu, true));
                    raycastObject = raycastObjects[^1]; // Saved as a ref to the corresponding object in the list
                }
                else
                {
                    raycastObjects.Add(new RaycastObject(obj, ppu, false));
                }
            }
            layerObjects = ObjectHelper.FindGameObjectsWithLayer(layerMask, objectStudied);

            castingArea = new CastingArea(castingAreaMethod, debug);

            if (firstActiveChild != null)
            {
                Renderer childRenderer = firstActiveChild.GetComponentInChildren<Renderer>();
                if (childRenderer != null)
                {
                    instrumentBounds = childRenderer.bounds;
                    instrumentOrigin = childRenderer.transform.position;
                    Vector3 instrumentSize = instrumentBounds.size;
                
                    // Compute ray launch volume
                    Mesh instrumentMesh = firstActiveChild.GetComponentInChildren<MeshFilter>(false).sharedMesh;
                    List<InstrumentSamplePoint> sampledPoints = VolumeSampler.Sample(instrumentMesh, instrumentSize);
                    sampledPoints = VolumeSampler.ReduceSampledPoints(sampledPoints, instrumentSamplingPoints);
                    instrumentSamplingPoints = sampledPoints.Count;
                    Vector3 originOffset = instrumentBounds.center - instrumentOrigin - instrumentBounds.size/2;
                    VolumeSampler.AddOriginOffset(ref sampledPoints, originOffset);
                    sampledRayLaunchPoints = new Float3[instrumentSamplingPoints];
                    for (int i = 0; i < instrumentSamplingPoints; i++)
                    {
                        var point = sampledPoints[i];
                        sampledRayLaunchPoints[i] = new (point.point.x, point.point.y, point.point.z);
                    }
                }
            }

            IntPtr sampledRayLaunchPointsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Float3)) * sampledRayLaunchPoints.Length);
            Marshal.Copy(sampledRayLaunchPoints.SelectMany(f => new float[] { f.x, f.y, f.z }).ToArray(), 0, sampledRayLaunchPointsPtr, sampledRayLaunchPoints.Length * 3);
            optixState = initializeOptix(fullPath, resolution, resolution, instrumentSamplingPoints, sampledRayLaunchPointsPtr);
            Marshal.FreeHGlobal(sampledRayLaunchPointsPtr);

            optixHits = new NativeArray<OptixHit>(resolution * resolution * instrumentSamplingPoints, Allocator.Persistent);

            stopwatch.Stop();
            Debug.Log("OptiX initialization time : " + stopwatch.ElapsedMilliseconds + " ms");
            optixInitializationDone = true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during OptiX initialization: {e}");
        }
    }

    public void Update()
    {
        // Recalculate heatmap if the ray start position has changed
        if (continuousRaycasting && cameraTransform.position != previousRayStartPosition)
        {
            PerformRaycasting();
        }
    }

    public void PerformRaycasting()
    {
        if (!optixInitializationDone)
        {
            return;
        }

        stopwatch.Restart();
        try
        {
            rayStart.SetPositionAndRotation(cameraTransform.position, cameraTransform.rotation);
            rayStartPosition = new Float3(rayStart.position.x, rayStart.position.y, rayStart.position.z);
            Quaternion rayStartRotation = rayStart.rotation;

                if (colorEveryObject)
                {
                    castingArea.Update(rayStart.position, aggregateBounds);
                }
                else
                {
                    castingArea.Update(rayStart.position, raycastObject.objectBounds);
                }

                Float3 planePosition = new Float3(castingArea.position.x, castingArea.position.y, castingArea.position.z);
                Quaternion planeRotation = castingArea.rotation;
                Float3 planeScale = new Float3(castingArea.scale.x, castingArea.scale.y, castingArea.scale.z);

            IntPtr outputPtr = performRaycasting(optixState, ref rayStartPosition, ref rayStartRotation, ref planePosition, ref planeRotation, ref planeScale);

            RaycastingOutput output = Marshal.PtrToStructure<RaycastingOutput>(outputPtr);
            byte[] ints = new byte[output.arraySize];
            Marshal.Copy(output.data, ints, 0, output.arraySize);

            OptixHit[] hits = new OptixHit[output.arraySize * instrumentSamplingPoints];
            IntPtr currentPtr = output.hits;
            for (int i = 0; i < hits.Length; i++)
            {
                hits[i] = Marshal.PtrToStructure<OptixHit>(currentPtr);
                currentPtr = IntPtr.Add(currentPtr, Marshal.SizeOf(typeof(OptixHit)));
            }

            // Copy hits to NativeArray
            for (int i = 0; i < hits.Length; ++i)
            {
                optixHits[i] = hits[i];
            }
            raycastResultsAvailable = true;

            earlyCleanup(optixState, outputPtr);

            if (deleteObjectInitalColor)
            {
                objectStudied.GetComponent<Renderer>().material.SetColor("_BaseColor", Color.white);
                deleteObjectInitalColor = false;
            }

            ComputeObjectsColoring();

            previousRayStartPosition = cameraTransform.position;
            stopwatch.Stop();
            Debug.Log("Computation time : " + stopwatch.ElapsedMilliseconds + " ms");

        }
        catch (Exception e)
        {
            Debug.LogError($"Error during OptiX raycasting: {e}");
        }
    }

    public void Dispose()
    {
        try
        {
            if (optixState != IntPtr.Zero)
            {
                cleanupOptix(optixState);
            }
            if (optixHits.IsCreated)
            {
                optixHits.Dispose();
            }
            Debug.Log("OptiX resources cleaned up successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error during OptiX cleanup: {e}");
        }
    }

    private void ComputeObjectsColoring()
    {
        if (!raycastResultsAvailable)
        {
            return;
        }
        raycastObject.ClearMaterialColor();
        if (colorEveryObject)
        {
            // We color all objects in the layer
            for (int i = 0; i < raycastObjects.Count; ++i)
            {
                raycastObjects[i].ClearMaterialColor();
                // RaycastHelper.ComputeObjectColor(coloringMethod, raycastObjects[i], instrumentSamplingPoints, optixHits, vertexNeighborhoodRadius, normalizeColorsWithMaxHit, drawIsoLines);
            }
        }
        else
        {
            // We color only the object studied
            // RaycastHelper.ComputeObjectColor(coloringMethod, raycastObject, instrumentSamplingPoints, optixHits, vertexNeighborhoodRadius, normalizeColorsWithMaxHit, drawIsoLines);
        }
    }

    private void UpdateObjectsColoring()
    {
        if (!raycastResultsAvailable)
        {
            return;
        }
        if (colorEveryObject)
        {
            for (int i = 0; i < raycastObjects.Count; ++i)
            {
                TextureColoringHelper.UpdateObjectColor(raycastObjects[i]);
            }
        }
        else
        {
            TextureColoringHelper.UpdateObjectColor(raycastObject);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct Float3
{
    public float x;
    public float y;
    public float z;

    public Float3(float x, float y, float z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct RaycastingOutput
{
    public int arraySize;
    public int width;
    public int height;
    public IntPtr data;
    public IntPtr hits;
}
*/