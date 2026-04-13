using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.CrossPlatformInput;
using UnityEngine.InputSystem;

public enum InstrumentSamplingMethod
{
    ConeUniformSampling,
    InstrumentSamplingWithRotations,
    InstrumentSamplingWithoutRotations
};

public enum CastingAreaMethod
{
    OldVersionPlane,
    DynamicPlane
};

public class GameManager : MonoBehaviour
{
    public enum UsageScenario
    {
        HeartAccessibilityExploration,
        InstrumentPlacementPlanning
    }

    [Header("Usage scenario")]
    public UsageScenario usageScenario = UsageScenario.HeartAccessibilityExploration;

    [Header("Objects selection")]
    public Transform instrument;
    public GameObject objectStudied;
    public LayerMask layerMask;
    [Header("Instrument sampling, changeable at runtime")]
    public InstrumentSamplingMethod instrumentSamplingMethod = InstrumentSamplingMethod.ConeUniformSampling;
    public int instrumentSamplingPoints = 50;
    public float rotationAngle = 30.0f;
    public int coneSamplingRotations = 10;
    [Header("Instrument display properties, changeable at runtime")]
    [Tooltip("Toggle to display the instrument object")] public bool displayInstrumentObject = true;
    [Tooltip("Toggle to display the instrument points")] public bool displayInstrumentPoints = false;
    [Tooltip("Toggle to display the instrument cone")] public bool displayInstrumentCone = false;

    [Header("Accessibility coloring properties, changeable at runtime")]
    public AccessibilityAggregationMethod accessibilityAggregationMethod;
    [Range(3, 6)] public int accessibilityColoringValues = 3;
    [Range(0.0f, 1.0f)] public float sigmaGaussianSmoothing = 1.0f;
    [Range(1, 10)] public int textureScaleFactor = 6;
    public int vertexNeighborhoodRadius = 0;
    public bool drawIsoLines = true;
    public bool drawColors = true;

    [Header("Instrument placement planning properties, changeable at runtime")]
    public int depthLines = 3;
    [Range(1, 100)] public int reachabilityPercentage = 80;
    [Header("Raycasting properties, changeable at runtime")]
    public int resolution = 2000;
    public bool displayOnlyObjectStudied = false;
    public float maxHitDistance = 50f;
    public bool continuousRaycasting = false;
    public bool reduceInvalidInstrumentPoints = false;
    [Header("Debug")]
    public bool debug = false;

    public static readonly Vector3 cameraInstrumentOffset = new(0.11f, -0.03f, 0.3f);

    private readonly CastingAreaMethod castingAreaMethod = CastingAreaMethod.DynamicPlane;
    private AnatomicalAccessibilityExploration anatomicalAccessibilityExploration;
    private Dictionary<string, object> previousValues = new();
    private InstrumentPlacementPlanning instrumentPlacementPlanning;
    private GameObject canvas;
    private ParticleSystem instrumentPointsParticleSystem;
    private PlayerInput playerInput;
    private InputAction submitAction;
    private InputAction switchAction;
    private bool inputSystemInitialized = false;
    private GameObject instrumentCone;
    private GameObject instrumentObject;
    private bool colorEveryObject = false;

