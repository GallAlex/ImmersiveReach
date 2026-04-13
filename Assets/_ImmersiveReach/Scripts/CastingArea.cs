using System.Collections.Generic;
using UnityEngine;

public class CastingArea
{
    public CastingArea()
    {

    }

    public CastingArea(CastingAreaMethod castingAreaMethod, bool debug)
    {
        this.castingAreaMethod = castingAreaMethod;
        if(debug)
        {
            switch(this.castingAreaMethod){
                case CastingAreaMethod.OldVersionPlane:
                    planeSupport = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    planeSupport.GetComponent<Renderer>().enabled = true; // display/hide the plane
                    planeSupport.GetComponent<MeshCollider>().enabled = false; // disable collisions
                    break;

                case CastingAreaMethod.DynamicPlane:
                    planeSupport = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    planeSupport.GetComponent<Renderer>().enabled = true; // display/hide the plane
                    planeSupport.GetComponent<MeshCollider>().enabled = false; // disable collisions
                    break;

                default:
                    break;
            }
        }
    }

    public void Update(Vector3 rayStartPosition, Bounds objectTestedBounds)
    {
        isCastingSurfaceSpherical = false; // rays are casted on a plane by default

        Vector3 objectCenter = objectTestedBounds.center;

        // Set the plane perpendicular to the ray direction and passing through the object center
        Vector3 normal = rayStartPosition - objectCenter; // y Axis, not normalized
        Vector3 xAxisNormalized = Vector3.Cross(normal, Vector3.up).normalized;
        if (xAxisNormalized.sqrMagnitude == 0)
        {
            // normal and up are collinear -> set local xAxis to right by default
            xAxisNormalized = Vector3.right;
        }
        Vector3 zAxisNormalized = Vector3.Cross(xAxisNormalized, normal).normalized;

        // local 2D coordinates of the launchPlane : in 3D world coords, origin is objectCenter and axises are xAxis / zAxis
        Vector2 relative2DMin = new(0.0f,0.0f); // min local coordinates of the launchPlane
        Vector2 relative2DMax = new(0.0f,0.0f); // max local coordinates of the launchPlane

        switch(castingAreaMethod){
            case CastingAreaMethod.OldVersionPlane:
                // Old version of launchPlane
                Vector3 objectSize = objectTestedBounds.size;
                float sphereRadius = objectSize.magnitude / 2;
                float d = normal.magnitude;
                d = d < sphereRadius ? sphereRadius + 0.1f : d; // to avoid crash when the ray start is inside the object sphere
                float l =  2 * d * Mathf.Tan(Mathf.Asin(sphereRadius / d));
                float maxDimension = Mathf.Max(objectSize.x, objectSize.y);
                
                relative2DMin = new Vector2(-l/2 * objectSize.x/maxDimension, -l/2 * objectSize.y/maxDimension);
                relative2DMax = new Vector2(l/2 * objectSize.x/maxDimension, l/2 * objectSize.y/maxDimension);
                break;

            case CastingAreaMethod.DynamicPlane:
                // Dynamic launchPlane: Crop to the bounding box of the object from the point of view of the tool (rayStartPosition)

                // 1. List the 8 bounding points of the object, in 3D world coordinates
                List<Vector3> boundPoints = new();
                Vector3 boundPointMin = objectTestedBounds.min;
                Vector3 boundPointMax = objectTestedBounds.max;
                boundPoints.Add(boundPointMin);
                boundPoints.Add(boundPointMax);
                boundPoints.Add( new Vector3(boundPointMin.x, boundPointMin.y, boundPointMax.z));
                boundPoints.Add( new Vector3(boundPointMin.x, boundPointMax.y, boundPointMin.z));
                boundPoints.Add( new Vector3(boundPointMax.x, boundPointMin.y, boundPointMin.z));
                boundPoints.Add( new Vector3(boundPointMin.x, boundPointMax.y, boundPointMax.z));
                boundPoints.Add( new Vector3(boundPointMax.x, boundPointMin.y, boundPointMax.z));
                boundPoints.Add( new Vector3(boundPointMax.x, boundPointMax.y, boundPointMin.z));

                // 2. Iterate through each of these points to compute the "2D bounding box", described by a min and a max point
                foreach (Vector3 boundPoint in boundPoints)
                {
                    // 2.1. Cast/Project the point onto the plane, in the direction of the tool
                    Vector3 sourceToBound = boundPoint-rayStartPosition;
                    float rayCastDenominator = Vector3.Dot(-normal, sourceToBound);

                    // 2.2. Check if the point is facing the tool or behind the tool
                    if (rayCastDenominator <= 0)
                    {
                        // 2.3. Deal with the case where the tool is inside the bounding box of the object
                        // Send rays on a sphere centered in rayStartPostion and oriented towards the object
                        this.isCastingSurfaceSpherical = true;
                        this.sphereCenter = rayStartPosition;
                        this.sphereNorthAxis = -normal.normalized;
                        this.sphereRadius = 2*normal.magnitude;
                        // Vector2 furthestBoundFromCenter2D = new Vector2(Mathf.Max(relative2DMax.x, -relative2DMin.x), Mathf.Max(relative2DMax.y, -relative2DMin.y));
                        // Vector3 furthestBoundFromCenter3D = objectCenter + furthestBoundFromCenter2D.x * xAxisNormalized + furthestBoundFromCenter2D.y * zAxisNormalized;
                        // this.sphereCapHalfAngle = Vector3.Angle(this.origin - rayStartPosition, -normal);
                        this.sphereCapHalfAngle = 180.0f;
                        break;
                    }
                    else
                    {
                        
                        Vector3 rayCast = rayStartPosition + (normal.sqrMagnitude/rayCastDenominator) * sourceToBound;
                        Vector3 relative3DPosition = rayCast - objectCenter;

                        // 2.4. Convert the projected point on the plane to 2D local coordinates system
                        Vector2 relative2DPosition = new(Vector3.Dot(relative3DPosition, xAxisNormalized), Vector3.Dot(relative3DPosition, zAxisNormalized));


                        // 2.5. Update the min / max point for the "2D bounding box"
                        if (relative2DPosition.x < relative2DMin.x)
                        {
                            relative2DMin.x = relative2DPosition.x;
                        }
                        else if (relative2DPosition.x > relative2DMax.x)
                        {
                            relative2DMax.x = relative2DPosition.x;
                        }

                        if (relative2DPosition.y < relative2DMin.y)
                        {
                            relative2DMin.y = relative2DPosition.y;
                        }
                        else if (relative2DPosition.y > relative2DMax.y)
                        {
                            relative2DMax.y = relative2DPosition.y;
                        }
                    }
                }
                break;

            default:
                break;
        }

        // Convert 2D local coordinates to 3D world coordinates
        origin = objectCenter + relative2DMin.x * xAxisNormalized + relative2DMin.y * zAxisNormalized;
        xAxis = (relative2DMax.x - relative2DMin.x) * xAxisNormalized;
        zAxis = (relative2DMax.y - relative2DMin.y) * zAxisNormalized;

        // Used by Optix and for debugging, but useless for the Unity release version
        Vector3 planeCenter = origin + xAxis/2 + zAxis / 2;
        Quaternion planeRotation = Quaternion.LookRotation(zAxisNormalized, normal);
        Vector3 planeScale = new((relative2DMax.x - relative2DMin.x) / 10.0f, 0, (relative2DMax.y - relative2DMin.y) / 10.0f);  // Plane size is 10x10 by default
        UpdatePlaneSupport(planeCenter, planeRotation, planeScale);
    }

    private void UpdatePlaneSupport(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        if (planeSupport)
        {
            planeSupport.transform.SetPositionAndRotation(position, rotation);
            planeSupport.transform.localScale = scale;
        }

        // duplicate of the Transform so that it works outside Debugging version
        this.position = position;
        this.rotation = rotation;
        this.scale = scale;
    }

    public CastingAreaMethod castingAreaMethod;
    public bool isCastingSurfaceSpherical;

    public Vector3 origin;
    public Vector3 xAxis;
    public Vector3 zAxis;

    // for spherical cap cast
    public Vector3 sphereCenter;
    public Vector3 sphereNorthAxis;
    public float sphereRadius;
    public float sphereCapHalfAngle;

    // used only by Optix
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;

    // for debugging purposes only
    public GameObject planeSupport;
}