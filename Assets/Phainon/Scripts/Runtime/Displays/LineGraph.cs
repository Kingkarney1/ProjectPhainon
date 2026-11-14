using UnityEngine;
using UnityEngine.UI;

namespace Phainon.Displays
{
    /// <summary>A UI polyline. Points are given in 0..1 coordinates of the graphic's rectangle.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class LineGraph : MaskableGraphic
    {
        [SerializeField] internal float thickness = 2.5f;

        private Vector2[] points;
        private int count;

        public void SetPoints(Vector2[] source, int pointCount)
        {
            points = source;
            count = source == null ? 0 : Mathf.Min(pointCount, source.Length);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points == null || count < 2) return;

            Rect rect = GetPixelAdjustedRect();
            float half = thickness * 0.5f;
            Color32 vertexColor = color;
            int vertex = 0;

            for (int i = 0; i < count - 1; i++)
            {
                Vector2 a = new Vector2(rect.xMin + points[i].x * rect.width, rect.yMin + points[i].y * rect.height);
                Vector2 b = new Vector2(rect.xMin + points[i + 1].x * rect.width, rect.yMin + points[i + 1].y * rect.height);
                Vector2 direction = b - a;
                if (direction.sqrMagnitude < 1e-6f) continue;

                // Each segment is a quad extruded along the segment's normal.
                Vector2 normal = new Vector2(-direction.y, direction.x).normalized * half;
                vh.AddVert(a - normal, vertexColor, Vector2.zero);
                vh.AddVert(a + normal, vertexColor, Vector2.zero);
                vh.AddVert(b + normal, vertexColor, Vector2.zero);
                vh.AddVert(b - normal, vertexColor, Vector2.zero);
                vh.AddTriangle(vertex, vertex + 1, vertex + 2);
                vh.AddTriangle(vertex, vertex + 2, vertex + 3);
                vertex += 4;
            }
        }
    }
}
