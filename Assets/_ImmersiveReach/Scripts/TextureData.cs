using System.Collections.Generic;
using UnityEngine;

/// Represents a single pixel in the texture with a value and isoline flag.
/// Used for generating isolines in the visualization.
public class Pixel {
    private int value;      // Represents the color index value
    private bool isIsoline; // Is the pixel part of an isoline

    public Pixel(int value, bool isIsoline) {
        this.value = value;
        this.isIsoline = isIsoline;
    }

    public Pixel() {
        this.value = 0;
        this.isIsoline = false;
    }

    public void SetData(int value, bool isIsoline) {
        this.value = value;
        this.isIsoline = isIsoline;
    }

    public void SetData(Pixel pixel) {
        this.value = pixel.GetValue();
        this.isIsoline = pixel.GetIsIsoline();
    }

    public void SetValue(int value) {
        this.value = value;
    }

    public int GetValue() {
        return value;
    }

    public void SetIsIsoline(bool isIsoline) {
        this.isIsoline = isIsoline;
    }

    public bool GetIsIsoline() {
        return isIsoline;
    }
}

/// Implements the Marching Squares algorithm to create a kernel 
/// that generates isolines between different intensity values.
public class Kernel {
    private int size;
    private Pixel[,] pixels;    // The pixel data within the kernel
    private int[] corners;      // Values at the four corners of the cell being processed
    private int[] sortedCorners; // Corner values sorted in ascending order

    public Kernel(int size) {
        this.size = size;
        this.pixels = new Pixel[size, size];
        this.corners = new int[4];
        this.sortedCorners = new int[4];

        for (int i = 0; i < size; ++i)
        {
            for (int j = 0; j < size; ++j)
            {
                pixels[i, j] = new Pixel();
            }
        }
    }

    /// Computes the kernel applying a mask for each threshold level.
    public void ComputeKernel() {
        int configIndex;
        int threshold = sortedCorners[0];
        ApplyMask0(threshold);  // Initial uniform fill with lowest value

        // Process each threshold level (sorted corner values)
        for (int layer = 1; layer<4; ++layer) {
            if (sortedCorners[layer] > threshold) {
                configIndex = 0;
                threshold = sortedCorners[layer];
                // Calculate configuration index based on which corners are above threshold
                for (int cornerId = 0; cornerId<4; ++cornerId) {
                    configIndex <<= 1;
                    if (corners[cornerId] >= threshold) {
                        ++configIndex;
                    }
                }
                ApplyMask(configIndex, threshold, sortedCorners[layer - 1]);
            }
        }
    }

    public void SetCorners(int tl, int tr, int br, int bl) {
        sortedCorners[0] = corners[0] = tl;
        sortedCorners[1] = corners[1] = tr;
        sortedCorners[2] = corners[2] = br;
        sortedCorners[3] = corners[3] = bl;

        // sort corner values
        if(sortedCorners[0] > sortedCorners[1]) (sortedCorners[0], sortedCorners[1]) = (sortedCorners[1], sortedCorners[0]);
        if(sortedCorners[2] > sortedCorners[3]) (sortedCorners[2], sortedCorners[3]) = (sortedCorners[3], sortedCorners[2]);
        if(sortedCorners[0] > sortedCorners[2]) (sortedCorners[0], sortedCorners[2]) = (sortedCorners[2], sortedCorners[0]);
        if(sortedCorners[1] > sortedCorners[3]) (sortedCorners[1], sortedCorners[3]) = (sortedCorners[3], sortedCorners[1]);
        if(sortedCorners[1] > sortedCorners[2]) (sortedCorners[1], sortedCorners[2]) = (sortedCorners[2], sortedCorners[1]);
    }

    public Pixel GetPixel(int x, int y) {
        return pixels[x, y];
    }

    /// Applies the appropriate mask based on the Marching Squares configuration index.
    /// Each configuration represents a different isoline pattern.
    private void ApplyMask(int configIndex, int upperValue, int lowerValue) {
        // 16 possible configurations in Marching Squares algorithm (0-15)
        switch (configIndex)
        {
        case 0:
            ApplyMask0(upperValue);
            break;
        case 1:
            ApplyMask1(upperValue, 1);
            break;
        case 2:
            ApplyMask1(upperValue, 0);
            break;
        case 3:
            ApplyMask4(upperValue, 2);
            break;
        case 4:
            ApplyMask1(upperValue, 3);
            break;
        case 5:
            ApplyMask3(upperValue, lowerValue, 0);
            break;
        case 6:
            ApplyMask4(upperValue, 1);
            break;
        case 7:
            ApplyMask2(upperValue, lowerValue, 0);
            break;
        case 8:
            ApplyMask1(upperValue, 2);
            break;
        case 9:
            ApplyMask4(upperValue, 3);
            break;
        case 10:
            ApplyMask3(upperValue, lowerValue, 1);
            break;
        case 11:
            ApplyMask2(upperValue, lowerValue, 1);
            break;
        case 12:
            ApplyMask4(upperValue, 0);
            break;
        case 13:
            ApplyMask2(upperValue, lowerValue, 2);
            break;
        case 14:
            ApplyMask2(upperValue, lowerValue, 3);
            break;
        case 15:
            ApplyMask0(upperValue);
            break;
        
        default:
            break;
        }
    }

