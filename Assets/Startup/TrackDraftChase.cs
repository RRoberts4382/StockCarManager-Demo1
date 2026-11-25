using System;
using UnityEngine;

internal sealed class TrackDraftChase
{
    private const float ContactLength = VehicleCollision001.ContactLength;
    private const int MinimumPackSize = 4;
    private const float PackBreakGap = ContactLength + 20f;
    private const float RiskGap = ContactLength + 6f;
    private const float RecoveryGap = ContactLength + 3f;
    private const float RiskOpeningSpeed = .45f;
    private const int RiskReviews = 4;
    private const float MinimumLaneSeparation = 1.8f;
    private const float WakeRange = 200f;
    private const float MaximumWakeBoost = 5f * TrackDefinition.MetersPerMile / 3600f;

    private readonly (float NegativeDistance, int Car)[] order;
    private readonly int[] groups, groupTail, riskReviews, wakeTails;
    private readonly float[] groupScore, targetWake, wake;

    public TrackDraftChase(int count)
    {
        order = new (float, int)[count];
        groups = new int[count];
        groupTail = new int[count];
        groupScore = new float[count];
        riskReviews = new int[count];
        wakeTails = new int[count];
        targetWake = new float[count];
        wake = new float[count];
        Regrouping = new bool[count];
    }

    internal bool[] Regrouping { get; }
    internal float WakeBoost(int car) => wake[car];
    internal bool IsDetached(int car) => Regrouping[car] && groups[car] > 0;

    internal void Reset()
    {
        Array.Clear(Regrouping, 0, Regrouping.Length);
        Array.Clear(riskReviews, 0, riskReviews.Length);
        Array.Clear(targetWake, 0, targetWake.Length);
        Array.Clear(wake, 0, wake.Length);
    }

    internal void AdvanceWake(float seconds, bool[] onTrack, bool[] eligible)
    {
        for (int car = 0; car < wake.Length; car++)
            wake[car] = onTrack[car] && eligible[car]
                ? Mathf.MoveTowards(wake[car], targetWake[car], seconds * (targetWake[car] > wake[car] ? .9f : 2f))
                : 0f;
    }

    internal bool CanReceive(int source, int tail, bool attachedTail)
    {
        if (!Regrouping[source] || groups[tail] < 0)
            return false;
        // A local chase group commits to one forward line. A threatened line still
        // beside the main pack can tuck into an established tail there.
        return groups[tail] == 0 ? attachedTail && !Regrouping[tail] :
            groups[tail] <= groups[source] && groups[tail] >= groups[source] - 1 && groupTail[groups[tail]] == tail;
    }

