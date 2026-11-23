using System;
using UnityEngine;

internal class VehicleBehavior004 : VehicleBehavior003
{
    internal const string ModelId = "vehicle-behavior-004";
    private const float OutsideLane = .88f;
    private const float InsideLane = 1f;
    private const float FitLaneLimit = .96f;

    public override string Id => ModelId;
    public override string Name => "VehicleBehavior004";

    internal float[] PreferredLineAdvance { get; set; }
    internal float[] PreferredInsideClearance { get; set; }

    protected override float StepTowardPreferredLane(int index, float lane, float target, float seconds) =>
        base.StepTowardPreferredLane(index, lane, target, seconds) + (PreferredLineAdvance?[index] ?? 0f);

    protected override float PreferredLaneEdgeClearance(int index, float lane, float inside) =>
        lane * inside > 0f && PreferredInsideClearance != null
            ? PreferredInsideClearance[index] - .05f : base.PreferredLaneEdgeClearance(index, lane, inside);

    internal virtual Func<float, float> BuildSweepingLine(float lapLength, float inside, TrackDefinition track,
        Func<float, Vector3> heading, Func<float, float, Vector3> point, Func<float, float> width,
        out Func<float, float> trackingWeight, out Func<float, float> kinkSteeringBlend)
    {
        bool oval = track.majorStyle == "Oval";
        Func<float, float> baseline = track.restrictorPlate
            ? _ => RestrictorPlateRacingLine(inside)
            : BuildTrackRacingLine(lapLength, inside, oval, heading, track);
        trackingWeight = null;
        kinkSteeringBlend = null;
        if (!oval)
            return baseline;

        var sweep = FitFrontstretch(lapLength, inside, track, heading, point, width, baseline,
            out var sweepTracking, out float frontStart, out float frontLength, out kinkSteeringBlend);
        var turns = MainTurns(lapLength, inside, heading, frontStart, frontLength);
        float ApexWeight(float distance)
        {
            foreach (var (start, length) in turns)
            {
                float progress = Mathf.Repeat(distance - start, lapLength) / length;
                if (progress < 1f)
                    return Ease(3f * Mathf.Min(progress, 1f - progress));
            }
            return 0f;
        }
        trackingWeight = distance => Mathf.Max(sweepTracking?.Invoke(distance) ?? 0f, Ease(6f * ApexWeight(distance)));
        return distance => Mathf.Lerp(sweep(distance), inside * InsideLane, ApexWeight(distance));
    }

