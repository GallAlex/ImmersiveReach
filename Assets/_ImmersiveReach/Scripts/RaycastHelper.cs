using UnityEngine;
using System;
using System.Linq;
using Unity.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;


public enum AccessibilityAggregationMethod{
        VertexNeighborhood,
        NearestVertex,
        NearestVerticesWeighted,
        Texture
    };

public interface IHit
{
    float distance { get; }
    int colliderInstanceID { get; }
    Vector3 barycentricCoordinate { get; }
    Vector3 point { get; }
    int triangleIndex { get; }
    Vector2 textureCoord { get; }
}

public struct OptixHit : IHit
{
    public float distance;
    public Vector3 geom_normal;
    public Vector2 barycentricCoordinate;
    public int triangleIndex;
    public Vector2 textureCoord;
    public int colliderInstanceID;
    public Vector3 point;

    float IHit.distance => distance;
    int IHit.colliderInstanceID => colliderInstanceID;
    Vector3 IHit.point => point;
    Vector3 IHit.barycentricCoordinate => barycentricCoordinate;
    int IHit.triangleIndex => triangleIndex;
    Vector2 IHit.textureCoord => textureCoord;
}

public struct RaycastHitWrapper : IHit
{
    private RaycastHit hit;

    public RaycastHitWrapper(RaycastHit hit)
    {
        this.hit = hit;
    }

    public float distance => hit.distance;
    public int colliderInstanceID => hit.colliderInstanceID;
    public Vector3 point => hit.point;
    public Vector3 barycentricCoordinate => hit.barycentricCoordinate;
    public int triangleIndex => hit.triangleIndex;
    public Vector2 textureCoord => hit.textureCoord;
}

public static class RaycastHelper
{
    private static float[,] GaussianKernel;

    public static void ComputeObjectTextureData<T>(AccessibilityAggregationMethod coloringMethod, RaycastObject raycastObject, NativeArray<T> raycastResults, NativeArray<InstrumentSamplePoint> rayOrigins, int vertexNeighborhoodRadius) where T : struct, IHit
    {
        float[,] hitGrid = null;
        switch (coloringMethod)
        {
            case AccessibilityAggregationMethod.Texture:
                hitGrid = FillTextureCoordinatesHitGrid(raycastResults, raycastObject, rayOrigins);
                break;
            case AccessibilityAggregationMethod.VertexNeighborhood:
                hitGrid = FillVertexNeighborhoodHitGrid(raycastResults, raycastObject, vertexNeighborhoodRadius, rayOrigins);
                break;
            case AccessibilityAggregationMethod.NearestVertex:
                hitGrid = FillNearestHitGrid(raycastResults, raycastObject, rayOrigins);
                break;
            case AccessibilityAggregationMethod.NearestVerticesWeighted:
                hitGrid = FillNearestWeightedHitGrid(raycastResults, raycastObject, rayOrigins);
                break;
        }
        raycastObject.SetHitGrid(hitGrid);

        UpdateObjectTextureData(raycastObject);
    }

    public static void UpdateObjectTextureData(RaycastObject raycastObject)
    {
        float[,] hitGrid = raycastObject.GetHitGrid();
        int scaleFactor = raycastObject.GetTextureScaleFactor();
        int colorPaletteSize = TextureColoringHelper.GetColorPalette().Length;

        TextureData textureData = new TextureData(hitGrid, raycastObject.widthResolution, raycastObject.heightResolution, scaleFactor, colorPaletteSize, GaussianKernel, raycastObject);
        textureData.ComputePixels();
        raycastObject.SetTextureData(textureData.GetTextureData());
    }

