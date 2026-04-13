using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Class for handling UV discontinuities in meshes
/// </summary>
public class UVDiscontinuityHelper
{
    private Mesh mesh;
    private int width; // Scale 1
    private int height; // Scale 1
    private Dictionary<Vector3, List<Vector2>> vertexUVsDiscontinuities;
    private HashSet<(Vector2, Vector2)> uniqueConnectionsVertexBorders;
    private bool[,] discontinuityBorders;
    private bool[,] discontinuityBordersScale1;
    private List<Vector2Int> pixelsOnDiscontinuityBorders;
    private List<Vector2Int> pixelsOnDiscontinuityBordersScale1;
    private int zonesCount;
    private int[,] zoneMap;
    private int[,] zoneMapScale1;
    private int[] triangleZone;
    
    public UVDiscontinuityHelper(GameObject gameObject, int width, int height)
    {
        this.mesh = gameObject.GetComponent<MeshFilter>().sharedMesh;
        this.width = width;
        this.height = height;

        ComputeVertexDiscontinuitiesAndBorders();
        ComputeZones();
        ComputeDiscontinuityBordersScale1();
        ComputeContinuityZonesScale1();
    }
    
    public Dictionary<Vector3, List<Vector2>> GetVertexUVsDiscontinuities()
    {
        return vertexUVsDiscontinuities;
    }
    
    public List<Vector2Int> GetPixelsOnDiscontinuityBorders()
    {
        return pixelsOnDiscontinuityBorders;
    }
    
    public List<Vector2Int> GetPixelsOnDiscontinuityBordersScale1()
    {
        return pixelsOnDiscontinuityBordersScale1;
    }
    
    public bool[,] GetDiscontinuityBorders()
    {
        return discontinuityBorders;
    }

    public int[,] GetTextureZoneMap()
    {
        return zoneMap;
    }

    public int[,] GetZoneMapScale1()
    {
        return zoneMapScale1;
    }

    /// <summary>
    /// Computes a boolean map marking the discontinuity borders on the texture
    /// </summary>
    public void ComputeDiscontinuityBorders(int textureWidth, int textureHeight)
    {
        discontinuityBorders = new bool[textureWidth, textureHeight];
        pixelsOnDiscontinuityBorders = new List<Vector2Int>();
        ComputeDiscontinuityBordersInternal(textureWidth, textureHeight, discontinuityBorders, pixelsOnDiscontinuityBorders);
    }
    
    private void ComputeDiscontinuityBordersScale1()
    {
        discontinuityBordersScale1 = new bool[width, height];
        pixelsOnDiscontinuityBordersScale1 = new List<Vector2Int>();
        ComputeDiscontinuityBordersInternal(width, height, discontinuityBordersScale1, pixelsOnDiscontinuityBordersScale1);
    }
    
    /// <summary>
    /// Common implementation for computing discontinuity borders
    /// </summary>
    private void ComputeDiscontinuityBordersInternal(int targetWidth, int targetHeight, bool[,] targetArray, List<Vector2Int> targetPixelsList)
    {
        // Initialize array
        for (int i = 0; i < targetWidth; ++i)
        {
            for (int j = 0; j < targetHeight; ++j)
            {
                targetArray[i, j] = false;
            }
        }
        
        // Mark individual points first
        foreach (var entry in vertexUVsDiscontinuities)
        {
            foreach (var uv in entry.Value)
            {
                int i = Mathf.RoundToInt(uv.x * targetWidth);
                int j = Mathf.RoundToInt(uv.y * targetHeight);
                
                // Ensure we're within bounds
                if (i >= 0 && i < targetWidth && j >= 0 && j < targetHeight)
                {
                    targetArray[i, j] = true;
                    targetPixelsList.Add(new Vector2Int(i, j));
                }
            }
        }
        
        // Draw lines between connected points using Bresenham's line algorithm
        foreach (var (uv1, uv2) in uniqueConnectionsVertexBorders)
        {
            int x0 = Mathf.RoundToInt(uv1.x * targetWidth);
            int y0 = Mathf.RoundToInt(uv1.y * targetHeight);
            int x1 = Mathf.RoundToInt(uv2.x * targetWidth);
            int y1 = Mathf.RoundToInt(uv2.y * targetHeight);
            
            DrawBresenhamLine(targetArray, x0, y0, x1, y1, targetWidth, targetHeight, targetPixelsList);
        }
    }