    private Func<float, float> FitFrontstretch(float lapLength, float inside, TrackDefinition track,
        Func<float, Vector3> heading, Func<float, float, Vector3> point, Func<float, float> width,
        Func<float, float> baseline, out Func<float, float> trackingWeight, out float frontStart, out float frontLength,
        out Func<float, float> kinkSteeringBlend)
    {
        trackingWeight = null;
        kinkSteeringBlend = null;
        frontStart = frontLength = 0f;
        if (track.layoutPoints == null || track.layoutPoints.Length < 4)
            return baseline;

        var (startFraction, endFraction) = Frontstretch(track.id);
        if (startFraction == endFraction)
        {
            if (track.minorStyle is not ("Tri-Oval" or "Quad Oval" or "D-Oval"))
                return baseline;
            (startFraction, endFraction) = FindFrontstretch(track.StartFinishFraction, lapLength, heading);
            if (startFraction == endFraction)
                return baseline;
        }

        float start = startFraction * lapLength;
        float length = Mathf.Repeat(endFraction - startFraction, 1f) * lapLength;
        int count = Mathf.Max(8, Mathf.CeilToInt(length / 4f));
        float spacing = length / count;
        bool quad = track.minorStyle == "Quad Oval";
        float[] contactFractions = FrontstretchContacts(track.id);
        float firstContact = contactFractions.Length == 0 ? length * (quad ? 1f / 3f : .5f) : length;
        float lastContact = contactFractions.Length == 0 ? length * (quad ? 2f / 3f : .5f) : 0f;
        foreach (float fraction in contactFractions)
        {
            float station = Mathf.Repeat(fraction - startFraction, 1f) * lapLength;
            firstContact = Mathf.Min(firstContact, station);
            lastContact = Mathf.Max(lastContact, station);
        }
        float outside = -inside;
        float HalfWidth(float distance) => Mathf.Max(0f, width(distance) * .5f - 1.1f);
        Vector3 At(float distance, float fraction) => point(distance, outside * fraction * HalfWidth(distance));
        Vector3 origin = At(start, OutsideLane);
        Vector3 axis = At(start + length, OutsideLane) - origin;
        axis.y = 0f;
        float chord = axis.magnitude;
        if (chord < length * .6f)
            return baseline;
        axis /= chord;
        Vector3 normal = Vector3.Cross(Vector3.up, axis);
        // These layouts keep the sweep closer to the outside edge and need
        // more room for the arc's shoulders.
        bool broadArc = track.id is 5 or 11 or 16 or 20 or 24 or 35;

        // Fit one broad parabola in plan view between the outside entry and exit.
        // Use the actual sampled pavement instead of applying the same offset at every oval.
        float Along(Vector3 position) => Vector3.Dot(position - origin, axis) / chord;
        float ArcWeight(float t) => 4f * t * (1f - t);
        float sagitta = 0f;
        float weightSquared = 0f;
        for (int i = 1; i < count; i++)
        {
            Vector3 preferred = At(start + i * spacing, broadArc ? .65f : .22f);
            float weight = ArcWeight(Along(preferred));
            sagitta += weight * Vector3.Dot(preferred - origin, normal);
            weightSquared += weight * weight;
        }
        if (weightSquared < 1f)
            return baseline;
        sagitta /= weightSquared;

        // One chord-based arc through both kink contacts. The tilt lets the
        // same curve graze each kink even when their geometry is asymmetric.
        int firstKink = Mathf.Clamp(Mathf.RoundToInt(firstContact / spacing), 1, count - 2);
        int lastKink = Mathf.Clamp(Mathf.RoundToInt(lastContact / spacing), firstKink + 1, count - 1);
        Vector3 firstPoint = At(start + firstKink * spacing, -InsideLane);
        Vector3 lastPoint = At(start + lastKink * spacing, -InsideLane);
        float firstT = Along(firstPoint);
        float lastT = Along(lastPoint);
        bool fitKinks = quad && firstT > 0f && lastT < 1f && lastT - firstT > .05f;
        float firstSagitta = fitKinks ? Vector3.Dot(firstPoint - origin, normal) / ArcWeight(firstT) : sagitta;
        float lastSagitta = fitKinks ? Vector3.Dot(lastPoint - origin, normal) / ArcWeight(lastT) : sagitta;
        float InsideLimit(int sample)
        {
            if (!fitKinks)
                return FitLaneLimit;
            float separation = Mathf.Min(Mathf.Abs(sample - firstKink), Mathf.Abs(sample - lastKink));
            return InsideLane - .06f * Ease(separation * spacing / (length * .07f));
        }

        // Tight kinks and asymmetric tri-ovals can require local corrections to
        // the arc. Fit those corrections inside the paved corridor and minimize
        // their bending so touching a bound does not create a new sharp kink.
        float Error(Vector3 position)
        {
            float t = Along(position);
            float depth = fitKinks ? Mathf.LerpUnclamped(firstSagitta, lastSagitta, (t - firstT) / (lastT - firstT)) : sagitta;
            return Vector3.Dot(position - origin, normal) - depth * ArcWeight(t);
        }
        var inner = new float[count + 1];
        var outer = new float[count + 1];
        var correction = new float[count + 1];
        for (int i = 1; i < count; i++)
        {
            float distance = start + i * spacing;
            inner[i] = Error(At(distance, -InsideLimit(i)));
            outer[i] = Error(At(distance, FitLaneLimit));
            correction[i] = fitKinks && (i == firstKink || i == lastKink) ? inner[i]
                : Mathf.Clamp(0f, Mathf.Min(inner[i], outer[i]), Mathf.Max(inner[i], outer[i]));
        }
        for (int stride = Mathf.Max(1, Mathf.RoundToInt(32f / spacing)); stride >= 1; stride /= 2)
        {
            for (int pass = 0; pass < 48; pass++)
            {
                for (int step = 1; step < count; step++)
                {
                    int i = pass % 2 == 0 ? step : count - step;
                    if (fitKinks && (i == firstKink || i == lastKink))
                        continue;
                    float near = correction[Mathf.Max(0, i - stride)] + correction[Mathf.Min(count, i + stride)];
                    float far = correction[Mathf.Max(0, i - 2 * stride)] + correction[Mathf.Min(count, i + 2 * stride)];
                    correction[i] = Mathf.Clamp((4f * near - far) / 6.02f,
                        Mathf.Min(inner[i], outer[i]), Mathf.Max(inner[i], outer[i]));
                }
            }
        }

        var lane = new float[count + 1];
        lane[0] = lane[count] = outside * OutsideLane;
        float shoulder = Mathf.Min(length * (broadArc ? .18f : .06f), broadArc ? 120f : 55f);
        if (fitKinks)
            shoulder = Mathf.Min(shoulder, Mathf.Min(firstKink, count - lastKink) * spacing);
        for (int i = 1; i < count; i++)
        {
            float fitted = Mathf.Lerp(-InsideLimit(i), FitLaneLimit, Mathf.InverseLerp(inner[i], outer[i], correction[i]));
            float edgeBlend = Ease(Mathf.Min(i, count - i) * spacing / shoulder);
            lane[i] = outside * Mathf.Lerp(OutsideLane, fitted, edgeBlend);
        }

        var (entryFraction, exitFraction) = TransitionFractions(track);
        float entryBlend = Mathf.Min(lapLength * entryFraction, (lapLength - length) * .4f);
        float exitBlend = Mathf.Min(lapLength * exitFraction, (lapLength - length) * .4f);
        // The two shorter frontstretches need more runway for the move to the stripe.
        float contactLead = lapLength * (track.id is 5 or 35 ? .022f : 0f);
        float contactTrail = lapLength * (track.id is 5 or 35 ? .018f : 0f);
        frontStart = start;
        frontLength = length;
        float Station(float distance) => Mathf.Repeat(distance - start + entryBlend, lapLength) - entryBlend;
        if (fitKinks)
        {
            kinkSteeringBlend = distance =>
            {
                float station = Station(distance);
                float nearest = Mathf.Min(Mathf.Abs(station - firstKink * spacing), Mathf.Abs(station - lastKink * spacing));
                return 1f - Ease((nearest - 15f) / 25f);
            };
        }
        trackingWeight = distance =>
        {
            float station = Station(distance);
            return Ease(1f + 2f * station / entryBlend) * Ease(1f + 2f * (length - station) / exitBlend);
        };
        return distance =>
        {
            float station = Station(distance);
            float contact = quad ? 0f : Ease(Mathf.Min((station + contactLead) / (firstContact + contactLead),
                (length + contactTrail - station) / (length + contactTrail - lastContact)));
            if (station < 0f)
                return Mathf.Lerp(Mathf.Lerp(baseline(distance), lane[0], Ease(1f + station / entryBlend)),
                    inside * InsideLane, contact);
            if (station > length)
                return Mathf.Lerp(Mathf.Lerp(lane[count], baseline(distance), Ease((station - length) / exitBlend)),
                    inside * InsideLane, contact);
            float sample = station / spacing;
            int i = Mathf.Min(count - 1, (int)sample);
            float t = sample - i;
            float delta = lane[i + 1] - lane[i];
            float incoming = i == 0 ? 0f : MatchedSlope(lane[i] - lane[i - 1], delta);
            float outgoing = i + 1 == count ? 0f : MatchedSlope(delta, lane[i + 2] - lane[i + 1]);
            float fitted = lane[i] + t * (incoming + t * (3f * delta - 2f * incoming - outgoing
                + t * (incoming + outgoing - 2f * delta)));
            if (fitKinks)
            {
                // Preserve the fitted arc and ease outward only near either kink.
                float distanceToKink = Mathf.Min(Mathf.Abs(sample - firstKink), Mathf.Abs(sample - lastKink)) * spacing;
                float taper = 1f - Ease(distanceToKink / 40f);
                float usableHalfWidth = HalfWidth(start + station);
                float offset = TrackDefinition.MetersPerFoot / Mathf.Max(1f, usableHalfWidth);
                fitted += outside * offset * taper;
            }
            return Mathf.Lerp(fitted, inside * InsideLane, contact);
        };
    }

