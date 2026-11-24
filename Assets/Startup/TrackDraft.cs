using UnityEngine;

internal sealed class TrackDraft
{
    internal const float MaximumDraftBoostMph = 5f;
    private const float DraftStrengthMultiplier = 2f;
    private const float NonPlateStrength = .25f;
    private const float MphToMetersPerSecond = TrackDefinition.MetersPerMile / 3600f;
    private const float DraftStartSpeed = 95f * MphToMetersPerSecond;
    private const float FullDraftSpeed = 190f * MphToMetersPerSecond;
    private const float ForwardWakeRange = 130f * TrackDefinition.MetersPerFoot;
    private const float RearWakeRange = 24f * TrackDefinition.MetersPerFoot;
    private const float TrafficDraftRange = 65f * TrackDefinition.MetersPerFoot;
    private const float NearWakeHalfWidth = 3f * TrackDefinition.MetersPerFoot;
    private const float FarWakeHalfWidth = 6f * TrackDefinition.MetersPerFoot;
    private const float LeadSupportMph = 2.6f;
    private const float AdditionalLeadSupportMph = .9f;
    private const float MaximumLineBonusMph = 3f;
    private const float LineBonusSaturationCount = 11f;
    private const float FullLineGap = 5f * TrackDefinition.MetersPerFoot;
    private const float MaximumLineGap = 15f * TrackDefinition.MetersPerFoot;
    private const float FullLineAlignment = TrackDefinition.MetersPerFoot;
    private const float LineContactTolerance = .05f;
    private const float NaturalVariationStartSpeed = 45f * MphToMetersPerSecond;
    private const float NaturalVariationFullSpeed = 85f * MphToMetersPerSecond;
    private const float PacingVariationStartSpeed = 20f * MphToMetersPerSecond;
    private const float PacingVariationFullSpeed = 65f * MphToMetersPerSecond;
    private const float NaturalLaneAmplitude = .15f;
    private const float RearTurbulenceAmplitude = .08f;
    private const float SideTurbulenceAmplitude = .05f;
    private const float SideTurbulenceReach = 27f;
    private const float SideTurbulenceWidth = 4.8f;
    private const float CloseFollowSpeed = 120f * MphToMetersPerSecond;
    internal const float CloseFollowAlignment = .6f;
    private const float JoinRange = 120f * TrackDefinition.MetersPerFoot;
    private const float Alignment = 3f * TrackDefinition.MetersPerFoot;
    private readonly float[] boost;
    private readonly float[] lineBoost;
    private readonly float[] speedStrength;
    private readonly int[] lineAhead;
    private readonly int[] lineBehind;
    private readonly float[] lineLinkWeight;
    private readonly float[] carsInLine;
    private readonly float[] carsAheadInLine;
    private readonly bool[] active;
    private readonly bool[] inLine;
    private readonly int[] closeFollowLeaders;
    private readonly float[] bumpIntent;
    private readonly BumpDecision[] bumpDecisions;
    private readonly System.Random bumpRandom = new System.Random(27099);
    private float laneVariationTime;

    private struct BumpDecision
    {
        internal int Leader;
        internal float TimeUntilAttempt;
        internal float Remaining;
    }

    public TrackDraft(int count)
    {
        boost = new float[count];
        lineBoost = new float[count];
        speedStrength = new float[count];
        lineAhead = new int[count];
        lineBehind = new int[count];
        lineLinkWeight = new float[count];
        carsInLine = new float[count];
        carsAheadInLine = new float[count];
        LaneOffsets = new float[count];
        AppliedLaneOffsets = new float[count];
        ApronEdgeAllowance = new float[count];
        active = new bool[count];
        inLine = new bool[count];
        closeFollowLeaders = new int[count];
        bumpIntent = new float[count];
        bumpDecisions = new BumpDecision[count];
        System.Array.Fill(closeFollowLeaders, -1);
    }

    public float SpeedBoost(int index) => boost[index] + lineBoost[index];
    public bool[] InLine => inLine;
    public int[] CloseFollowLeaders => closeFollowLeaders;
    public float[] BumpIntent => bumpIntent;
    public float[] LaneOffsets { get; }
    public float[] AppliedLaneOffsets { get; }
    public float[] ApronEdgeAllowance { get; }

    public void ClearSpeed()
    {
        System.Array.Clear(boost, 0, boost.Length);
        System.Array.Clear(lineBoost, 0, lineBoost.Length);
        System.Array.Fill(closeFollowLeaders, -1);
    }

