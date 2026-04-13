using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class InstrumentPlacementPlanning
{
    public Transform instrument;
    public GameObject objectStudied;
    public bool debug = false;
    public LayerMask layerMask;
    public float maxHitDistance = 50f;
    public int ppu;
    public int textureScaleFactor = 6;
    public int nbDepthLines = 3;
    public float rotationAngle;
    public int instrumentSamplingPoints = 50;
    public int coneSamplingRotations;
    public InstrumentSamplingMethod instrumentSamplingMethod;
    public bool isActive;
    public ParticleSystem instrumentPointsParticleSystem;
    public int reachabilityPercentage;
    public bool displayInstrumentObject = true;
    public bool displayInstrumentCone = false;
    public bool displayInstrumentPoints = false;

    private Camera camera;
    private Vector3 screenCenter;
    private RaycastObject raycastObject;
    private float isoDepthOffset;
    private int[,] textureData;
    private readonly int isoDepthCount = 10;
    private RaycastHit objectHit;
    private GameObject cameraInstrument;
    private readonly float particleSize = 0.005f;
    private NativeArray<RaycastHit> raycastResults;
    private NativeArray<RaycastCommand> raycastCommands;
    private NativeArray<InstrumentSamplePoint> instrumentSamplePoints;
    private int nbRaysTotal;
    private List<InstrumentSamplePoint> initialInstrumentSampledPoints; // Store the initial sampled points for the instrument to avoid recomputing them
    private List<InstrumentSamplePoint> reducedSampledPoints; // Store the reduced sampled points for the instrument to avoid recomputing them
    private List<InstrumentSamplePoint> sampledInstrumentPoints;
    private Bounds instrumentBounds;
    private Vector3 instrumentOriginOffset;
    private Stopwatch stopwatch;
    private QueryParameters raycastCommandParams => new(layerMask);
    private int passedInstrumentSamplingPoints; // Store the value of the passed parameter instrument sampling points
    private Mesh pointsMesh;
    private MeshFilter pointsMeshFilter;
    private MeshRenderer pointsMeshRenderer;
    private NativeArray<ObjectSamplePoint> objectSamplePoints;
    private bool raycastResultsAvailable = false;
    private GameObject instrumentCone;
    private Renderer instrumentRenderer;
    private Color initialInstrumentColor;

    public void SetNbDepthLines(int value)
    {
        nbDepthLines = value;
        if (isActive)
        {
            PerformRaycasting();
        }
    }

    public void SetTextureScaleFactor(int value)
    {
        textureScaleFactor = value;
        raycastObject.SetTextureScaleFactor(textureScaleFactor, isActive);
        textureData = new int[raycastObject.textureWidth, raycastObject.textureHeight];
        if (isActive)
        {
            PerformRaycasting();
        }
    }

    public void SetInstrumentSamplingPoints(int value)
    {
        instrumentSamplingPoints = value;
        passedInstrumentSamplingPoints = value;
        ComputeInstrumentPoints(true);
        DisposeNativeArrays();
        instrumentSamplePoints = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
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
            ComputeInstrumentPoints(false);
            if (instrumentSamplingPoints != previousInstrumentSamplingPoints)
            {
                DisposeNativeArrays();
                instrumentSamplePoints = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
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
            ComputeInstrumentPoints(false);
            if (instrumentSamplingPoints != previousInstrumentSamplingPoints)
            {
                DisposeNativeArrays();
                instrumentSamplePoints = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
            }
            raycastResultsAvailable = false;
            if (isActive)
            {
                PerformRaycasting();
            }
        }
    }

    public void SetInstrumentSamplingMethod(InstrumentSamplingMethod value)
    {
        instrumentSamplingMethod = value;
        int previousInstrumentSamplingPoints = instrumentSamplingPoints;
        instrumentSamplingPoints = passedInstrumentSamplingPoints;
        bool reduceSampledPoints = reducedSampledPoints == null;
        ComputeInstrumentPoints(reduceSampledPoints);
        if (instrumentSamplingPoints != previousInstrumentSamplingPoints)
        {
            DisposeNativeArrays();
            instrumentSamplePoints = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);
        }
        raycastResultsAvailable = false;
        if (isActive)
        {
            PerformRaycasting();
        }
    }

    public void SetMaxHitDistance(float value)
    {
        maxHitDistance = value;
    }

    public void SetReachabilityPercentage(int value)
    {
        reachabilityPercentage = value;
        if (isActive)
        {
            UpdateInstrumentReachability();
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
                InstrumentHelper.UpdateConeReachability(instrumentCone, instrument, instrumentBounds.size.z, instrumentSamplePoints, rotationAngle);
            }
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
                    TextureColoringHelper.UpdateParticleSystem(instrumentSamplePoints, instrumentPointsParticleSystem, particleSize);
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
            raycastObject.ResetMaterialToInitial();
            raycastResultsAvailable = false;
        }
        if (debug)
        {
            pointsMesh.Clear();
        }
    }

    public void Initialize()
    {
        stopwatch = new Stopwatch();
        camera = Camera.main;
        screenCenter = new Vector3(Screen.width / 2, Screen.height / 2, 0);
        raycastObject = new RaycastObject(objectStudied, ppu, true, textureScaleFactor);
        passedInstrumentSamplingPoints = instrumentSamplingPoints;

        Bounds bounds = raycastObject.objectBounds;
        isoDepthOffset = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) / (2 * isoDepthCount);

        textureData = new int[raycastObject.textureWidth, raycastObject.textureHeight];
        initialInstrumentColor = raycastObject.GetInitialColor();

        // works but only because we initialize unityRaycasting script before in RaycastingManager
        cameraInstrument = GameObject.Find("CameraInstrument");

        SetDisplayInstrumentPoints(displayInstrumentPoints);

        try
        {
            Mesh instrumentMesh = instrument.GetComponentInChildren<MeshFilter>().sharedMesh;
            if (instrumentMesh == null)
            {
                throw new Exception("MeshFilter not found in instrument object");
            }
            MeshCollider instrumentMeshCollider = instrument.GetComponentInChildren<MeshCollider>(false);
            if (instrumentMeshCollider == null)
            {
                throw new Exception("MeshCollider not found in instrument object");
            }
            instrumentRenderer = instrument.GetComponentInChildren<Renderer>();
            if (instrumentRenderer == null)
            {
                throw new Exception("Renderer not found in instrument object");
            }
            instrumentRenderer.enabled = false;

            instrumentBounds = ObjectHelper.GetObjectBounds(instrument.gameObject);
            instrumentOriginOffset = instrumentBounds.center - instrument.position - instrumentBounds.size/2;
            initialInstrumentSampledPoints = VolumeSampler.Sample(instrumentMesh, instrumentBounds.size);
            VolumeSampler.AddOriginOffset(ref initialInstrumentSampledPoints, instrumentOriginOffset);
            ComputeInstrumentPoints(true);
            instrumentSamplePoints = new NativeArray<InstrumentSamplePoint>(instrumentSamplingPoints, Allocator.Persistent);

            instrumentCone = GameObject.Find("InstrumentCone");
            if (instrumentCone == null)
            {
                Material coneMaterial = new Material(Shader.Find("HDRP/Lit"));
                instrumentCone = InstrumentHelper.CreateInstrumentConeMesh(instrumentBounds.size.z, rotationAngle, coneMaterial);
            }
            instrumentCone.GetComponent<Renderer>().enabled = false;
        }
        catch (Exception e)
        {
            Debug.LogError("Error while initializing the instrument: " + e.Message);
        }

        if (debug)
        {
            pointsMesh = new Mesh();
            GameObject pointsObject = new GameObject("DepthObjectPointsMesh");
            pointsMeshFilter = pointsObject.AddComponent<MeshFilter>();
            pointsMeshRenderer = pointsObject.AddComponent<MeshRenderer>();
            pointsMeshRenderer.material = new Material(Shader.Find("Standard"));
        }
    }

    public void Update()
    {
        Ray ray = camera.ScreenPointToRay(screenCenter);
        if (Physics.Raycast(ray, out RaycastHit hit, maxHitDistance, layerMask))
        {
            if (raycastObject.colliderId == hit.colliderInstanceID)
            {
                objectHit = hit;
                PerformRaycasting();
            }
        }
    }

    public void PerformRaycasting()
    {
        stopwatch.Restart();

        if (objectHit.collider == null)
        {
            return;
        }

        FillDepthIsolines();
        PositionInstrument();
        ComputeRaycasting();
        UpdateInstrumentReachability();
        
        stopwatch.Stop();
        Debug.Log("Computation time : " + stopwatch.ElapsedMilliseconds + " ms");
    }

    public void Dispose()
    {
        DisposeNativeArrays();
    }

    // Dispose existing NativeArrays
    private void DisposeNativeArrays(bool disposeInstrumentSamplePoints = true)
    {
        if (raycastResults.IsCreated)
        {
            raycastResults.Dispose();
        }
        if (raycastCommands.IsCreated)
        {
            raycastCommands.Dispose();
        }
        if (instrumentSamplePoints.IsCreated && disposeInstrumentSamplePoints)
        {
            instrumentSamplePoints.Dispose();
        }
        if (objectSamplePoints.IsCreated)
        {
            objectSamplePoints.Dispose();
        }
    }

    private void PositionInstrument()
    {
        // place and rotate the instrument to face the hit point
        instrument.position = cameraInstrument.transform.position;
        Vector3 normal = objectHit.point - instrument.position;
        Vector3 instrumentForward = instrument.forward;
        Vector3 axis = Vector3.Cross(instrumentForward, normal);
        float angle = Vector3.Angle(instrumentForward, normal);
        instrument.RotateAround(instrument.position, axis, angle);

        VolumeSampler.Position(ref instrumentSamplePoints, sampledInstrumentPoints, instrument.position, instrument.rotation);
    }

    private void FillDepthIsolines()
    {
        if (objectHit.collider == null)
        {
            return;
        }
        
        ResetTextureData();
        List<ObjectSamplePoint> objectSamplePointsList = new();

        Vector2 objectHitPointUV = objectHit.textureCoord;
        int textureWidth = raycastObject.textureWidth;
        int textureHeight = raycastObject.textureHeight;
        int x = (int)(objectHitPointUV.x * textureWidth);
        int y = (int)(objectHitPointUV.y * textureHeight);
        textureData[x, y] = 1;
        Vector3 hitPoint = objectHit.point;

        // Fill depth isolines in textureData and objectSamplePointsList
        for (int i = 1; i <= nbDepthLines; ++i)
        {
            float sphereRadius = i * isoDepthOffset / 2f;
            // Point weight decreases as we go further from the hit point
            float weight = 1f - 1f / nbDepthLines * (i - 1);
            var depthPoints = ObjectHelper.FindObjectDepthIsoline(raycastObject.gameObject, textureData, textureWidth, textureHeight, hitPoint, sphereRadius, i + 1);
            for (int j = 0; j < depthPoints.Count; ++j)
            {
                depthPoints[j].SetWeight(weight);
            }
            objectSamplePointsList.AddRange(depthPoints);
        }
        float weightHitPoint = objectSamplePointsList.Count > 0 ? objectSamplePointsList.Count : 1;
        objectSamplePointsList.Add(new ObjectSamplePoint(hitPoint, 1, weightHitPoint));

        // Dispose existing NativeArrays
        DisposeNativeArrays(false);
        nbRaysTotal = instrumentSamplingPoints * objectSamplePointsList.Count;
        raycastCommands = new NativeArray<RaycastCommand>(nbRaysTotal, Allocator.Persistent);
        raycastResults = new NativeArray<RaycastHit>(nbRaysTotal, Allocator.Persistent);
        objectSamplePoints = new NativeArray<ObjectSamplePoint>(objectSamplePointsList.ToArray(), Allocator.Persistent);
        raycastResultsAvailable = false;

        if (debug)
        {
            UpdateObjectPointsMesh();
        }
        
        // Color texture
        Color[] textureColors = new Color[textureWidth * textureHeight];
        for (int i = 0; i < textureWidth; ++i)
        {
            for (int j = 0; j < textureHeight; ++j)
            {
                textureColors[i + j * textureWidth] = textureData[i, j] != 0 ? Color.black : initialInstrumentColor;
            }
        }
        raycastObject.SetTexture(textureColors);
    }

    private void ComputeRaycasting()
    {
        RaycastJob raycastJob = new RaycastJob
        {
            instrumentSamplingPoints = instrumentSamplingPoints,
            objectSamplePoints = objectSamplePoints,
            instrumentSamplePoints = instrumentSamplePoints,
            raycastCommands = raycastCommands,
            raycastCommandParams = raycastCommandParams,
            debug = debug
        };

        JobHandle raycastJobHandle = raycastJob.Schedule(nbRaysTotal, 64);
        raycastJobHandle.Complete();

        JobHandle handle = RaycastCommand.ScheduleBatch(raycastCommands, raycastResults, 1, 1, default);
        handle.Complete();

        raycastResultsAvailable = true;
    }

    private void UpdateInstrumentReachability()
    {
        if (!raycastResultsAvailable)
        {
            return;
        }
        RaycastHelper.ComputeInstrumentReachability(ref instrumentSamplePoints, raycastResults, objectSamplePoints, reachabilityPercentage);
        TextureColoringHelper.UpdateParticleSystem(instrumentSamplePoints, instrumentPointsParticleSystem, particleSize);

        if (displayInstrumentCone)
        {
            instrumentCone.GetComponent<Renderer>().enabled = true;
            InstrumentHelper.UpdateConeReachability(instrumentCone, instrument, instrumentBounds.size.z, instrumentSamplePoints, rotationAngle);
        }
        if (displayInstrumentObject)
        {
            instrumentRenderer.enabled = true;
        }
    }

    private void ResetTextureData()
    {
        for (int i = 0; i < raycastObject.textureWidth; i++)
        {
            for (int j = 0; j < raycastObject.textureHeight; j++)
            {
                textureData[i, j] = 0;
            }
        }
    }

    private void ComputeInstrumentPoints(bool reduceSampledPoints = false)
    {
        sampledInstrumentPoints = InstrumentHelper.ComputeInstrumentPoints(instrumentSamplingMethod, initialInstrumentSampledPoints, ref reducedSampledPoints,
        ref instrumentSamplingPoints, instrumentBounds, rotationAngle, coneSamplingRotations, instrumentOriginOffset, reduceSampledPoints);
    }

    private void UpdateObjectPointsMesh()
    {
        Vector3[] vertices = new Vector3[objectSamplePoints.Length];
        int[] indices = new int[objectSamplePoints.Length];

        for (int i = 0; i < objectSamplePoints.Length; ++i)
        {
            vertices[i] = objectSamplePoints[i].point;
            indices[i] = i;
        }

        pointsMesh.Clear();
        pointsMesh.vertices = vertices;
        pointsMesh.SetIndices(indices, MeshTopology.Points, 0);
        pointsMeshFilter.mesh = pointsMesh;
    }

    // Job struct to create the rays for the raycasting process
    [BurstCompile]
    private struct RaycastJob : IJobParallelFor
    {
        [ReadOnly] public int instrumentSamplingPoints;
        [ReadOnly] public NativeArray<ObjectSamplePoint> objectSamplePoints;
        [ReadOnly] public NativeArray<InstrumentSamplePoint> instrumentSamplePoints;
        public NativeArray<RaycastCommand> raycastCommands;
        [ReadOnly] public QueryParameters raycastCommandParams;
        [ReadOnly] public bool debug;

        public void Execute(int index)
        {
            int objectSamplePointsCount = objectSamplePoints.Length;
            int i = index / objectSamplePointsCount;
            int j = index % objectSamplePointsCount;

            Vector3 instrumentPoint = instrumentSamplePoints[i].point;
            Vector3 rayOrigin = objectSamplePoints[j].point;
            Vector3 rayDirection = (instrumentPoint - rayOrigin).normalized;

            float distance = Vector3.Distance(instrumentPoint, rayOrigin);
            // no need to go further than the instrument point
            // and we shouldn't, we could hit an object behind the instrument

            if (debug)
            {
                Debug.DrawRay(rayOrigin, instrumentPoint - rayOrigin, Color.red, 1f);
            }
            
            raycastCommands[index] = new RaycastCommand(rayOrigin, rayDirection, raycastCommandParams, distance);
        }
    }
}