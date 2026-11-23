using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    private const int IowaTowGarageCapacity = 24;

    // Travel south in the garage-side aisle and use the northwest access road.
    private static readonly Vector2[] IowaTowGarageEntrance =
    {
        new Vector2(222.8f, 182f), new Vector2(228f, 190f), new Vector2(245f, 210f),
        new Vector2(249f, 221f), new Vector2(257f, 230f), new Vector2(271f, 232f),
        new Vector2(283f, 233f), new Vector2(289.5f, 236.5f), new Vector2(291f, 241f)
    };

    // The empty truck leaves each bay in the hauler-side aisle and uses the
    // existing southwest access road. The aisle stays clear of all parked wrecks.
    private static readonly Vector2[] IowaTowGarageExit =
    {
        new Vector2(263f, 330f), new Vector2(263f, 332f), new Vector2(261.75f, 334.3f),
        new Vector2(257.75f, 336.15f), new Vector2(251.75f, 334.2f), new Vector2(244.75f, 331.25f),
        new Vector2(239.75f, 331.1f), new Vector2(237.35f, 334.5f), new Vector2(240.85f, 341.8f),
        new Vector2(250f, 353f), new Vector2(254f, 358.7f)
    };

    private void GetIowaTowGarageYard(float height, int stall, out int entryGate, out Vector3[] delivery,
        out int exitGate, out Vector3[] departure)
    {
        if (stall < 0 || stall >= IowaTowGarageCapacity)
            throw new ArgumentOutOfRangeException(nameof(stall));
        entryGate = 1;
        exitGate = 3;

        // Fill outward from the middle of the garage. Each stall is a pull-through
        // lane from the entrance aisle to the physically separate exit aisle.
        int row = stall % 2 == 0 ? 11 - stall / 2 : 12 + stall / 2;
        float y = 249f + row * 3.1f;
        const float entryRadius = 7f;
        const float exitRadius = 6.5f;
        const float exitX = 263f;
        var route = new List<Vector3>(300);
        AddSpline(IowaTowGarageEntrance);
        AddStraight(new Vector2(291f, y - entryRadius));
        AddArc(new Vector2(291f - entryRadius, y - entryRadius), entryRadius, towardWest: true);
        AddStraight(new Vector2(270f, y));
        delivery = route.ToArray();

        route.Clear();
        route.Add(ToWorld(new Vector2(270f, y)));
        AddStraight(new Vector2(exitX + exitRadius, y));
        AddArc(new Vector2(exitX + exitRadius, y + exitRadius), exitRadius, towardWest: false);
        AddStraight(IowaTowGarageExit[0]);
        AddSpline(IowaTowGarageExit, skipStart: true);
        departure = route.ToArray();

        Vector3 ToWorld(Vector2 point) => IowaLayoutPoint(point.x, point.y, height);

        void AddSpline(Vector2[] controls, bool skipStart = false)
        {
            var spline = new TrackPitSpline(Array.ConvertAll(controls,
                point => TrackPitSpline.Data(ToWorld(point))));
            int steps = Mathf.CeilToInt(spline.Length / .60f);
            for (int i = skipStart ? 1 : 0; i <= steps; ++i)
            {
                Vector3 point = spline.Sample(i / (float)steps);
                point.y = height;
                route.Add(point);
            }
        }

        void AddStraight(Vector2 to)
        {
            Vector3 start = route[^1], end = ToWorld(to);
            int steps = Mathf.CeilToInt(Vector3.Distance(start, end) / .60f);
            for (int i = 1; i <= steps; i++)
                route.Add(Vector3.Lerp(start, end, i / (float)steps));
        }

        void AddArc(Vector2 center, float radius, bool towardWest)
        {
            int steps = Mathf.CeilToInt(radius * IowaMetersPerPixel * Mathf.PI * .5f / .60f);
            for (int i = 1; i <= steps; i++)
            {
                float angle = i / (float)steps * Mathf.PI * .5f;
                Vector2 offset = towardWest ? new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) :
                    new Vector2(-Mathf.Sin(angle), -Mathf.Cos(angle));
                route.Add(ToWorld(center + offset * radius));
            }
        }
    }

    private static (float Lead, float ApronTangent, float RoadTangent) IowaTowGarageTransition(int gate) =>
        gate == 3 ? (10f, 9f, 24f) : (9f, 12f, 16f);

    // Join the existing home drive several meters inside the gate so the responder
    // can turn onto it in a single arc after traveling along the apron.
    private static (float Inset, float Lead, float ApronTangent, float RoadTangent) IowaTowHomeTransition(
        int gate, bool ambulance) => gate switch
    {
        1 => (6f, 8f, 12f, 13f),
        2 => (14f, 10f, 22f, 22f),
        3 => (5f, 8f, 22f, 39f),
        4 when ambulance => (14f, 12f, 23f, 17f),
        4 => (14f, 12f, 18f, 22f),
        _ => throw new ArgumentOutOfRangeException(nameof(gate))
    };
}
