using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;


// References:
// https://github.com/mattatz/unity-volume-sampler
// https://github.com/mattatz/unity-voxel

public static class VolumeSampler
{
    public static void Position(ref NativeArray<InstrumentSamplePoint> output, List<InstrumentSamplePoint> originalPoints, Vector3 position, Quaternion rotation)
    {
        for (int i = 0; i < originalPoints.Count; ++i)
        {
            var temp = originalPoints[i];
            temp.point = rotation * originalPoints[i].point + position;
            output[i] = temp;
        }
    }

    public static void AddOriginOffset(ref List<InstrumentSamplePoint> points, Vector3 originOffset)
    {
        for (int i = 0; i < points.Count; ++i)
        {
            var temp = points[i];
            temp.point += originOffset;
            points[i] = temp;
        }
    }

    public static List<InstrumentSamplePoint> Sample(Mesh mesh, Vector3 sizeMultiplier = default, int resolution = 256)
    {
        if (sizeMultiplier == default)
        {
            sizeMultiplier = Vector3.one;
        }

        int width, height, depth;
        float unit;
        var grids = Voxelize(mesh, resolution, out width, out height, out depth, out unit);

        var min = mesh.bounds.min;
        var size = mesh.bounds.size;

        var points = new List<Vector3>();
        for (int z = 0; z < depth; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (grids[x, y, z].fill)
                    {
                        points.Add(grids[x, y, z].position);
                    }
                }
            }
        }

        // Extract mesh data on the main thread (not accessible from other threads)
        var triangles = GetTriangles(mesh).ToList();

        points = FilterPointsInsideMesh(points, triangles);
        points = ToLocalCoordinates(points, min, size);
        points = SizeMultiplier(points, sizeMultiplier);

        List<InstrumentSamplePoint> samplePoints = new();
        foreach (var p in points)
        {
            samplePoints.Add(new InstrumentSamplePoint(p, 0));
        }
        return samplePoints;
    }

    public static List<InstrumentSamplePoint> SampleVolumeInsideCone(int raysPerPixel, float coneHeight, float coneAngle, Quaternion initalRotation = default)
    {
        List<InstrumentSamplePoint> points = new();

        float tanTheta = Mathf.Tan(Mathf.Deg2Rad * coneAngle);
        for (int i = 0; i < raysPerPixel; ++i)
        {
            float h = coneHeight * Mathf.Pow(Random.value, 1f / 3f);
            float r = h * tanTheta * Mathf.Sqrt(Random.value);
            float t = 2 * Mathf.PI * Random.value;

            float x = r * Mathf.Cos(t);
            float y = h;
            float z = r * Mathf.Sin(t);

            points.Add(new InstrumentSamplePoint(initalRotation * new Vector3(x, y, z), 0));
        }

        return points;
    }

    public static List<InstrumentSamplePoint> RotateAndAddVolumeSample(List<InstrumentSamplePoint> samplePoints, int rotations, float maxRotationAngle, Bounds objectBounds, Vector3 objectOriginOffset)
    {
        List<InstrumentSamplePoint> points = new();
        Vector3 objectOrigin = new(objectBounds.extents.x, objectBounds.extents.y, objectBounds.size.z); // not true if tip not centered
        float phi = (1 + Mathf.Sqrt(5)) / 2; // golden ratio

        for (int i = 0; i < rotations; ++i)
        {
            // Using Spherical Fibonacci Lattice to distribute points on a sphere
            float x = i * phi;
            float y = (Mathf.Cos(0.5f * maxRotationAngle * Mathf.PI / 180.0f) * -0.5f + 0.5f) * i / rotations;
            float lambda = 2 * Mathf.PI * x;
            float phiAngle = Mathf.Acos(2 * y - 1) - Mathf.PI / 2;

            // add -90 degrees rotation to align back to the object
            Quaternion rotation = Quaternion.Euler(-90, 0, 0) * Quaternion.Euler(phiAngle * Mathf.Rad2Deg, lambda * Mathf.Rad2Deg, 0);

            for (int j = 0; j < samplePoints.Count; ++j)
            {
                Vector3 rotatedPoint = rotation * (samplePoints[j].point - objectOriginOffset - objectOrigin) + objectOrigin + objectOriginOffset;
                points.Add(new InstrumentSamplePoint(rotatedPoint, i));
            }
        }

        return points;
    }

    public static List<InstrumentSamplePoint> ReduceSampledPoints(List<InstrumentSamplePoint> points, int targetCount)
    {
        if (points.Count <= targetCount)
        {
            return points;
        }

        var reducedPoints = new List<InstrumentSamplePoint>();
        float step = (float)points.Count / targetCount;
        for (float i = 0; i < points.Count; i += step)
        {
            reducedPoints.Add(points[(int)i]);
        }

        return reducedPoints;
    }

    private static List<Vector3> FilterPointsInsideMesh(List<Vector3> points, List<Triangle> triangles)
    {
        var filteredPoints = new List<Vector3>();
        var tasks = new List<Task>();

        int partitionSize = points.Count / System.Environment.ProcessorCount;
        var partitions = Partitioner.Create(0, points.Count, partitionSize);

        foreach (var range in partitions.GetDynamicPartitions())
        {
            tasks.Add(Task.Run(() =>
            {
                var localFilteredPoints = new List<Vector3>();
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    if (IsPointInsideMesh(points[i], triangles))
                    {
                        localFilteredPoints.Add(points[i]);
                    }
                }

                lock (filteredPoints)
                {
                    filteredPoints.AddRange(localFilteredPoints);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        return filteredPoints;
    }

    private static bool IsPointInsideMesh(Vector3 point, List<Triangle> triangles)
    {
        int hitCount = 0;
        Ray ray = new Ray(point, Vector3.up);
        RaycastHit hit;

        foreach (var triangle in triangles)
        {
            if (triangle.IntersectRay(ray, out hit))
            {
                ++hitCount;
            }
        }

        return hitCount % 2 != 0;
    }

    private static IEnumerable<Triangle> GetTriangles(Mesh mesh)
    {
        var vertices = mesh.vertices;
        var indices = mesh.triangles;

        for (int i = 0; i < indices.Length; i += 3)
        {
            yield return new Triangle(vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]]);
        }
    }

    private static List<Vector3> SizeMultiplier(List<Vector3> points, Vector3 sizeMultiplier)
    {
        var multipliedPoints = new List<Vector3>();
        foreach (var p in points)
        {
            var x = p.x * sizeMultiplier.x;
            var y = p.y * sizeMultiplier.y;
            var z = p.z * sizeMultiplier.z;
            multipliedPoints.Add(new Vector3(x, y, z));
        }
        return multipliedPoints;
    }

    private static List<Vector3> ToLocalCoordinates(List<Vector3> points, Vector3 min, Vector3 size)
    {
        var localPoints = new List<Vector3>();
        foreach (var p in points)
        {
            var x = (p.x - min.x) / size.x;
            var y = (p.y - min.y) / size.y;
            var z = (p.z - min.z) / size.z;
            localPoints.Add(new Vector3(x, y, z));
        }
        return localPoints;
    }

    #region Voxelizer

    private static Grid[,,] Voxelize(Mesh mesh, int resolution, out int width, out int height, out int depth, out float unit)
    {
        mesh.RecalculateBounds();

        var bounds = mesh.bounds;
        float maxLength = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        unit = maxLength / resolution;
        var hunit = unit * 0.5f;

        var start = bounds.min - new Vector3(hunit, hunit, hunit);
        var end = bounds.max + new Vector3(hunit, hunit, hunit);
        var size = end - start;

        width = Mathf.CeilToInt(size.x / unit);
        height = Mathf.CeilToInt(size.y / unit);
        depth = Mathf.CeilToInt(size.z / unit);

        var volume = new Grid[width, height, depth];
        var boxes = new Bounds[width, height, depth];
        var voxelSize = Vector3.one * unit;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = 0; z < depth; z++)
                {
                    var p = new Vector3(x, y, z) * unit + start;
                    var aabb = new Bounds(p, voxelSize);
                    boxes[x, y, z] = aabb;
                }
            }
        }

        // build triangles
        var vertices = mesh.vertices;
        var indices = mesh.triangles;
        var direction = Vector3.forward;

        for (int i = 0, n = indices.Length; i < n; i += 3)
        {
            var tri = new Triangle(
                vertices[indices[i]],
                vertices[indices[i + 1]],
                vertices[indices[i + 2]],
                direction
            );

            var min = tri.bounds.min - start;
            var max = tri.bounds.max - start;
            int iminX = Mathf.RoundToInt(min.x / unit), iminY = Mathf.RoundToInt(min.y / unit), iminZ = Mathf.RoundToInt(min.z / unit);
            int imaxX = Mathf.RoundToInt(max.x / unit), imaxY = Mathf.RoundToInt(max.y / unit), imaxZ = Mathf.RoundToInt(max.z / unit);
            
            iminX = Mathf.Clamp(iminX, 0, width - 1);
            iminY = Mathf.Clamp(iminY, 0, height - 1);
            iminZ = Mathf.Clamp(iminZ, 0, depth - 1);
            imaxX = Mathf.Clamp(imaxX, 0, width - 1);
            imaxY = Mathf.Clamp(imaxY, 0, height - 1);
            imaxZ = Mathf.Clamp(imaxZ, 0, depth - 1);

            var front = tri.frontFacing;

            for (int x = iminX; x <= imaxX; x++)
            {
                for (int y = iminY; y <= imaxY; y++)
                {
                    for (int z = iminZ; z <= imaxZ; z++)
                    {
                        if (Intersects(tri, boxes[x, y, z]))
                        {
                            var voxel = volume[x, y, z];
                            voxel.position = boxes[x, y, z].center;
                            if (!voxel.fill)
                            {
                                voxel.front = front;
                            }
                            else
                            {
                                voxel.front = voxel.front || front;
                            }
                            voxel.fill = true;
                            volume[x, y, z] = voxel;
                        }
                    }
                }
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int z = 0; z < depth; z++)
                {
                    if (volume[x, y, z].IsEmpty()) continue;

                    int ifront = z;

                    for (; ifront < depth; ifront++)
                    {
                        if (!volume[x, y, ifront].IsFrontFace())
                        {
                            break;
                        }
                    }

                    if (ifront >= depth) break;

                    var iback = ifront;

                    // step forward to cavity
                    for (; iback < depth && volume[x, y, iback].IsEmpty(); iback++) { }

                    if (iback >= depth) break;

                    // check if iback is back voxel
                    if (volume[x, y, iback].IsBackFace())
                    {
                        // step forward to back face
                        for (; iback < depth && volume[x, y, iback].IsBackFace(); iback++) { }
                    }

                    // fill from ifront to iback
                    for (int z2 = ifront; z2 < iback; z2++)
                    {
                        var p = boxes[x, y, z2].center;
                        var voxel = volume[x, y, z2];
                        voxel.position = p;
                        voxel.fill = true;
                        volume[x, y, z2] = voxel;
                    }

                    z = iback;
                }
            }
        }

        return volume;
    }

    private static bool Intersects(Triangle tri, Bounds aabb)
    {
        float p0, p1, p2, r;

        Vector3 center = aabb.center, extents = aabb.max - center;

        Vector3 v0 = tri.a - center,
            v1 = tri.b - center,
            v2 = tri.c - center;

        Vector3 f0 = v1 - v0,
            f1 = v2 - v1,
            f2 = v0 - v2;

        Vector3 a00 = new Vector3(0, -f0.z, f0.y),
            a01 = new Vector3(0, -f1.z, f1.y),
            a02 = new Vector3(0, -f2.z, f2.y),
            a10 = new Vector3(f0.z, 0, -f0.x),
            a11 = new Vector3(f1.z, 0, -f1.x),
            a12 = new Vector3(f2.z, 0, -f2.x),
            a20 = new Vector3(-f0.y, f0.x, 0),
            a21 = new Vector3(-f1.y, f1.x, 0),
            a22 = new Vector3(-f2.y, f2.x, 0);

        // Test axis a00
        p0 = Vector3.Dot(v0, a00);
        p1 = Vector3.Dot(v1, a00);
        p2 = Vector3.Dot(v2, a00);
        r = extents.y * Mathf.Abs(f0.z) + extents.z * Mathf.Abs(f0.y);

        if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a01
        p0 = Vector3.Dot(v0, a01);
        p1 = Vector3.Dot(v1, a01);
        p2 = Vector3.Dot(v2, a01);
        r = extents.y * Mathf.Abs(f1.z) + extents.z * Mathf.Abs(f1.y);

        if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a02
        p0 = Vector3.Dot(v0, a02);
        p1 = Vector3.Dot(v1, a02);
        p2 = Vector3.Dot(v2, a02);
        r = extents.y * Mathf.Abs(f2.z) + extents.z * Mathf.Abs(f2.y);

        if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a10
        p0 = Vector3.Dot(v0, a10);
        p1 = Vector3.Dot(v1, a10);
        p2 = Vector3.Dot(v2, a10);
        r = extents.x * Mathf.Abs(f0.z) + extents.z * Mathf.Abs(f0.x);
        if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a11
        p0 = Vector3.Dot(v0, a11);
        p1 = Vector3.Dot(v1, a11);
        p2 = Vector3.Dot(v2, a11);
        r = extents.x * Mathf.Abs(f1.z) + extents.z * Mathf.Abs(f1.x);

        if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a12
        p0 = Vector3.Dot(v0, a12);
        p1 = Vector3.Dot(v1, a12);
        p2 = Vector3.Dot(v2, a12);
        r = extents.x * Mathf.Abs(f2.z) + extents.z * Mathf.Abs(f2.x);

        if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a20
        p0 = Vector3.Dot(v0, a20);
        p1 = Vector3.Dot(v1, a20);
        p2 = Vector3.Dot(v2, a20);
        r = extents.x * Mathf.Abs(f0.y) + extents.y * Mathf.Abs(f0.x);

        if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a21
        p0 = Vector3.Dot(v0, a21);
        p1 = Vector3.Dot(v1, a21);
        p2 = Vector3.Dot(v2, a21);
        r = extents.x * Mathf.Abs(f1.y) + extents.y * Mathf.Abs(f1.x);

        if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
        {
            return false;
        }

        // Test axis a22
        p0 = Vector3.Dot(v0, a22);
        p1 = Vector3.Dot(v1, a22);
        p2 = Vector3.Dot(v2, a22);
        r = extents.x * Mathf.Abs(f2.y) + extents.y * Mathf.Abs(f2.x);

        if (Mathf.Max(-Mathf.Max(p0, p1, p2), Mathf.Min(p0, p1, p2)) > r)
        {
            return false;
        }

        if (Mathf.Max(v0.x, v1.x, v2.x) < -extents.x || Mathf.Min(v0.x, v1.x, v2.x) > extents.x)
        {
            return false;
        }

        if (Mathf.Max(v0.y, v1.y, v2.y) < -extents.y || Mathf.Min(v0.y, v1.y, v2.y) > extents.y)
        {
            return false;
        }

        if (Mathf.Max(v0.z, v1.z, v2.z) < -extents.z || Mathf.Min(v0.z, v1.z, v2.z) > extents.z)
        {
            return false;
        }

        var normal = Vector3.Cross(f1, f0).normalized;
        var pl = new Plane(normal, Vector3.Dot(normal, tri.a));
        return Intersects(pl, aabb);
    }

    private static bool Intersects(Plane pl, Bounds aabb)
    {
        Vector3 center = aabb.center;
        var extents = aabb.max - center;

        var r = extents.x * Mathf.Abs(pl.normal.x) + extents.y * Mathf.Abs(pl.normal.y) + extents.z * Mathf.Abs(pl.normal.z);
        var s = Vector3.Dot(pl.normal, center) - pl.distance;

        return Mathf.Abs(s) <= r;
    }
    #endregion

    #region Classes

    private class Triangle
    {
        public Vector3 a, b, c;
        public Bounds bounds;
        public bool frontFacing;

        public Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 dir)
        {
            this.a = a;
            this.b = b;
            this.c = c;

            var cross = Vector3.Cross(b - a, c - a);
            this.frontFacing = (Vector3.Dot(cross, dir) <= 0f);

            var min = Vector3.Min(Vector3.Min(a, b), c);
            var max = Vector3.Max(Vector3.Max(a, b), c);
            bounds.SetMinMax(min, max);
        }

        public Triangle(Vector3 a, Vector3 b, Vector3 c)
        {
            this.a = a;
            this.b = b;
            this.c = c;

            var min = Vector3.Min(Vector3.Min(a, b), c);
            var max = Vector3.Max(Vector3.Max(a, b), c);
            bounds.SetMinMax(min, max);
        }

        public bool IntersectRay(Ray ray, out RaycastHit hit)
        {
            hit = new RaycastHit();
            Vector3 edge1 = b - this.a;
            Vector3 edge2 = c - this.a;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float a = Vector3.Dot(edge1, h);

            if (a > -0.00001f && a < 0.00001f)
                return false;

            float f = 1.0f / a;
            Vector3 s = ray.origin - this.a;
            float u = f * Vector3.Dot(s, h);

            if (u < 0.0f || u > 1.0f)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = f * Vector3.Dot(ray.direction, q);

            if (v < 0.0f || u + v > 1.0f)
                return false;

            float t = f * Vector3.Dot(edge2, q);

            if (t > 0.00001f)
            {
                hit.point = ray.origin + ray.direction * t;
                return true;
            }
            else
                return false;
        }
    }

    private struct Grid
    {
        public Vector3 position;
        public bool fill, front;

        public Vector3 sample;
        public bool found;

        public bool IsFrontFace()
        {
            return fill && front;
        }

        public bool IsBackFace()
        {
            return fill && !front;
        }

        public bool IsEmpty()
        {
            return !fill;
        }
    }

    #endregion

}