    internal void Review(int raceLeader, bool[] onTrack, bool[] eligible, float[] distances, float[] lanes,
        float[] speeds, int[] ahead, int[] behind)
    {
        if (raceLeader < 0 || !CanDraft(raceLeader))
        {
            Reset();
            return;
        }

        Array.Clear(Regrouping, 0, Regrouping.Length);
        Array.Clear(targetWake, 0, targetWake.Length);
        Array.Fill(groups, -1);
        Array.Fill(groupTail, -1);
        Array.Fill(groupScore, float.NegativeInfinity);
        int count = 0;
        for (int car = 0; car < order.Length; car++)
        {
            if (CanDraft(car) && distances[car] <= distances[raceLeader])
                order[count++] = (-distances[car], car);
        }
        Array.Sort(order, 0, count);

        int packStart = 0;
        int packEnd = 0;
        while (packStart < count)
        {
            packEnd = packStart + 1;
            while (packEnd < count && order[packEnd].NegativeDistance - order[packEnd - 1].NegativeDistance <= PackBreakGap)
                packEnd++;
            if (packEnd - packStart >= MinimumPackSize)
                break;
            packStart = packEnd;
        }
        if (packStart == count)
        {
            Array.Clear(riskReviews, 0, riskReviews.Length);
            return;
        }

        int group = 0;
        for (int rank = packStart; rank < count; rank++)
        {
            int car = order[rank].Car;
            if (rank > packStart && distances[order[rank - 1].Car] - distances[car] > PackBreakGap)
                group++;
            groups[car] = group;
        }

        for (int rank = packStart; rank < count; rank++)
        {
            int car = order[rank].Car;
            group = groups[car];
            if (group > 0)
            {
                if (LinkAhead(car) < 0)
                {
                    int tail = car;
                    int following = 0;
                    for (int follower = LinkBehind(tail); follower >= 0; follower = LinkBehind(tail))
                    {
                        tail = follower;
                        following++;
                    }
                    float score = distances[car] + 2f * Math.Min(5, following);
                    if (score > groupScore[group])
                    {
                        groupScore[group] = score;
                        groupTail[group] = tail;
                    }
                }
                Regrouping[car] = true;
                riskReviews[car] = RiskReviews;
                continue;
            }

            bool wasAtRisk = riskReviews[car] >= RiskReviews;
            int preceding = rank > packStart ? order[rank - 1].Car : car;
            float gap = distances[preceding] - distances[car];
            bool threatened = rank >= packStart + (packEnd - packStart) / 2 && LinkAhead(car) < 0 &&
                (wasAtRisk ? gap >= RecoveryGap : gap >= RiskGap && speeds[preceding] - speeds[car] >= RiskOpeningSpeed);
            riskReviews[car] = threatened ? Math.Min(RiskReviews, riskReviews[car] + 1) : 0;
            if (riskReviews[car] < RiskReviews)
                continue;
            for (int member = car; member >= 0; member = LinkBehind(member))
                Regrouping[member] = true;
        }
        for (int car = 0; car < groups.Length; car++)
            if (groups[car] < 0)
                riskReviews[car] = 0;

        int tailCount = 0;
        for (int rank = packEnd - 1; rank >= packStart; rank--)
        {
            int car = order[rank].Car;
            if (Regrouping[car] || LinkAhead(car) < 0 || LinkBehind(car) >= 0)
                continue;
            bool newLane = true;
            for (int i = 0; i < tailCount; i++)
                if (Mathf.Abs(lanes[car] - lanes[wakeTails[i]]) < MinimumLaneSeparation)
                    newLane = false;
            if (newLane)
                wakeTails[tailCount++] = car;
        }
        if (tailCount < 2)
            return;

        for (int rank = packStart; rank < count; rank++)
        {
            int car = order[rank].Car;
            if (!Regrouping[car] || LinkAhead(car) >= 0)
                continue;
            int lineSize = 1;
            for (int member = LinkBehind(car); member >= 0 && Regrouping[member]; member = LinkBehind(member))
                lineSize++;
            if (lineSize < 2)
                continue;

            float lineSupport = Mathf.Lerp(.6f, 1f, Mathf.Clamp01((lineSize - 2f) / 2f));
            for (int member = car; member >= 0 && Regrouping[member]; member = LinkBehind(member))
                targetWake[member] = lineSupport * CombinedWake(member, tailCount, distances, lanes);
        }

        bool CanDraft(int car) => onTrack[car] && eligible[car] && speeds[car] >= VehicleCollision001.DraftBumpMinimumSpeed;

        int LinkAhead(int car)
        {
            int leader = ahead[car];
            return leader >= 0 && behind[leader] == car && groups[leader] == groups[car] ? leader : -1;
        }

        int LinkBehind(int car)
        {
            int follower = behind[car];
            return follower >= 0 && ahead[follower] == car && groups[follower] == groups[car] ? follower : -1;
        }
    }

    private float CombinedWake(int car, int tailCount, float[] distances, float[] lanes)
    {
        float combined = 0f;
        int wakes = 0;
        for (int i = 0; i < tailCount; i++)
        {
            int tail = wakeTails[i];
            float gap = distances[tail] - distances[car] - ContactLength;
            if (gap < 0f || gap >= WakeRange)
                continue;
            float fraction = gap / WakeRange;
            float halfWidth = Mathf.Lerp(4.6f, 6f, fraction);
            float lateral = Mathf.Abs(lanes[tail] - lanes[car]);
            float alignment = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(halfWidth * .5f, halfWidth, lateral));
            if (alignment <= 0f)
                continue;
            wakes++;
            combined += alignment * Mathf.SmoothStep(1f, 0f, fraction);
        }
        // The normal forward wake is already accounted for in TrackDraft.
        return wakes >= 2 ? MaximumWakeBoost * Mathf.Clamp01(combined * .55f) : 0f;
    }
}
