
using UnityEngine;
using System.Threading.Tasks;

public static class TextureHelper
{
    public static void FlipTextureVertically(Texture2D source, Texture2D destination)
    {
        Color[] pixels = source.GetPixels();
        Color[] flippedPixels = new Color[pixels.Length];
        int width = source.width;
        int height = source.height;

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; ++x)
            {
                flippedPixels[(height - 1 - y) * width + (width - 1 - x)] = pixels[y * width + x];
            }
        });

        destination.SetPixels(flippedPixels);
    }
}