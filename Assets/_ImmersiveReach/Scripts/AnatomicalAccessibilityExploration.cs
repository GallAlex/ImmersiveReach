using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using System.Diagnostics;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System;
using System.Linq;



public class AnatomicalAccessibilityExploration
{
    public Transform instrument;
    public int instrumentSamplingPoints = 5;
    public float maxHitDistance = 50f;
    public LayerMask layerMask;
    public GameObject objectStudied;
    public int vertexNeighborhoodRadius = 0;
    public AccessibilityAggregationMethod accessibilityAggregationMethod;
    public bool debug = false;
    public bool colorEveryObject = false;
    public InstrumentSamplingMethod instrumentSamplingMethod;
    public CastingAreaMethod castingAreaMethod;
    public float rotationAngle;
    public int coneSamplingRotations;
    public int ppu = 10; // Pixels per unit (= meter in the scene)
    public bool displayOnlyObjectStudied = false;
    public int accessibilityColoringValues = 3;
    public bool drawIsoLines = true;
    public float sigmaGaussianSmoothing = 1.0f;
    public bool continuousRaycasting = false;
    public bool reduceInvalidInstrumentPoints = false;
    public bool displayInstrumentPoints = false;
    public bool displayInstrumentObject = true;
    public bool displayInstrumentCone = false;
    public bool drawColors = true;
    public int textureScaleFactor = 6;
    public bool isActive = true;
    public ParticleSystem instrumentPointsParticleSystem;

    private Vector3 previousInstrumentPosition;
    private Stopwatch stopwatch;
    private NativeArray<RaycastHit> raycastResults;
    private NativeArray<RaycastCommand> raycastCommands;
    private int nbRaysTotal;
    private NativeArray<InstrumentSamplePoint> rayOrigins;
    private QueryParameters raycastCommandParams => new QueryParameters(layerMask);
    private List<InstrumentSamplePoint> sampledRayLaunchPoints;
    private Bounds instrumentBounds;
    private CastingArea castingArea; // Surface used to determine the rays direction
    private Bounds aggregateBounds; // New variable to store the aggregate bounds of all objects
    private RaycastObject raycastObject;
    private List<RaycastObject> raycastObjects;
    private NativeArray<RaycastHitWrapper> raycastResultsWrapper;
    private readonly int numberOfThreads = System.Environment.ProcessorCount;
    private int widthResolution;
    private int heightResolution;
    private GameObject[] layerObjects; // GameObjects in the layer, excluding the object studied
    private Transform cameraTransform;
    private int passedInstrumentSamplingPoints; // Store the value of the passed parameter instrument sampling points
    private Vector3 instrumentOriginOffset;
    private List<InstrumentSamplePoint> initialInstrumentSampledPoints; // Store the initial sampled points for the instrument to avoid recomputing them
    private List<InstrumentSamplePoint> reducedSampledPoints; // Store the reduced sampled points for the instrument to avoid recomputing them
    private bool raycastResultsAvailable = false;
    private Renderer instrumentRenderer;
    private GameObject cameraInstrumentDuplicate;
    private bool reducedValidSampledPoints = false; // Store if the sampled positioned points have been reduced to avoid computing from points inside a layer object
    private readonly float particleSize = 0.005f;
    private GameObject instrumentCone;
    
    public void SetDisplayOnlyObjectStudied(bool value)
    {
        displayOnlyObjectStudied = value;
        ObjectHelper.UpdateDisplayOnlyObjectStudied(displayOnlyObjectStudied, layerObjects);
        // if true, we disable the collider of the non-displayed objects to move around more easily
        foreach (GameObject obj in layerObjects)
        {
            obj.GetComponent<Collider>().enabled = !displayOnlyObjectStudied;
        }
    }

    public void SetAccessibilityColoringValues(int value)
    {
        accessibilityColoringValues = value;
        TextureColoringHelper.SetColorPalette(accessibilityColoringValues);
        if (isActive)
        {
            UpdateObjectsColoring();
        }
    }
    
    public void SetDrawIsoLines(bool value)
    {
        drawIsoLines = value;
        if (isActive)
        {
            ColorObjects();
        }
    }

