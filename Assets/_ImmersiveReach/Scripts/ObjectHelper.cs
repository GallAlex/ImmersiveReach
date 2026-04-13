using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public struct ObjectSamplePoint {
    public Vector3 point;
    public int isoDepthLine;
    public float weight;

    public ObjectSamplePoint(Vector3 point, int isoDepthLine)
    {
        this.point = point;
        this.isoDepthLine = isoDepthLine;
        this.weight = 1.0f;
    }

    public ObjectSamplePoint(Vector3 point, int isoDepthLine, float weight)
    {
        this.point = point;
        this.isoDepthLine = isoDepthLine;
        this.weight = weight;
    }

    public void SetWeight(float weight)
    {
        this.weight = weight;
    }
};

public static class ObjectHelper
{
    public static List<int> ObjectPoints(GameObject objectTested, Bounds objectBounds, int widthResolution, int heightResolution, int depthResolution)
    {
        List<int> objectPoints = new List<int>();
        MeshFilter[] meshFilters = objectTested.GetComponentsInChildren<MeshFilter>();
        Vector3 objectMin = objectBounds.min;
        Vector3 objectSize = objectBounds.size;
        foreach (MeshFilter meshFilter in meshFilters)
        {
            Vector3[] objectVertices = meshFilter.mesh.vertices;
            foreach (Vector3 vertex in objectVertices)
            {
                Vector3 worldPoint = meshFilter.transform.TransformPoint(vertex);
                int index = ObjectInWorldToTexture3DIndex(worldPoint, objectMin, objectSize, widthResolution, heightResolution, depthResolution);
                objectPoints.Add(index);
            }
        }
        return objectPoints;
    }

    public static int ObjectInWorldToTexture3DIndex(Vector3 worldPoint, Vector3 objectMin, Vector3 objectSize, int width, int height, int depth)
    {
        float normalizedX = (worldPoint.x - objectMin.x) / objectSize.x;
        float normalizedY = (worldPoint.y - objectMin.y) / objectSize.y;
        float normalizedZ = (worldPoint.z - objectMin.z) / objectSize.z;

        int x = Mathf.RoundToInt(normalizedX * width);
        int y = Mathf.RoundToInt(normalizedY * height);
        int z = Mathf.RoundToInt(normalizedZ * depth);

        return x + y * width + z * width * height;
    }