    // Set the Gaussian kernel based on the given sigma value
    public static void SetGaussianKernel(float sigma)
    {
        // Handle the case where sigma = 0 (no blur)
        if (sigma == 0)
        {
            GaussianKernel = new float[1, 1];
            GaussianKernel[0, 0] = 1.0f;
            return;
        }

        int size = 2 * Mathf.CeilToInt(sigma * 3) + 1;
        GaussianKernel = new float[size, size];
        float twoSigmaSquared = 2 * sigma * sigma;
        float constant = 1 / (Mathf.PI * twoSigmaSquared);
        int halfSize = (size - 1) / 2;

        for (int y = -halfSize; y <= halfSize; ++y)
        {
            for (int x = -halfSize; x <= halfSize; ++x)
            {
                float distance = (x * x + y * y) / twoSigmaSquared;
                GaussianKernel[y + halfSize, x + halfSize] = constant * Mathf.Exp(-distance);
            }
        }
    }

    // Compute the reachability of the instrument points based on the raycast results
    // The reachability is computed based on the percentage of the object points that are reachable from the instrument points (weighted percentage)
    public static void ComputeInstrumentReachability(ref NativeArray<InstrumentSamplePoint> instrumentPoints, NativeArray<RaycastHit> raycastResults, NativeArray<ObjectSamplePoint> objectPoints, int reachabilityPercentage)
    {
        for (int i = 0; i < instrumentPoints.Length; ++i)
        {
            var point = instrumentPoints[i];
            point.SetValidity(true);
            instrumentPoints[i] = point;
        }

        float[] unreachabilityWeights = new float[instrumentPoints.Length];

        int objectSamplePointsCount = objectPoints.Length;
        var localInstrumentPoints = instrumentPoints; // using a copy because ref variables are not allowed in parallel loops not on the main thread
        Parallel.ForEach(Partitioner.Create(0, raycastResults.Length), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, range =>
        {
            for (int i = range.Item1; i < range.Item2; ++i)
            {
                RaycastHit hit = raycastResults[i];
                if (hit.distance > 0) // check if the hit is not null
                {
                    int instrumentIndex = i / objectSamplePointsCount;
                    int objectIndex = i % objectSamplePointsCount;

                    Vector3 instrumentPoint = localInstrumentPoints[instrumentIndex].point;
                    Vector3 rayOrigin = objectPoints[objectIndex].point;
                    float maxHitDistance = Vector3.Distance(rayOrigin, instrumentPoint);
                    // check if the hit is in front of the instrument point (i.e. the instrument point is not reachable) or behind it
                    if (hit.distance <= maxHitDistance) 
                    {
                        unreachabilityWeights[instrumentIndex] += objectPoints[objectIndex].weight;
                    }
                }
            }
        });

        float weightSum = 0f;
        for (int i = 0; i < objectSamplePointsCount; ++i)
        {
            weightSum += objectPoints[i].weight;
        }
        float reachabilityThreshold = weightSum * reachabilityPercentage / 100.0f;

        for (int i = 0; i < instrumentPoints.Length; ++i)
        {
            var point = instrumentPoints[i];
            // point is valid if the weights of reachable points are greater than the threshold
            point.SetValidity(weightSum - unreachabilityWeights[i] >= reachabilityThreshold);
            instrumentPoints[i] = point;
        }
    }