    public void SetSigmaGaussianSmoothing(float value)
    {
        sigmaGaussianSmoothing = value;
        RaycastHelper.SetGaussianKernel(sigmaGaussianSmoothing);
        if (isActive)
        {
            UpdateObjectsColoring();
        }
    }

    public void SetAccessibilityAggregationMethod(AccessibilityAggregationMethod value)
    {
        accessibilityAggregationMethod = value;
        if (isActive)
        {
            PerformRaycasting();
        }
    }

    public void SetPpu(int value)
    {
        ppu = value;
        if (colorEveryObject)
        {
            widthResolution = Mathf.Min(Mathf.CeilToInt(aggregateBounds.size.x * ppu), 1000); // Limit resolution to 1000 to avoid memory issues
            heightResolution = Mathf.Min(Mathf.CeilToInt(aggregateBounds.size.y * ppu), 1000);
            foreach (RaycastObject obj in raycastObjects)
            {
                obj.UpdateResolution(ppu);
            }
        }
        else
        {
            Vector3 objectSize = ObjectHelper.GetObjectBounds(objectStudied).size;
            widthResolution = Mathf.Min(Mathf.CeilToInt(objectSize.x * ppu), 1000);
            heightResolution = Mathf.Min(Mathf.CeilToInt(objectSize.y * ppu), 1000);
            raycastObject.UpdateResolution(ppu);
        }
        nbRaysTotal = widthResolution * heightResolution * instrumentSamplingPoints;
        DisposeNativeArrays(false);
        raycastResults = new NativeArray<RaycastHit>(nbRaysTotal, Allocator.Persistent);
        raycastCommands = new NativeArray<RaycastCommand>(nbRaysTotal, Allocator.Persistent);
        raycastResultsWrapper = new NativeArray<RaycastHitWrapper>(nbRaysTotal, Allocator.Persistent);
        raycastResultsAvailable = false;
        if (isActive)
        {
            PerformRaycasting();
        }
    }

    public void SetInstrumentSamplingPoints(int value)
    {
        instrumentSamplingPoints = value;
        passedInstrumentSamplingPoints = value;
        ComputeRayLaunchPoints(true);
        nbRaysTotal = widthResolution * heightResolution * instrumentSamplingPoints;
        DisposeNativeArrays();
        raycastResults = new NativeArray<RaycastHit>(nbRaysTotal, Allocator.Persistent);
        raycastCommands = new NativeArray<RaycastCommand>(nbRaysTotal, Allocator.Persistent);
        rayOrigins = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
        raycastResultsWrapper = new NativeArray<RaycastHitWrapper>(nbRaysTotal, Allocator.Persistent);
        raycastResultsAvailable = false;
        if (isActive)
        {
            PerformRaycasting();
        }
    }

    public void SetInstrumentSamplingMethod(InstrumentSamplingMethod value)
    {
        instrumentSamplingMethod = value;
        int previousInstrumentSamplingPoints = instrumentSamplingPoints;
        instrumentSamplingPoints = passedInstrumentSamplingPoints;
        bool reduceSampledPoints = reducedSampledPoints == null;
        ComputeRayLaunchPoints(reduceSampledPoints);
        if (instrumentSamplingPoints != previousInstrumentSamplingPoints)
        {
            nbRaysTotal = widthResolution * heightResolution * instrumentSamplingPoints;
            DisposeNativeArrays();
            raycastResults = new NativeArray<RaycastHit>(nbRaysTotal, Allocator.Persistent);
            raycastCommands = new NativeArray<RaycastCommand>(nbRaysTotal, Allocator.Persistent);
            rayOrigins = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
            raycastResultsWrapper = new NativeArray<RaycastHitWrapper>(nbRaysTotal, Allocator.Persistent);
        }
        raycastResultsAvailable = false;
        if (isActive)
        {
            PerformRaycasting();
        }
    }

