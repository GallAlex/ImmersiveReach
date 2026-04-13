using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// This class is used to store information about the object that is being raycasted
// It is used to store the object's bounds, points, colliderId, height, width, and depth
public class RaycastObject
{
    public GameObject gameObject;
    public Bounds objectBounds;
    public int colliderId;
    public int widthResolution;
    public int heightResolution;
    public int depthResolution;
    public bool isObjectStudied = false;
    public int textureWidth;
    public int textureHeight;

    private Texture2D texture;
    private List<int> objectPoints;
    private Color initialMaterialColor;
    private Texture2D initialTexture;
    private readonly MeshCollider meshCollider;
    private bool isTextureSet = false;
    private int scaleFactor = 6;
    private float[,] hitGrid;
    private Pixel[,] textureData;
    private UVDiscontinuityHelper discontinuityHelper;

    public RaycastObject(GameObject gameObject, int ppu, bool isObjectStudied, int scaleFactor)
    {
        this.gameObject = gameObject;
        objectBounds = ObjectHelper.GetObjectBounds(this.gameObject);
        Vector3 objectSize = objectBounds.size;
        widthResolution = Mathf.Min(Mathf.CeilToInt(objectSize.x * ppu), 1000); // Limit resolution to 1000 to avoid memory issues
        heightResolution = Mathf.Min(Mathf.CeilToInt(objectSize.y * ppu), 1000);
        depthResolution = Mathf.Min(Mathf.CeilToInt(objectSize.z * ppu), 1000);
        objectPoints = ObjectHelper.ObjectPoints(this.gameObject, objectBounds, widthResolution, heightResolution, depthResolution);
        colliderId = this.gameObject.GetComponent<Collider>().GetInstanceID();
        initialMaterialColor = this.gameObject.GetComponent<Renderer>().material.color;
        initialTexture = (Texture2D)this.gameObject.GetComponent<Renderer>().material.mainTexture;
        this.scaleFactor = scaleFactor;
        textureWidth = scaleFactor * widthResolution;
        textureHeight = scaleFactor * heightResolution;
        texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Mirror;
        this.isObjectStudied = isObjectStudied;
        meshCollider = this.gameObject.GetComponent<MeshCollider>();
        if (isObjectStudied){
            // should do this for every object (in case colorEveryObject is set to true)
            // but doing only this one for debugging purposes
            discontinuityHelper = new UVDiscontinuityHelper(gameObject, widthResolution, heightResolution);
            discontinuityHelper.ComputeDiscontinuityBorders(textureWidth, textureHeight);
            discontinuityHelper.ComputeContinuityZonesTexture(textureWidth, textureHeight);
        }
    }

    public List<int> GetObjectPoints()
    {
        return objectPoints;
    }

    public void SetTexture(Color[] textureColors)
    {
        if (textureColors == null)
        {
            return;
        }
        if (!isTextureSet)
        {
            gameObject.GetComponent<Renderer>().material.mainTexture = texture;
            gameObject.GetComponent<Renderer>().material.color = Color.white;
            isTextureSet = true;
        }
        texture.SetPixels(textureColors);
        texture.Apply();
    }

    public void UpdateResolution(int ppu)
    {
        Vector3 objectSize = objectBounds.size;
        widthResolution = Mathf.Min(Mathf.CeilToInt(objectSize.x * ppu), 1000);
        heightResolution = Mathf.Min(Mathf.CeilToInt(objectSize.y * ppu), 1000);
        depthResolution = Mathf.Min(Mathf.CeilToInt(objectSize.z * ppu), 1000);
        objectPoints = ObjectHelper.ObjectPoints(gameObject, objectBounds, widthResolution, heightResolution, depthResolution);
        gameObject.GetComponent<Renderer>().material.mainTexture = new Texture2D(textureWidth, textureHeight);
        texture = (Texture2D)gameObject.GetComponent<Renderer>().material.mainTexture;
        if (isObjectStudied){
            discontinuityHelper.ComputeDiscontinuityBorders(textureWidth, textureHeight);
            discontinuityHelper.ComputeContinuityZonesTexture(textureWidth, textureHeight);
        }
    }

    public void ResetMaterialToInitial(bool applyOnObjectStudied = true)
    {
        if ((!applyOnObjectStudied && isObjectStudied) || !isTextureSet)
        {
            return;
        }
        gameObject.GetComponent<Renderer>().material.color = initialMaterialColor;
        gameObject.GetComponent<Renderer>().material.mainTexture = initialTexture;
        isTextureSet = false;
    }

    public MeshCollider GetMeshCollider()
    {
        return meshCollider;
    }

    public Color GetInitialColor()
    {
        return initialMaterialColor;
    }

    public int GetTextureScaleFactor()
    {
        return scaleFactor;
    }

    public void SetTextureScaleFactor(int scaleFactor, bool setTexture = true)
    {
        this.scaleFactor = scaleFactor;
        textureWidth = scaleFactor * widthResolution;
        textureHeight = scaleFactor * heightResolution;
        texture = new Texture2D(textureWidth, textureHeight);
        if (setTexture)
        {
            gameObject.GetComponent<Renderer>().material.mainTexture = texture;
        }
        if (isObjectStudied){
            discontinuityHelper.ComputeDiscontinuityBorders(textureWidth, textureHeight);
            discontinuityHelper.ComputeContinuityZonesTexture(textureWidth, textureHeight);
        }
    }

    public void SetHitGrid(float[,] hitGrid)
    {
        this.hitGrid = hitGrid;
    }

    public float[,] GetHitGrid()
    {
        return hitGrid;
    }

    public void SetTextureData(Pixel[,] textureData)
    {
        this.textureData = textureData;
    }

    public Pixel[,] GetTextureData()
    {
        return textureData;
    }

    public List<Vector2Int> GetPixelsOnDiscontinuityBordersScale1()
    {
        return discontinuityHelper.GetPixelsOnDiscontinuityBordersScale1();
    }

    public bool[,] GetDiscontinuityBorders()
    {
        return discontinuityHelper.GetDiscontinuityBorders();
    }

    public int[,] GetTextureZoneMap()
    {
        return discontinuityHelper.GetTextureZoneMap();
    }

    public int[,] GetTextureZoneMapScale1()
    {
        return discontinuityHelper.GetZoneMapScale1();
    }

    public Color[] VisualizeZones()
    {
        return discontinuityHelper.VisualizeZones();
    }
}