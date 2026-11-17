using UnityEngine;
using UnityEngine.UIElements;

internal sealed class TrackOutlineView
{
    private readonly VisualElement element;
    private readonly Vector2[] points = new Vector2[256];
    private Rect bounds;

    public TrackOutlineView(VisualElement element)
    {
        this.element = element;
        element.generateVisualContent += Draw;
    }

    public void SetTrack(TrackDefinition track)
    {
        element.style.display = track == null ? DisplayStyle.None : DisplayStyle.Flex;
        if (track == null)
            return;

        float lapLength = track.lengthMiles * TrackDefinition.MetersPerMile;
        var path = track.layoutPoints?.Length >= 3 ? track.layoutPoints : TrackUtilityGeometry.EditableOval(track);
        var layout = new TrackLayout(path, lapLength, track.widthMeters);
        Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = -min;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 position = layout.Sample(lapLength * i / points.Length, out _, out _);
            var point = new Vector2(position.x, -position.z);
            points[i] = point;
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        element.MarkDirtyRepaint();
    }

    private void Draw(MeshGenerationContext context)
    {
        Rect viewport = element.contentRect;
        const float padding = 6f;
        if (viewport.width <= padding * 2f || viewport.height <= padding * 2f ||
            bounds.size.sqrMagnitude <= 0f)
            return;

        float scale = Mathf.Min(
            (viewport.width - padding * 2f) / Mathf.Max(1f, bounds.width),
            (viewport.height - padding * 2f) / Mathf.Max(1f, bounds.height));
        Vector2 offset = viewport.center - bounds.center * scale;
        var painter = context.painter2D;
        painter.strokeColor = element.resolvedStyle.color;
        painter.lineWidth = 2.5f;
        painter.lineJoin = LineJoin.Round;
        painter.BeginPath();
        painter.MoveTo(points[0] * scale + offset);
        for (int i = 1; i < points.Length; i++)
            painter.LineTo(points[i] * scale + offset);
        painter.ClosePath();
        painter.Stroke();
    }

}
