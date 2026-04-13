using UnityEngine;
using Unity.Collections;

public static class TextureColoringHelper
{
    private static Color[] colorPalette;
    private static readonly Color IsoLineColor = Color.black;
    private static readonly Color InaccessibleColor = new(225 / 255f, 225 / 255f, 225 / 255f);

    // colors 3 values : #f7fcb9, #addd8e, #31a354
    // colors 4 values : #ffffcc, #c2e699, #78c679, #238443
    // colors 5 values : #ffffcc, #c2e699, #78c679, #31a354, #006837
    // colors 6 values : #ffffcc, #d9f0a3, #addd8e, #78c679, #31a354, #006837
    private static readonly Color[] Colors3 = { new(0xf7 / 255f, 0xfc / 255f, 0xb9 / 255f), new(0xad / 255f, 0xdd / 255f, 0x8e / 255f), new(0x31 / 255f, 0xa3 / 255f, 0x54 / 255f) };
    private static readonly Color[] Colors4 = { new(0xff / 255f, 0xff / 255f, 0xcc / 255f), new(0xc2 / 255f, 0xe6 / 255f, 0x99 / 255f), new(0x78 / 255f, 0xc6 / 255f, 0x79 / 255f), new(0x23 / 255f, 0x84 / 255f, 0x43 / 255f) };
    private static readonly Color[] Colors5 = { new(0xff / 255f, 0xff / 255f, 0xcc / 255f), new(0xc2 / 255f, 0xe6 / 255f, 0x99 / 255f), new(0x78 / 255f, 0xc6 / 255f, 0x79 / 255f), new(0x31 / 255f, 0xa3 / 255f, 0x54 / 255f), new(0x00 / 255f, 0x68 / 255f, 0x37 / 255f) };
    private static readonly Color[] Colors6 = { new(0xff / 255f, 0xff / 255f, 0xcc / 255f), new(0xd9 / 255f, 0xf0 / 255f, 0xa3 / 255f), new(0xad / 255f, 0xdd / 255f, 0x8e / 255f), new(0x78 / 255f, 0xc6 / 255f, 0x79 / 255f), new(0x31 / 255f, 0xa3 / 255f, 0x54 / 255f), new(0x00 / 255f, 0x68 / 255f, 0x37 / 255f) };
    private static readonly Color ValidPointColor = new(0x5a / 255f, 0xae / 255f, 0x61 / 255f);
    private static readonly Color InvalidPointColor = new(255f / 255f, 255f / 255f, 255f / 255f);

    public static void SetColorPalette(int colorCount)
    {
        colorPalette = colorCount switch
        {
            3 => Colors3,
            4 => Colors4,
            5 => Colors5,
            6 => Colors6,
            _ => Colors3,
        };
    }

    public static Color[] GetColorPalette()
    {
        return colorPalette;
    }

    public static void UpdateParticleSystem(NativeArray<InstrumentSamplePoint> points, ParticleSystem particleSystem, float pointSize = 0.1f)
    {
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[points.Length];

        for (int i = 0; i < points.Length; ++i)
        {
            particles[i].position = points[i].point;
            particles[i].startSize = pointSize;
            particles[i].startColor = points[i].IsValid() ? ValidPointColor : InvalidPointColor;
        }

        particleSystem.SetParticles(particles, particles.Length);
    }

    // Color the object based on its current texture data
    public static void ColorObject(RaycastObject raycastObject, bool drawColors, bool drawIsoLines)
    {
        int textureWidth = raycastObject.textureWidth;
        int textureHeight = raycastObject.textureHeight;
        Color[] textureColors = new Color[textureWidth * textureHeight];
        Color undrawnColor = raycastObject.GetInitialColor();

        ColorTexture(textureColors, raycastObject.GetTextureData(), textureWidth, textureHeight, drawIsoLines, drawColors, undrawnColor);
        raycastObject.SetTexture(textureColors);
    }

    // Set the texture colors based on the texture data
    private static void ColorTexture(Color[] textureColors, Pixel[,] textureData, int width, int height, bool drawIsoLines, bool drawColors, Color undrawnColor)
    {
        if (textureData == null)
        {
            return;
        }

        for (int i = 0; i < width; ++i)
        {
            for (int j = 0; j < height; ++j)
            {
                int index = j * width + i;
                Color color;
                if (textureData[i, j].GetIsIsoline() && drawIsoLines)
                {
                    color = IsoLineColor;
                }
                else if (drawColors)
                {
                    int colorIndex = textureData[i, j].GetValue() - 1;
                    if (colorIndex >= 0 && colorIndex < colorPalette.Length)
                    {
                        color = colorPalette[colorIndex];
                    }
                    else
                    {
                        color = InaccessibleColor;
                    }
                }
                else
                {
                    color = undrawnColor;
                }
                textureColors[index] = color;
            }
        }
    }

