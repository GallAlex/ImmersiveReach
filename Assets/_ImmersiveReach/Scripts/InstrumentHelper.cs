using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

public struct InstrumentSamplePoint {
    public Vector3 point;
    public int rotationIndex;
    private bool isValid;

    public InstrumentSamplePoint(Vector3 point, int rotationIndex)
    {
        this.point = point;
        this.rotationIndex = rotationIndex;
        this.isValid = true;
    }
    
    public void SetValidity(bool isValid)
    {
        if (this.isValid != isValid)
        {
            this.isValid = isValid;
        }
    }

    public readonly bool IsValid()
    {
        return isValid;
    }
};

public static class InstrumentHelper
{
    // Compare the hit between if backface is active and if it's not
    // If the hit is the same point, we are outside the collider
    // If the hit is different, we are inside the collider
    private static bool IsPointInsideCollider(Vector3 position, MeshCollider collider)
    {
        Physics.queriesHitBackfaces = true;
        Vector3 direction = Vector3.up;

        Vector3 backFaceHitPos = Vector3.zero;
        Vector3 frontFaceHitPos = Vector3.zero;
        bool backHit = false;
        bool frontHit = false;
        RaycastHit[] hits = Physics.RaycastAll(position, direction);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == collider)
            {
                backFaceHitPos = hits[i].point;
                backHit = true;
                break;
            }
        }

        Physics.queriesHitBackfaces = false;
        if (backHit)
        {
            hits = Physics.RaycastAll(position, direction);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == collider)
                {
                    frontFaceHitPos = hits[i].point;
                    frontHit = true;
                    break;
                }
            }
            if (frontHit)
            {
                if (frontFaceHitPos.Equals(backFaceHitPos))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
            else
            {
                return true;
            }
        }
        return false;
    }

    // returns True if the point is inside any of the layer objects
    private static bool IsPointInsideLayerObjects(Vector3 point, List<RaycastObject> layerObjects)
    {
        foreach (RaycastObject layerObject in layerObjects)
        {
            MeshCollider meshCollider = layerObject.GetMeshCollider();
            if (meshCollider != null && IsPointInsideCollider(point, meshCollider))
            {
                return true;
            }
        }
        return false;
    }

    // just keeping the points that are not inside any of the layer objects
    private static NativeArray<InstrumentSamplePoint> KeepValidSampledPoints(NativeArray<InstrumentSamplePoint> instrumentPoints, List<RaycastObject> layerObjects)
    {
        List<InstrumentSamplePoint> validSampledPoints = new();
        foreach (InstrumentSamplePoint point in instrumentPoints)
        {
            if (!IsPointInsideLayerObjects(point.point, layerObjects))
            {
                validSampledPoints.Add(point);
            }
        }
        return new NativeArray<InstrumentSamplePoint>(validSampledPoints.ToArray(), Allocator.Persistent);
    }

    // we delete the whole instrument rotation if one of the points is inside a layer object
    private static NativeArray<InstrumentSamplePoint> KeepValidSampledRotationPoints(NativeArray<InstrumentSamplePoint> rotationPoints, List<RaycastObject> layerObjects)
    {
        List<InstrumentSamplePoint> validSampledRotationPoints = new(rotationPoints.ToArray());
        HashSet<int> rotationsToDiscard = new();

        foreach (var rotationPoint in validSampledRotationPoints)
        {
            if (!rotationsToDiscard.Contains(rotationPoint.rotationIndex) && IsPointInsideLayerObjects(rotationPoint.point, layerObjects))
            {
                rotationsToDiscard.Add(rotationPoint.rotationIndex);
            }
        }
        validSampledRotationPoints.RemoveAll(point => rotationsToDiscard.Contains(point.rotationIndex));
        return new NativeArray<InstrumentSamplePoint>(validSampledRotationPoints.ToArray(), Allocator.Persistent);
    }

    private static void SetSampledRotationPointsValidity(ref NativeArray<InstrumentSamplePoint> rotationPoints, List<RaycastObject> layerObjects)
    {
        HashSet<int> invalidRotations = new();
        foreach (var rotationPoint in rotationPoints)
        {
            if (!invalidRotations.Contains(rotationPoint.rotationIndex) && IsPointInsideLayerObjects(rotationPoint.point, layerObjects))
            {
                invalidRotations.Add(rotationPoint.rotationIndex);
            }
        }
        for (int i = 0; i < rotationPoints.Length; ++i)
        {
            var point = rotationPoints[i];
            point.SetValidity(!invalidRotations.Contains(point.rotationIndex));
            rotationPoints[i] = point;
        }
    }

    private static bool AreInitialPointsValid(NativeArray<InstrumentSamplePoint> initialPoints, List<RaycastObject> layerObjects)
    {
        foreach (var initialPoint in initialPoints)
        {
            if (IsPointInsideLayerObjects(initialPoint.point, layerObjects))
            {
                return false;
            }
        }
        return true;
    }

    // This method validates the instrument points based on the sampling method.
    // It checks if the points are inside any layer objects and reduces the points if necessary.
    public static NativeArray<InstrumentSamplePoint> GetValidInstrumentPoints(NativeArray<InstrumentSamplePoint> initialInstrumentPoints, List<RaycastObject> layerObjects, InstrumentSamplingMethod instrumentSamplingMethod, out bool reducedSampledPoints)
    {
        NativeArray<InstrumentSamplePoint> validPointsArray;
        switch (instrumentSamplingMethod)
        {
            case InstrumentSamplingMethod.InstrumentSamplingWithRotations:
                // Check if initial rotation points are valid
                if (AreInitialPointsValid(initialInstrumentPoints, layerObjects))
                {
                    reducedSampledPoints = false;
                    return initialInstrumentPoints;
                }
                else
                {
                    // Else reduce the points to keep only the valid instrument rotations points
                    reducedSampledPoints = true;
                    validPointsArray = KeepValidSampledRotationPoints(initialInstrumentPoints, layerObjects);
                }
                break;
            case InstrumentSamplingMethod.ConeUniformSampling:
            case InstrumentSamplingMethod.InstrumentSamplingWithoutRotations:
            default:
                // Check if initial points are valid
                if (AreInitialPointsValid(initialInstrumentPoints, layerObjects))
                {
                    reducedSampledPoints = false;
                    return initialInstrumentPoints;
                }
                else
                {
                    // Else reduce the points to keep only the valid ones
                    reducedSampledPoints = true;
                    validPointsArray = KeepValidSampledPoints(initialInstrumentPoints, layerObjects);
                }
                break;
        }

        // Dispose the old NativeArray
        initialInstrumentPoints.Dispose();

        return validPointsArray;
    }

    // This method updates the validity of the instrument points based on the sampling method
    // Contrary to GetValidInstrumentPoints, this method doesn't reduce the number of points
    // We keep the same number of points but we update their validity, to know if we'll use them in the accessibility computation
    public static void UpdateInstrumentPointsValidity(ref NativeArray<InstrumentSamplePoint> instrumentPoints, List<RaycastObject> layerObjects, InstrumentSamplingMethod instrumentSamplingMethod)
    {
        switch (instrumentSamplingMethod)
        {
            case InstrumentSamplingMethod.InstrumentSamplingWithRotations:
                SetSampledRotationPointsValidity(ref instrumentPoints, layerObjects);
                break;
            case InstrumentSamplingMethod.ConeUniformSampling:
            case InstrumentSamplingMethod.InstrumentSamplingWithoutRotations:
            default:
                for (int i = 0; i < instrumentPoints.Length; ++i)
                {
                    var point = instrumentPoints[i];
                    point.SetValidity(!IsPointInsideLayerObjects(point.point, layerObjects));
                    instrumentPoints[i] = point;
                }
                break;
        }
    }

    public static List<InstrumentSamplePoint> ComputeInstrumentPoints(InstrumentSamplingMethod instrumentSamplingMethod, List<InstrumentSamplePoint> initialInstrumentSampledPoints, ref List<InstrumentSamplePoint> reducedSampledPoints,
    ref int instrumentSamplingPoints, Bounds instrumentBounds, float rotationAngle, int coneSamplingRotations, Vector3 instrumentOriginOffset, bool reduceSampledPoints = false)
    {
        List<InstrumentSamplePoint> sampledRayLaunchPoints;
        switch(instrumentSamplingMethod){
            case InstrumentSamplingMethod.ConeUniformSampling:
                sampledRayLaunchPoints = VolumeSampler.SampleVolumeInsideCone(instrumentSamplingPoints, instrumentBounds.size.z, rotationAngle, Quaternion.Euler(-90, 0, 0));
                break;
            
            case InstrumentSamplingMethod.InstrumentSamplingWithRotations:
                if (reduceSampledPoints)
                {
                    sampledRayLaunchPoints = VolumeSampler.ReduceSampledPoints(initialInstrumentSampledPoints, instrumentSamplingPoints);
                    reducedSampledPoints = sampledRayLaunchPoints;
                }
                else {
                    sampledRayLaunchPoints = reducedSampledPoints;
                }
                sampledRayLaunchPoints = VolumeSampler.RotateAndAddVolumeSample(sampledRayLaunchPoints, coneSamplingRotations, rotationAngle, instrumentBounds, instrumentOriginOffset);
                instrumentSamplingPoints = sampledRayLaunchPoints.Count;
                break;
            
            case InstrumentSamplingMethod.InstrumentSamplingWithoutRotations:
            default:
                if (reduceSampledPoints)
                {
                    sampledRayLaunchPoints = VolumeSampler.ReduceSampledPoints(initialInstrumentSampledPoints, instrumentSamplingPoints);
                    reducedSampledPoints = sampledRayLaunchPoints;
                }
                else {
                    sampledRayLaunchPoints = reducedSampledPoints;
                }
                instrumentSamplingPoints = sampledRayLaunchPoints.Count;
                break;
        }
        return sampledRayLaunchPoints;
    }

    public static GameObject CreateInstrumentConeMesh(float height, float coneAngle, Material material, int segments = 50)
    {
        GameObject cone = new("InstrumentCone");
        MeshRenderer meshRenderer = cone.AddComponent<MeshRenderer>();
        meshRenderer.material = material;
        Mesh mesh = new();
        cone.AddComponent<MeshFilter>().mesh = mesh;
        Quaternion instrumentOrientation = Quaternion.Euler(90, 0, 0);

        List<Vector3> vertices = new();
        List<Vector2> uvs = new();

        float angleAmount = 2 * Mathf.PI / segments;
        float angle = 0f;

        // Cone tip
        Vector3 pos = new(0f, height, 0f);
        vertices.Add(instrumentOrientation * pos);
        uvs.Add(new Vector2(0.5f, 1f)); // Tip is at the center top of the UV map
        
        float radius = height * Mathf.Tan(coneAngle * Mathf.Deg2Rad);
        pos.y = 0f;
        for (int i = 0; i < segments; ++i)
        {
            pos.x = radius * Mathf.Cos(angle);
            pos.z = radius * Mathf.Sin(angle);
            vertices.Add(instrumentOrientation * pos);
            
            float u = (float)i / segments; // Spread UVs evenly around the base
            uvs.Add(new Vector2(u, 0f));

            angle -= angleAmount;
        }

        // Add last base vertex (duplicate of the first base vertex, but with u = 1.0)
        pos.x = radius * Mathf.Cos(0);
        pos.z = radius * Mathf.Sin(0);
        vertices.Add(instrumentOrientation * pos);
        uvs.Add(new Vector2(1f, 0f)); // duplicate UV at the seam
        int lastVertexIndex = vertices.Count - 1;

        mesh.vertices = vertices.ToArray();
        mesh.uv = uvs.ToArray();

        List<int> triangles = new();
        for (int i = 1; i < segments; ++i)
        {
            triangles.Add(0); // Cone tip
            triangles.Add(i);
            triangles.Add(i + 1);
        }
        triangles.Add(0);
        triangles.Add(segments);
        triangles.Add(lastVertexIndex);

        mesh.triangles = triangles.ToArray();
        mesh.RecalculateNormals();

        return cone;
    }

    // Updates the instrument cone : position, rotation and reachability coloring
    public static void UpdateConeReachability(GameObject instrumentCone, Transform instrumentTransform, float coneHeight, NativeArray<InstrumentSamplePoint> instrumentSamplePoints, float rotationAngle)
    {
        instrumentCone.transform.position = instrumentTransform.position - instrumentTransform.TransformDirection(Vector3.forward) * coneHeight;
        instrumentCone.transform.rotation = instrumentTransform.rotation;
        bool[] vertexReachability = ComputeInstrumentConeReachability(instrumentCone, instrumentSamplePoints, coneHeight, rotationAngle);
        TextureColoringHelper.ColorCone(instrumentCone, vertexReachability);
    }

    // Computes the reachability of the vertices of the instrument cone mesh based on the instrument sample points
    private static bool[] ComputeInstrumentConeReachability(GameObject instrumentCone, NativeArray<InstrumentSamplePoint> instrumentSamplePoints, float coneHeight, float rotationAngle)
    {
        Mesh coneMesh = instrumentCone.GetComponent<MeshFilter>().mesh;
        int vertexCount = coneMesh.vertexCount;
        List<InstrumentSamplePoint>[] coneClosePoints = new List<InstrumentSamplePoint>[vertexCount];
        for (int i = 0; i < vertexCount; ++i)
        {
            coneClosePoints[i] = new List<InstrumentSamplePoint>();
        }
        List<int> coneTriangles = coneMesh.triangles.ToList();
        Vector3 coneTip = instrumentCone.transform.TransformPoint(coneMesh.vertices[0]);
        Vector3 coneAxisDirection = instrumentCone.transform.TransformDirection(Vector3.forward);

        // Fill the coneClosePoints list with the instrument points close to each vertex of the cone
        // A point is close to a a vertex if it is close enough to the triangle of which the vertex is part
        // We check the distance between the point and the middle point of the triangle at the height of the point
        for (int i = 0; i < instrumentSamplePoints.Length; ++i)
        {
            InstrumentSamplePoint instrumentPoint = instrumentSamplePoints[i];
            // Calculate vector from tip to instrument point
            Vector3 tipToPoint = instrumentPoint.point - coneTip;
            // Project this vector onto the cone axis to get the distance along the axis
            float distanceAlongAxis = Vector3.Dot(tipToPoint, coneAxisDirection);
            // Find the point on the cone axis at this distance from the tip
            Vector3 coneCenter = coneTip + coneAxisDirection * distanceAlongAxis;
            // Calculate the cone radius at this height
            float coneRadiusAtHeight = Mathf.Abs(distanceAlongAxis * Mathf.Tan(rotationAngle * Mathf.Deg2Rad));

            for (int j = 0; j < coneTriangles.Count; j += 3)
            {
                Vector3 vertex1 = instrumentCone.transform.TransformPoint(coneMesh.vertices[coneTriangles[j]]); // should be the tip (0)
                Vector3 vertex2 = instrumentCone.transform.TransformPoint(coneMesh.vertices[coneTriangles[j + 1]]);
                Vector3 vertex3 = instrumentCone.transform.TransformPoint(coneMesh.vertices[coneTriangles[j + 2]]);

                // Find the middle point between the vertex 2 and vertex 3
                Vector3 triangleBaseMiddlePoint = (vertex2 + vertex3) / 2f;
                // Find the line in the triangle, between the tip and the base middle point
                Vector3 triangleLine = vertex1 - triangleBaseMiddlePoint;
                Vector3 planeNormal = coneTip - coneCenter;
                // Find point of intersection between triangleLine and the plane described by planeNormal and planePoint
                float t = Vector3.Dot(coneCenter - triangleBaseMiddlePoint, planeNormal) / Vector3.Dot(triangleLine, planeNormal);
                Vector3 intersectionPoint = triangleBaseMiddlePoint + t * triangleLine;

                float pointDistanceToTriangle = Vector3.Distance(instrumentPoint.point, intersectionPoint);
                if (pointDistanceToTriangle < coneRadiusAtHeight)
                {
                    // Add the point to each vertex of the triangle if it's not already there
                    // Only for the vertex 2 and 3, because the vertex 1 is the tip of the cone
                    if (!coneClosePoints[coneTriangles[j + 1]].Contains(instrumentPoint))
                    {
                        coneClosePoints[coneTriangles[j + 1]].Add(instrumentPoint);
                    }
                    if (!coneClosePoints[coneTriangles[j + 2]].Contains(instrumentPoint))
                    {
                        coneClosePoints[coneTriangles[j + 2]].Add(instrumentPoint);
                    }
                }
            }

            // Check if the point is close to the tip of the cone
            float distanceToTip = Vector3.Distance(instrumentPoint.point, coneTip);
            if (distanceToTip <= coneHeight / 2f)
            {
                coneClosePoints[0].Add(instrumentPoint);
            }
        }

        // Aggregate the reachability of the close points for each vertex
        // A vertex is reachable if at least 50% of the points close to it are valid
        bool[] vertexReachability = new bool[vertexCount];
        for (int i = 0; i < vertexCount; ++i)
        {
            int reachablePointsCount = 0;
            for (int j = 0; j < coneClosePoints[i].Count; ++j)
            {
                if (coneClosePoints[i][j].IsValid())
                {
                    ++reachablePointsCount;
                }
            }
            float reachability = (float)reachablePointsCount / coneClosePoints[i].Count;
            vertexReachability[i] = reachability >= 0.5f;
        }

        return vertexReachability;
    }
}