    private static float[,] FillTextureCoordinatesHitGrid<T>(NativeArray<T> raycastResults, RaycastObject raycastObject, NativeArray<InstrumentSamplePoint> rayOrigins) where T : struct, IHit
    {
        int width = raycastObject.widthResolution;
        int height = raycastObject.heightResolution;
        int depth = raycastObject.depthResolution;
        int colliderId = raycastObject.colliderId;
        Vector3 objectMin = raycastObject.objectBounds.min;
        Vector3 objectSize = raycastObject.objectBounds.size;
        int[,] textureZoneMap = raycastObject.GetTextureZoneMapScale1();

        float[,] hitGrid = new float[width, height];
        float[,] maxPotentialHitGrid = new float[width, height];
        // max potential hits : number of hits for each texture coordinates, regardless of the raycast start point validity
        // this is used to normalize the colors : will reflect the decreased accessibility on the result value
        // note : by default, all points are valid, so when not using the validity of the raycast start points, maxPotentialHitGrid is equal to hitGrid

        Parallel.ForEach(Partitioner.Create(0, raycastResults.Length), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, range =>
        {
            for (int i = range.Item1; i < range.Item2; ++i)
            {
                if (raycastResults[i].colliderInstanceID == colliderId)
                {
                    T hit = raycastResults[i];
                    Vector2 hitPointUV = hit.textureCoord;
                    float x = hitPointUV.x * width;
                    float y = hitPointUV.y * height;
                    int xFloor = Mathf.FloorToInt(x);
                    int yFloor = Mathf.FloorToInt(y);
                    int xRound = Mathf.RoundToInt(x);
                    int yRound = Mathf.RoundToInt(y);
                    Vector2 squareUvBL = new(x-0.5f, y+0.5f);
                    Vector2 squareUvTL = new(x-0.5f, y-0.5f);
                    Vector2 squareUvTR = new(x+0.5f, y-0.5f);
                    Vector2 squareUvBR = new(x+0.5f, y+0.5f);
                    Vector2Int squarePixelBL = new(xRound-1, yRound);
                    Vector2Int squarePixelTL = new(xRound-1, yRound-1);
                    Vector2Int squarePixelTR = new(xRound, yRound-1);
                    Vector2Int squarePixelBR = new(xRound, yRound);
                    Vector2[] squareUvBoundaries = new Vector2[4] { squareUvBL, squareUvTL, squareUvTR, squareUvBR };
                    Vector2Int[] squarePixelBoundaries = new Vector2Int[4] { squarePixelBL, squarePixelTL, squarePixelTR, squarePixelBR };
                    
                    // Calculate all surface values first
                    float[] surfaces = new float[4] {0f, 0f, 0f, 0f};
                    float totalSurface = 0f;
                    
                    for (int boundId=0; boundId<4; ++boundId) {
                        Vector2 uv = squareUvBoundaries[boundId];
                        Vector2Int pixel = squarePixelBoundaries[boundId];
                        
                        if (pixel.x >= 0 && pixel.x < width && pixel.y >= 0 && pixel.y < height && textureZoneMap[pixel.x, pixel.y] != -1)
                        {
                            surfaces[boundId] = Mathf.Abs(xRound - uv.x) * Mathf.Abs(yRound - uv.y);
                            totalSurface += surfaces[boundId];
                        }
                    }
                    
                    // Now add normalized surfaces to in-bounds pixels
                    for (int boundId=0; boundId<4; ++boundId) {
                        Vector2Int pixel = squarePixelBoundaries[boundId];
                        if (pixel.x >= 0 && pixel.x < width && pixel.y >= 0 && pixel.y < height && textureZoneMap[pixel.x, pixel.y] != -1)
                        {
                            // Normalize the surface so in-bounds pixels sum to 1
                            float normalizedSurface = surfaces[boundId] / totalSurface;
                            int samplePointIndex = i % rayOrigins.Length;
                            if (rayOrigins[samplePointIndex].IsValid())
                            {
                                hitGrid[pixel.x, pixel.y] += normalizedSurface;
                            }
                            maxPotentialHitGrid[pixel.x, pixel.y] += normalizedSurface;
                        }
                    }
                }
            }
        });

        hitGrid = FixDiscontinuitiesHitGrid(hitGrid, width, height, raycastObject);
        return hitGrid;
    }