    public void SetRotationAngle(float value)
    {
        rotationAngle = value;
        if (instrumentSamplingMethod == InstrumentSamplingMethod.ConeUniformSampling || instrumentSamplingMethod == InstrumentSamplingMethod.InstrumentSamplingWithRotations)
        {
            int previousInstrumentSamplingPoints = instrumentSamplingPoints;
            instrumentSamplingPoints = passedInstrumentSamplingPoints;
            ComputeRayLaunchPoints(false);
            if (instrumentSamplingPoints != previousInstrumentSamplingPoints)
            {
                nbRaysTotal = widthResolution * heightResolution * instrumentSamplingPoints;
                DisposeNativeArrays();
                raycastResults = new NativeArray<RaycastHit>(nbRaysTotal, Allocator.Persistent);
                raycastCommands = new NativeArray<RaycastCommand>(nbRaysTotal, Allocator.Persistent);
                rayOrigins = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
                raycastResultsWrapper = new NativeArray<RaycastHitWrapper>(nbRaysTotal, Allocator.Persistent);
            }
            raycastResultsAvailable = false;
            if (isActive)
            {
                PerformRaycasting();
            }
        }
    }

    public void SetConeSamplingRotations(int value)
    {
        coneSamplingRotations = value;
        if (instrumentSamplingMethod == InstrumentSamplingMethod.InstrumentSamplingWithRotations)
        {
            int previousInstrumentSamplingPoints = instrumentSamplingPoints;
            instrumentSamplingPoints = passedInstrumentSamplingPoints;
            ComputeRayLaunchPoints(false);
            if (instrumentSamplingPoints != previousInstrumentSamplingPoints)
            {
                nbRaysTotal = widthResolution * heightResolution * instrumentSamplingPoints;
                DisposeNativeArrays();
                raycastResults = new NativeArray<RaycastHit>(nbRaysTotal, Allocator.Persistent);
                raycastCommands = new NativeArray<RaycastCommand>(nbRaysTotal, Allocator.Persistent);
                rayOrigins = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
                raycastResultsWrapper = new NativeArray<RaycastHitWrapper>(nbRaysTotal, Allocator.Persistent);
            }
            raycastResultsAvailable = false;
            if (isActive)
            {
                PerformRaycasting();
            }
        }
    }

    public void SetMaxHitDistance(float value)
    {
        maxHitDistance = value;
        if (isActive)
        {
            PerformRaycasting();
        }
    }

    public void SetVertexNeighborhoodRadius(int value)
    {
        vertexNeighborhoodRadius = value;
        if (isActive && AccessibilityAggregationMethod.VertexNeighborhood == accessibilityAggregationMethod)
        {
            ComputeObjectsColoring();
            ColorObjects();
        }
    }

    public void SetColorEveryObject(bool value)
    {
        colorEveryObject = value;
        widthResolution = colorEveryObject ? Mathf.Min(Mathf.CeilToInt(aggregateBounds.size.x * ppu), 1000) : raycastObject.widthResolution;
        heightResolution = colorEveryObject? Mathf.Min(Mathf.CeilToInt(aggregateBounds.size.y * ppu), 1000) : raycastObject.heightResolution;
        nbRaysTotal = widthResolution * heightResolution * instrumentSamplingPoints;
        DisposeNativeArrays();
        raycastResults = new NativeArray<RaycastHit>(nbRaysTotal, Allocator.Persistent);
        raycastCommands = new NativeArray<RaycastCommand>(nbRaysTotal, Allocator.Persistent);
        rayOrigins = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
        raycastResultsWrapper = new NativeArray<RaycastHitWrapper>(nbRaysTotal, Allocator.Persistent);
        raycastResultsAvailable = false;
        if (!colorEveryObject)
        {
            ResetObjectsColoring(false);
        }
        if (isActive)
        {
            PerformRaycasting();
        }
    }