    private void ApplyMaskWithRotation(Kernel mask, int rotation, int value) {
        switch (rotation)
        {
        case 0: // no rotation
            for (int x = 0; x<size; ++x) {
                for (int y = 0; y<size; ++y) {
                    if (mask.GetPixel(x, y).GetValue() == value || mask.GetPixel(x, y).GetIsIsoline()) {
                        pixels[x, y].SetData(mask.GetPixel(x, y));
                    }
                }
            }
            break;
        
        case 1: // rotate 90° clockwise
            for (int x = 0; x<size; ++x) {
                for (int y = 0; y<size; ++y) {
                    if (mask.GetPixel(x, y).GetValue() == value || mask.GetPixel(x, y).GetIsIsoline()) {
                        pixels[size-1-y, x].SetData(mask.GetPixel(x, y));
                    }
                }
            }
            break;

        case 2: // rotate 180° clockwise (= centered symetry)
            for (int x = 0; x<size; ++x) {
                for (int y = 0; y<size; ++y) {
                    if (mask.GetPixel(x, y).GetValue() == value || mask.GetPixel(x, y).GetIsIsoline()) {
                        pixels[size-1-x, size-1-y].SetData(mask.GetPixel(x, y));
                    }
                }
            }
            break;
        
        case 3: // rotate 270° clockwise
            for (int x = 0; x<size; ++x) {
                for (int y = 0; y<size; ++y) {
                    if (mask.GetPixel(x, y).GetValue() == value || mask.GetPixel(x, y).GetIsIsoline()) {
                        pixels[y, size-1-x].SetData(mask.GetPixel(x, y));
                    }
                }
            }
            break;
        
        default:
            break;
        }
    }

    // Various mask application methods for different isoline patterns:
    
    // Uniform mask
    private void ApplyMask0(int value) {
        for (int x = 0; x<size; ++x) {
            for (int y = 0; y<size; ++y) {
                pixels[x, y].SetData(value, false); // isoline set to false to reset the kernel
            }
        }
    }

    // Low value diagonal, default is bottom right position (config 2)
    private void ApplyMask1(int value, int rotation) {
        Kernel tempKernel = new(size);
        int d = size + size/2 -1;
        for (int x = size/2; x<size; ++x) {
            tempKernel.GetPixel(x, d-x).SetData(value, true);
        }
        for (int x = size-1; x>(size/2); --x) {
            for (int y = size-1; y>(size/2) & x+y>d; --y) {
                tempKernel.GetPixel(x, y).SetValue(value);
            }
        }

        ApplyMaskWithRotation(tempKernel, rotation, value);
    }

    // high value diagonal, default is top left position (config 7)
    private void ApplyMask2(int upperValue, int lowerValue, int rotation) {
        Kernel tempKernel = new(size);
        int d = size/2 -1;
        for (int x = 0; x<size/2; ++x) {
            tempKernel.GetPixel(x, d-x).SetData(upperValue, true);
        }
        for (int x = size-1; x>=0; --x) {
            for (int y = size-1; y>=0 & x+y>d; --y) {
                tempKernel.GetPixel(x, y).SetValue(upperValue);
            }
        }
        // force the corner to keep low value
        tempKernel.GetPixel(0, 0).SetValue(lowerValue);

        ApplyMaskWithRotation(tempKernel, rotation, upperValue);
    }

    // double diagonal, default is top left + bottom right positions (config 5)
    private void ApplyMask3(int upperValue, int lowerValue, int rotation) {
        Kernel tempKernel = new(size);
        int d1 = size/2 -1;
        int d2 = size + size/2 -1;
        for (int x = 0; x<size; ++x) {
            for (int y = 0; y<size; ++y) {
                if (x+y == d1 || x+y == d2) {
                    tempKernel.GetPixel(x, y).SetData(upperValue, true);
                }
                else if (x+y > d1 && x+y < d2) {
                    tempKernel.GetPixel(x, y).SetValue(upperValue);
                }
            }
        }
        // force the corners to keep low value
        tempKernel.GetPixel(0, 0).SetValue(lowerValue);
        tempKernel.GetPixel(size-1, size-1).SetValue(lowerValue);

        ApplyMaskWithRotation(tempKernel, rotation, upperValue);
    }