    private static (float Start, float Length)[] MainTurns(float lapLength, float inside,
        Func<float, Vector3> heading, float frontStart, float frontLength)
    {
        int count = Mathf.Clamp(Mathf.CeilToInt(lapLength / 3f), 128, 4096);
        float spacing = lapLength / count;
        float aperture = Mathf.Min(22f, lapLength * .015f);
        var curvature = new float[count];
        var front = new bool[count];
        for (int i = 0; i < count; i++)
        {
            float distance = i * spacing;
            Vector3 before = heading(distance - aperture);
            Vector3 after = heading(distance + aperture);
            before.y = after.y = 0f;
            curvature[i] = Vector3.SignedAngle(before, after, Vector3.up) * inside / (2f * aperture);
            front[i] = Mathf.Repeat(distance - frontStart, lapLength) < frontLength;
        }

        // Some ovals have enough bend in their straights to join both turns
        // at the usual cutoff. Raise it until the main turns separate.
        var best = Array.Empty<(float Start, float Length)>();
        for (float threshold = .035f; threshold <= .20f; threshold *= 1.45f)
        {
            var turns = FindTurnRuns(curvature, front, lapLength, spacing, threshold);
            if (turns.Length >= 2)
                return turns;
            if (turns.Length > best.Length)
                best = turns;
        }
        return best;
    }

