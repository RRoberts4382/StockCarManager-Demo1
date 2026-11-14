using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class TrackUtilityMap : VisualElement
{
    private const int MapSamples = 256;
    private static readonly Color Road = new Color32(102, 116, 132, 255);
    private static readonly Color Apron = new Color32(73, 120, 137, 255);
    private static readonly Color Orange = new Color32(255, 175, 99, 255);
    private static readonly Color Blue = new Color32(101, 216, 255, 255);
    private TrackDefinition track;
    private TrackLayout main;
    private TrackLayoutPoint[] points;
    private TrackPitSpline pitSpline;
    private Vector2 mapCenter;
    private Vector2 worldCenter;
    private float scale;
    private Texture2D referenceImage;
    private Vector2 imageCenter;
    private float imageWidth;
    private float fittedWidth;
    private bool alignImage;
    private TrackUtilityTab activeTab;
    private bool[] splineVisibility;
    private bool placingConnector;
    private float viewZoom = 1f;
    private Vector2 viewPan;
    private int dragPointer = -1;
    private Vector2 dragStart;
    private Vector2 previousPointer;
    private bool dragging;
    private bool draggingImage;
    private bool selectingRectangle;
    private int[] selectedPoints = Array.Empty<int>();

    public event Action<Vector3, int> PointClicked;
    public event Action<int[]> SelectionChanged;

    public TrackUtilityMap()
    {
        generateVisualContent += Draw;
        style.overflow = Overflow.Hidden;
        RegisterCallback<PointerDownEvent>(Click);
        RegisterCallback<PointerMoveEvent>(MovePointer);
        RegisterCallback<PointerUpEvent>(FinishDrag);
        RegisterCallback<PointerCaptureOutEvent>(evt =>
        {
            if (evt.pointerId == dragPointer)
            {
                dragPointer = -1;
                MarkDirtyRepaint();
            }
        });
        RegisterCallback<WheelEvent>(Zoom);
    }

    public bool HasReferenceImage => referenceImage != null;
    public Vector3 ReferenceAlignment => new Vector3(imageCenter.x, imageCenter.y, imageWidth);

    public void SetReferenceImage(Texture2D image, Vector3 alignment = default)
    {
        CancelDrag();
        if (referenceImage != null)
            UnityEngine.Object.Destroy(referenceImage);
        referenceImage = image;
        imageCenter = new Vector2(alignment.x, alignment.y);
        imageWidth = alignment.z > 0f && !float.IsInfinity(alignment.z) &&
            !float.IsNaN(alignment.x) && !float.IsInfinity(alignment.x) &&
            !float.IsNaN(alignment.y) && !float.IsInfinity(alignment.y) ? alignment.z : 0f;
        MarkDirtyRepaint();
    }

    public void SetImageAlignment(bool enabled)
    {
        alignImage = enabled;
        if (!enabled)
            CancelDrag();
    }

    public void FitImage()
    {
        imageWidth = 0f;
        MarkDirtyRepaint();
    }

    public void Show(TrackDefinition definition, int[] selection, TrackUtilityTab tab, bool[] visibility, bool placingConnector)
    {
        if (track?.id != definition?.id)
            ResetView();
        track = definition;
        activeTab = tab;
        splineVisibility = visibility;
        this.placingConnector = placingConnector;
        selectedPoints = selection;
        main = null;
        pitSpline = null;
        points = track?.layoutPoints;
        if (track != null && track.lengthMiles > 0f && track.lengthMiles <= 100f)
        {
            if ((points?.Length ?? 0) == 0)
                points = TrackUtilityGeometry.EditableOval(track);
            if (points.Length >= 3 && points.All(point => point != null &&
                Mathf.Abs(point.x) <= 100000f && Mathf.Abs(point.z) <= 100000f) &&
                points.Any(point => point.x != points[0].x || point.z != points[0].z))
                main = new TrackLayout(points, track.lengthMiles * TrackDefinition.MetersPerMile, track.widthMeters);
            if (track.pitRoad?.enabled == true && track.pitRoad.points?.Length >= 2 && track.pitRoad.points.All(point => point != null &&
                Mathf.Abs(point.x) <= 100000f && Mathf.Abs(point.z) <= 100000f))
                pitSpline = new TrackPitSpline(track.pitRoad.points);
        }
        MarkDirtyRepaint();
    }

    private void Draw(MeshGenerationContext context)
    {
        if (main == null || contentRect.width < 60f || contentRect.height < 60f)
            return;

        float lap = track.lengthMiles * TrackDefinition.MetersPerMile;
        var center = new Vector3[MapSamples];
        var inside = new Vector3[MapSamples];
        var outside = new Vector3[MapSamples];
        var apronEdge = new Vector3[MapSamples];
        Vector3 min = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
        Vector3 max = -min;
        for (int i = 0; i < MapSamples; i++)
        {
            float distance = lap * i / MapSamples;
            center[i] = main.Sample(distance, out var tangent, out _);
            Vector2 widths = main.Widths(distance);
            Vector3 inward = Vector3.Cross(Vector3.up, tangent) * main.InsideSign;
            float half = Mathf.Clamp(widths.x, 0f, 100f) * 0.5f;
            inside[i] = center[i] + inward * half;
            outside[i] = center[i] - inward * half;
            apronEdge[i] = inside[i] + inward * Mathf.Clamp(widths.y, 0f, 100f);
            min = Vector3.Min(min, Vector3.Min(outside[i], apronEdge[i]));
            max = Vector3.Max(max, Vector3.Max(outside[i], apronEdge[i]));
        }
        foreach (var point in track.pitRoad?.points ?? Array.Empty<TrackPitPoint>())
        {
            if (point == null || !(Mathf.Abs(point.x) <= 100000f && Mathf.Abs(point.z) <= 100000f))
                continue;
            Vector3 position = main.ToTrackSpace(TrackPitSpline.Position(point));
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }
        float fitScale = Mathf.Min((contentRect.width - 42f) / Mathf.Max(1f, max.x - min.x),
            (contentRect.height - 42f) / Mathf.Max(1f, max.z - min.z));
        scale = fitScale * viewZoom;
        mapCenter = contentRect.center + viewPan;
        worldCenter = new Vector2((min.x + max.x) * 0.5f, (min.z + max.z) * 0.5f);

        DrawReferenceImage(context, fitScale);
        var painter = context.painter2D;
        if (Visible(TrackUtilityTab.Apron))
            Ribbon(painter, inside, apronEdge, WithReferenceOpacity(Apron));
        if (Visible(TrackUtilityTab.Track) || placingConnector)
        {
            Ribbon(painter, outside, inside, WithReferenceOpacity(Road));
            painter.strokeColor = new Color32(173, 185, 199, 255);
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.MoveTo(Project(center[0]));
            for (int i = 1; i < center.Length; i++)
                painter.LineTo(Project(center[i]));
            painter.ClosePath();
            painter.Stroke();
        }

        if (Visible(TrackUtilityTab.Apron))
        {
            painter.strokeColor = Blue;
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.MoveTo(Project(apronEdge[0]));
            for (int i = 1; i < apronEdge.Length; i++)
                painter.LineTo(Project(apronEdge[i]));
            painter.ClosePath();
            painter.Stroke();
        }

        if (track.layoutPoints?.Length > 0 && (activeTab == TrackUtilityTab.Track ||
            activeTab == TrackUtilityTab.Apron || activeTab == TrackUtilityTab.RaceMarkers))
        {
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 position = activeTab == TrackUtilityTab.Apron ? ApronPoint(i) : MainPoint(i);
                bool selected = (activeTab == TrackUtilityTab.Track || activeTab == TrackUtilityTab.Apron ||
                    activeTab == TrackUtilityTab.RaceMarkers) && selectedPoints.Contains(i);
                Dot(painter, position, selected ? Orange : Color.white, selected ? 5f : 2.5f);
            }
        }

        if (Visible(TrackUtilityTab.RaceMarkers))
        {
            var markers = track.raceMarkers;
            if (markers?.hasRestartZone == true)
                DrawRestartZone(painter, lap, markers);
            float finish = track.StartFinishFraction;
            DrawMainLine(painter, Mathf.Repeat(finish, 1f) * lap, Color.white);
        }
        DrawPit(painter);
        if (dragPointer != -1 && selectingRectangle && dragging)
            DrawSelectionRectangle(painter);
    }

    private bool Visible(TrackUtilityTab tab) => activeTab == tab || splineVisibility[(int)tab];

    private Color WithReferenceOpacity(Color color)
    {
        if (referenceImage != null)
            color.a = 0.40f;
        return color;
    }

    private void DrawReferenceImage(MeshGenerationContext context, float fitScale)
    {
        if (referenceImage == null)
            return;

        float aspect = (float)referenceImage.width / referenceImage.height;
        fittedWidth = Mathf.Min(contentRect.width - 42f, (contentRect.height - 42f) * aspect) / fitScale;
        if (imageWidth <= 0f)
        {
            imageCenter = worldCenter;
            imageWidth = fittedWidth;
        }

        Vector2 center = Project(new Vector3(imageCenter.x, 0f, imageCenter.y));
        Vector2 halfSize = new Vector2(imageWidth, imageWidth / aspect) * (scale * 0.5f);
        Vector2 topLeft = center - halfSize;
        Vector2 bottomRight = center + halfSize;
        var mesh = context.Allocate(4, 6, referenceImage);
        Color32 tint = new Color32(255, 255, 255, 185);
        mesh.SetNextVertex(ImageVertex(topLeft.x, topLeft.y, 0f, 1f, tint));
        mesh.SetNextVertex(ImageVertex(bottomRight.x, topLeft.y, 1f, 1f, tint));
        mesh.SetNextVertex(ImageVertex(bottomRight.x, bottomRight.y, 1f, 0f, tint));
        mesh.SetNextVertex(ImageVertex(topLeft.x, bottomRight.y, 0f, 0f, tint));
        mesh.SetNextIndex(0);
        mesh.SetNextIndex(1);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(2);
        mesh.SetNextIndex(3);
        mesh.SetNextIndex(0);
    }

    private static Vertex ImageVertex(float x, float y, float u, float v, Color32 tint)
    {
        return new Vertex { position = new Vector3(x, y, Vertex.nearZ), uv = new Vector2(u, v), tint = tint };
    }

    private void DrawRestartZone(Painter2D painter, float lap, TrackRaceMarkers markers)
    {
        float length = Mathf.Repeat(markers.restartZoneEnd - markers.restartZoneStart, 1f);
        painter.strokeColor = new Color32(101, 223, 165, 255);
        painter.lineWidth = 4f;
        painter.BeginPath();
        for (int i = 0; i <= 48; i++)
        {
            float fraction = Mathf.Repeat(markers.restartZoneStart + length * i / 48f, 1f);
            Vector2 point = Project(main.Sample(fraction * lap, out _, out _));
            if (i == 0)
                painter.MoveTo(point);
            else
                painter.LineTo(point);
        }
        painter.Stroke();
    }

    private void DrawMainLine(Painter2D painter, float distance, Color color)
    {
        Vector3 center = main.Sample(distance, out var forward, out _);
        Vector3 cross = Vector3.Cross(Vector3.up, forward) * (main.Widths(distance).x * 0.5f);
        Line(painter, Project(center - cross), Project(center + cross), color, 3f);
    }

    private void DrawPit(Painter2D painter)
    {
        var pit = track.pitRoad;
        if (pit?.enabled != true)
            return;

        if (Visible(TrackUtilityTab.PitEntry))
            DrawPitPath(painter, pit.entrance, Orange, TrackUtilityTab.PitEntry);
        if (Visible(TrackUtilityTab.PitExit))
            DrawPitPath(painter, pit.exit, new Color32(207, 154, 255, 255), TrackUtilityTab.PitExit);
        if (!Visible(TrackUtilityTab.PitRoad))
            return;
        if (pitSpline != null)
        {
            DrawSpline(painter, pitSpline, Blue, Mathf.Max(2f, pit.widthMeters * scale));
            PitLine(painter, pit.speedLimitStart, pit.widthMeters, new Color32(111, 255, 154, 255));
            PitLine(painter, pit.speedLimitEnd, pit.widthMeters, new Color32(255, 113, 113, 255));
        }

        foreach (var stall in pit.stalls ?? Array.Empty<TrackPitPoint>())
            Dot(painter, PitPoint(stall), Color.white, 2f);
        if (activeTab != TrackUtilityTab.PitRoad)
            return;
        var controls = pit.points ?? Array.Empty<TrackPitPoint>();
        for (int i = 0; i < controls.Length; i++)
        {
            bool selected = activeTab == TrackUtilityTab.PitRoad && selectedPoints.Contains(i);
            Dot(painter, PitPoint(controls[i]), selected ? Orange : Blue, selected ? 5f : 3f);
        }
    }

    private void PitLine(Painter2D painter, float fraction, float width, Color color)
    {
        Vector3 center = main.ToTrackSpace(pitSpline.Sample(fraction));
        Vector3 before = pitSpline.Sample(fraction - 0.005f);
        Vector3 after = pitSpline.Sample(fraction + 0.005f);
        Vector3 cross = Vector3.Cross(Vector3.up, (after - before).normalized) * (width * 0.5f);
        Line(painter, Project(center - cross), Project(center + cross), color, 3f);
    }

    private void DrawPitPath(Painter2D painter, TrackPitPoint[] path, Color color, TrackUtilityTab tab)
    {
        if ((path?.Length ?? 0) < 2)
            return;
        DrawSpline(painter, new TrackPitSpline(path), color, 2f);
        if (activeTab != tab)
            return;
        for (int i = 0; i < path.Length; i++)
        {
            bool selected = activeTab == tab && selectedPoints.Contains(i);
            Dot(painter, PitPoint(path[i]), selected ? Color.white : color, selected ? 4.5f : 2.5f);
        }
    }

    private void DrawSpline(Painter2D painter, TrackPitSpline spline, Color color, float width)
    {
        painter.strokeColor = color;
        painter.lineWidth = width;
        painter.BeginPath();
        painter.MoveTo(Project(main.ToTrackSpace(spline.Sample(0f))));
        for (int i = 1; i <= 96; i++)
            painter.LineTo(Project(main.ToTrackSpace(spline.Sample(i / 96f))));
        painter.Stroke();
    }

    private void Ribbon(Painter2D painter, Vector3[] first, Vector3[] second, Color color)
    {
        painter.fillColor = color;
        painter.BeginPath();
        painter.MoveTo(Project(first[0]));
        for (int i = 1; i < first.Length; i++)
            painter.LineTo(Project(first[i]));
        painter.ClosePath();
        painter.MoveTo(Project(second[second.Length - 1]));
        for (int i = second.Length - 2; i >= 0; i--)
            painter.LineTo(Project(second[i]));
        painter.ClosePath();
        painter.Fill(FillRule.OddEven);
    }

    private static void Line(Painter2D painter, Vector2 a, Vector2 b, Color color, float width)
    {
        painter.strokeColor = color;
        painter.lineWidth = width;
        painter.BeginPath();
        painter.MoveTo(a);
        painter.LineTo(b);
        painter.Stroke();
    }

    private static void Dot(Painter2D painter, Vector2 point, Color color, float radius)
    {
        painter.fillColor = color;
        painter.BeginPath();
        painter.Arc(point, radius, Angle.Degrees(0f), Angle.Degrees(360f));
        painter.ClosePath();
        painter.Fill();
    }

    private Vector2 Project(Vector3 point) => mapCenter + new Vector2(point.x - worldCenter.x, worldCenter.y - point.z) * scale;
    private Vector2 MainPoint(int index) => Project(main.ToTrackSpace(new Vector3(points[index].x, 0f, points[index].z)));
    private Vector2 PitPoint(TrackPitPoint point) => Project(main.ToTrackSpace(TrackPitSpline.Position(point)));

    private Vector2 ApronPoint(int index)
    {
        float distance = main.ControlFraction(index) * track.lengthMiles * TrackDefinition.MetersPerMile;
        Vector3 center = main.Sample(distance, out var forward, out _);
        Vector2 widths = main.Widths(distance);
        Vector3 inward = Vector3.Cross(Vector3.up, forward) * main.InsideSign;
        return Project(center + inward * (widths.x * 0.5f + widths.y));
    }

    private void Click(PointerDownEvent evt)
    {
        if (evt.button == 1)
        {
            CancelDrag();
            SelectionChanged?.Invoke(Array.Empty<int>());
            evt.StopPropagation();
            return;
        }
        if (evt.button == 2)
        {
            ResetView();
            evt.StopPropagation();
            return;
        }
        if (evt.button != 0 || main == null || scale <= 0f || dragPointer != -1)
            return;

        dragPointer = evt.pointerId;
        dragStart = previousPointer = evt.localPosition;
        dragging = false;
        selectingRectangle = evt.shiftKey;
        draggingImage = !selectingRectangle && !evt.ctrlKey && alignImage && referenceImage != null;
        this.CapturePointer(dragPointer);
        evt.StopPropagation();
    }

    private void MovePointer(PointerMoveEvent evt)
    {
        if (evt.pointerId != dragPointer || scale <= 0f)
            return;

        Vector2 position = evt.localPosition;
        if (!dragging && (position - dragStart).sqrMagnitude < 16f)
            return;

        dragging = true;
        Vector2 delta = position - previousPointer;
        if (draggingImage)
            imageCenter += new Vector2(delta.x, -delta.y) / scale;
        else if (!selectingRectangle)
            viewPan += delta;
        previousPointer = position;
        MarkDirtyRepaint();
        evt.StopPropagation();
    }

    private void FinishDrag(PointerUpEvent evt)
    {
        if (evt.pointerId != dragPointer || evt.button != 0)
            return;
        Vector2 position = evt.localPosition;
        bool box = selectingRectangle && dragging;
        bool click = !dragging && !draggingImage && !alignImage && !selectingRectangle
            && (position - dragStart).sqrMagnitude < 16f && contentRect.Contains(position);
        CancelDrag();
        if (box)
        {
            var rectangle = SelectionRectangle(position);
            var (count, pointAt) = ActivePoints();
            var selection = new System.Collections.Generic.List<int>();
            for (int i = 0; i < count; i++)
            {
                Vector2 point = pointAt(i);
                if (rectangle.Contains(point) && contentRect.Contains(point))
                    selection.Add(i);
            }
            SelectionChanged?.Invoke(selection.ToArray());
        }
        else if (click)
        {
            Vector2 offset = (position - mapCenter) / scale;
            Vector3 source = main.ToSourceSpace(new Vector3(worldCenter.x + offset.x, 0f, worldCenter.y - offset.y));
            var (count, pointAt) = ActivePoints();
            PointClicked?.Invoke(source, Nearest(position, count, pointAt));
        }
        evt.StopPropagation();
    }

    private Rect SelectionRectangle(Vector2 position)
    {
        Vector2 min = Vector2.Min(dragStart, position);
        Vector2 max = Vector2.Max(dragStart, position);
        return new Rect(min, max - min);
    }

    private void DrawSelectionRectangle(Painter2D painter)
    {
        Rect rectangle = SelectionRectangle(previousPointer);
        painter.BeginPath();
        painter.MoveTo(rectangle.min);
        painter.LineTo(new Vector2(rectangle.xMax, rectangle.yMin));
        painter.LineTo(rectangle.max);
        painter.LineTo(new Vector2(rectangle.xMin, rectangle.yMax));
        painter.ClosePath();
        painter.fillColor = new Color(0.4f, 0.8f, 1f, 0.16f);
        painter.Fill();
        painter.strokeColor = Blue;
        painter.lineWidth = 1f;
        painter.Stroke();
    }

    private (int count, Func<int, Vector2> pointAt) ActivePoints()
    {
        int mainCount = track.layoutPoints?.Length ?? 0;
        switch (activeTab)
        {
            case TrackUtilityTab.Track:
            case TrackUtilityTab.RaceMarkers:
                return (mainCount, MainPoint);
            case TrackUtilityTab.Apron:
                return (mainCount, ApronPoint);
        }

        var pit = track.pitRoad;
        var path = activeTab == TrackUtilityTab.PitRoad ? pit?.points
            : activeTab == TrackUtilityTab.PitEntry ? pit?.entrance : pit?.exit;
        return (path?.Length ?? 0, index => PitPoint(path[index]));
    }

    private void CancelDrag()
    {
        if (dragPointer == -1)
            return;
        int pointer = dragPointer;
        dragPointer = -1;
        this.ReleasePointer(pointer);
        MarkDirtyRepaint();
    }

    private void ResetView()
    {
        CancelDrag();
        viewZoom = 1f;
        viewPan = Vector2.zero;
        MarkDirtyRepaint();
    }

    private void Zoom(WheelEvent evt)
    {
        if (main == null || scale <= 0f)
            return;

        if (evt.ctrlKey)
        {
            float zoom = Mathf.Clamp(viewZoom * Mathf.Exp(-evt.delta.y * 0.055f), 0.25f, 20f);
            Vector2 anchor = evt.localMousePosition - contentRect.center;
            viewPan = anchor + (viewPan - anchor) * (zoom / viewZoom);
            viewZoom = zoom;
        }
        else if (alignImage && referenceImage != null && imageWidth > 0f)
        {
            float width = Mathf.Clamp(imageWidth * Mathf.Exp(-evt.delta.y * 0.055f),
                fittedWidth * 0.05f, fittedWidth * 20f);
            Vector2 offset = (evt.localMousePosition - mapCenter) / scale;
            Vector2 anchor = worldCenter + new Vector2(offset.x, -offset.y);
            imageCenter = anchor + (imageCenter - anchor) * (width / imageWidth);
            imageWidth = width;
        }
        else
        {
            return;
        }

        MarkDirtyRepaint();
        evt.StopPropagation();
    }

    private static int Nearest(Vector2 position, int count, Func<int, Vector2> pointAt)
    {
        int selected = -1;
        float closest = 12f * 12f;
        for (int i = 0; i < count; i++)
        {
            float distance = (pointAt(i) - position).sqrMagnitude;
            if (distance < closest)
            {
                closest = distance;
                selected = i;
            }
        }
        return selected;
    }
}