    public void UpdateSpeed(float lapLength, float seconds, RaceSessionRun run, bool[] onTrack,
        float[] distances, float[] lanes, float[] speeds, float[] pitApproach, bool plate)
    {
        const float contactLength = VehicleCollision001.ContactLength;
        float scale = MphToMetersPerSecond * DraftStrengthMultiplier * (plate ? 1f : NonPlateStrength);
        for (int i = 0; i < speedStrength.Length; i++)
        {
            speedStrength[i] = onTrack[i] && !run.Finished(i) && pitApproach[i] > 15f
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(DraftStartSpeed, FullDraftSpeed, speeds[i]))
                : 0f;
        }

        for (int i = 0; i < speedStrength.Length; i++)
        {
            closeFollowLeaders[i] = -1;
            lineAhead[i] = lineBehind[i] = -1;
            float ownStrength = speedStrength[i];
            if (ownStrength <= 0f)
            {
                boost[i] = 0f;
                continue;
            }

            float frontWake = 0f;
            float rearWake = 0f;
            float secondRearWake = 0f;
            float nearestLead = float.PositiveInfinity;
            float nearestLineAhead = float.PositiveInfinity;
            float nearestLineBehind = float.PositiveInfinity;
            for (int j = 0; j < speedStrength.Length; j++)
            {
                if (i == j || speedStrength[j] <= 0f)
                    continue;

                float gap = TrackGap(lapLength, distances[i], distances[j]);
                float centerDistance = Mathf.Abs(gap);
                float separation = centerDistance - contactLength;
                bool ahead = gap > 0f;
                float range = ahead ? ForwardWakeRange : RearWakeRange;
                if (separation < -LineContactTolerance || separation >= range)
                    continue;

                float lateral = Mathf.Abs(lanes[j] - lanes[i]);
                if (separation < MaximumLineGap && lateral < Alignment)
                {
                    if (gap > 0f && centerDistance < nearestLineAhead)
                    {
                        nearestLineAhead = centerDistance;
                        lineAhead[i] = j;
                        lineLinkWeight[i] = LineLinkWeight(separation, lateral);
                    }
                    else if (gap < 0f && centerDistance < nearestLineBehind)
                    {
                        nearestLineBehind = centerDistance;
                        lineBehind[i] = j;
                    }
                }

                // Keep a line intact during slight bumper contact.
                if (separation < 0f)
                    continue;

                float sharedStrength = Mathf.Min(ownStrength, speedStrength[j]);
                float weight = WakeWeight(separation, lateral, range,
                    ahead ? FarWakeHalfWidth : NearWakeHalfWidth, sharedStrength);
                if (ahead)
                {
                    frontWake = Mathf.Max(frontWake, weight);
                    if (gap < nearestLead && separation < TrafficDraftRange && lateral <= CloseFollowAlignment &&
                        speeds[i] >= CloseFollowSpeed && speeds[j] >= CloseFollowSpeed &&
                        WakeWeight(separation, lateral, TrafficDraftRange, FarWakeHalfWidth, sharedStrength) > .1f)
                    {
                        nearestLead = gap;
                        closeFollowLeaders[i] = j;
                    }
                }
                else if (weight > rearWake)
                {
                    secondRearWake = rearWake;
                    rearWake = weight;
                }
                else
                {
                    secondRearWake = Mathf.Max(secondRearWake, weight);
                }
            }

            // Two close followers can help a leader, with less credit for the second.
            // Apply that support within the headroom left by the strongest wake ahead.
            float rearSupportMph = LeadSupportMph * rearWake + AdditionalLeadSupportMph * secondRearWake;
            float targetMph = MaximumDraftBoostMph * frontWake + rearSupportMph * (1f - frontWake);
            boost[i] = MoveBoost(boost[i], targetMph, MaximumDraftBoostMph * ownStrength, scale, seconds);
        }