    public void SetReduceInvalidInstrumentPoints(bool value)
    {
        reduceInvalidInstrumentPoints = value;
        if (!reduceInvalidInstrumentPoints)
        {
            // using 2nd option : we keep all the points and include non-valid points in the accessibility encoding
            // we check the current number of sampled points used for the instrument
            if (instrumentSamplingPoints != sampledRayLaunchPoints.Count)
            {
                instrumentSamplingPoints = sampledRayLaunchPoints.Count;
                nbRaysTotal = widthResolution * heightResolution * instrumentSamplingPoints;
                DisposeNativeArrays();
                raycastResults = new NativeArray<RaycastHit>(nbRaysTotal, Allocator.Persistent);
                raycastCommands = new NativeArray<RaycastCommand>(nbRaysTotal, Allocator.Persistent);
                rayOrigins = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
                raycastResultsWrapper = new NativeArray<RaycastHitWrapper>(nbRaysTotal, Allocator.Persistent);
            }
        }
        else
        {
            // using 1st option : we remove the points that are inside the layer objects, decreasing the number of points
            // checking the number of points used for the sampling will be done in the next PerformRaycasting call (in ComputeRaycasting)
            reducedValidSampledPoints = false;
        }
        raycastResultsAvailable = false;
        if (isActive)
        {
            PerformRaycasting();
        }
    }

    public void SetDisplayInstrumentPoints(bool value)
    {
        displayInstrumentPoints = value;
        if (isActive)
        {
            if (displayInstrumentPoints)
            {
                instrumentPointsParticleSystem.gameObject.SetActive(true);
                if (raycastResultsAvailable)
                {
                    TextureColoringHelper.UpdateParticleSystem(rayOrigins, instrumentPointsParticleSystem, particleSize);
                }
            }
            else
            {
                if (!debug)
                {
                    instrumentPointsParticleSystem.gameObject.SetActive(false);
                }
            }
        }
    }

    public void SetDisplayInstrumentObject(bool value)
    {
        displayInstrumentObject = value;
        if (isActive)
        {
            instrumentRenderer.enabled = displayInstrumentObject;
        }
    }

    public void SetDisplayInstrumentCone(bool value)
    {
        displayInstrumentCone = value;
        if (isActive)
        {
            instrumentCone.GetComponent<Renderer>().enabled = displayInstrumentCone;
            if (displayInstrumentCone && raycastResultsAvailable)
            {
                InstrumentHelper.UpdateConeReachability(instrumentCone, instrument, instrumentBounds.size.z, rayOrigins, rotationAngle);
            }
        }
    }

    public void SetDrawColors(bool value)
    {
        drawColors = value;
        if (isActive)
        {
            ColorObjects();
        }
    }

    public void SetTextureScaleFactor(int value)
    {
        textureScaleFactor = value;
        foreach (RaycastObject obj in raycastObjects)
        {
            obj.SetTextureScaleFactor(textureScaleFactor, isActive);
        }
        if (isActive)
        {
            UpdateObjectsColoring();
        }
    }

    public void SetIsActive(bool value)
    {
        isActive = value;
        if (isActive)
        {
            SetDisplayInstrumentPoints(displayInstrumentPoints);
            SetDisplayInstrumentCone(displayInstrumentCone);
            SetDisplayInstrumentObject(displayInstrumentObject);
        }
        else
        {
            ResetObjectsColoring();
            raycastResultsAvailable = false;
        }
    }