    private static (float Start, float Length)[] FindTurnRuns(float[] curvature, bool[] front,
        float lapLength, float spacing, float threshold)
    {
        int count = curvature.Length;
        var corner = new bool[count];
        for (int i = 0; i < count; i++)
            corner[i] = !front[i] && curvature[i] >= threshold;

        // Join short curvature dips inside a corner. A gap on the sweeping
        // frontstretch belongs to its own line and must keep the turns separate.
        int firstCorner = Array.IndexOf(corner, true);
        if (firstCorner < 0)
            return Array.Empty<(float, float)>();
        int maxGap = Mathf.CeilToInt(lapLength * .03f / spacing);
        for (int i = firstCorner + 1; i < firstCorner + count;)
        {
            if (corner[i % count])
            {
                i++;
                continue;
            }
            int gap = i;
            bool crossesFront = false;
            while (i < firstCorner + count && !corner[i % count])
            {
                crossesFront |= front[i % count];
                i++;
            }
            if (!crossesFront && i - gap <= maxGap)
                for (int j = gap; j < i; j++)
                    corner[j % count] = true;
        }

        int boundary = Array.IndexOf(corner, false);
        if (boundary < 0)
            return Array.Empty<(float, float)>();
        var turns = new System.Collections.Generic.List<(float, float)>();
        for (int i = boundary + 1; i <= boundary + count;)
        {
            if (!corner[i % count])
            {
                i++;
                continue;
            }
            int start = i;
            float angle = 0f;
            while (i <= boundary + count && corner[i % count])
            {
                angle += curvature[i % count] * spacing;
                i++;
            }
            if (angle >= 60f)
                turns.Add((start % count * spacing, (i - start) * spacing));
        }
        return turns.ToArray();
    }