    private static float[,] FillVertexNeighborhoodHitGrid<T>(NativeArray<T> raycastResults, RaycastObject raycastObject, int vertexNeighborhoodRadius, NativeArray<InstrumentSamplePoint> rayOrigins) where T : struct, IHit
    {
        int width = raycastObject.widthResolution;
        int height = raycastObject.heightResolution;
        int depth = raycastObject.depthResolution;
        int colliderId = raycastObject.colliderId;
        Vector3 objectMin = raycastObject.objectBounds.min;
        Vector3 objectSize = raycastObject.objectBounds.size;
        Mesh objectMesh = raycastObject.gameObject.GetComponent<MeshFilter>().mesh;

        int[] hitCounts3D = new int[width * height * depth];
        int[] maxPotentialHits3D = new int[width * height * depth];
        int objectVerticesCount = objectMesh.vertices.Length;
        float[] verticesGrid = new float[objectVerticesCount];
        float[] maxPotentialVerticesGrid = new float[objectVerticesCount];

        Parallel.ForEach(Partitioner.Create(0, raycastResults.Length), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, range =>
        {
            for (int i = range.Item1; i < range.Item2; ++i)
            {
                if (raycastResults[i].colliderInstanceID == colliderId)
                {
                    T hit = raycastResults[i];
                    int index = ObjectHelper.ObjectInWorldToTexture3DIndex(hit.point, objectMin, objectSize, width, height, depth);
                    if (index >= 0 && index < hitCounts3D.Length)
                    {
                        int samplePointIndex = i % rayOrigins.Length;
                        if (rayOrigins[samplePointIndex].IsValid())
                        {
                            ++hitCounts3D[index];
                        }
                        ++maxPotentialHits3D[index];
                    }
                }
            }
        });

        int radius = vertexNeighborhoodRadius;
        List<int> objectPoints = raycastObject.GetObjectPoints();
        for (int vertexId = 0; vertexId < objectPoints.Count; ++vertexId)
        {
            int pointId = objectPoints[vertexId];
            int z = pointId / (width * height);
            int y = (pointId - z * (width * height)) / width;
            int x = pointId - z * (width * height) - y * width;

            int minX = Math.Max(0, x - radius);
            int maxX = Math.Min(width - 1, x + radius);
            int minY = Math.Max(0, y - radius);
            int maxY = Math.Min(height - 1, y + radius);
            int minZ = Math.Max(0, z - radius);
            int maxZ = Math.Min(depth - 1, z + radius);

            for (int nx = minX; nx <= maxX; ++nx)
            {
                for (int ny = minY; ny <= maxY; ++ny)
                {
                    for (int nz = minZ; nz <= maxZ; ++nz)
                    {
                        int nId = nx + ny * width + nz * width * height;
                        verticesGrid[vertexId] += hitCounts3D[nId];
                        maxPotentialVerticesGrid[vertexId] += maxPotentialHits3D[nId];
                    }
                }
            }
        }
        float[,] hitGrid = VertexIntensityToTextureIntensity(verticesGrid, objectMesh, width, height);
        hitGrid = FixDiscontinuitiesHitGrid(hitGrid, width, height, raycastObject);
        return hitGrid;
    }

    private static float[,] FillNearestHitGrid<T>(NativeArray<T> raycastResults, RaycastObject raycastObject, NativeArray<InstrumentSamplePoint> rayOrigins) where T : struct, IHit
    {
        int width = raycastObject.widthResolution;
        int height = raycastObject.heightResolution;
        int depth = raycastObject.depthResolution;
        int colliderId = raycastObject.colliderId;
        Vector3 objectMin = raycastObject.objectBounds.min;
        Vector3 objectSize = raycastObject.objectBounds.size;
        Mesh objectMesh = raycastObject.gameObject.GetComponent<MeshFilter>().mesh;

        int[] triangles = objectMesh.triangles;
        float[] verticesGrid = new float[objectMesh.vertices.Length];
        float[] maxPotentialVerticesGrid = new float[objectMesh.vertices.Length];

        Parallel.ForEach(Partitioner.Create(0, raycastResults.Length), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, range =>
        {
            for (int i = range.Item1; i < range.Item2; ++i)
            {
                if (raycastResults[i].colliderInstanceID == colliderId)
                {
                    T hit = raycastResults[i];
                    Vector3 barycentricCoord = hit.barycentricCoordinate;
                    int nearestVertexInTriangle = 0;
                    float maxWeight = barycentricCoord[0];
                    for (int v = 1; v < 3; ++v)
                    {
                        if (barycentricCoord[v] > maxWeight)
                        {
                            maxWeight = barycentricCoord[v];
                            nearestVertexInTriangle = v;
                        }
                    }
                    int nearestVertexId = triangles[hit.triangleIndex * 3 + nearestVertexInTriangle];
                    if (nearestVertexId >= 0 && nearestVertexId < verticesGrid.Length)
                    {
                        int samplePointIndex = i % rayOrigins.Length;
                        if (rayOrigins[samplePointIndex].IsValid())
                        {
                            ++verticesGrid[nearestVertexId];
                        }
                        ++maxPotentialVerticesGrid[nearestVertexId];
                    }
                }
            }
        });

        float[,] hitGrid = VertexIntensityToTextureIntensity(verticesGrid, objectMesh, width, height);
        hitGrid = FixDiscontinuitiesHitGrid(hitGrid, width, height, raycastObject);
        return hitGrid;
    }