    /// <summary>
    /// Finds vertices with multiple UV mappings and the borders between them
    /// </summary>
    private void ComputeVertexDiscontinuitiesAndBorders()
    {
        Vector2[] uvs = mesh.uv;
        int uvsCount = mesh.uv.Length;
        int[] triangles = mesh.triangles;
        Vector3[] vertices = mesh.vertices;
        vertexUVsDiscontinuities = new Dictionary<Vector3, List<Vector2>>();
        HashSet<(int, int)> processedEdges = new();

        // 1. Find vertices with multiple UVs
        for (int i = 0; i < uvsCount; ++i)
        {
            int index = i;
            Vector3 vertex = mesh.vertices[index];
            Vector2 uv = uvs[index];

            if (!vertexUVsDiscontinuities.ContainsKey(vertex))
            {
                vertexUVsDiscontinuities[vertex] = new List<Vector2>();
            }

            bool uvExists = false;
            foreach (Vector2 existingUV in vertexUVsDiscontinuities[vertex])
            {
                if (Vector2.Distance(existingUV, uv) == 0)
                {
                    uvExists = true;
                    break;
                }
            }

            if (!uvExists)
                vertexUVsDiscontinuities[vertex].Add(uv);
        }

        // Keep only the vertices with multiple UVs for original discontinuities
        vertexUVsDiscontinuities = vertexUVsDiscontinuities.Where(entry => entry.Value.Count > 1)
                                    .ToDictionary(entry => entry.Key, entry => entry.Value);


        // 2. Find the edges between discontinuous vertices
        // Create a set of tuples to store unique UV edges that are actually used in the mesh
        uniqueConnectionsVertexBorders = new HashSet<(Vector2, Vector2)>();
        
        // Process each triangle in the mesh
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int idx0 = triangles[i];
            int idx1 = triangles[i + 1];
            int idx2 = triangles[i + 2];
            
            // Get vertices of the triangle
            Vector3 v0 = vertices[idx0];
            Vector3 v1 = vertices[idx1];
            Vector3 v2 = vertices[idx2];
            
            // Get UVs of the triangle (these are the actual UVs used in this triangle)
            Vector2 uv0 = uvs[idx0];
            Vector2 uv1 = uvs[idx1];
            Vector2 uv2 = uvs[idx2];
            
            // Check if any of the vertices are on a discontinuity
            bool v0IsDiscontinuity = vertexUVsDiscontinuities.ContainsKey(v0);
            bool v1IsDiscontinuity = vertexUVsDiscontinuities.ContainsKey(v1);
            bool v2IsDiscontinuity = vertexUVsDiscontinuities.ContainsKey(v2);
            
            // Connect only the UVs that are actually used in this triangle
            if (v0IsDiscontinuity && v1IsDiscontinuity)
            {
                // Only connect if both vertices are on a discontinuity
                AddUnorderedPair(uniqueConnectionsVertexBorders, uv0, uv1);
            }
            
            if (v1IsDiscontinuity && v2IsDiscontinuity)
            {
                AddUnorderedPair(uniqueConnectionsVertexBorders, uv1, uv2);
            }
            
            if (v0IsDiscontinuity && v2IsDiscontinuity)
            {
                AddUnorderedPair(uniqueConnectionsVertexBorders, uv0, uv2);
            }
        }
        
