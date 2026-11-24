using System;
using UnityEngine;

internal sealed class TrackDraftLineMerge
{
    private const float ContactLength = VehicleCollision001.ContactLength;
    private const float ContactWidth = VehicleCollision001.ContactWidth;
    private const float MinimumDraftSpeed = 145f * TrackDefinition.MetersPerMile / 3600f;
    private const float LinkAlignment = .9f;
    private const float MaxLinkDistance = ContactLength + 13.4f;
    private const float MinimumLaneMove = ContactWidth + .3f;
    private const float MaximumLaneMove = 3.8f;
    private const float EdgeClearance = 1.1f;
    private const float EntryGap = 2.2f;
    private const float AttachedGap = .45f;
    private const float MaximumEntryGap = 17f;
    private const float MaximumPaceDifference = 1.6f;
    private const float MergeForecastSeconds = 2.4f;
    private const float DepartureForecastSeconds = .95f;
    private const float LaneSettled = .25f;
    private const float ReviewInterval = .25f;
    private const float MaximumWait = 3f;

    private readonly int[] ahead, behind, queue;
    private int queueSize;
    private int queueIndex;
    private int mergeTail;
    private float targetLane;
    private float sourceLane;
    private float blockedFor;
    private float lastRemaining;
    private float reviewIn;
    private bool startedMoving;

    public TrackDraftLineMerge(int count)
    {
        ahead = new int[count];
        behind = new int[count];
        queue = new int[count];
        LaneChanges = new bool[count];
    }

    internal bool Active => queueIndex < queueSize;
    internal bool[] LaneChanges { get; }

    internal void Update(float lapLength, float seconds, bool enabled, bool[] onTrack,
        bool[] eligible, float[] distances, float[] lanes, float[] widths, float[] speeds, float[] targets)
    {
        Array.Clear(LaneChanges, 0, LaneChanges.Length);
        if (!enabled || lapLength <= 0f)
        {
            Finish(0f);
            return;
        }

        if (!Active)
        {
            reviewIn -= seconds;
            if (reviewIn > 0f)
                return;

            reviewIn = ReviewInterval;
            if (!FindLines())
                return;
        }

        int car = queue[queueIndex];
        if (!CanDraft(car) || !CanDraft(mergeTail) || Mathf.Abs(lanes[mergeTail] - targetLane) > LinkAlignment)
        {
            Finish();
            return;
        }

        float remaining = Mathf.Abs(lanes[car] - targetLane);
        if (remaining <= LaneSettled)
        {
            mergeTail = car;
            queueIndex++;
            blockedFor = 0f;
            startedMoving = false;
            if (!Active)
                Finish();
            return;
        }

        float gap = queueIndex == 0 ? EntryGap : AttachedGap;
        bool clear = CanJoin(car, mergeTail, targetLane, sourceLane, gap);
        if (clear && (!startedMoving || remaining < lastRemaining - .0001f))
            blockedFor = 0f;
        else
            blockedFor += seconds;
        if (blockedFor >= MaximumWait)
        {
            Finish();
            return;
        }
        lastRemaining = remaining;
        if (clear || startedMoving)
        {
            LaneChanges[car] = true;
            targets[car] = clear ? targetLane : lanes[car];
            startedMoving = true;
        }

        bool FindLines()
        {
            Array.Fill(ahead, -1);
            Array.Fill(behind, -1);
            for (int i = 0; i < LaneChanges.Length; i++)
            {
                if (!CanDraft(i))
                    continue;

                float nearestAhead = MaxLinkDistance;
                float nearestBehind = MaxLinkDistance;
                for (int j = 0; j < LaneChanges.Length; j++)
                {
                    if (i == j || !CanDraft(j) || Mathf.Abs(lanes[i] - lanes[j]) > LinkAlignment)
                        continue;

                    float offset = Gap(i, j);
                    float distance = Mathf.Abs(offset);
                    if (distance < ContactLength - .05f)
                        continue;

                    if (offset > 0f && distance < nearestAhead)
                    {
                        nearestAhead = distance;
                        ahead[i] = j;
                    }
                    else if (offset < 0f && distance < nearestBehind)
                    {
                        nearestBehind = distance;
                        behind[i] = j;
                    }
                }
            }

            int front = -1;
            int tail = -1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < LaneChanges.Length; i++)
            {
                // The front of a line has a follower and nobody ahead in its wake.
                if (ahead[i] >= 0 || behind[i] < 0 || ahead[behind[i]] != i)
                    continue;

                for (int j = 0; j < LaneChanges.Length; j++)
                {
                    if (behind[j] >= 0 || ahead[j] < 0 || behind[ahead[j]] != j)
                        continue;

                    float lateral = Mathf.Abs(lanes[i] - lanes[j]);
                    float distance = Gap(i, j);
                    if (lateral < MinimumLaneMove || lateral > MaximumLaneMove || distance >= bestDistance ||
                        !CanJoin(i, j, lanes[j], lanes[i], EntryGap))
                        continue;

                    front = i;
                    tail = j;
                    bestDistance = distance;
                }
            }
            if (front < 0)
                return false;

            mergeTail = tail;
            sourceLane = lanes[front];
            targetLane = lanes[tail];
            queueIndex = 0;
            queueSize = 0;
            for (int car = front; car >= 0 && queueSize < queue.Length;)
            {
                queue[queueSize++] = car;
                int follower = behind[car];
                car = follower >= 0 && ahead[follower] == car && Mathf.Abs(lanes[follower] - sourceLane) <= LinkAlignment
                    ? follower : -1;
            }
            return true;
        }

        bool CanJoin(int index, int tail, float destination, float origin, float bumperGap)
        {
            float lane = lanes[index];
            if (Mathf.Abs(destination) > widths[index] * .5f - EdgeClearance)
                return false;

            float paceDifference = speeds[tail] - speeds[index];
            if (Mathf.Abs(paceDifference) > MaximumPaceDifference)
                return false;
            float distance = Gap(index, tail);
            float futureDistance = distance + paceDifference * MergeForecastSeconds;
            if (Mathf.Min(distance, futureDistance) < ContactLength + bumperGap ||
                Mathf.Max(distance, futureDistance) > ContactLength + MaximumEntryGap)
                return false;

            float low = Mathf.Min(lane, destination) - ContactWidth - .20f;
            float high = Mathf.Max(lane, destination) + ContactWidth + .20f;
            for (int j = 0; j < LaneChanges.Length; j++)
            {
                if (j == index || !onTrack[j] || lanes[j] <= low || lanes[j] >= high)
                    continue;

                // A close original-lane partner leaves the corridor early.
                bool departing = Mathf.Abs(lanes[j] - origin) <= LinkAlignment;
                float horizon = departing ? DepartureForecastSeconds : MergeForecastSeconds;
                float clearance = ContactLength + (departing ? .04f : j == tail ? bumperGap : .75f);
                float start = Gap(index, j);
                float end = start + (speeds[j] - speeds[index]) * horizon;
                if (Mathf.Min(start, end) < clearance && Mathf.Max(start, end) > -clearance)
                    return false;
            }
            return true;
        }

        bool CanDraft(int i) => onTrack[i] && eligible[i] && speeds[i] >= MinimumDraftSpeed;

        float Gap(int from, int to) =>
            Mathf.Repeat(distances[to] - distances[from] + lapLength * .5f, lapLength) - lapLength * .5f;
    }

    private void Finish(float retrySeconds = .8f)
    {
        queueIndex = queueSize = 0;
        blockedFor = 0f;
        startedMoving = false;
        reviewIn = retrySeconds;
    }
}