    // straight line, default is top position (config 12)
    private void ApplyMask4(int value, int rotation) {
        Kernel tempKernel = new(size);
        int d = size/2 -1;
        for (int x = 0; x<size; ++x) {
            tempKernel.GetPixel(x, d).SetData(value, true);
        }
        for (int x = 0; x<size; ++x) {
            for (int y = 0; y<d; ++y) {
                tempKernel.GetPixel(x, y).SetValue(value);
            }
        }

        ApplyMaskWithRotation(tempKernel, rotation, value);
    }
}

/// Handles upscaling a low-resolution texture using Marching Squares algorithm
/// to create a smoother, higher resolution representation with isolines.
public class Upscaler {
    private int textureWidth;   // Original texture width
    private int textureHeight;  // Original texture height
    private int scale;          // Upscaling factor
    private int kernelSize;     // Size of kernel used for interpolation
    private int borderWidth;    // Width of border region
    private Pixel[,] pixels;    // Output pixel data
    private int[,] originalTexture; // Input texture data
    private Kernel kernel;      // Kernel used for interpolation

    public void ComputePixels() {
        // reset isolines
        for (int i = 0; i< textureWidth * scale; ++i){
            for (int j = 0; j< textureHeight * scale; ++j) {
                pixels[i, j].SetIsIsoline(false);
            }
        }
        
        // fill the "inside" with marching squares kernels
        for (int i = 0; i< textureWidth - 1; ++i) {
            for (int j = 0; j< textureHeight - 1; ++j) {
                kernel.SetCorners(originalTexture[i, j], originalTexture[i+1, j], originalTexture[i+1, j+1], originalTexture[i, j+1]);
                kernel.ComputeKernel();
                for (int x = 0; x< kernelSize; ++x) {
                    for (int y = 0; y< kernelSize; ++y) {
                        Pixel pix = pixels[i * scale + borderWidth + x, j * scale + borderWidth + y];
                        pix.SetValue(kernel.GetPixel(x, y).GetValue());
                        if (!pix.GetIsIsoline()) {
                            pix.SetIsIsoline(kernel.GetPixel(x, y).GetIsIsoline());
                        }
                    }
                }
            }
        }

        // fill the corners
        FillCorner(originalTexture[0, 0], 0, 0);
        FillCorner(originalTexture[0, textureHeight -1], 0, textureHeight * scale - borderWidth);
        FillCorner(originalTexture[textureWidth -1, 0], textureWidth * scale - borderWidth, 0);
        FillCorner(originalTexture[textureWidth -1, textureHeight-1], textureWidth * scale - borderWidth, textureHeight * scale - borderWidth);

        // fill the borders
        for (int i = 0; i<textureWidth -1; ++i) {
            FillHorizontalBorder(originalTexture[i, 0], originalTexture[i+1, 0], i*scale + borderWidth, 0);
            FillHorizontalBorder(originalTexture[i, textureHeight -1], originalTexture[i+1, textureHeight -1], i * scale + borderWidth, textureHeight * scale - borderWidth);
        }
        for (int j = 0; j<textureHeight -1; ++j) {
            FillVerticalBorder(originalTexture[0, j], originalTexture[0, j+1], 0, j*scale + borderWidth);
            FillVerticalBorder(originalTexture[textureWidth -1, j], originalTexture[textureWidth -1, j+1], textureWidth * scale - borderWidth, j * scale + borderWidth);
        }
    }

    public Pixel[,] GetTextureData() {
        return pixels;
    }

    public Upscaler(int width, int height, int scale, int[,] originalTexture) {
        textureWidth = width;
        textureHeight = height;
        this.scale = scale;
        kernelSize = 2*((scale+1)/2);
        borderWidth = scale/2;

        this.originalTexture = new int[textureWidth, textureHeight];
        for (int i = 0; i<textureWidth; ++i) {
            for (int j = 0; j<textureHeight; ++j) {
                this.originalTexture[i, j] = originalTexture[i, j];
            }
        }

        pixels = new Pixel[textureWidth * scale, textureHeight * scale];
        for (int i = 0; i<textureWidth * scale; ++i) {
            for (int j = 0; j<textureHeight * scale; ++j) {
                pixels[i, j] = new Pixel();
            }
        }
        
        kernel = new Kernel(kernelSize);
    }