    void Start()
    {
        StorePreviousValues();

        // Initialize the input system
        try
        {
            // Find the EventSystem in the scene
            var eventSystem = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem != null)
            {
                playerInput = eventSystem.GetComponent<PlayerInput>();
                if (playerInput != null && playerInput.actions != null)
                {
                    submitAction = playerInput.actions["Submit"];
                    switchAction = playerInput.actions["Switch"];
                    inputSystemInitialized = true;
                    Debug.Log("Input System initialized successfully using EventSystem");
                }
                else
                {
                    Debug.LogWarning("PlayerInput component not found on EventSystem or Input Action asset not assigned.");
                }
            }
            else
            {
                Debug.LogWarning("EventSystem not found in scene. Please ensure you have an EventSystem with Input System setup.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Failed to initialize Input System: {e.Message}");
            inputSystemInitialized = false;
        }

        instrumentPointsParticleSystem = GameObject.Find("InstrumentSamplePoints").GetComponent<ParticleSystem>();
        if (instrumentPointsParticleSystem == null)
        {
            Debug.LogError("Instrument sample points particle system not found");
        }

        anatomicalAccessibilityExploration = new AnatomicalAccessibilityExploration();
        SetCommonProperties(anatomicalAccessibilityExploration);
        anatomicalAccessibilityExploration.Initialize();

        instrumentPlacementPlanning = new InstrumentPlacementPlanning();
        SetCommonProperties(instrumentPlacementPlanning);
        instrumentPlacementPlanning.Initialize();

        canvas = GameObject.Find("CircleCanvas");
        if (canvas == null)
        {
            Debug.LogError("Canvas not found");
        }
        canvas.SetActive(usageScenario == UsageScenario.InstrumentPlacementPlanning);

        instrumentCone = GameObject.Find("InstrumentCone");
        if (instrumentCone == null)
        {
            Debug.LogError("Instrument cone not found");
        }

        instrumentObject = GameObject.Find("Instrument");
        if (instrumentObject == null)
        {
            Debug.LogError("Instrument object not found");
        }
    }

    private void StorePreviousValues()
    {
        previousValues["usageScenario"] = usageScenario;
        previousValues["displayOnlyObjectStudied"] = displayOnlyObjectStudied;
        previousValues["accessibilityColoringValues"] = accessibilityColoringValues;
        previousValues["drawIsoLines"] = drawIsoLines;
        previousValues["sigmaGaussianSmoothing"] = sigmaGaussianSmoothing;
        previousValues["continuousRaycasting"] = continuousRaycasting;
        previousValues["colorEveryObject"] = colorEveryObject;
        previousValues["accessibilityAggregationMethod"] = accessibilityAggregationMethod;
        previousValues["resolution"] = resolution;
        previousValues["instrumentSamplingPoints"] = instrumentSamplingPoints;
        previousValues["instrumentSamplingMethod"] = instrumentSamplingMethod;
        previousValues["rotationAngle"] = rotationAngle;
        previousValues["coneSamplingRotations"] = coneSamplingRotations;
        previousValues["maxHitDistance"] = maxHitDistance;
        previousValues["vertexNeighborhoodRadius"] = vertexNeighborhoodRadius;
        previousValues["reduceInvalidInstrumentPoints"] = reduceInvalidInstrumentPoints;
        previousValues["displayInstrumentPoints"] = displayInstrumentPoints;
        previousValues["displayInstrumentObject"] = displayInstrumentObject;
        previousValues["displayInstrumentCone"] = displayInstrumentCone;
        previousValues["drawColors"] = drawColors;
        previousValues["textureScaleFactor"] = textureScaleFactor;
        previousValues["depthLines"] = depthLines;
        previousValues["reachabilityPercentage"] = reachabilityPercentage;
    }