    // Match both segments at each sample while keeping the curve between their
    // fitted lane limits. A change in lateral direction has a flat tangent.
    private static float MatchedSlope(float before, float after) =>
        before * after > 0f ? 2f * before * after / (before + after) : 0f;

    // Quintic easing matches the position, slope and curvature at both ends.
    private static float Ease(float value)
    {
        float t = Mathf.Clamp01(value);
        return t * t * t * (t * (6f * t - 15f) + 10f);
    }

    // Entry and exit are measured lap fractions near the outside wall, beyond
    // the frontstretch bends. Several frontstretches cross the lap-distance seam.
    private static (float Start, float End) Frontstretch(int trackId) => trackId switch
    {
        5 => (.960f, .115f), // Richmond
        6 => (.876f, .130f), // Charlotte
        7 => (.892f, .120f), // EchoPark
        9 => (.988f, .189f), // Phoenix
        11 => (.890f, .110f), // Michigan
        14 => (.865f, .135f), // Rockingham
        16 => (.900f, .105f), // Nashville Superspeedway
        20 => (.810f, .965f), // Talladega
        24 => (.880f, .123f), // Chicagoland
        26 => (.863f, .138f), // Las Vegas
        30 => (.110f, .223f), // Daytona
        31 => (.880f, .120f), // Texas
        32 => (.085f, .214f), // Kansas
        35 => (.974f, .149f), // Iowa
        _ => default
    };

    // Oval-specific high-curvature stations. Quad-ovals have two independent
    // contact points. Broad arcs contact the stripe near their center.
    private static float[] FrontstretchContacts(int trackId) => trackId switch
    {
        5 => new[] { .046f, .059f }, // Richmond
        6 => new[] { .953f, .050f }, // Charlotte
        7 => new[] { .968f, .032f }, // EchoPark
        9 => new[] { .065f, .108f }, // Phoenix
        11 => new[] { .000f }, // Michigan
        14 => new[] { .000f }, // Rockingham
        16 => new[] { .000f }, // Nashville Superspeedway
        20 => new[] { .889f }, // Talladega
        24 => new[] { .000f }, // Chicagoland
        26 => new[] { .000f }, // Las Vegas
        30 => new[] { .168f }, // Daytona
        31 => new[] { .964f, .036f }, // Texas
        32 => new[] { .153f }, // Kansas
        35 => new[] { .025f, .096f }, // Iowa
        _ => Array.Empty<float>()
    };


    // Shorter blends work where 003 is already outside at turn exit/entry.
    // Elsewhere, give the car more distance to join the arc without a kink.
    private static (float Entry, float Exit) TransitionFractions(TrackDefinition track) => track.id switch
    {
        5 or 35 => (.11f, .11f), // Richmond and Iowa
        7 => (.055f, .055f), // EchoPark
        9 => (.11f, .065f), // Phoenix
        11 or 26 or 32 => (.08f, .08f), // Michigan, Las Vegas and Kansas
        14 => (.095f, .095f), // Rockingham
        _ => track.restrictorPlate ? (.065f, .065f) : (.035f, .035f)
    };

    private static (float Start, float End) FindFrontstretch(float finish, float lapLength, Func<float, Vector3> heading)
    {
        const float step = .005f;
        float Boundary(float direction)
        {
            for (float offset = .06f; offset <= .20f; offset += step)
            {
                float sample = finish + offset * direction;
                Vector3 before = heading((sample - .02f) * lapLength);
                Vector3 after = heading((sample + .02f) * lapLength);
                before.y = after.y = 0f;
                if (Vector3.Angle(before, after) > 26f)
                    return offset;
            }
            return 0f;
        }
        float entry = Boundary(-1f);
        float exit = Boundary(1f);
        return entry > 0f && exit > 0f ? (finish - entry, finish + exit) : default;
    }
}