    // Color the cone based on the vertices reachability
    public static void ColorCone(GameObject cone, bool[] vertexReachability, int textureWidth = 256, int textureHeight = 256)
    {
        Mesh coneMesh = cone.GetComponent<MeshFilter>().mesh;
        Color[] vertexColors = new Color[coneMesh.vertexCount];
        for (int i = 0; i < coneMesh.vertexCount; ++i)
        {
            vertexColors[i] = vertexReachability[i] ? ValidPointColor : InvalidPointColor;
        }

        Texture2D texture = new(textureWidth, textureHeight, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        int[] triangles = coneMesh.triangles;
        Vector2[] uvs = coneMesh.uv;
        Color[] pixels = new Color[textureWidth * textureHeight];
        bool[] painted = new bool[textureWidth * textureHeight];

        // Rasterize triangle colors based on UVs
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int i0 = triangles[i];
            int i1 = triangles[i + 1];
            int i2 = triangles[i + 2];

            Vector2 uv0 = uvs[i0] * new Vector2(textureWidth, textureHeight);
            Vector2 uv1 = uvs[i1] * new Vector2(textureWidth, textureHeight);
            Vector2 uv2 = uvs[i2] * new Vector2(textureWidth, textureHeight);

            // Color c0 = vertexColors[i0]; // tip
            Color c1 = vertexColors[i1];
            Color c2 = vertexColors[i2];
            Color c0 = (c1 + c2) / 2; // base

            RasterizeTriangle(uv0, uv1, uv2, c0, c1, c2, pixels, painted, textureWidth, textureHeight);
        }

        // Step 2: Dilation to fill small gaps (simple box blur style pass)
        int dilationIterations = 3;
        for (int iteration = 0; iteration < dilationIterations; iteration++)
        {
            Color[] newPixels = (Color[])pixels.Clone();
            bool[] newPainted = (bool[])painted.Clone();

            for (int y = 1; y < textureHeight - 1; y++)
            {
                for (int x = 1; x < textureWidth - 1; x++)
                {
                    int index = y * textureWidth + x;
                    if (painted[index]) continue;

                    Color accumulated = Color.black;
                    int count = 0;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int ni = (y + dy) * textureWidth + (x + dx);
                            if (painted[ni])
                            {
                                accumulated += pixels[ni];
                                count++;
                            }
                        }
                    }

                    if (count > 0)
                    {
                        newPixels[index] = accumulated / count;
                        newPainted[index] = true;
                    }
                }
            }

            pixels = newPixels;
            painted = newPainted;
        }

        // Fill borders with the neighbor color
        // Borders aren't filled by the dilation pass, so we need to do it manually
        for (int j = 0; j < textureHeight; ++j)
        {
            pixels[j * textureWidth] = pixels[j * textureWidth + 1];
            pixels[j * textureWidth + textureWidth - 1] = pixels[j * textureWidth + textureWidth - 2];
        }
        for (int i = 0; i < textureWidth; ++i)
        {
            pixels[(textureHeight - 1) * textureWidth + i] = pixels[(textureHeight - 2) * textureWidth + i];
        }

        texture.SetPixels(pixels);
        texture.Apply();
        cone.GetComponent<Renderer>().material.mainTexture = texture;
    }

    // Rasterize a triangle using barycentric coordinates
    // This function fills the pixels array with the color of the triangle based on its barycentric coordinates
    private static void RasterizeTriangle(Vector2 uv0, Vector2 uv1, Vector2 uv2, Color c0, Color c1, Color c2, Color[] pixels, bool[] painted, int texWidth, int texHeight)
    {
        Rect bounds = new Rect(
            Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x)),
            Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y)),
            Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x)),
            Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y))
        );

        int xMin = Mathf.Clamp(Mathf.FloorToInt(bounds.xMin) -1, 0, texWidth - 1);
        int xMax = Mathf.Clamp(Mathf.CeilToInt(bounds.xMax) +1, 0, texWidth - 1);
        int yMin = Mathf.Clamp(Mathf.FloorToInt(bounds.yMin) -1, 0, texHeight - 1);
        int yMax = Mathf.Clamp(Mathf.CeilToInt(bounds.yMax) +1, 0, texHeight - 1);

        for (int y = yMin; y <= yMax; y++)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                Vector3 bary = Barycentric(p, uv0, uv1, uv2);

                if (bary.x >= 0 && bary.y >= 0 && bary.z >= 0)
                {
                    Color color = bary.x * c0 + bary.y * c1 + bary.z * c2;
                    int index = y * texWidth + x;
                    pixels[index] = color;
                    painted[index] = true;
                }
            }
        }
    }

    // Barycentric coordinates calculation
    private static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
        float d00 = Vector2.Dot(v0, v0);
        float d01 = Vector2.Dot(v0, v1);
        float d11 = Vector2.Dot(v1, v1);
        float d20 = Vector2.Dot(v2, v0);
        float d21 = Vector2.Dot(v2, v1);
        float denom = d00 * d11 - d01 * d01;
        if (Mathf.Abs(denom) < 1e-5f) return new Vector3(-1, -1, -1); // Degenerate
        float v = (d11 * d20 - d01 * d21) / denom;
        float w = (d00 * d21 - d01 * d20) / denom;
        float u = 1.0f - v - w;
        return new Vector3(u, v, w);
    }
}