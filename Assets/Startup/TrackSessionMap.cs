using UnityEngine;
using UnityEngine.UIElements;

internal sealed class TrackSessionMap
{
    private const string ExpandedPreference = "StockCarManager.TrackMapExpanded";
    private const float MarkerRadius = 10f;
    private const float Padding = 13f;
    private readonly VisualElement container;
    private readonly VisualElement element;
    private readonly Button toggle;
    private bool expanded;
    private readonly Label[] markers = new Label[TrackPreview.CarCount];
    private readonly string[] markerNumbers = new string[TrackPreview.CarCount];
    private TrackPreview preview;
    private Vector2[] inside;
    private Vector2[] outside;
    private Vector2[] apron;
    private Rect bounds;

    public TrackSessionMap(VisualElement container)
    {
        this.container = container;
        element = container.Q("trackSessionMap");
        toggle = container.Q<Button>("toggleTrackMap");
        toggle.clicked += Toggle;
        expanded = PlayerPrefs.GetInt(ExpandedPreference, 1) != 0;
        UpdateToggle();
        container.style.display = DisplayStyle.None;
        element.generateVisualContent += DrawTrack;
        for (int i = 0; i < markers.Length; i++)
        {
            var marker = new Label { pickingMode = PickingMode.Ignore };
            marker.AddToClassList("track-map-car");
            marker.EnableInClassList("track-map-player", i == 0);
            markers[i] = marker;
            element.Add(marker);
        }
        markers[0].BringToFront();
    }

    private void Toggle()
    {
        expanded = !expanded;
        UpdateToggle();
        PlayerPrefs.SetInt(ExpandedPreference, expanded ? 1 : 0);
        PlayerPrefs.Save();
    }

    private void UpdateToggle()
    {
        container.EnableInClassList("track-map-collapsed", !expanded);
        toggle.text = expanded ? ">" : "<";
        toggle.tooltip = expanded ? "Collapse track map" : "Expand track map";
    }

    public void SetPreview(TrackPreview trackPreview)
    {
        preview = trackPreview;
        container.style.display = preview == null ? DisplayStyle.None : DisplayStyle.Flex;
        foreach (var marker in markers)
            marker.style.display = DisplayStyle.None;
        if (preview == null)
            return;

        for (int i = 0; i < markers.Length; i++)
            preview.StyleMapNumber(markers[i], i);

        preview.GetMapSurface(out inside, out outside, out apron);
        Vector2 min = inside[0];
        Vector2 max = min;
        Include(inside);
        Include(outside);
        Include(apron);
        foreach (var ribbon in preview.MapPitRibbons)
            Include(ribbon);
        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        element.MarkDirtyRepaint();

        void Include(Vector2[] points)
        {
            foreach (var point in points)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
        }
    }

    private bool TryGetProjection(out float scale, out Vector2 offset)
    {
        Rect viewport = element.contentRect;
        scale = Mathf.Min((viewport.width - Padding * 2f) / Mathf.Max(1f, bounds.width),
            (viewport.height - Padding * 2f) / Mathf.Max(1f, bounds.height));
        offset = viewport.center - bounds.center * scale;
        return expanded && preview != null && scale > 0f;
    }

    public void UpdateCars()
    {
        if (!TryGetProjection(out float scale, out Vector2 offset))
            return;

        for (int i = 0; i < markers.Length; i++)
        {
            var marker = markers[i];
            bool visible = preview.TryGetMapCar(i, out Vector2 position, out string number);
            marker.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
                continue;

            var point = position * scale + offset;
            marker.style.translate = new Translate(point.x - MarkerRadius, point.y - MarkerRadius);
            if (markerNumbers[i] != number)
            {
                markerNumbers[i] = number;
                marker.text = number;
                marker.style.fontSize = number.Length switch { 4 => 6, 3 => 8, _ => StyleKeyword.Null };
            }
        }
    }

    private void DrawTrack(MeshGenerationContext context)
    {
        if (!TryGetProjection(out float scale, out Vector2 offset))
            return;

        var painter = context.painter2D;
        DrawRing(apron, inside, new Color32(71, 86, 96, 255));
        DrawRing(outside, inside, new Color32(175, 187, 196, 255));
        painter.fillColor = new Color32(99, 186, 207, 255);
        painter.strokeColor = painter.fillColor;
        painter.lineWidth = 1f;
        painter.lineJoin = LineJoin.Round;
        foreach (var ribbon in preview.MapPitRibbons)
        {
            painter.BeginPath();
            AddContour(ribbon);
            painter.Fill(FillRule.NonZero);
            painter.Stroke();
        }

        void DrawRing(Vector2[] outer, Vector2[] inner, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            AddContour(outer);
            AddContour(inner);
            painter.Fill(FillRule.OddEven);
        }

        void AddContour(Vector2[] points)
        {
            painter.MoveTo(points[0] * scale + offset);
            for (int i = 1; i < points.Length; i++)
                painter.LineTo(points[i] * scale + offset);
            painter.ClosePath();
        }
    }
}