    /// Fills corner regions with the specified value
    private void FillCorner(int value, int i, int j) {
        for (int x = i; x < i + borderWidth; ++x) {
            for (int y = j; y < j + borderWidth; ++y) {
                pixels[x, y].SetValue(value);
            }
        }
    }

    /// Interpolates between values along horizontal borders
    /// Creates isolines where needed between different values
    private void FillHorizontalBorder(int leftValue, int rightValue, int i, int j) {
        int d = -1;
        int halfKernel = kernelSize / 2;
        if (leftValue < rightValue) d = halfKernel;
        else if (leftValue > rightValue) d = halfKernel - 1;
        for (int y = 0; y< borderWidth; ++y) {
            for (int x = 0; x< halfKernel; ++x) {
                pixels[i+x, j+y].SetData(leftValue, x == d);
            }
            for (int x = halfKernel; x < kernelSize; ++x) {
                pixels[i+x, j+y].SetData(rightValue, x == d);
            }
        }
    }

    /// Interpolates between values along vertical borders
    /// Creates isolines where needed between different values
    private void FillVerticalBorder(int topValue, int bottomValue, int i, int j) {
        int d = -1;
        int halfKernel = kernelSize / 2;
        if (topValue < bottomValue) d = halfKernel;
        else if (topValue > bottomValue) d = halfKernel - 1;
        for (int x = 0; x< borderWidth; ++x) {
            for (int y = 0; y<halfKernel; ++y) {
                pixels[i+x, j+y].SetData(topValue, y==d);
            }
            for (int y = halfKernel; y<kernelSize; ++y) {
                pixels[i+x, j+y].SetData(bottomValue, y==d);
            }
        }
    }
}

/// Main class that manages texture generation, upscaling, and processing.
/// Converts intensity values to color-coded representations with isolines.
public class TextureData {
    private int width;          // Texture width
    private int height;         // Texture height
    private int colorPaletteSize; // Number of colors in the palette
    private Upscaler upscaler;  // Handles the upscaling process
    private int[,] originalPoints;
    private RaycastObject raycastObject;

    /// <summary>
    /// Creates a new TextureData instance that processes ray intensity data into a visual representation.
    /// </summary>
    /// <param name="originalFloatingPoints">The raw floating-point intensity values from ray calculations</param>
    /// <param name="width">Width of the original texture</param>
    /// <param name="height">Height of the original texture</param>
    /// <param name="scaleFactor">The amount by which to upscale the texture. This creates a higher resolution output 
    /// texture that is scaleFactor times larger in both dimensions than the input texture. Higher values produce smoother 
    /// isolines but require more processing power. For example, a scaleFactor of 4 turns a 256x256 texture into a 1024x1024 output.</param>
    /// <param name="colorPaletteSize">Number of distinct colors available in the palette</param>
    /// <param name="gaussianKernel">Kernel used for smoothing the texture</param>
    /// <param name="raycastObject">Reference to the object being raycast</param>
    public TextureData(float[,] originalFloatingPoints, int width, int height, int scaleFactor, int colorPaletteSize, float[,] gaussianKernel, RaycastObject raycastObject)
    {
        this.width = width;
        this.height = height;
        this.colorPaletteSize = colorPaletteSize;
        this.originalPoints = new int[width, height];
        this.raycastObject = raycastObject;

        ConvertHitIntenityToColorCodingValue(originalFloatingPoints);
        ApplyGaussianSmoothing(gaussianKernel);
        ExpandTextureColors();

        upscaler = new Upscaler(width, height, scaleFactor, originalPoints);
    }

    public Pixel[,] GetTextureData()
    {
        return upscaler.GetTextureData();
    }

    public void ComputePixels()
    {
        upscaler.ComputePixels();
    }