    private static float[,] FillNearestWeightedHitGrid<T>(NativeArray<T> raycastResults, RaycastObject raycastObject, NativeArray<InstrumentSamplePoint> rayOrigins) where T : struct, IHit
    {
        int width = raycastObject.widthResolution;
        int height = raycastObject.heightResolution;
        int depth = raycastObject.depthResolution;
        int colliderId = raycastObject.colliderId;
        Vector3 objectMin = raycastObject.objectBounds.min;
        Vector3 objectSize = raycastObject.objectBounds.size;
        Mesh objectMesh = raycastObject.gameObject.GetComponent<MeshFilter>().mesh;

        int[] triangles = objectMesh.triangles;
        float[] verticesGrid = new float[objectMesh.vertices.Length];
        float[] maxPotentialVerticesGrid = new float[objectMesh.vertices.Length];

        Parallel.ForEach(Partitioner.Create(0, raycastResults.Length), new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, range =>
        {
            for (int i = range.Item1; i < range.Item2; ++i)
            {
                if (raycastResults[i].colliderInstanceID == colliderId)
                {
                    T hit = raycastResults[i];
                    Vector3 barycentricCoord = hit.barycentricCoordinate;
                    int triangleIndex = hit.triangleIndex;
                    for (int v = 0; v < 3; ++v)
                    {
                        int vertexId = triangles[triangleIndex * 3 + v];
                        int samplePointIndex = i % rayOrigins.Length;
                        if (rayOrigins[samplePointIndex].IsValid())
                        {
                            verticesGrid[vertexId] += barycentricCoord[v];
                        }
                        maxPotentialVerticesGrid[vertexId] += barycentricCoord[v];
                    }
                }
            }
        });

        float[,] hitGrid = VertexIntensityToTextureIntensity(verticesGrid, objectMesh, width, height);
        hitGrid = FixDiscontinuitiesHitGrid(hitGrid, width, height, raycastObject);
        return hitGrid;
    }

    private static float[,] FixDiscontinuitiesHitGrid(float[,] hitGrid, int width, int height, RaycastObject raycastObject)
    {
        float[,] fixedHitGrid = new float[width, height];
        for (int x = 0; x < width; ++x)
        {
            for (int y = 0; y < height; ++y)
            {
                fixedHitGrid[x, y] = hitGrid[x, y];
            }
        }

        List<Vector2Int> pixelsOnDiscontinuityBorders = raycastObject.GetPixelsOnDiscontinuityBordersScale1();
        int[,] textureZoneMap = raycastObject.GetTextureZoneMapScale1();
        foreach (Vector2Int pixel in pixelsOnDiscontinuityBorders)
        {
            int x = pixel.x;
            int y = pixel.y;
            if (x < 0 || x >= width || y < 0 || y >= height) continue;
            int zone = textureZoneMap[x, y];
            if (zone == -1) continue;

            int neighborhoodSquareRadius = 1;
            float maxValue = 0;
            for (int dx = -neighborhoodSquareRadius; dx <= neighborhoodSquareRadius; ++dx)
            {
                for (int dy = -neighborhoodSquareRadius; dy <= neighborhoodSquareRadius; ++dy)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    {
                        if (textureZoneMap[nx, ny] == zone)
                        {
                            maxValue = MathF.Max(maxValue, hitGrid[nx, ny]);
                        }
                    }
                }
            }
            fixedHitGrid[x, y] = maxValue;
        }