        // Fix missing vertices in the discontinuity graph before drawing
        FixMissingVerticesInDiscontinuityGraph();
    }

    /// <summary>
    /// Computes the zones of continuity on the texture
    /// </summary>
    private void ComputeContinuityZonesScale1()
    {
        // Compute zones at the base resolution (width x height)
        zoneMapScale1 = new int[width, height];
        for (int x = 0; x < width; ++x)
            for (int y = 0; y < height; ++y)
                zoneMapScale1[x, y] = -1;
                
        ComputeContinuityZonesInternal(width, height, zoneMapScale1, discontinuityBordersScale1);
    }
    
    /// <summary>
    /// Optional method to compute zones at texture resolution
    /// </summary>
    public void ComputeContinuityZonesTexture(int textureWidth, int textureHeight)
    {
        zoneMap = new int[textureWidth, textureHeight];
        for (int x = 0; x < textureWidth; ++x)
            for (int y = 0; y < textureHeight; ++y)
                zoneMap[x, y] = -1;
                
        ComputeContinuityZonesInternal(textureWidth, textureHeight, zoneMap, discontinuityBorders);
    }
    
    /// <summary>
    /// Computes zone information once for the entire mesh
    /// </summary>
    private void ComputeZones()
    {
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;
        int triangleCount = triangles.Length / 3;
        
        // Build triangle adjacency map
        Dictionary<int, List<int>> triangleAdjacency = new();
        for (int i = 0; i < triangleCount; ++i)
            triangleAdjacency[i] = new List<int>();

        // Initialize vertex-to-triangle mapping for efficiency
        Dictionary<int, List<int>> vertexToTriangles = new();
        for (int tri = 0; tri < triangleCount; ++tri)
        {
            for (int k = 0; k < 3; ++k)
            {
                int vi = triangles[tri * 3 + k];
                if (!vertexToTriangles.ContainsKey(vi))
                    vertexToTriangles[vi] = new List<int>();
                vertexToTriangles[vi].Add(tri);
            }
        }
        
        // Connect triangles through shared vertices that aren't on discontinuities
        // or shared edges that aren't discontinuity borders
        for (int tri = 0; tri < triangleCount; ++tri)
        {
            int idx0 = triangles[tri * 3];
            int idx1 = triangles[tri * 3 + 1];
            int idx2 = triangles[tri * 3 + 2];
            
            // For each vertex of the triangle
            for (int k = 0; k < 3; ++k)
            {
                int vi = triangles[tri * 3 + k];
                
                // Get the vertex UVs for this triangle
                Vector2 uv0 = uvs[idx0];
                Vector2 uv1 = uvs[idx1];
                Vector2 uv2 = uvs[idx2];
                
                // Find triangles sharing this vertex that we might want to connect
                foreach (int neighborTri in vertexToTriangles[vi])
                {
                    if (neighborTri == tri || triangleAdjacency[tri].Contains(neighborTri))
                        continue;
                    
                    // Check if the two triangles share an edge
                    int nIdx0 = triangles[neighborTri * 3];
                    int nIdx1 = triangles[neighborTri * 3 + 1];
                    int nIdx2 = triangles[neighborTri * 3 + 2];
                    
                    // Get the neighboring triangle's UVs
                    Vector2 nUv0 = uvs[nIdx0];
                    Vector2 nUv1 = uvs[nIdx1];
                    Vector2 nUv2 = uvs[nIdx2];
                    
                    // Determine if triangles share an edge and if it's a discontinuity
                    bool shareEdge = false;
                    bool edgeIsDiscontinuity = false;
                    
                    // Check all possible shared edges between the two triangles
                    if ((idx0 == nIdx0 && idx1 == nIdx1) || (idx0 == nIdx1 && idx1 == nIdx0))
                    {
                        shareEdge = true;
                        // Simply use HasConnection which checks uniqueConnectionsVertexBorders
                        edgeIsDiscontinuity = HasConnection(uv0, uv1);
                    }
                    else if ((idx1 == nIdx1 && idx2 == nIdx2) || (idx1 == nIdx2 && idx2 == nIdx1))
                    {
                        shareEdge = true;
                        edgeIsDiscontinuity = HasConnection(uv1, uv2);
                    }
                    else if ((idx0 == nIdx0 && idx2 == nIdx2) || (idx0 == nIdx2 && idx2 == nIdx0))
                    {
                        shareEdge = true;
                        edgeIsDiscontinuity = HasConnection(uv0, uv2);
                    }
                    else if ((idx0 == nIdx1 && idx1 == nIdx2) || (idx0 == nIdx2 && idx1 == nIdx1))
                    {
                        shareEdge = true;
                        edgeIsDiscontinuity = HasConnection(uv0, uv1);
                    }
                    else if ((idx1 == nIdx0 && idx2 == nIdx1) || (idx1 == nIdx1 && idx2 == nIdx0))
                    {
                        shareEdge = true;
                        edgeIsDiscontinuity = HasConnection(uv1, uv2);
                    }
                    else if ((idx0 == nIdx0 && idx2 == nIdx1) || (idx0 == nIdx1 && idx2 == nIdx0))
                    {
                        shareEdge = true;
                        edgeIsDiscontinuity = HasConnection(uv0, uv2);
                    }
                    
                    // Simply connect if:
                    // 1. They share an edge that's not a discontinuity border (using uniqueConnectionsVertexBorders)
                    // 2. Or if they just share a vertex
                    if ((shareEdge && !edgeIsDiscontinuity) || !shareEdge)
                    {
                        triangleAdjacency[tri].Add(neighborTri);
                    }
                }
            }
        }
        
        // Flood-fill to identify zones
        triangleZone = new int[triangleCount];
        for (int i = 0; i < triangleZone.Length; ++i)
            triangleZone[i] = -1;
            
        zonesCount = 0;
        
        for (int i = 0; i < triangleCount; ++i)
        {
            if (triangleZone[i] != -1)
                continue;
                
            Queue<int> queue = new Queue<int>();
            List<int> trianglesInZone = new List<int>();
            
            queue.Enqueue(i);
            triangleZone[i] = zonesCount;
            trianglesInZone.Add(i);
            
            while (queue.Count > 0)
            {
                int tri = queue.Dequeue();
                foreach (int neighbor in triangleAdjacency[tri])
                {
                    if (triangleZone[neighbor] == -1)
                    {
                        triangleZone[neighbor] = zonesCount;
                        trianglesInZone.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }
            }
            ++zonesCount;
        }
    }
    
    /// <summary>
    /// Common implementation for computing continuity zones
    /// </summary>
    private void ComputeContinuityZonesInternal(int targetWidth, int targetHeight, int[,] targetZoneMap, bool[,] targetDiscontinuityBorders)
    {
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;
        int triangleCount = triangles.Length / 3;
        
        // We already have zone information from ComputeZones()
        // Just need to fill the texture with the zones

        // Initialize the zone map
        for (int x = 0; x < targetWidth; ++x)
            for (int y = 0; y < targetHeight; ++y)
                targetZoneMap[x, y] = -1;
        
        // Fill zoneMap with triangle coverage
        for (int tri = 0; tri < triangleCount; ++tri)
        {
            int zone = triangleZone[tri];
            
            int i0 = triangles[tri * 3 + 0];
            int i1 = triangles[tri * 3 + 1];
            int i2 = triangles[tri * 3 + 2];

            Vector2 uv0 = uvs[i0];
            Vector2 uv1 = uvs[i1];
            Vector2 uv2 = uvs[i2];

            Vector2 p0 = new(uv0.x * targetWidth, uv0.y * targetHeight);
            Vector2 p1 = new(uv1.x * targetWidth, uv1.y * targetHeight);
            Vector2 p2 = new(uv2.x * targetWidth, uv2.y * targetHeight);

            FillTriangle(targetZoneMap, targetWidth, targetHeight, Mathf.RoundToInt(p0.x), Mathf.RoundToInt(p0.y),
                Mathf.RoundToInt(p1.x), Mathf.RoundToInt(p1.y), Mathf.RoundToInt(p2.x), Mathf.RoundToInt(p2.y), zone);
        }

        // Assign zones to discontinuity borders
        for (int i = 0; i < targetWidth; ++i)
        {
            for (int j = 0; j < targetHeight; ++j)
            {
                if (targetDiscontinuityBorders[i, j] && targetZoneMap[i, j] == -1)
                {
                    // Look for neighboring zones (check 8 neighbors)
                    Dictionary<int, int> neighborZones = new Dictionary<int, int>();
                    bool foundZone = false;
                    
                    for (int di = -1; di <= 1; ++di)
                    {
                        for (int dj = -1; dj <= 1; ++dj)
                        {
                            if (di == 0 && dj == 0) continue;
                            
                            int ni = i + di;
                            int nj = j + dj;
                            
                            // Check if neighbor is in bounds and has a zone assigned
                            if (ni >= 0 && ni < targetWidth && nj >= 0 && nj < targetHeight && 
                                targetZoneMap[ni, nj] != -1)
                            {
                                int zone = targetZoneMap[ni, nj];
                                if (!neighborZones.ContainsKey(zone))
                                    neighborZones[zone] = 0;
                                
                                neighborZones[zone]++;
                                foundZone = true;
                            }
                        }
                    }
                    
                    // Assign border to the most common neighboring zone
                    if (foundZone)
                    {
                        int bestZone = neighborZones.OrderByDescending(z => z.Value).First().Key;
                        targetZoneMap[i, j] = bestZone;
                    }
                }
            }
        }

        // Check single pixels unassigned inside zones - only fill holes within zones
        for (int i = 0; i < targetWidth; ++i)
        {
            for (int j = 0; j < targetHeight; ++j)
            {
                // Only process unassigned pixels
                if (targetZoneMap[i, j] != -1)
                    continue;
                
                Dictionary<int, int> neighborZones = new Dictionary<int, int>();
                int neighborPixels = 0;
                
                for (int di = -1; di <= 1; ++di)
                {
                    for (int dj = -1; dj <= 1; ++dj)
                    {
                        if (di == 0 && dj == 0) continue;
                        
                        int ni = i + di;
                        int nj = j + dj;
                        
                        // Check if neighbor is in bounds and has a zone assigned
                        if (ni >= 0 && ni < targetWidth && nj >= 0 && nj < targetHeight && targetZoneMap[ni, nj] != -1)
                        {
                            ++neighborPixels;
                            int zone = targetZoneMap[ni, nj];
                            if (!neighborZones.ContainsKey(zone))
                                neighborZones[zone] = 0;
                            
                            ++neighborZones[zone];
                        }
                    }
                }
                
                // Fill if pixel is mostly surrounded by one zone
                if (neighborZones.Count > 0 && neighborPixels >= 5) // At least 5 out of 8 neighbors
                {
                    var bestZone = neighborZones.OrderByDescending(z => z.Value).First();
                    if (bestZone.Value >= 5) // At least 5
                    {
                        targetZoneMap[i, j] = bestZone.Key;
                    }
                }
            }
        }
    }

    #region Helper Methods

    /// <summary>
    /// Adds a pair of UV coordinates to the set in a consistent order
    /// </summary>
    private void AddUnorderedPair(HashSet<(Vector2, Vector2)> set, Vector2 a, Vector2 b)
    {
        // Ensure we don't add duplicate connections by ordering the pair consistently
        if (Vector2.SqrMagnitude(a) <= Vector2.SqrMagnitude(b))
        {
            set.Add((a, b));
        }
        else
        {
            set.Add((b, a));
        }
    }
    
    /// <summary>
    /// Attempts to fix disconnected discontinuity borders
    /// </summary>
    private void FixMissingVerticesInDiscontinuityGraph()
    {
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;
        Vector3[] vertices = mesh.vertices;
        
        // Count how many times each UV appears in the connections
        Dictionary<Vector2, int> uvConnectCount = new Dictionary<Vector2, int>();
        
        // Build a lookup of UVs to their index in the mesh
        Dictionary<Vector2, List<int>> uvToIndices = new Dictionary<Vector2, List<int>>();
        for (int i = 0; i < uvs.Length; ++i)
        {
            if (!uvToIndices.ContainsKey(uvs[i]))
                uvToIndices[uvs[i]] = new List<int>();
            uvToIndices[uvs[i]].Add(i);
        }
        
        // Count connections for each UV coordinate in our discontinuity borders
        foreach (var (uv1, uv2) in uniqueConnectionsVertexBorders)
        {
            if (!uvConnectCount.ContainsKey(uv1))
                uvConnectCount[uv1] = 0;
            if (!uvConnectCount.ContainsKey(uv2))
                uvConnectCount[uv2] = 0;
                
            uvConnectCount[uv1]++;
            uvConnectCount[uv2]++;
        }
        
        // Find endpoints (UVs that appear only once in connections)
        List<Vector2> endpoints = uvConnectCount
            .Where(kvp => kvp.Value == 1)
            .Select(kvp => kvp.Key)
            .ToList();
        
        if (endpoints.Count > 0)
        {            
            // STRATEGY 1: Examine mesh triangles containing the endpoints to find missing connections
            bool fixedAny = FixEndpointsThroughMeshAnalysis(endpoints, uvToIndices);
            
            // STRATEGY 2: If that didn't work, consider connecting endpoints directly if they're close enough
            if (!fixedAny)
            {
                FixEndpointsByDirectConnection(endpoints);
            }
            
            // STRATEGY 3: If all else fails, try a more aggressive approach using all discontinuity vertices
            if (!fixedAny)
            {
                FixEndpointsByFindingMostLikelyConnections(endpoints, uvConnectCount);
            }
        }
    }

    /// <summary>
    /// Strategy 1: Analyze the mesh to find connections through triangles
    /// </summary>
    private bool FixEndpointsThroughMeshAnalysis(List<Vector2> endpoints, Dictionary<Vector2, List<int>> uvToIndices)
    {
        Vector2[] uvs = mesh.uv;
        int[] triangles = mesh.triangles;
        Vector3[] vertices = mesh.vertices;
        bool fixedAny = false;
        
        // For each endpoint, try to find a missing connection
        foreach (Vector2 endpoint in endpoints)
        {
            // Get the mesh indices associated with this UV
            if (!uvToIndices.ContainsKey(endpoint))
                continue;
                
            List<int> indices = uvToIndices[endpoint];
            
            // For each index of this UV
            foreach (int idx in indices)
            {
                // Find triangles that use this index
                List<int> trianglesUsingIdx = new List<int>();
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    int v0 = triangles[t];
                    int v1 = triangles[t + 1];
                    int v2 = triangles[t + 2];
                    
                    if (v0 == idx || v1 == idx || v2 == idx)
                    {
                        trianglesUsingIdx.Add(t / 3);
                    }
                }
                                
                // Examine triangles that use this index
                for (int ti = 0; ti < trianglesUsingIdx.Count; ++ti)
                {
                    int triIdx = trianglesUsingIdx[ti];
                    int v0 = triangles[triIdx * 3];
                    int v1 = triangles[triIdx * 3 + 1];
                    int v2 = triangles[triIdx * 3 + 2];
                    
                    Vector2 uv0 = uvs[v0];
                    Vector2 uv1 = uvs[v1];
                    Vector2 uv2 = uvs[v2];
                    
                    Vector3 vert0 = vertices[v0];
                    Vector3 vert1 = vertices[v1];
                    Vector3 vert2 = vertices[v2];
                    
                    bool v0OnDiscontinuity = vertexUVsDiscontinuities.ContainsKey(vert0);
                    bool v1OnDiscontinuity = vertexUVsDiscontinuities.ContainsKey(vert1);
                    bool v2OnDiscontinuity = vertexUVsDiscontinuities.ContainsKey(vert2);
                                        
                    // If at least two vertices are on discontinuities including our endpoint
                    if ((v0 == idx && v0OnDiscontinuity) && (v1OnDiscontinuity || v2OnDiscontinuity))
                    {
                        if (v1OnDiscontinuity && !HasConnection(uv0, uv1))
                        {
                            AddUnorderedPair(uniqueConnectionsVertexBorders, uv0, uv1);
                            fixedAny = true;
                        }
                            
                        if (v2OnDiscontinuity && !HasConnection(uv0, uv2))
                        {
                            AddUnorderedPair(uniqueConnectionsVertexBorders, uv0, uv2);
                            fixedAny = true;
                        }
                    }
                    else if ((v1 == idx && v1OnDiscontinuity) && (v0OnDiscontinuity || v2OnDiscontinuity))
                    {
                        if (v0OnDiscontinuity && !HasConnection(uv1, uv0))
                        {
                            AddUnorderedPair(uniqueConnectionsVertexBorders, uv1, uv0);
                            fixedAny = true;
                        }
                            
                        if (v2OnDiscontinuity && !HasConnection(uv1, uv2))
                        {
                            AddUnorderedPair(uniqueConnectionsVertexBorders, uv1, uv2);
                            fixedAny = true;
                        }
                    }
                    else if ((v2 == idx && v2OnDiscontinuity) && (v0OnDiscontinuity || v1OnDiscontinuity))
                    {
                        if (v0OnDiscontinuity && !HasConnection(uv2, uv0))
                        {
                            AddUnorderedPair(uniqueConnectionsVertexBorders, uv2, uv0);
                            fixedAny = true;
                        }
                            
                        if (v1OnDiscontinuity && !HasConnection(uv2, uv1))
                        {
                            AddUnorderedPair(uniqueConnectionsVertexBorders, uv2, uv1);
                            fixedAny = true;
                        }
                    }
                }
            }
        }
        
        return fixedAny;
    }
    
    /// <summary>
    /// Strategy 2: Connect endpoints directly if they're close enough
    /// </summary>
    private void FixEndpointsByDirectConnection(List<Vector2> endpoints)
    {
        if (endpoints.Count < 2)
            return;
            
        // Try to connect endpoints that are close to each other
        float maxDistance = 0.1f; // Maximum UV space distance to consider connecting
        
        // Sort endpoints by position for better pairing
        endpoints.Sort((a, b) => a.x.CompareTo(b.x));
        
        for (int i = 0; i < endpoints.Count; ++i)
        {
            Vector2 ep1 = endpoints[i];
            float closestDist = float.MaxValue;
            int closestIdx = -1;
            
            // Find the closest other endpoint
            for (int j = 0; j < endpoints.Count; ++j)
            {
                if (i == j) continue;
                
                Vector2 ep2 = endpoints[j];
                float dist = Vector2.Distance(ep1, ep2);
                
                if (dist < closestDist && dist < maxDistance)
                {
                    closestDist = dist;
                    closestIdx = j;
                }
            }
            
            // If found a close enough endpoint, connect them
            if (closestIdx != -1)
            {
                Vector2 ep2 = endpoints[closestIdx];
                if (!HasConnection(ep1, ep2))
                {
                    AddUnorderedPair(uniqueConnectionsVertexBorders, ep1, ep2);
                }
            }
        }
    }
    
    /// <summary>
    /// Strategy 3: More aggressive approach to find likely connections
    /// </summary>
    private void FixEndpointsByFindingMostLikelyConnections(List<Vector2> endpoints, Dictionary<Vector2, int> uvConnectCount)
    {
        // Get all discontinuity UVs (including those with 2+ connections)
        HashSet<Vector2> allDiscontinuityUVs = new HashSet<Vector2>();
        foreach (var entry in vertexUVsDiscontinuities)
        {
            foreach (var uv in entry.Value)
            {
                allDiscontinuityUVs.Add(uv);
            }
        }
        
        // Maximum distance in UV space to consider for connection (UV coordinates are normalized 0-1)
        float maxUVDistance = 0.2f; 
        int connectionsAdded = 0;
        
        // For each endpoint, find most likely connection based on UV space proximity
        foreach (Vector2 endpoint in endpoints)
        {
            // Skip if this endpoint no longer exists or already has 2+ connections
            if (!uvConnectCount.ContainsKey(endpoint) || uvConnectCount[endpoint] >= 2)
                continue;
                
            Vector2 bestTarget = Vector2.zero;
            float bestDistance = float.MaxValue;
            
            // Consider all other discontinuity UVs as potential connections
            foreach (Vector2 targetUV in allDiscontinuityUVs)
            {
                // Skip self
                if (targetUV == endpoint)
                    continue;
                    
                // Skip if already connected
                if (HasConnection(endpoint, targetUV))
                    continue;
                    
                // Calculate distance in UV space
                float distance = Vector2.Distance(endpoint, targetUV);
                
                // Only consider UVs within the maximum distance
                if (distance <= maxUVDistance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTarget = targetUV;
                }
            }
            
            // Only connect if we found a target within max distance
            if (bestDistance < maxUVDistance)
            {
                AddUnorderedPair(uniqueConnectionsVertexBorders, endpoint, bestTarget);
                ++connectionsAdded;
            }
        }
    }

    /// <summary>
    /// Checks if there's a connection between two UV coordinates
    /// </summary>
    private bool HasConnection(Vector2 uv1, Vector2 uv2)
    {
        // Check if the connection exists in either direction
        return uniqueConnectionsVertexBorders.Contains((uv1, uv2)) || uniqueConnectionsVertexBorders.Contains((uv2, uv1));
    }

    /// <summary>
    /// Fills a triangle in the provided integer array with the given value
    /// </summary>
    private void FillTriangle(int[,] targetArray, int targetWidth, int targetHeight, int x0, int y0, int x1, int y1, int x2, int y2, int value)
    {
        int minX = Mathf.Clamp(Mathf.Min(x0, Mathf.Min(x1, x2)) - 1, 0, targetWidth - 1);
        int maxX = Mathf.Clamp(Mathf.Max(x0, Mathf.Max(x1, x2)) + 1, 0, targetWidth - 1);
        int minY = Mathf.Clamp(Mathf.Min(y0, Mathf.Min(y1, y2)) - 1, 0, targetHeight - 1);
        int maxY = Mathf.Clamp(Mathf.Max(y0, Mathf.Max(y1, y2)) + 1, 0, targetHeight - 1);

        Vector2 v0 = new(x0, y0);
        Vector2 v1 = new(x1, y1);
        Vector2 v2 = new(x2, y2);

        for (int y = minY; y <= maxY; ++y)
        {
            for (int x = minX; x <= maxX; ++x)
            {
                Vector2 p = new(x + 0.5f, y + 0.5f);
                if (PointInTriangle(p, v0, v1, v2))
                {
                    targetArray[x, y] = value;
                }
            }
        }
    }

    /// <summary>
    /// Checks if a point is inside a triangle
    /// </summary>
    private bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float edge0 = (p.x - a.x) * (b.y - a.y) - (p.y - a.y) * (b.x - a.x);
        float edge1 = (p.x - b.x) * (c.y - b.y) - (p.y - b.y) * (c.x - b.x);
        float edge2 = (p.x - c.x) * (a.y - c.y) - (p.y - c.y) * (a.x - c.x);

        return (edge0 >= 0 && edge1 >= 0 && edge2 >= 0) || (edge0 <= 0 && edge1 <= 0 && edge2 <= 0);
    }

    /// <summary>
    /// Draws a line between two points using Bresenham's algorithm
    /// Maintains exactly 1 pixel thickness while ensuring no gaps
    /// </summary>
    private void DrawBresenhamLine(bool[,] texture, int x0, int y0, int x1, int y1, int width, int height, List<Vector2Int> pixelsList)
    {
        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;
        
        while (true)
        {
            // Check bounds before setting the pixel
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                texture[x0, y0] = true;
                pixelsList.Add(new Vector2Int(x0, y0));
            }
            
            if (x0 == x1 && y0 == y1) break;
            
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    #endregion

    #region Debugging

    public Color[] VisualizeZones()
    {
        // Generate unique random color per zone
        Color[] zoneColors = new Color[zonesCount];
        System.Random rand = new System.Random();
        for (int i = 0; i < zonesCount; ++i)
        {
            zoneColors[i] = new Color(
                (float)rand.NextDouble(),
                (float)rand.NextDouble(),
                (float)rand.NextDouble()
            );
        }

        // Create the color array for the texture
        int width = zoneMap.GetLength(0);
        int height = zoneMap.GetLength(1);
        Color[] textureColors = new Color[width * height];

        for (int x = 0; x < width; ++x)
        {
            for (int y = 0; y < height; ++y)
            {
                var zone = zoneMap[x, y];
                Color finalColor = Color.white; // Default for unassigned pixels
                if (zone >= 0 && zone < zoneColors.Length)
                {
                    finalColor = zoneColors[zone];
                }
                textureColors[x + y * width] = finalColor;
            }
        }
        return textureColors;
    }

    #endregion
} 