    public void Initialize()
    {
        try {
            cameraTransform = Camera.main.transform;
            previousInstrumentPosition = instrument.position;
            stopwatch = new Stopwatch();
            RaycastHelper.SetGaussianKernel(sigmaGaussianSmoothing);
            TextureColoringHelper.SetColorPalette(accessibilityColoringValues);
            passedInstrumentSamplingPoints = instrumentSamplingPoints;
            GameObject[] allLayerObjects = ObjectHelper.FindGameObjectsWithLayer(layerMask);
            aggregateBounds = ObjectHelper.GetAggregatedBounds(allLayerObjects);
            // Filter out objectStudied from allLayerObjects to create layerObjects
            layerObjects = allLayerObjects.Where(obj => obj != objectStudied).ToArray();
            ObjectHelper.UpdateDisplayOnlyObjectStudied(displayOnlyObjectStudied, layerObjects);

            // Initialize raycasting objects
            raycastObjects = new List<RaycastObject>();
            foreach (GameObject obj in allLayerObjects)
            {
                if (obj == objectStudied)
                {
                    raycastObjects.Add(new RaycastObject(obj, ppu, true, textureScaleFactor));
                    raycastObject = raycastObjects[^1]; // Saved as a ref to the corresponding object in the list
                }
                else
                {
                    raycastObjects.Add(new RaycastObject(obj, ppu, false, textureScaleFactor));
                }
            }
            // Limit resolution to 1000 to avoid memory issues
            widthResolution = colorEveryObject ? Mathf.Min(Mathf.CeilToInt(aggregateBounds.size.x * ppu), 1000) : raycastObject.widthResolution;
            heightResolution = colorEveryObject? Mathf.Min(Mathf.CeilToInt(aggregateBounds.size.y * ppu), 1000) : raycastObject.heightResolution;

            castingArea = new CastingArea(castingAreaMethod, debug);

            instrumentRenderer = instrument.GetComponentInChildren<Renderer>();
            if (instrumentRenderer == null)
            {
                throw new Exception("Renderer not found in instrument object");
            }
            Mesh instrumentMesh = instrument.GetComponentInChildren<MeshFilter>(false).sharedMesh;
            if (instrumentMesh == null)
            {
                throw new Exception("MeshFilter not found in instrument object");
            }

            // create a duplicate of the instrument for the FP camera
            cameraInstrumentDuplicate = UnityEngine.Object.Instantiate(instrument.gameObject, cameraTransform.position + GameManager.cameraInstrumentOffset, cameraTransform.rotation);
            cameraInstrumentDuplicate.name = "CameraInstrument";
            cameraInstrumentDuplicate.transform.parent = cameraTransform;
            cameraInstrumentDuplicate.GetComponentInChildren<MeshCollider>(false).enabled = false;
            // Camera instrument is a bit transparent, and yellow to differentiate it from the instrument placed to cast rays
            // Load the material from the Resources folder
            Material transparentObjectMaterial = Resources.Load<Material>("Materials/InstrumentObjectTransparentMaterial");
            if (transparentObjectMaterial != null)
            {
                cameraInstrumentDuplicate.GetComponentInChildren<Renderer>(false).material = transparentObjectMaterial;
            }
            else
            {
                Debug.Log("InstrumentObjectTransparentMaterial not found in Resources folder, using default material");
                cameraInstrumentDuplicate.GetComponentInChildren<Renderer>(false).material.color = new Color(0.964f, 0.980f, 0.408f);
            }

            SetDisplayInstrumentPoints(displayInstrumentPoints);
            instrumentRenderer.enabled = false;

            // Transform origin of the instrument object to the center of its bounding box
            instrumentBounds = ObjectHelper.GetObjectBounds(instrument.gameObject);
            Vector3 instrumentOrigin = instrumentRenderer.transform.position;
            instrumentOriginOffset = instrumentBounds.center - instrumentOrigin - instrumentBounds.size/2;

            initialInstrumentSampledPoints = VolumeSampler.Sample(instrumentMesh, instrumentBounds.size);
            VolumeSampler.AddOriginOffset(ref initialInstrumentSampledPoints, instrumentOriginOffset);
            ComputeRayLaunchPoints(true);
            Material transparentConeMaterial = Resources.Load<Material>("Materials/InstrumentConeTransparentMaterial");
            instrumentCone = InstrumentHelper.CreateInstrumentConeMesh(instrumentBounds.size.z, rotationAngle, transparentConeMaterial);
            instrumentCone.GetComponent<Renderer>().enabled = false;
            nbRaysTotal = widthResolution * heightResolution * instrumentSamplingPoints;

            // Initialize NativeArrays for raycasting and heatmap colors
            raycastResults = new NativeArray<RaycastHit>(nbRaysTotal, Allocator.Persistent);
            raycastCommands = new NativeArray<RaycastCommand>(nbRaysTotal, Allocator.Persistent);
            rayOrigins = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
            raycastResultsWrapper = new NativeArray<RaycastHitWrapper>(nbRaysTotal, Allocator.Persistent);
        }
        catch (Exception e)
        {
            Debug.LogError("Error while initializing the application: " + e.Message);
        }
    }