        float[] extremum = Get2DGridExtremum(fixedHitGrid);
        float max = extremum[1];

        Parallel.For(0, width, x =>
        {
            for (int y = 0; y < height; ++y)
            {
                fixedHitGrid[x, y] /= max;
            }
        });
        return fixedHitGrid;
    }

    private static float[] Get2DGridExtremum(float[,] grid)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);

        for (int x = 0; x < width; ++x)
        {
            for (int y = 0; y < height; ++y)
            {
                float value = grid[x, y];
                min = MathF.Min(min, value);
                max = MathF.Max(max, value);
            }
        }

        return new float[] { min, max };
    }

    // Helper method to convert a grid of vertex intensities to a grid of texture intensities
    public static float[,] VertexIntensityToTextureIntensity(float[] vertexGrid, Mesh objectMesh, int textureWidth, int textureHeight)
    {
        float[,] textureIntensity = new float[textureWidth, textureHeight];
        int[,] weights = new int[textureWidth, textureHeight];

        Vector2[] uv = objectMesh.uv;
        int[] triangles = objectMesh.triangles;

        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];

            Vector2 uv0 = uv[i0] * new Vector2(textureWidth, textureHeight);
            Vector2 uv1 = uv[i1] * new Vector2(textureWidth, textureHeight);
            Vector2 uv2 = uv[i2] * new Vector2(textureWidth, textureHeight);

            float intensity0 = vertexGrid[i0];
            float intensity1 = vertexGrid[i1];
            float intensity2 = vertexGrid[i2];

            FillTriangle(textureIntensity, weights, uv0, uv1, uv2, intensity0, intensity1, intensity2, textureWidth, textureHeight);
        }

        for (int y = 0; y < textureHeight; ++y)
        {
            for (int x = 0; x < textureWidth; ++x)
            {
                if (weights[x, y] > 0)
                {
                    textureIntensity[x, y] /= weights[x, y];
                }
                else
                {
                    textureIntensity[x, y] = 0;
                }
            }
        }

        return textureIntensity;
    }

    // Helper method to fill a triangle in the texture with interpolated intensities based on the vertex intensities of the triangle
    private static void FillTriangle(float[,] textureIntensity, int[,] weights, Vector2 uv0, Vector2 uv1, Vector2 uv2, float intensity0, float intensity1, float intensity2, int textureWidth, int textureHeight)
    {
        int minX = Mathf.Clamp(Mathf.Min(Mathf.FloorToInt(uv0.x), Mathf.FloorToInt(uv1.x), Mathf.FloorToInt(uv2.x)), 0, textureWidth - 1);
        int maxX = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(uv0.x), Mathf.CeilToInt(uv1.x), Mathf.CeilToInt(uv2.x)), 0, textureWidth - 1);
        int minY = Mathf.Clamp(Mathf.Min(Mathf.FloorToInt(uv0.y), Mathf.FloorToInt(uv1.y), Mathf.FloorToInt(uv2.y)), 0, textureHeight - 1);
        int maxY = Mathf.Clamp(Mathf.Max(Mathf.CeilToInt(uv0.y), Mathf.CeilToInt(uv1.y), Mathf.CeilToInt(uv2.y)), 0, textureHeight - 1);

        Vector2 v0 = uv1 - uv0;
        Vector2 v1 = uv2 - uv0;
        float denom = v0.x * v1.y - v0.y * v1.x;

        for (int y = minY; y <= maxY; ++y)
        {
            for (int x = minX; x <= maxX; ++x)
            {
                Vector2 p = new(x, y);
                Vector2 v2 = p - uv0;
                float a = (v2.x * v1.y - v2.y * v1.x) / denom;
                float b = (v0.x * v2.y - v0.y * v2.x) / denom;
                float c = 1 - a - b;

                if (a >= 0 && b >= 0 && c >= 0) // check if the point is inside the triangle
                {
                    textureIntensity[x, y] += a * intensity0 + b * intensity1 + c * intensity2;
                    ++weights[x, y];
                }
            }
        }
    }
}