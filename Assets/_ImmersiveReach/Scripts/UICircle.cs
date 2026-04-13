using UnityEngine;
using UnityEngine.UI;

// This class is used to draw a circle in the UI
[RequireComponent(typeof(CanvasRenderer))]
public class UICircle : Graphic
{
    public int segments = 64;
    public float thickness = 1f;
    public float radius = 5f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        float angleStep = 2 * Mathf.PI / segments;
        Vector2 prevOuter = new(radius, 0);
        Vector2 prevInner = new(radius - thickness, 0);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep;
            Vector2 newOuter = new(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            Vector2 newInner = new(Mathf.Cos(angle) * (radius - thickness), Mathf.Sin(angle) * (radius - thickness));

            vh.AddVert(prevOuter, color, Vector2.zero);
            vh.AddVert(prevInner, color, Vector2.zero);
            vh.AddVert(newInner, color, Vector2.zero);
            vh.AddVert(newOuter, color, Vector2.zero);

            int baseIndex = (i - 1) * 4;
            vh.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
            vh.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);

            prevOuter = newOuter;
            prevInner = newInner;
        }
    }
}