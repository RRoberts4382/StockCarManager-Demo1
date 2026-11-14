using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class TrackImageTrace
{
    private const int MaxTraceDimension = 1000;
    private const int MaxFileBytes = 20 * 1024 * 1024;

    public static TrackLayoutPoint[] Import(string path, TrackDefinition track)
    {
        return TraceImage(LoadTexture(path), track);
    }

    internal static TrackLayoutPoint[] Import(byte[] bytes, TrackDefinition track)
    {
        return TraceImage(LoadTexture(bytes), track);
    }

    internal static Texture2D LoadTexture(string path)
    {
        return LoadTexture(ReadImageBytes(path));
    }

    internal static byte[] ReadImageBytes(string path)
    {
        if (new FileInfo(path).Length > MaxFileBytes)
            throw new ArgumentException("Choose a PNG or JPEG smaller than 20 MB.");
        return File.ReadAllBytes(path);
    }

    internal static Texture2D LoadTexture(byte[] bytes)
    {
        var image = new Texture2D(2, 2);
        if (ImageConversion.LoadImage(image, bytes))
            return image;

        UnityEngine.Object.Destroy(image);
        throw new ArgumentException("Choose a readable PNG or JPEG track diagram.");
    }

    private static TrackLayoutPoint[] TraceImage(Texture2D image, TrackDefinition track)
    {
        try
        {
            return Trace(image.GetPixels32(), image.width, image.height, track);
        }
        finally
        {
            UnityEngine.Object.Destroy(image);
        }
    }

    internal static TrackLayoutPoint[] Trace(Color32[] pixels, int imageWidth, int imageHeight, TrackDefinition track)
    {
        if (!(track.lengthMiles >= 0.01f && track.lengthMiles <= 100f) ||
            !(track.widthMeters >= 3f && track.widthMeters <= 100f))
            throw new ArgumentException("Enter a valid lap length and track width before importing.");
        var banking = track.banking;
        foreach (float angle in new[] { banking.turns12, banking.turns34, banking.frontStraight, banking.backStraight })
        {
            if (!(angle >= 0f && angle <= 60f))
                throw new ArgumentException("Enter banking values from 0 to 60 degrees before importing.");
        }

        float shrink = Mathf.Min(1f, (float)MaxTraceDimension / Mathf.Max(imageWidth, imageHeight));
        int width = Mathf.Max(1, Mathf.RoundToInt(imageWidth * shrink));
        int height = Mathf.Max(1, Mathf.RoundToInt(imageHeight * shrink));
        var dark = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color32 pixel = pixels[(y * imageHeight / height) * imageWidth + x * imageWidth / width];
                dark[y * width + x] = pixel.a >= 128 && 3 * pixel.r + 6 * pixel.g + pixel.b < 1250;
            }
        }

        var outline = FindOuterLoop(dark, width, height);
        if (outline == null)
            throw new ArgumentException("No clear course outline found. Use a dark, closed racing line on a light background.");

        float lapMeters = track.lengthMiles * TrackDefinition.MetersPerMile;
        int count = Mathf.Clamp(Mathf.RoundToInt(lapMeters / (track.widthMeters * 1.4f)), 40, 128);
        var points = Resample(outline, count);
        Vector2 min = points[0];
        Vector2 max = points[0];
        float perimeter = 0f;
        for (int i = 0; i < count; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
            perimeter += Vector2.Distance(points[i], points[(i + 1) % count]);
        }

        float middleX = (min.x + max.x) * 0.5f;
        float middleY = (min.y + max.y) * 0.5f;
        int start = 0;
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            float score = Mathf.Abs(points[i].x - middleX) / (max.x - min.x)
                + 1.5f * (points[i].y - min.y) / (max.y - min.y);
            if (score < best)
            {
                start = i;
                best = score;
            }
        }

        float scale = lapMeters / perimeter;
        var result = new TrackLayoutPoint[count];
        for (int i = 0; i < count; i++)
        {
            int index = (start + i) % count;
            Vector2 position = points[index];
            Vector2 tangent = (points[(index + 2) % count] - points[(index + count - 2) % count]).normalized;
            float straightBank = Mathf.Lerp(track.banking.frontStraight, track.banking.backStraight,
                Mathf.SmoothStep(0f, 1f, 0.5f + (position.y - middleY) / (max.y - min.y)));
            float turnBank = Mathf.Lerp(track.banking.turns34, track.banking.turns12,
                Mathf.SmoothStep(0f, 1f, 0.5f + 2f * (position.x - middleX) / (max.x - min.x)));
            float turnWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.85f, Mathf.Abs(tangent.y)));
            Vector2 offset = (position - points[start]) * scale;
            result[i] = new TrackLayoutPoint
            {
                x = offset.x,
                z = offset.y,
                banking = Mathf.Lerp(straightBank, turnBank, turnWeight)
            };
        }
        return result;
    }

    private static List<Vector2> FindOuterLoop(bool[] dark, int width, int height)
    {
        int stride = width + 1;
        var edges = new Dictionary<int, int>();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (!dark[index])
                    continue;

                int corner = y * stride + x;
                if (y == 0 || !dark[index - width]) AddEdge(edges, corner, 0);
                if (x == width - 1 || !dark[index + 1]) AddEdge(edges, corner + 1, 1);
                if (y == height - 1 || !dark[index + width]) AddEdge(edges, corner + stride + 1, 2);
                if (x == 0 || !dark[index - 1]) AddEdge(edges, corner + stride, 3);
            }
        }

        int[] steps = { 1, stride, -1, -stride };
        List<Vector2> largest = null;
        float largestArea = width * height * 0.04f;
        foreach (int start in new List<int>(edges.Keys))
        {
            while (edges.TryGetValue(start, out int startMask))
            {
                int vertex = start;
                int direction = NextDirection(startMask, 0);
                var loop = new List<Vector2>();
                float twiceArea = 0f;
                do
                {
                    int remaining = edges[vertex] & ~(1 << direction);
                    if (remaining == 0)
                        edges.Remove(vertex);
                    else
                        edges[vertex] = remaining;

                    var point = new Vector2(vertex % stride, vertex / stride);
                    loop.Add(point);
                    vertex += steps[direction];
                    twiceArea += point.x * (vertex / stride) - point.y * (vertex % stride);
                    if (vertex == start || !edges.TryGetValue(vertex, out int nextMask))
                        break;
                    direction = NextDirection(nextMask, direction);
                } while (true);

                float area = twiceArea * 0.5f;
                if (vertex == start && area > largestArea)
                {
                    largestArea = area;
                    largest = loop;
                }
            }
        }
        return largest;
    }

    private static void AddEdge(Dictionary<int, int> edges, int corner, int direction)
    {
        edges.TryGetValue(corner, out int mask);
        edges[corner] = mask | (1 << direction);
    }

    private static int NextDirection(int mask, int direction)
    {
        // At a diagonal contact, follow the boundary of the same filled pixel first.
        int left = (direction + 1) % 4;
        if ((mask & (1 << left)) != 0) return left;
        if ((mask & (1 << direction)) != 0) return direction;
        int right = (direction + 3) % 4;
        return (mask & (1 << right)) != 0 ? right : (direction + 2) % 4;
    }

    private static Vector2[] Resample(List<Vector2> outline, int count)
    {
        int size = outline.Count;
        var distances = new float[size + 1];
        for (int i = 0; i < size; i++)
            distances[i + 1] = distances[i] + Vector2.Distance(outline[i], outline[(i + 1) % size]);

        var points = new Vector2[count];
        int edge = 0;
        for (int i = 0; i < count; i++)
        {
            float distance = distances[size] * i / count;
            while (distances[edge + 1] < distance)
                edge++;
            // Average nearby vertices to soften pixel stair steps and small marks.
            int radius = Mathf.Clamp(size / count / 3, 2, 12);
            Vector2 sum = Vector2.zero;
            for (int j = -radius; j <= radius; j++)
                sum += outline[(edge + j + size) % size];
            points[i] = sum / (2 * radius + 1);
        }
        return points;
    }
}