    private void SetCommonProperties(object script)
    {
        if (script is AnatomicalAccessibilityExploration anatomicalAccessibilityExploration)
        {
            anatomicalAccessibilityExploration.instrument = instrument;
            anatomicalAccessibilityExploration.instrumentSamplingPoints = instrumentSamplingPoints;
            anatomicalAccessibilityExploration.maxHitDistance = maxHitDistance;
            anatomicalAccessibilityExploration.layerMask = layerMask;
            anatomicalAccessibilityExploration.objectStudied = objectStudied;
            anatomicalAccessibilityExploration.vertexNeighborhoodRadius = vertexNeighborhoodRadius;
            anatomicalAccessibilityExploration.accessibilityAggregationMethod = accessibilityAggregationMethod;
            anatomicalAccessibilityExploration.debug = debug;
            anatomicalAccessibilityExploration.colorEveryObject = colorEveryObject;
            anatomicalAccessibilityExploration.instrumentSamplingMethod = instrumentSamplingMethod;
            anatomicalAccessibilityExploration.castingAreaMethod = castingAreaMethod;
            anatomicalAccessibilityExploration.rotationAngle = rotationAngle;
            anatomicalAccessibilityExploration.coneSamplingRotations = coneSamplingRotations;
            anatomicalAccessibilityExploration.ppu = resolution;
            anatomicalAccessibilityExploration.displayOnlyObjectStudied = displayOnlyObjectStudied;
            anatomicalAccessibilityExploration.accessibilityColoringValues = accessibilityColoringValues;
            anatomicalAccessibilityExploration.drawIsoLines = drawIsoLines;
            anatomicalAccessibilityExploration.sigmaGaussianSmoothing = sigmaGaussianSmoothing;
            anatomicalAccessibilityExploration.continuousRaycasting = continuousRaycasting;
            anatomicalAccessibilityExploration.reduceInvalidInstrumentPoints = reduceInvalidInstrumentPoints;
            anatomicalAccessibilityExploration.displayInstrumentPoints = displayInstrumentPoints;
            anatomicalAccessibilityExploration.displayInstrumentObject = displayInstrumentObject;
            anatomicalAccessibilityExploration.displayInstrumentCone = displayInstrumentCone;
            anatomicalAccessibilityExploration.drawColors = drawColors;
            anatomicalAccessibilityExploration.isActive = usageScenario == UsageScenario.HeartAccessibilityExploration;
            anatomicalAccessibilityExploration.instrumentPointsParticleSystem = instrumentPointsParticleSystem;
            anatomicalAccessibilityExploration.textureScaleFactor = textureScaleFactor;
        }
        else if (script is InstrumentPlacementPlanning instrumentPlacementPlanning)
        {
            instrumentPlacementPlanning.instrument = instrument;
            instrumentPlacementPlanning.objectStudied = objectStudied;
            instrumentPlacementPlanning.debug = debug;
            instrumentPlacementPlanning.layerMask = layerMask;
            instrumentPlacementPlanning.maxHitDistance = maxHitDistance;
            instrumentPlacementPlanning.ppu = resolution;
            instrumentPlacementPlanning.textureScaleFactor = textureScaleFactor;
            instrumentPlacementPlanning.nbDepthLines = depthLines;
            instrumentPlacementPlanning.rotationAngle = rotationAngle;
            instrumentPlacementPlanning.instrumentSamplingPoints = instrumentSamplingPoints;
            instrumentPlacementPlanning.coneSamplingRotations = coneSamplingRotations;
            instrumentPlacementPlanning.isActive = usageScenario == UsageScenario.InstrumentPlacementPlanning;
            instrumentPlacementPlanning.instrumentPointsParticleSystem = instrumentPointsParticleSystem;
            instrumentPlacementPlanning.reachabilityPercentage = reachabilityPercentage;
            instrumentPlacementPlanning.displayInstrumentPoints = displayInstrumentPoints;
            instrumentPlacementPlanning.displayInstrumentObject = displayInstrumentObject;
            instrumentPlacementPlanning.displayInstrumentCone = displayInstrumentCone;
        }
    }

    void Update()
    {

        if ((inputSystemInitialized && switchAction != null && switchAction.triggered) || CrossPlatformInputManager.GetButtonDown("Switch"))
        {
            if (usageScenario == UsageScenario.HeartAccessibilityExploration)
            {
                usageScenario = UsageScenario.InstrumentPlacementPlanning;
            }
            else
            {
                usageScenario = UsageScenario.HeartAccessibilityExploration;
            }
        }

        CheckAndApplyChanges();

        // Check for both input systems
        bool isButtonPressed = inputSystemInitialized && submitAction != null && submitAction.triggered;
        bool isKeyboardPressed = CrossPlatformInputManager.GetButtonDown("Submit");

        if (usageScenario == UsageScenario.InstrumentPlacementPlanning)
        {
            if (isKeyboardPressed || isButtonPressed)
            {
                instrumentPlacementPlanning?.Update();
            }
        }
        else
        {
            anatomicalAccessibilityExploration?.Update();
            if (isKeyboardPressed || isButtonPressed)
            {
                anatomicalAccessibilityExploration?.PerformRaycasting();
            }
        }
    }