    public void Update()
    {
        // Recalculate heatmap if the instrument position has changed
        if (isActive && continuousRaycasting && cameraTransform.position != previousInstrumentPosition)
        {
            PerformRaycasting();
        }
    }

    public void Dispose()
    {
        DisposeNativeArrays();
    }

    // Dispose existing NativeArrays
    private void DisposeNativeArrays(bool disposeRayOrigins = true)
    {
        if (raycastResults.IsCreated)
        {
            raycastResults.Dispose();
        }
        if (raycastCommands.IsCreated)
        {
            raycastCommands.Dispose();
        }
        if (rayOrigins.IsCreated && disposeRayOrigins)
        {
            rayOrigins.Dispose();
        }
        if (raycastResultsWrapper.IsCreated)
        {
            raycastResultsWrapper.Dispose();
        }
    }

    // Perform raycasting and update the object's texture with the results
    public void PerformRaycasting()
    {
        // TestDiscontinuities();
        // TestZonesMapping();
        stopwatch.Restart();
        PositionInstrument();
        ComputeRaycasting();
        ComputeObjectsColoring();
        ColorObjects();
        previousInstrumentPosition = cameraTransform.position;
        stopwatch.Stop();
        Debug.Log("Computation time : " + stopwatch.ElapsedMilliseconds + " ms");
    }

    // Debugging function
    // Displays on the object studied the discontinuities of the UVs of the object studied
    public void TestDiscontinuities()
    {
        int textureWidth = raycastObject.textureWidth;
        int textureHeight = raycastObject.textureHeight;
        Color[] textureColors = new Color[textureWidth * textureHeight];
        bool[,] discontinuityBorders = raycastObject.GetDiscontinuityBorders();
        for (int i = 0; i < textureWidth; ++i)
        {
            for (int j = 0; j < textureHeight; ++j)
            {
                if (discontinuityBorders[i, j])
                {
                    textureColors[i + j * textureWidth] = Color.black;
                }
                else
                {
                    textureColors[i + j * textureWidth] = Color.white;
                }
            }
        }
        raycastObject.SetTexture(textureColors);
    }

    // Debugging function
    // Displays the different zones of continuous UVs of the object studied
    public void TestZonesMapping()
    {
        Color[] textureColors = raycastObject.VisualizeZones();
        raycastObject.SetTexture(textureColors);
    }

    public void PositionInstrument()
    {
        instrument.SetPositionAndRotation(cameraInstrumentDuplicate.transform.position, cameraInstrumentDuplicate.transform.rotation);

        // 1st option : we remove the points that are inside the layer objects, decreasing the number of points
        if (reduceInvalidInstrumentPoints)
        {
            int currentNbRaysTotal = nbRaysTotal;
            // if the sampled points have been reduced the last time ComputeRaycasting was called,
            // we need to get back to the initial sampled points
            if (reducedValidSampledPoints)
            {
                instrumentSamplingPoints = sampledRayLaunchPoints.Count;
                nbRaysTotal = widthResolution * heightResolution * instrumentSamplingPoints;
                rayOrigins.Dispose();
                rayOrigins = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
            }
            // we position the instrument points at the instrument position
            // then we check if some points are inside the layer objects
            VolumeSampler.Position(ref rayOrigins, sampledRayLaunchPoints, instrument.position, instrument.rotation);
            rayOrigins = InstrumentHelper.GetValidInstrumentPoints(rayOrigins, raycastObjects, instrumentSamplingMethod, out reducedValidSampledPoints);
            // if so, remove those and adapt the number of points
            if (reducedValidSampledPoints)
            {
                instrumentSamplingPoints = rayOrigins.Length;
                nbRaysTotal = widthResolution * heightResolution * instrumentSamplingPoints;
            }
            // if the number of rays has changed, we need to reallocate the arrays
            if (currentNbRaysTotal != nbRaysTotal)
            {
                DisposeNativeArrays(false);
                raycastResults = new NativeArray<RaycastHit>(nbRaysTotal, Allocator.Persistent);
                raycastCommands = new NativeArray<RaycastCommand>(nbRaysTotal, Allocator.Persistent);
                raycastResultsWrapper = new NativeArray<RaycastHitWrapper>(nbRaysTotal, Allocator.Persistent);
                raycastResultsAvailable = false;
            }
        }
        else {
            // 2nd option : we keep all the points and include non-valid points in the accessibility encoding
            VolumeSampler.Position(ref rayOrigins, sampledRayLaunchPoints, instrument.position, instrument.rotation);
            InstrumentHelper.UpdateInstrumentPointsValidity(ref rayOrigins, raycastObjects, instrumentSamplingMethod);
        }

        TextureColoringHelper.UpdateParticleSystem(rayOrigins, instrumentPointsParticleSystem, particleSize);

        if (displayInstrumentCone)
        {
            instrumentCone.GetComponent<Renderer>().enabled = true;
            InstrumentHelper.UpdateConeReachability(instrumentCone, instrument, instrumentBounds.size.z, rayOrigins, rotationAngle);
        }
        if (displayInstrumentObject)
        {
            instrumentRenderer.enabled = true;
        }
    }

