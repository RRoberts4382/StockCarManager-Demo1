using System.Collections.Generic;
using UnityEngine;

internal static class TrackGrassLayoutMask
{
    private const byte Pavement = 1;
    private const byte Racing = 2;
    private const byte Exterior = 4;
    private const float ShoulderWidth = 3.8f;
    private const float MinimumPixelSize = .5f;
    private const int MaximumPixelsPerAxis = 1024;

    internal static Texture2D Build(IReadOnlyList<(Vector3[] Vertices, int[] Triangles, bool Racing)> surfaces,
        bool oval, Vector2 finishPosition, Vector2 finishForward, out Vector4 uvBounds)
    {
        uvBounds = default;
        if (surfaces.Count == 0)
            return null;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        bool hasRacing = false;
        foreach (var surface in surfaces)
        {
            hasRacing |= surface.Racing;
            foreach (Vector3 vertex in surface.Vertices)
            {
                var position = new Vector2(vertex.x, vertex.z);
                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
            }
        }
        if (min.x >= max.x || min.y >= max.y)
            return null;

        Vector2 trackSpan = max - min;
        float padding = ShoulderWidth * 2f;
        min -= Vector2.one * padding;
        Vector2 paddedSpan = trackSpan + Vector2.one * (padding * 2f);
        float pixelSize = Mathf.Max(MinimumPixelSize,
            Mathf.Max(paddedSpan.x, paddedSpan.y) / MaximumPixelsPerAxis);
        int width = Mathf.Min(MaximumPixelsPerAxis, Mathf.CeilToInt(paddedSpan.x / pixelSize));
        int height = Mathf.Min(MaximumPixelsPerAxis, Mathf.CeilToInt(paddedSpan.y / pixelSize));
        uvBounds = new Vector4(min.x, min.y, 1f / (width * pixelSize), 1f / (height * pixelSize));

        var coverage = new byte[width * height];
        foreach (var surface in surfaces)
        {
            byte flags = surface.Racing ? (byte)(Pavement | Racing) : Pavement;
            var points = new Vector2[surface.Vertices.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 vertex = surface.Vertices[i];
                points[i] = (new Vector2(vertex.x, vertex.z) - min) / pixelSize - Vector2.one * .5f;
            }
            for (int i = 0; i < surface.Triangles.Length; i += 3)
            {
                Rasterize(points[surface.Triangles[i]], points[surface.Triangles[i + 1]],
                    points[surface.Triangles[i + 2]], coverage, width, height, flags);
            }
        }

        if (hasRacing)
            MarkExterior(coverage, width, height);
        float[] pavementDistance = SquaredDistance(coverage, width, height, Pavement);
        float[] racingDistance = hasRacing ? SquaredDistance(coverage, width, height, Racing) : null;
        var pixels = new Color32[coverage.Length];
        float sweepDepth = Mathf.Clamp(Mathf.Min(trackSpan.x, trackSpan.y) * .35f, 22f, 122f);
        float period = Mathf.Clamp(sweepDepth * .18f, 8f, 13f);
        Vector2 mowAcross = new Vector2(-finishForward.y, finishForward.x).normalized;
        float finishFade = Mathf.Max(35f, sweepDepth * 2.8f);
        float shoulderPixels = ShoulderWidth / pixelSize + .5f;
        float sweepPixels = sweepDepth / pixelSize + .5f;
        float maxShoulderSquared = shoulderPixels * shoulderPixels;
        float maxSweepSquared = sweepPixels * sweepPixels;
        var unmarked = new Color32(128, 0, 0, 255);
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = z * width + x;
                pixels[index] = unmarked;
                if ((coverage[index] & Pavement) != 0)
                    continue;

                if (pavementDistance[index] < maxShoulderSquared)
                {
                    float shoulderDistance = (Mathf.Sqrt(pavementDistance[index]) - .5f) * pixelSize;
                    float wear = 1f - SmoothStep(.15f, ShoulderWidth, shoulderDistance);
                    pixels[index].g = (byte)Mathf.RoundToInt(wear * 255f);
                }

                if (racingDistance == null || (coverage[index] & Exterior) != 0 || racingDistance[index] >= maxSweepSquared)
                    continue;

                float raceDistance = (Mathf.Sqrt(racingDistance[index]) - .5f) * pixelSize;
                var position = min + new Vector2(x + .5f, z + .5f) * pixelSize;
                Vector2 fromFinish = position - finishPosition;
                float nearFinish = 1f - SmoothStep(finishFade * .28f, finishFade, fromFinish.magnitude);
                float strength = (1f - SmoothStep(sweepDepth * .48f, sweepDepth, raceDistance))
                    * (oval ? Mathf.Lerp(.72f, 1f, nearFinish) : Mathf.Lerp(.36f, .88f, nearFinish));
                float phase = oval ? raceDistance : Vector2.Dot(fromFinish, mowAcross) + period * .25f;
                float stripe = SmoothStep(-.38f, .38f, Mathf.Cos(phase * (2f * Mathf.PI / period)));
                float mow = .5f + (stripe - .5f) * strength;
                pixels[index].r = (byte)Mathf.RoundToInt(mow * 255f);
            }
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true)
        {
            name = "Track mowing and shoulder wear",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 4
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        return texture;
    }

    private static void Rasterize(Vector2 a, Vector2 b, Vector2 c, byte[] coverage, int width, int height, byte flags)
    {
        Vector2 ab = b - a;
        Vector2 bc = c - b;
        Vector2 ca = a - c;
        float area = ab.x * (c.y - a.y) - ab.y * (c.x - a.x);
        if (Mathf.Abs(area) < .00001f)
            return;

        int left = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
        int right = Mathf.Min(width - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
        int bottom = Mathf.Max(0, Mathf.CeilToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
        int top = Mathf.Min(height - 1, Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
        float winding = Mathf.Sign(area);
        for (int z = bottom; z <= top; z++)
        {
            for (int x = left; x <= right; x++)
            {
                if (winding * (ab.x * (z - a.y) - ab.y * (x - a.x)) >= -.0001f &&
                    winding * (bc.x * (z - b.y) - bc.y * (x - b.x)) >= -.0001f &&
                    winding * (ca.x * (z - c.y) - ca.y * (x - c.x)) >= -.0001f)
                {
                    coverage[z * width + x] |= flags;
                }
            }
        }
    }

    private static void MarkExterior(byte[] coverage, int width, int height)
    {
        // Flood from the padded bounds through everything except the racing ribbon.
        int[] queue = new int[coverage.Length];
        int count = 0;
        TryMarkExterior(0, coverage, queue, ref count);
        for (int read = 0; read < count; read++)
        {
            int i = queue[read];
            int x = i % width;
            if (x > 0)
                TryMarkExterior(i - 1, coverage, queue, ref count);
            if (x < width - 1)
                TryMarkExterior(i + 1, coverage, queue, ref count);
            if (i >= width)
                TryMarkExterior(i - width, coverage, queue, ref count);
            if (i < coverage.Length - width)
                TryMarkExterior(i + width, coverage, queue, ref count);
        }
    }

    private static void TryMarkExterior(int index, byte[] coverage, int[] queue, ref int count)
    {
        if ((coverage[index] & (Racing | Exterior)) != 0)
            return;

        coverage[index] |= Exterior;
        queue[count++] = index;
    }

    private static float[] SquaredDistance(byte[] coverage, int width, int height, byte seed)
    {
        // Squared Euclidean distance is separable into row and column transforms.
        var distance = new float[coverage.Length];
        for (int i = 0; i < coverage.Length; i++)
            distance[i] = (coverage[i] & seed) != 0 ? 0f : float.PositiveInfinity;

        int lineLength = Mathf.Max(width, height);
        var values = new float[lineLength];
        var envelope = new int[lineLength];
        var transition = new float[lineLength + 1];
        for (int z = 0; z < height; z++)
            TransformLine(distance, z * width, 1, width, values, envelope, transition);
        for (int x = 0; x < width; x++)
            TransformLine(distance, x, width, height, values, envelope, transition);
        return distance;
    }

    private static void TransformLine(float[] distance, int offset, int stride, int length,
        float[] values, int[] envelope, float[] transition)
    {
        int last = -1;
        for (int i = 0; i < length; i++)
        {
            float value = distance[offset + i * stride];
            values[i] = value;
            if (float.IsPositiveInfinity(value))
                continue;

            // Each finite input makes a parabola. Keep only the visible portions
            // of their lower envelope and the switches between adjacent sources.
            float crossing = float.NegativeInfinity;
            while (last >= 0)
            {
                int previous = envelope[last];
                crossing = ((value + i * i) - (values[previous] + previous * previous)) / (2f * (i - previous));
                if (crossing > transition[last])
                    break;
                last--;
            }
            envelope[++last] = i;
            transition[last] = last == 0 ? float.NegativeInfinity : crossing;
        }

        if (last < 0)
            return;
        transition[last + 1] = float.PositiveInfinity;
        int active = 0;
        for (int i = 0; i < length; i++)
        {
            while (transition[active + 1] < i)
                active++;
            int source = envelope[active];
            int delta = source - i;
            distance[offset + i * stride] = values[source] + delta * delta;
        }
    }

    private static float SmoothStep(float low, float high, float value)
    {
        float amount = Mathf.Clamp01((value - low) / (high - low));
        return amount * amount * (3f - 2f * amount);
    }
}