    private void CheckAndApplyChanges()
    {
        // Raycasting properties
        if ((bool)previousValues["displayOnlyObjectStudied"] != displayOnlyObjectStudied)
        {
            previousValues["displayOnlyObjectStudied"] = displayOnlyObjectStudied;
            anatomicalAccessibilityExploration?.SetDisplayOnlyObjectStudied(displayOnlyObjectStudied);
        }
        if ((int)previousValues["accessibilityColoringValues"] != accessibilityColoringValues)
        {
            previousValues["accessibilityColoringValues"] = accessibilityColoringValues;
            anatomicalAccessibilityExploration?.SetAccessibilityColoringValues(accessibilityColoringValues);
        }
        if ((bool)previousValues["drawIsoLines"] != drawIsoLines)
        {
            previousValues["drawIsoLines"] = drawIsoLines;
            anatomicalAccessibilityExploration?.SetDrawIsoLines(drawIsoLines);
        }
        if ((float)previousValues["sigmaGaussianSmoothing"] != sigmaGaussianSmoothing)
        {
            previousValues["sigmaGaussianSmoothing"] = sigmaGaussianSmoothing;
            anatomicalAccessibilityExploration?.SetSigmaGaussianSmoothing(sigmaGaussianSmoothing);
        }
        if ((bool)previousValues["continuousRaycasting"] != continuousRaycasting)
        {
            previousValues["continuousRaycasting"] = continuousRaycasting;
            if (anatomicalAccessibilityExploration != null)
            {
                anatomicalAccessibilityExploration.continuousRaycasting = continuousRaycasting;
            }
        }
        if ((AccessibilityAggregationMethod)previousValues["accessibilityAggregationMethod"] != accessibilityAggregationMethod)
        {
            previousValues["accessibilityAggregationMethod"] = accessibilityAggregationMethod;
            anatomicalAccessibilityExploration?.SetAccessibilityAggregationMethod(accessibilityAggregationMethod);
        }
        if ((int)previousValues["resolution"] != resolution && resolution > 0)
        {
            previousValues["resolution"] = resolution;
            anatomicalAccessibilityExploration?.SetPpu(resolution);
        }
        if ((int)previousValues["instrumentSamplingPoints"] != instrumentSamplingPoints && instrumentSamplingPoints > 0)
        {
            previousValues["instrumentSamplingPoints"] = instrumentSamplingPoints;
            anatomicalAccessibilityExploration?.SetInstrumentSamplingPoints(instrumentSamplingPoints);
            instrumentPlacementPlanning?.SetInstrumentSamplingPoints(instrumentSamplingPoints);
        }
        if ((InstrumentSamplingMethod)previousValues["instrumentSamplingMethod"] != instrumentSamplingMethod)
        {
            previousValues["instrumentSamplingMethod"] = instrumentSamplingMethod;
            anatomicalAccessibilityExploration?.SetInstrumentSamplingMethod(instrumentSamplingMethod);
            instrumentPlacementPlanning?.SetInstrumentSamplingMethod(instrumentSamplingMethod);
        }
        if ((float)previousValues["rotationAngle"] != rotationAngle && rotationAngle > 0)
        {
            previousValues["rotationAngle"] = rotationAngle;
            anatomicalAccessibilityExploration?.SetRotationAngle(rotationAngle);
            instrumentPlacementPlanning?.SetRotationAngle(rotationAngle);
        }
        if ((int)previousValues["coneSamplingRotations"] != coneSamplingRotations && coneSamplingRotations > 0)
        {
            previousValues["coneSamplingRotations"] = coneSamplingRotations;
            anatomicalAccessibilityExploration?.SetConeSamplingRotations(coneSamplingRotations);
            instrumentPlacementPlanning?.SetConeSamplingRotations(coneSamplingRotations);
        }
        if ((float)previousValues["maxHitDistance"] != maxHitDistance && maxHitDistance > 0)
        {
            previousValues["maxHitDistance"] = maxHitDistance;
            anatomicalAccessibilityExploration?.SetMaxHitDistance(maxHitDistance);
            instrumentPlacementPlanning?.SetMaxHitDistance(maxHitDistance);
        }
        if ((int)previousValues["vertexNeighborhoodRadius"] != vertexNeighborhoodRadius && vertexNeighborhoodRadius >= 0)
        {
            previousValues["vertexNeighborhoodRadius"] = vertexNeighborhoodRadius;
            anatomicalAccessibilityExploration?.SetVertexNeighborhoodRadius(vertexNeighborhoodRadius);
        }
        if ((bool)previousValues["colorEveryObject"] != colorEveryObject)
        {
            previousValues["colorEveryObject"] = colorEveryObject;
            anatomicalAccessibilityExploration?.SetColorEveryObject(colorEveryObject);
        }
        if ((bool)previousValues["reduceInvalidInstrumentPoints"] != reduceInvalidInstrumentPoints)
        {
            previousValues["reduceInvalidInstrumentPoints"] = reduceInvalidInstrumentPoints;
            anatomicalAccessibilityExploration?.SetReduceInvalidInstrumentPoints(reduceInvalidInstrumentPoints);
        }
        if ((bool)previousValues["displayInstrumentPoints"] != displayInstrumentPoints)
        {
            previousValues["displayInstrumentPoints"] = displayInstrumentPoints;
            anatomicalAccessibilityExploration?.SetDisplayInstrumentPoints(displayInstrumentPoints);
            instrumentPlacementPlanning?.SetDisplayInstrumentPoints(displayInstrumentPoints);
        }
        if ((bool)previousValues["displayInstrumentObject"] != displayInstrumentObject)
        {
            previousValues["displayInstrumentObject"] = displayInstrumentObject;
            anatomicalAccessibilityExploration?.SetDisplayInstrumentObject(displayInstrumentObject);
            instrumentPlacementPlanning?.SetDisplayInstrumentObject(displayInstrumentObject);
        }
        if ((bool)previousValues["displayInstrumentCone"] != displayInstrumentCone)
        {
            previousValues["displayInstrumentCone"] = displayInstrumentCone;
            anatomicalAccessibilityExploration?.SetDisplayInstrumentCone(displayInstrumentCone);
            instrumentPlacementPlanning?.SetDisplayInstrumentCone(displayInstrumentCone);
        }
        if ((bool)previousValues["drawColors"] != drawColors)
        {
            previousValues["drawColors"] = drawColors;
            anatomicalAccessibilityExploration?.SetDrawColors(drawColors);
        }
        if ((int)previousValues["textureScaleFactor"] != textureScaleFactor && textureScaleFactor >= 1)
        {
            previousValues["textureScaleFactor"] = textureScaleFactor;
            anatomicalAccessibilityExploration?.SetTextureScaleFactor(textureScaleFactor);
            instrumentPlacementPlanning?.SetTextureScaleFactor(textureScaleFactor);
        }
        if ((UsageScenario)previousValues["usageScenario"] != usageScenario)
        {
            previousValues["usageScenario"] = usageScenario;
            anatomicalAccessibilityExploration?.SetIsActive(usageScenario == UsageScenario.HeartAccessibilityExploration);
            instrumentPlacementPlanning?.SetIsActive(usageScenario == UsageScenario.InstrumentPlacementPlanning);

            canvas.SetActive(usageScenario == UsageScenario.InstrumentPlacementPlanning);
            instrumentPointsParticleSystem.Clear();
            instrumentCone.GetComponent<Renderer>().enabled = false;
            instrumentObject.GetComponentInChildren<Renderer>().enabled = false;
        }
        if ((int)previousValues["depthLines"] != depthLines && depthLines >= 1)
        {
            previousValues["depthLines"] = depthLines;
            instrumentPlacementPlanning?.SetNbDepthLines(depthLines);
        }
        if ((int)previousValues["reachabilityPercentage"] != reachabilityPercentage && reachabilityPercentage >= 1)
        {
            previousValues["reachabilityPercentage"] = reachabilityPercentage;
            instrumentPlacementPlanning?.SetReachabilityPercentage(reachabilityPercentage);
        }
    }

    void OnDestroy()
    {
        anatomicalAccessibilityExploration?.Dispose();
        instrumentPlacementPlanning?.Dispose();
    }
}