    public static Bounds GetObjectBounds(GameObject obj)
    {
        Quaternion currentRotation = obj.transform.rotation;
        obj.transform.rotation = Quaternion.identity;
        Bounds bounds = new();
        bool hasBounds = false;
        foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>())
        {
            if (hasBounds)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
        }
        obj.transform.rotation = currentRotation;
        return bounds;
    }

    public static Bounds GetAggregatedBounds(GameObject[] gameObjects)
    {
        Bounds bounds = new();
        bool hasBounds = false;
        foreach (GameObject obj in gameObjects)
        {
            Bounds objBounds = GetObjectBounds(obj);
            if (hasBounds)
            {
                bounds.Encapsulate(objBounds);
            }
            else
            {
                bounds = objBounds;
                hasBounds = true;
            }
        }
        return bounds;
    }

    public static GameObject[] FindGameObjectsWithLayer(LayerMask layer)
    {
        var goArray = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        var goList = new List<GameObject>();
        for (var i = 0; i < goArray.Length; ++i)
        {
            if (((layer & 1 << goArray[i].layer) == 1 << goArray[i].layer) && goArray[i].GetComponent<Collider>() != null)
            {
                if (goArray[i].GetComponent<MeshCollider>() != null)
            {
                goList.Add(goArray[i]);
                }
                else
                {
                    throw new("Object " + goArray[i].name + " does not have a mesh collider.");
                }
            }
        }
        return goList.ToArray();
    }

    public static void UpdateDisplayOnlyObjectStudied(bool displayOnlyObjectStudied, GameObject[] layerObjects)
    {
        if (displayOnlyObjectStudied)
        {
            foreach (GameObject obj in layerObjects)
            {
                obj.GetComponent<Renderer>().enabled = false;
            }
        }
        else
        {
            foreach (GameObject obj in layerObjects)
            {
                obj.GetComponent<Renderer>().enabled = true;
            }
        }
    }

    // Finds the depth isoline of a 3D object intersecting a sphere of given radius
    // marks the corresponding pixels in a 2D data array and returns the intersection points
    public static List<ObjectSamplePoint> FindObjectDepthIsoline(GameObject gameObject, int[,] data, int width, int height, Vector3 sphereCenter, float sphereRadius, int isoValue)
    {
        List<ObjectSamplePoint> allIntersectionPoints = new();
        float surfaceTolerance = 0.01f * sphereRadius; // Tighter tolerance for sharper lines
        
        // Calculate squared values for more efficient comparisons
        float innerSqrRadius = Mathf.Pow(sphereRadius - surfaceTolerance, 2);
        float outerSqrRadius = Mathf.Pow(sphereRadius + surfaceTolerance, 2);
        
        MeshFilter meshFilter = gameObject.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.mesh == null)
            return allIntersectionPoints;
            
        Mesh mesh = meshFilter.mesh;
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        Transform objectTransform = gameObject.transform;
        
        // Process triangles for intersection
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Vector3 v1 = objectTransform.TransformPoint(vertices[triangles[i]]);
            Vector3 v2 = objectTransform.TransformPoint(vertices[triangles[i+1]]);
            Vector3 v3 = objectTransform.TransformPoint(vertices[triangles[i+2]]);
            
            // Calculate squared distances from sphere center
            float sqrDist1 = (v1 - sphereCenter).sqrMagnitude;
            float sqrDist2 = (v2 - sphereCenter).sqrMagnitude;
            float sqrDist3 = (v3 - sphereCenter).sqrMagnitude;
            
            // Check if triangle crosses the sphere surface
            bool v1Inside = sqrDist1 < innerSqrRadius;
            bool v2Inside = sqrDist2 < innerSqrRadius;
            bool v3Inside = sqrDist3 < innerSqrRadius;
            bool v1Outside = sqrDist1 > outerSqrRadius;
            bool v2Outside = sqrDist2 > outerSqrRadius;
            bool v3Outside = sqrDist3 > outerSqrRadius;
            
            // If triangle crosses sphere boundary
            if ((v1Inside && !v2Inside && !v3Inside) || 
                (v2Inside && !v1Inside && !v3Inside) || 
                (v3Inside && !v1Inside && !v2Inside) ||
                (v1Outside && !v2Outside && !v3Outside) ||
                (v2Outside && !v1Outside && !v3Outside) ||
                (v3Outside && !v1Outside && !v2Outside))
            {
                // For each edge of the triangle, check if it intersects the sphere
                allIntersectionPoints.AddRange(MarkSphereEdgeIntersection(data, width, height, v1, v2, uvs[triangles[i]], uvs[triangles[i+1]], sphereCenter, sphereRadius, isoValue));
                allIntersectionPoints.AddRange(MarkSphereEdgeIntersection(data, width, height, v2, v3, uvs[triangles[i+1]], uvs[triangles[i+2]], sphereCenter, sphereRadius, isoValue));
                allIntersectionPoints.AddRange(MarkSphereEdgeIntersection(data, width, height, v3, v1, uvs[triangles[i+2]], uvs[triangles[i]], sphereCenter, sphereRadius, isoValue));
            }
        }
        
        return allIntersectionPoints;
    }

    // Marks the intersection points of a sphere and a line segment on a 2D data array
    // using UV coordinates for interpolation
    private static List<ObjectSamplePoint> MarkSphereEdgeIntersection(int[,] data, int width, int height, Vector3 p1, Vector3 p2, Vector2 uv1, Vector2 uv2, Vector3 sphereCenter, float sphereRadius, int isoValue)
    {
        List<ObjectSamplePoint> intersectionPoints = new();
        Vector3 edge = p2 - p1;
        Vector3 sphereToP1 = p1 - sphereCenter;
        
        // Quadratic equation coefficients for sphere-line intersection
        float a = Vector3.Dot(edge, edge);
        float b = 2 * Vector3.Dot(edge, sphereToP1);
        float c = Vector3.Dot(sphereToP1, sphereToP1) - sphereRadius * sphereRadius;
        
        // Calculate discriminant
        float discriminant = b * b - 4 * a * c;
        
        // If discriminant is negative, no intersection
        if (discriminant < 0)
            return intersectionPoints;
        
        // Calculate intersection parameters
        float t1 = (-b + Mathf.Sqrt(discriminant)) / (2 * a);
        float t2 = (-b - Mathf.Sqrt(discriminant)) / (2 * a);
        
        // Check if intersections are within edge segment (0 <= t <= 1)
        if ((t1 >= 0 && t1 <= 1) || (t2 >= 0 && t2 <= 1))
        {
            // Use the intersection points that are within the line segment
            if (t1 >= 0 && t1 <= 1)
            {
                // Calculate actual world position of the intersection
                Vector3 worldIntersection = Vector3.Lerp(p1, p2, t1);
                
                // Interpolate UV at intersection point
                Vector2 intersectionUV = Vector2.Lerp(uv1, uv2, t1);
                int x = (int)(intersectionUV.x * width);
                int y = (int)(intersectionUV.y * height);
                if (IsInBounds(x, y, width, height))
                {
                    data[x, y] = isoValue;
                    intersectionPoints.Add(new ObjectSamplePoint(worldIntersection, isoValue));
                    
                    // Pass UV coordinates and edge vertices to calculate accurate world positions
                    List<ObjectSamplePoint> neighborPoints = MarkIntersectionNeighborhood(
                        data, x, y, width, height, isoValue, 
                        worldIntersection, sphereCenter, sphereRadius);
                    intersectionPoints.AddRange(neighborPoints);
                }
            }
            if (t2 >= 0 && t2 <= 1)
            {
                // Calculate actual world position of the intersection
                Vector3 worldIntersection = Vector3.Lerp(p1, p2, t2);
                
                // Interpolate UV at intersection point
                Vector2 intersectionUV = Vector2.Lerp(uv1, uv2, t2);
                int x = (int)(intersectionUV.x * width);
                int y = (int)(intersectionUV.y * height);
                if (IsInBounds(x, y, width, height))
                {
                    data[x, y] = isoValue;
                    intersectionPoints.Add(new ObjectSamplePoint(worldIntersection, isoValue));
                    
                    // Pass UV coordinates and edge vertices to calculate accurate world positions
                    List<ObjectSamplePoint> neighborPoints = MarkIntersectionNeighborhood(
                        data, x, y, width, height, isoValue, 
                        worldIntersection, sphereCenter, sphereRadius);
                    intersectionPoints.AddRange(neighborPoints);
                }
            }
        }
        
        return intersectionPoints;
    }

    // Marks additional pixels around an intersection point to fill gaps in the circle
    // and improve the appearance of the isoline
    private static List<ObjectSamplePoint> MarkIntersectionNeighborhood(int[,] data, int x, int y, int width, int height, int isoValue, Vector3 worldPos, Vector3 sphereCenter, float sphereRadius)
    {
        List<ObjectSamplePoint> neighborPoints = new();
        
        // Define a larger neighborhood pattern
        int[][] neighborhoodPattern = new int[][] {
            new int[] {1, 0}, new int[] {0, 1},    // Cardinal directions
            new int[] {-1, 0}, new int[] {0, -1},
            new int[] {1, 1}, new int[] {1, -1},   // Diagonal directions
            new int[] {-1, 1}, new int[] {-1, -1},
            new int[] {2, 0}, new int[] {0, 2},    // Extended cardinal
            new int[] {-2, 0}, new int[] {0, -2},
            new int[] {2, 1}, new int[] {1, 2},    // Extended diagonal
            new int[] {-2, 1}, new int[] {1, -2},
            new int[] {2, -1}, new int[] {-1, 2},
            new int[] {-2, -1}, new int[] {-1, -2}
        };

        // Check for existing points in the neighborhood
        bool hasExistingPoints = false;
        foreach (int[] dir in neighborhoodPattern)
        {
            int nx = x + dir[0];
            int ny = y + dir[1];
            if (IsInBounds(nx, ny, width, height) && data[nx, ny] == isoValue)
            {
                hasExistingPoints = true;
                break;
            }
        }

        if (hasExistingPoints)
        {
            // Calculate the base direction vector from sphere center to world position
            Vector3 baseDirection = (worldPos - sphereCenter).normalized;
            
            // Generate points in the neighborhood
            foreach (int[] dir in neighborhoodPattern)
            {
                int nx = x + dir[0];
                int ny = y + dir[1];
                
                if (IsInBounds(nx, ny, width, height) && data[nx, ny] == 0)
                {
                    // Calculate UV delta for position adjustment
                    Vector2 uvDelta = new Vector2(dir[0] / (float)width, dir[1] / (float)height);
                    float uvDeltaMagnitude = uvDelta.magnitude * 0.05f;
                    
                    // Create a tangent vector based on the direction
                    Vector3 tangent = new Vector3(dir[0], dir[1], 0).normalized;
                    
                    // Calculate the new world position
                    Vector3 newWorldPos = sphereCenter + baseDirection * sphereRadius;
                    newWorldPos += sphereRadius * uvDeltaMagnitude * tangent;
                    
                    // Add some variation to avoid perfect alignment
                    float variation = Random.Range(-0.01f, 0.01f);
                    newWorldPos += baseDirection * sphereRadius * variation;
                    
                    data[nx, ny] = isoValue;
                    neighborPoints.Add(new ObjectSamplePoint(newWorldPos, isoValue));
                }
            }
        }
        
        return neighborPoints;
    }

    private static bool IsInBounds(int x, int y, int width, int height)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }
}