    private void ApplyGaussianSmoothing(float[,] gaussianKernel)
    {
        int halfSize = (gaussianKernel.GetLength(0) - 1) / 2;
        int[,] originalSmoothedPoints = new int[width, height];
        int[,] textureZoneMap = raycastObject.GetTextureZoneMapScale1();
        float kernelSum = 0.0f;

        for (int i = 0; i < width; ++i)
        {
            for (int j = 0; j < height; ++j)
            {
                float sum = 0;
                int zone = textureZoneMap[i, j];
                    
                // Apply Gaussian smoothing only to points in the same zone
                for (int ky = -halfSize; ky <= halfSize; ++ky)
                {
                    for (int kx = -halfSize; kx <= halfSize; ++kx)
                    {
                        int nx = i + kx;
                        int ny = j + ky;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            int neighborValue = originalPoints[nx, ny];
                            if (textureZoneMap[nx, ny] == zone)
                            {
                                sum += neighborValue * gaussianKernel[ky + halfSize, kx + halfSize];
                                kernelSum += gaussianKernel[ky + halfSize, kx + halfSize];
                            }
                        }
                    }
                }
                if (kernelSum == 0.0f)
                {
                    originalSmoothedPoints[i, j] = originalPoints[i, j]; // No smoothing if kernel sum is zero
                }
                else
                {
                    sum /= kernelSum; // Normalize by the sum of the kernel weights
                    kernelSum = 0.0f; // Reset kernel sum for the next pixel
                    originalSmoothedPoints[i, j] = Mathf.RoundToInt(sum);
                }
            }
        }
        for (int i = 0; i < width; ++i)
        {
            for (int j = 0; j < height; ++j)
            {
                originalPoints[i, j] = originalSmoothedPoints[i, j];
            }
        }
    }

    /// Converts floating point intensity values (0.0-1.0) to discrete color indices
    private void ConvertHitIntenityToColorCodingValue(float[,] originalFloatingPoints)
    {
        for (int i = 0; i < width; ++i)
        {
            for (int j = 0; j < height; ++j)
            {
                if (originalFloatingPoints[i, j] != 0)
                {
                    originalPoints[i, j] = Mathf.Min(Mathf.CeilToInt(originalFloatingPoints[i, j] * colorPaletteSize), colorPaletteSize);
                }
                else
                {
                    originalPoints[i, j] = 0;
                }
            }
        }
    }

    /// Expands texture colors around discontinuities to avoid uncolored pixels on the object
    private void ExpandTextureColors()
    {
        int searchRadius = 2; // Look up to 2 pixels around
        int[,] zoneMap = raycastObject.GetTextureZoneMapScale1();
        
        // Create a copy of the texture to use as the source during processing
        int[,] sourceValues = new int[width, height];
        bool[,] hasValidZone = new bool[width, height];
        
        // Initialize with existing data
        for (int i = 0; i < width; ++i)
        {
            for (int j = 0; j < height; ++j)
            {
                sourceValues[i, j] = originalPoints[i, j];
                hasValidZone[i, j] = zoneMap[i, j] != -1;
            }
        }
        
        // Process each pixel with an invalid zone
        for (int i = 0; i < width; ++i)
        {
            for (int j = 0; j < height; ++j)
            {
                if (!hasValidZone[i, j])
                {
                    // First pass: count all valid pixels by zone within search radius
                    Dictionary<int, int> zoneCounts = new();
                    Dictionary<int, Dictionary<int, int>> zoneDistances = new();
                    
                    for (int dx = -searchRadius; dx <= searchRadius; ++dx)
                    {
                        for (int dy = -searchRadius; dy <= searchRadius; ++dy)
                        {
                            int nx = i + dx;
                            int ny = j + dy;
                            
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height && hasValidZone[nx, ny])
                            {
                                int zoneId = zoneMap[nx, ny];
                                int distance = dx * dx + dy * dy;
                                
                                // Count zones
                                if (!zoneCounts.ContainsKey(zoneId))
                                {
                                    zoneCounts[zoneId] = 0;
                                    zoneDistances[zoneId] = new Dictionary<int, int>();
                                }
                                ++zoneCounts[zoneId];
                                
                                // Track distance to value mapping
                                if (!zoneDistances[zoneId].ContainsKey(distance))
                                {
                                    zoneDistances[zoneId][distance] = sourceValues[nx, ny];
                                }
                            }
                        }
                    }
                    
                    // Find the most represented zone
                    int bestZoneId = -1;
                    int maxPixelCount = 0;
                    
                    foreach (var entry in zoneCounts)
                    {
                        if (entry.Value > maxPixelCount)
                        {
                            maxPixelCount = entry.Value;
                            bestZoneId = entry.Key;
                        }
                    }
                    
                    if (bestZoneId != -1)
                    {
                        // From the most represented zone, find the nearest pixel value
                        int minDistance = int.MaxValue;
                        int bestValue = 0;
                        
                        foreach (var distEntry in zoneDistances[bestZoneId])
                        {
                            if (distEntry.Key < minDistance)
                            {
                                minDistance = distEntry.Key;
                                bestValue = distEntry.Value;
                            }
                        }
                        
                        originalPoints[i, j] = bestValue;
                    }
                }
            }
        }
    }
}