    private void ResetObjectsColoring(bool applyOnObjectStudied = true)
    {
        foreach (RaycastObject obj in raycastObjects)
        {
            obj.ResetMaterialToInitial(applyOnObjectStudied);
        }
    }

    // Compute raycasting for all rays
    private void ComputeRaycasting()
    {
        if (colorEveryObject)
        {
            castingArea.Update(instrument.position, aggregateBounds);
        }
        else
        {
            castingArea.Update(instrument.position, raycastObject.objectBounds);
        }

        RaycastJob raycastJob = new RaycastJob
        {
            width = widthResolution,
            height = heightResolution,
            instrumentSamplingPoints = instrumentSamplingPoints,
            planeOrigin = castingArea.origin,
            planeXAxis = castingArea.xAxis,
            planeZAxis = castingArea.zAxis,
            sphereCenter = castingArea.sphereCenter,
            sphereNorthAxis = castingArea.sphereNorthAxis,
            sphereRadius = castingArea.sphereRadius,
            sphereCapHalfAngle = castingArea.sphereCapHalfAngle,
            isCastingSurfaceSpherical = castingArea.isCastingSurfaceSpherical,
            rayOrigins = rayOrigins,
            raycastCommands = raycastCommands,
            raycastCommandParams = raycastCommandParams,
            maxDistance = maxHitDistance,
            debug = debug
        };

        JobHandle jobHandle = raycastJob.Schedule(nbRaysTotal, 64);
        jobHandle.Complete();

        JobHandle handle = RaycastCommand.ScheduleBatch(raycastCommands, raycastResults, 1, 1, default(JobHandle));
        handle.Complete();

        // Convert raycast results for coloring
        Parallel.ForEach(Partitioner.Create(0, raycastResults.Length), new ParallelOptions { MaxDegreeOfParallelism = numberOfThreads }, range =>
        {
            for (int i = range.Item1; i < range.Item2; ++i)
            {
                raycastResultsWrapper[i] = new RaycastHitWrapper(raycastResults[i]);
            }
        });
        raycastResultsAvailable = true;
    }

    private void ComputeObjectsColoring()
    {
        if (!raycastResultsAvailable)
        {
            return;
        }
        if (colorEveryObject)
        {
            // We color all objects in the layer
            for (int i = 0; i < raycastObjects.Count; ++i)
            {
                RaycastHelper.ComputeObjectTextureData(accessibilityAggregationMethod, raycastObjects[i], raycastResultsWrapper, rayOrigins, vertexNeighborhoodRadius);
            }
        }
        else
        {
            // We color only the object studied
            RaycastHelper.ComputeObjectTextureData(accessibilityAggregationMethod, raycastObject, raycastResultsWrapper, rayOrigins, vertexNeighborhoodRadius);
        }
    }