        for (int i = 0; i < speedStrength.Length; i++)
        {
            carsInLine[i] = LinkedCarCount(i, out carsAheadInLine[i]);
            float maximumMph = MaximumLineBonusMph * speedStrength[i];
            // A pair or trio keeps its existing draft. Additional cars contribute
            // progressively less as the line nears the cap.
            float headroom = 1f - Mathf.Clamp01((carsInLine[i] - 2f) / LineBonusSaturationCount);
            float targetMph = maximumMph * (1f - headroom * headroom);
            lineBoost[i] = MoveBoost(lineBoost[i], targetMph, maximumMph, scale, seconds);
        }
    }

    public void ClearLaneVariation()
    {
        System.Array.Clear(LaneOffsets, 0, LaneOffsets.Length);
        System.Array.Clear(AppliedLaneOffsets, 0, AppliedLaneOffsets.Length);
        System.Array.Clear(ApronEdgeAllowance, 0, ApronEdgeAllowance.Length);
    }

    public void UpdateLaneVariation(float lapLength, float seconds, float[] distances, float[] lanes,
        float[] speeds, bool[] eligible, bool plate, bool pacing = false)
    {
        laneVariationTime += seconds;
        float response = 1f - Mathf.Exp(-seconds / 1.2f);
        float startSpeed = pacing ? PacingVariationStartSpeed : NaturalVariationStartSpeed;
        float fullSpeed = pacing ? PacingVariationFullSpeed : NaturalVariationFullSpeed;
        bool turbulence = plate && !pacing;
        for (int i = 0; i < LaneOffsets.Length; i++)
        {
            float offset = 0f;
            if (eligible[i] && speeds[i] > startSpeed)
            {
                float naturalExposure = pacing ? 1f : 0f;
                float sideExposure = 0f;
                for (int j = 0; !pacing && j < LaneOffsets.Length; j++)
                {
                    if (i == j || !eligible[j] || speeds[j] <= startSpeed)
                        continue;

                    float distance = Mathf.Abs(TrackGap(lapLength, distances[i], distances[j]));
                    float lateral = Mathf.Abs(lanes[j] - lanes[i]);
                    float separation = distance - VehicleCollision001.ContactLength;
                    if (separation >= -LineContactTolerance && separation < MaximumLineGap && lateral < Alignment)
                        naturalExposure = Mathf.Max(naturalExposure, LineLinkWeight(separation, lateral));

                    // Cars alongside can be well outside the forward wake and
                    // overlap longitudinally. Use the strongest nearby line.
                    if (turbulence && carsInLine[j] > 0f && distance < SideTurbulenceReach &&
                        lateral > 1.15f && lateral < SideTurbulenceWidth)
                    {
                        float nearSide = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.15f, 1.8f, lateral));
                        float farSide = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(2.7f, SideTurbulenceWidth, lateral));
                        float longitudinal = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(8f, SideTurbulenceReach, distance));
                        float lineStrength = Mathf.Clamp01(carsInLine[j] / 7f) * speedStrength[j];
                        sideExposure = Mathf.Max(sideExposure, nearSide * farSide * longitudinal * lineStrength);
                    }
                }

                // Separate phases and periods give each driver a gradual change in line.
                float phase = i * 2.399963f;
                float naturalSpeed = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(startSpeed, fullSpeed, speeds[i]));
                offset = NaturalLaneAmplitude * naturalExposure * naturalSpeed *
                    Mathf.Sin(laneVariationTime * (.20f + .017f * (i % 5)) + phase);
                if (turbulence)
                {
                    float rearExposure = Mathf.SmoothStep(0f, 1f, (carsAheadInLine[i] - 1f) / 7f);
                    float rear = RearTurbulenceAmplitude * rearExposure * Mathf.Sin(laneVariationTime * .31f + phase * 1.7f);
                    float side = SideTurbulenceAmplitude * sideExposure * Mathf.Sin(laneVariationTime * .27f + phase * 2.3f);
                    offset += (rear + side) * speedStrength[i];
                }
            }

            float cap = VehicleCollision001.ContactWidth * .75f;
            float next = Mathf.Lerp(LaneOffsets[i], Mathf.Clamp(offset, -cap, cap), response);
            LaneOffsets[i] = offset == 0f && Mathf.Abs(next) < .0001f ? 0f : next;
        }
    }

    private static float LineLinkWeight(float separation, float lateral)
    {
        float gap = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(FullLineGap, MaximumLineGap, separation));
        float alignment = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(FullLineAlignment, Alignment, lateral));
        return gap * alignment;
    }

    private static float WakeWeight(float separation, float lateral, float range, float farHalfWidth, float speedStrength)
    {
        float distanceFraction = separation / range;
        float halfWidth = Mathf.Lerp(NearWakeHalfWidth, farHalfWidth, distanceFraction);
        return Mathf.SmoothStep(1f, 0f, distanceFraction) * Mathf.SmoothStep(1f, 0f, lateral / halfWidth) * speedStrength;
    }

    private float LinkedCarCount(int index, out float aheadCount)
    {
        // Require each pair to agree on its nearest neighbor. A weak link reduces
        // the contribution of every car beyond it.
        float count = 0f;
        float weight = 1f;
        int car = index;
        int ahead = lineAhead[car];
        while (ahead >= 0 && lineBehind[ahead] == car)
        {
            if (ahead == index)
            {
                aheadCount = count;
                return count;
            }

            weight *= lineLinkWeight[car];
            count += weight;
            car = ahead;
            ahead = lineAhead[car];
        }

        aheadCount = count;
        weight = 1f;
        car = index;
        int behind = lineBehind[car];
        while (behind >= 0 && lineAhead[behind] == car)
        {
            weight *= lineLinkWeight[behind];
            count += weight;
            car = behind;
            behind = lineBehind[car];
        }

        return count;
    }

    private static float MoveBoost(float current, float targetMph, float maximumMph, float scale, float seconds)
    {
        float target = targetMph * scale;
        float response = target > current ? 3f : 5f;
        return Mathf.Min(maximumMph * scale, Mathf.MoveTowards(current, target, response * scale * seconds));
    }

    internal static float BumpAttemptChance(float aggression) => Mathf.Lerp(.08f, .55f, aggression);
    internal static float BumpIntensity(float aggression) => Mathf.Lerp(.25f, 1f, aggression);

    public void UpdateBumpIntent(float lapLength, float seconds, System.Collections.Generic.IReadOnlyList<SessionCarResult> drivers,
        float[] distances, float[] lanes, float[] speeds, bool allowBumps)
    {
        for (int i = 0; i < bumpDecisions.Length; i++)
        {
            int leader = closeFollowLeaders[i];
            if (!allowBumps || leader < 0 ||
                Mathf.Min(speeds[i], speeds[leader]) < VehicleCollision001.DraftBumpMinimumSpeed ||
                Mathf.Abs(lanes[i] - lanes[leader]) > VehicleCollision001.DraftBumpAlignment ||
                TrackGap(lapLength, distances[i], distances[leader]) > VehicleCollision001.ContactLength + .4f)
            {
                bumpDecisions[i] = default;
                bumpIntent[i] = 0f;
                continue;
            }

            ref var decision = ref bumpDecisions[i];
            if (decision.Leader != leader + 1)
            {
                decision.Leader = leader + 1;
                decision.TimeUntilAttempt = .75f + 2f * (float)bumpRandom.NextDouble();
                decision.Remaining = 0f;
            }

            decision.TimeUntilAttempt -= seconds;
            decision.Remaining = Mathf.Max(0f, decision.Remaining - seconds);
            float aggression = drivers?[i].driver?.personalityTraits?.aggression ?? .5f;
            if (float.IsNaN(aggression))
                aggression = .5f;
            if (decision.TimeUntilAttempt <= 0f)
            {
                decision.TimeUntilAttempt = 3f + 1.5f * (float)bumpRandom.NextDouble();
                if (bumpRandom.NextDouble() < BumpAttemptChance(aggression))
                    decision.Remaining = 1.6f;
            }
            bumpIntent[i] = decision.Remaining > 0f ? BumpIntensity(aggression) : 0f;
        }
    }

    public void FinishBump(int index)
    {
        bumpDecisions[index].Remaining = 0f;
        bumpIntent[index] = 0f;
    }

    public void UpdatePlateLanes(float lapLength, RaceSessionRun run, bool[] onTrack,
        float[] distances, float[] widths, float[] lanes, float[] targets, float[] speeds, float[] pitApproach, bool holdDraftingLane = false)
    {
        for (int i = 0; i < active.Length; i++)
        {
            active[i] = onTrack[i] && !run.Finished(i) && speeds[i] >= 65f * MphToMetersPerSecond &&
                pitApproach[i] > 15f;
        }

        for (int i = 0; i < active.Length; i++)
        {
            bool frontWake = false;
            bool rearWake = false;
            float bestLine = 0f;
            int partner = -1;
            if (active[i])
            {
                for (int j = 0; j < active.Length; j++)
                {
                    if (i == j || !active[j])
                        continue;

                    float gap = TrackGap(lapLength, distances[i], distances[j]);
                    float lateral = Mathf.Abs(lanes[j] - lanes[i]);
                    float separation = Mathf.Max(0f, Mathf.Abs(gap) - VehicleCollision001.ContactLength);
                    if (lateral < Alignment && separation < TrafficDraftRange && gap != 0f)
                    {
                        if (gap > 0f)
                            frontWake = true;
                        else
                            rearWake = true;
                    }

                    if (gap <= VehicleCollision001.ContactLength || gap > JoinRange || lateral > 3.5f)
                        continue;

                    float score = (1f - gap / JoinRange) * (1f - 0.65f * lateral / 3.5f);
                    if (score > bestLine)
                    {
                        bestLine = score;
                        partner = j;
                    }
                }
            }

            inLine[i] = frontWake || holdDraftingLane && rearWake;
            if (holdDraftingLane && inLine[i])
            {
                // This includes the leading car being pushed: it must not drag its
                // whole line down to the solo low-line target through every turn.
                targets[i] = lanes[i];
            }
            else if (partner >= 0)
            {
                float edge = Mathf.Max(0f, widths[i] * 0.5f - 1.1f);
                targets[i] = Mathf.Clamp(lanes[partner], -edge, edge);
            }
        }
    }

    private static float TrackGap(float lapLength, float from, float to) =>
        Mathf.Repeat(to - from + lapLength * .5f, lapLength) - lapLength * .5f;
}