    private void ColorObjects()
    {
        if (!raycastResultsAvailable)
        {
            return;
        }
        if (colorEveryObject)
        {
            foreach (RaycastObject obj in raycastObjects)
            {
                TextureColoringHelper.ColorObject(obj, drawColors, drawIsoLines);
            }
        }
        else
        {
            TextureColoringHelper.ColorObject(raycastObject, drawColors, drawIsoLines);
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
            foreach (RaycastObject obj in raycastObjects)
            {
                RaycastHelper.UpdateObjectTextureData(obj);
                TextureColoringHelper.ColorObject(obj, drawColors, drawIsoLines);
            }
        }
        else
        {
            RaycastHelper.UpdateObjectTextureData(raycastObject);
            TextureColoringHelper.ColorObject(raycastObject, drawColors, drawIsoLines);
        }
    }

    private void ComputeRayLaunchPoints(bool reduceSampledPoints = false)
    {
        sampledRayLaunchPoints = InstrumentHelper.ComputeInstrumentPoints(instrumentSamplingMethod, initialInstrumentSampledPoints, ref reducedSampledPoints,
        ref instrumentSamplingPoints, instrumentBounds, rotationAngle, coneSamplingRotations, instrumentOriginOffset, reduceSampledPoints);
    }

    // Job struct to create the rays for the raycasting process
    [BurstCompile]
    private struct RaycastJob : IJobParallelFor
    {
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public int instrumentSamplingPoints;
        [ReadOnly] public Vector3 planeOrigin;
        [ReadOnly] public Vector3 planeXAxis;
        [ReadOnly] public Vector3 planeZAxis;
        [ReadOnly] public Vector3 sphereCenter;
        [ReadOnly] public Vector3 sphereNorthAxis;
        [ReadOnly] public float sphereRadius;
        [ReadOnly] public float sphereCapHalfAngle;
        [ReadOnly] public bool isCastingSurfaceSpherical;
        [ReadOnly] public NativeArray<InstrumentSamplePoint> rayOrigins;
        public NativeArray<RaycastCommand> raycastCommands;
        [ReadOnly] public QueryParameters raycastCommandParams;
        [ReadOnly] public float maxDistance;
        [ReadOnly] public bool debug;

        public void Execute(int index)
        {
            int i = index / (width * instrumentSamplingPoints);
            int j = (index % (width * instrumentSamplingPoints)) / instrumentSamplingPoints;
            int k = index % instrumentSamplingPoints;
            int l = index / instrumentSamplingPoints;

            Vector3 rayOrigin = rayOrigins[k].point;
            Vector3 pixelPosition;
            Vector3 rayDirection;

            if (isCastingSurfaceSpherical)
            {
                float phi = (1 + Mathf.Sqrt(5)) / 2; // golden ratio

                // Using Spherical Fibonacci Lattice to distribute points on a sphere cap
                float x = l * phi;
                float y = (Mathf.Cos(sphereCapHalfAngle * Mathf.PI / 180.0f) * -0.5f + 0.5f) * l / ((float)(width*height));
                float longitude = 2 * Mathf.PI * x; // longitude, around yAxis
                // float colatitude = Mathf.PI - Mathf.Acos(2 * y - 1); // colatitude, around zAxis
                float latitude = Mathf.Acos(2 * y - 1) - Mathf.PI/2.0f; 

                float a = Mathf.Cos(latitude);
                Vector3 sphericalPos = new Vector3(a * Mathf.Cos(longitude), Mathf.Sin(latitude), a * Mathf.Sin(longitude));
                // Vector3 sphericalPos = Quaternion.Euler(0, longitude * Mathf.Rad2Deg, colatitude * Mathf.Rad2Deg) * Vector3.up;
                
                Quaternion toolRotation = Quaternion.FromToRotation(Vector3.up, sphereNorthAxis);

                pixelPosition = toolRotation*sphericalPos*sphereRadius + sphereCenter;
                rayDirection = (pixelPosition - rayOrigin).normalized;
            }
            else // casting surface is planar
            {
                pixelPosition = planeOrigin + (j / (float)width) * planeXAxis + (i / (float)height) * planeZAxis;
                rayDirection = (pixelPosition - rayOrigin).normalized;
            }
            

            if (debug)
            {
                Debug.DrawRay(rayOrigin, pixelPosition - rayOrigin, Color.red, 0.1f);
            }
            
            raycastCommands[index] = new RaycastCommand(rayOrigin, rayDirection, raycastCommandParams, maxDistance);
        }
    }
}