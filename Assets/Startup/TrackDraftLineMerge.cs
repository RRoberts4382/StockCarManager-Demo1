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
    private const float MaximumDetachedLaneMove = 4.2f;
    private const float EdgeClearance = 1.1f;
    private const float EntryGap = 2.2f;
    private const float AttachedGap = .45f;
    private const float MaximumEntryGap = 17f;
    private const float MaximumDetachedEntryGap = 50f;
    private const float MaximumFrontEntryGap = 8f;
    private const float MaximumPaceDifference = 1.6f;
    private const float MinimumOpeningSpeed = .4f;
    private const float MergeForecastSeconds = 2.4f;
    private const float DepartureForecastSeconds = .95f;
    private const float LaneSettled = .25f;
    private const float FrontLaneSettled = .20f;
    private const float FrontAlignmentSeconds = .25f;
    private const float MaximumFrontJoinTime = 4f;
    private const float ReviewInterval = .25f;
    private const int RequiredOpeningReviews = 4;
    private const int RequiredChaseReviews = 2;
    private const float MaximumWait = 3f;

    private readonly TrackDraftChase chase;
    private readonly int[] ahead, behind, queue;
    private readonly int[] openingReviews;
    private int queueSize;
    private int queueIndex;
    private int mergeReference;
    private int frontLineFollower;
    private float targetLane;
    private float sourceLane;
    private float maximumEntryGap;
    private float blockedFor;
    private float lastRemaining;
    private float reviewIn;
    private float chaseReviewIn;
    private float frontJoinTime;
    private float frontAlignedFor;
    private bool startedMoving;

    public TrackDraftLineMerge(int count)
    {
        chase = new TrackDraftChase(count);
        ahead = new int[count];
        behind = new int[count];
        queue = new int[count];
        openingReviews = new int[count * count];
        LaneChanges = new bool[count];
    }

    internal bool Active => queueIndex < queueSize;
    internal TrackDraftPass Slingshots { get; set; }
    internal Action<int, int, int> MergeCommitted { get; set; }
    internal bool[] LaneChanges { get; }
    internal bool[] Regrouping => chase.Regrouping;
    internal bool IsChaseMerge { get; private set; }
    internal bool IsFrontJoin { get; private set; }
    internal float PackWakeBoost(int car) => chase.WakeBoost(car);

    internal bool ReservesCar(int car)
    {
        if (!Active)
            return false;
        if (car == mergeReference)
            return true;
        for (int i = queueIndex; i < queueSize; i++)
            if (queue[i] == car)
                return true;
        return false;
    }

    internal void Update(float lapLength, float seconds, bool enabled, bool[] onTrack,
        bool[] eligible, float[] distances, float[] lanes, float[] widths, float[] speeds, float[] targets,
        int raceLeader = -1)
    {
        Array.Clear(LaneChanges, 0, LaneChanges.Length);
        if (!enabled || lapLength <= 0f)
        {
            ResetChase();
            Finish(0f);
            return;
        }

        bool reviewMerge = !Active && (reviewIn -= seconds) <= 0f;
        bool reviewChase = raceLeader >= 0 && (chaseReviewIn -= seconds) <= 0f;
        if (reviewMerge || reviewChase)
            LinkLines();
        if (reviewChase)
        {
            chaseReviewIn = ReviewInterval;
            chase.Review(raceLeader, onTrack, eligible, distances, lanes, speeds, ahead, behind);
        }
        if (raceLeader >= 0)
            chase.AdvanceWake(seconds, onTrack, eligible);
        else
            ResetChase();

        if (!Active)
        {
            if (!reviewMerge)
                return;

            reviewIn = ReviewInterval;
            if (!FindMerge())
                return;
        }

        int car = queue[queueIndex];
        if (IsFrontJoin)
        {
            UpdateFrontJoin(car);
            return;
        }
        if (!CanDraft(car) || !CanDraft(mergeReference) || Mathf.Abs(lanes[mergeReference] - targetLane) > LinkAlignment)
        {
            Finish();
            return;
        }

        float remaining = Mathf.Abs(lanes[car] - targetLane);
        if (remaining <= LaneSettled)
        {
            mergeReference = car;
            queueIndex++;
            blockedFor = 0f;
            startedMoving = false;
            if (!Active)
                Finish();
            return;
        }

        float gap = queueIndex == 0 ? EntryGap : AttachedGap;
        bool clear = CanJoin(car, mergeReference, targetLane, sourceLane, gap, maximumEntryGap);
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
            if (IsChaseMerge)
                targets[mergeReference] = lanes[mergeReference];
        }

        void UpdateFrontJoin(int index)
        {
            frontJoinTime += seconds;
            float rearGap = distances[index] - distances[mergeReference];
            float followerGap = distances[mergeReference] - distances[frontLineFollower];
            float destination = lanes[mergeReference];
            if (raceLeader < 0 || frontJoinTime >= MaximumFrontJoinTime || !CanDraft(index) || !CanDraft(mergeReference) ||
                !CanDraft(frontLineFollower) || Mathf.Abs(lanes[frontLineFollower] - destination) > LinkAlignment ||
                followerGap < ContactLength - .05f || followerGap >= MaxLinkDistance ||
                rearGap < ContactLength - .05f || rearGap > ContactLength + MaximumFrontEntryGap ||
                Mathf.Abs(destination) > widths[index] * .5f - EdgeClearance ||
                Slingshots?.OwnsCar(index) == true || Slingshots?.OwnsCar(mergeReference) == true)
            {
                targets[index] = lanes[index];
                Finish();
                return;
            }

            float remaining = Mathf.Abs(lanes[index] - destination);
            frontAlignedFor = remaining <= FrontLaneSettled ? frontAlignedFor + seconds : 0f;
            if (frontAlignedFor >= FrontAlignmentSeconds)
            {
                Finish();
                return;
            }

            float bumperGap = remaining <= TrackDraft.CloseFollowAlignment ? AttachedGap : EntryGap;
            bool clear = CanJoin(index, mergeReference, destination, sourceLane, bumperGap, MaximumFrontEntryGap, frontJoin: true);
            blockedFor = clear ? 0f : blockedFor + seconds;
            if (blockedFor >= MaximumWait)
            {
                targets[index] = lanes[index];
                Finish();
                return;
            }

            LaneChanges[index] = true;
            targets[index] = clear ? destination : lanes[index];
        }

        void LinkLines()
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
        }

        bool FindMerge()
        {
            int front = -1;
            int reference = -1;
            bool chaseMerge = false;
            bool frontJoin = false;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < LaneChanges.Length; i++)
            {
                int firstReview = i * LaneChanges.Length;
                bool regrouping = Regrouping[i];
                bool detached = chase.IsDetached(i);
                float entryLimit = detached ? MaximumDetachedEntryGap : MaximumEntryGap;
                float laneMoveLimit = detached ? MaximumDetachedLaneMove : MaximumLaneMove;
                // A chase can start with a solo car. Ordinary merges still need a source line.
                bool sourceBlocked = regrouping ? ahead[i] >= 0 && behind[ahead[i]] == i :
                    ahead[i] >= 0 || behind[i] < 0 || ahead[behind[i]] != i;
                if (!CanDraft(i) || sourceBlocked)
                {
                    Array.Clear(openingReviews, firstReview, LaneChanges.Length);
                    continue;
                }

                int sourceLineSize = regrouping ? LineSize(i) : 0;
                for (int j = 0; j < LaneChanges.Length; j++)
                {
                    int review = firstReview + j;
                    float lateral = Mathf.Abs(lanes[i] - lanes[j]);
                    float distance = Gap(i, j);
                    bool attachedAhead = ahead[j] >= 0 && behind[ahead[j]] == j;
                    bool joinAtFront = regrouping && distance < 0f;
                    if (joinAtFront)
                        distance = distances[i] - distances[j];
                    bool hasOpening = CanDraft(j) && (joinAtFront
                        ? CanLeadLine(j, attachedAhead, lateral, distance, sourceLineSize)
                        : CanFollowLine(i, j, regrouping, attachedAhead, lateral, distance, laneMoveLimit, entryLimit));
                    if (!hasOpening)
                    {
                        openingReviews[review] = 0;
                        continue;
                    }

                    // Keep an opening stable through consecutive reviews before committing.
                    int required = regrouping ? RequiredChaseReviews : RequiredOpeningReviews;
                    if (openingReviews[review] < required)
                        openingReviews[review]++;
                    // Compare physical gaps, so a nearby front opening beats a distant tail.
                    bool betterOpening = regrouping && !chaseMerge || chaseMerge == regrouping &&
                        (distance < bestDistance || distance == bestDistance && joinAtFront && !frontJoin);
                    float gapLimit = joinAtFront ? MaximumFrontEntryGap : entryLimit;
                    if (openingReviews[review] < required || !betterOpening ||
                        (!joinAtFront && !SourceAvailable(i)) ||
                        !CanJoin(i, j, lanes[j], lanes[i], EntryGap, gapLimit, joinAtFront))
                        continue;

                    front = i;
                    reference = j;
                    chaseMerge = regrouping;
                    frontJoin = joinAtFront;
                    bestDistance = distance;
                }
            }
            if (front < 0)
                return false;

            mergeReference = reference;
            IsChaseMerge = chaseMerge;
            IsFrontJoin = frontJoin;
            maximumEntryGap = chase.IsDetached(front) ? MaximumDetachedEntryGap : MaximumEntryGap;
            sourceLane = lanes[front];
            targetLane = lanes[reference];
            queueIndex = 0;
            queue[0] = front;
            queueSize = 1;
            if (frontJoin)
            {
                frontLineFollower = behind[reference];
                frontJoinTime = frontAlignedFor = 0f;
            }
            else
            {
                for (int follower = FollowerInSourceLine(front, sourceLane); follower >= 0 && queueSize < queue.Length;
                    follower = FollowerInSourceLine(follower, sourceLane))
                    queue[queueSize++] = follower;
            }
            MergeCommitted?.Invoke(front, reference, queueSize);
            return true;
        }

        bool CanLeadLine(int head, bool attachedAhead, float lateral, float distance, int sourceLineSize)
        {
            int follower = behind[head];
            return !attachedAhead && follower >= 0 && ahead[follower] == head &&
                lateral > TrackDraft.CloseFollowAlignment && lateral <= MaximumLaneMove &&
                distance >= ContactLength + EntryGap && distance <= ContactLength + MaximumFrontEntryGap &&
                Slingshots?.OwnsCar(follower) != true && LineSize(head) > sourceLineSize;
        }

        bool CanFollowLine(int source, int tail, bool regrouping, bool attachedTail, float lateral, float distance,
            float laneMoveLimit, float entryLimit)
        {
            float openingSpeed = speeds[tail] - speeds[source];
            return behind[tail] < 0 && (regrouping ? chase.CanReceive(source, tail, attachedTail) : attachedTail) &&
                lateral >= MinimumLaneMove && lateral <= laneMoveLimit && distance >= ContactLength &&
                (regrouping ? Mathf.Abs(openingSpeed) <= MaximumPaceDifference :
                    openingSpeed >= MinimumOpeningSpeed && openingSpeed <= MaximumPaceDifference) &&
                distance + openingSpeed * MergeForecastSeconds <= ContactLength + entryLimit;
        }

        int LineSize(int head)
        {
            int size = 1;
            float lane = lanes[head];
            for (int car = FollowerInSourceLine(head, lane); car >= 0 && size < queue.Length; car = FollowerInSourceLine(car, lane))
                size++;
            return size;
        }

        bool SourceAvailable(int front)
        {
            if (Slingshots?.Active != true)
                return true;
            for (int car = front; car >= 0; car = FollowerInSourceLine(car, lanes[front]))
                if (Slingshots.OwnsCar(car))
                    return false;
            return true;
        }

        int FollowerInSourceLine(int car, float origin)
        {
            int follower = behind[car];
            return follower >= 0 && ahead[follower] == car && Mathf.Abs(lanes[follower] - origin) <= LinkAlignment
                ? follower : -1;
        }

        bool CanJoin(int index, int reference, float destination, float origin, float bumperGap, float entryLimit,
            bool frontJoin = false)
        {
            float lane = lanes[index];
            if (Mathf.Abs(destination) > widths[index] * .5f - EdgeClearance ||
                Slingshots?.OwnsCar(index) == true || Slingshots?.OwnsCar(reference) == true)
                return false;

            float paceDifference = frontJoin ? speeds[index] - speeds[reference] : speeds[reference] - speeds[index];
            if (Mathf.Abs(paceDifference) > MaximumPaceDifference)
                return false;
            float distance = frontJoin ? distances[index] - distances[reference] : Gap(index, reference);
            float futureDistance = distance + paceDifference * MergeForecastSeconds;
            if (Mathf.Min(distance, futureDistance) < ContactLength + bumperGap ||
                Mathf.Max(distance, futureDistance) > ContactLength + entryLimit)
                return false;

            float low = Mathf.Min(lane, destination) - ContactWidth - .20f;
            float high = Mathf.Max(lane, destination) + ContactWidth + .20f;
            for (int j = 0; j < LaneChanges.Length; j++)
            {
                if (j == index || !onTrack[j])
                    continue;

                float otherLane = lanes[j];
                float passingLane = Slingshots?.CommittedLane(j, otherLane, widths[j]) ?? otherLane;
                if (Mathf.Max(otherLane, passingLane) <= low || Mathf.Min(otherLane, passingLane) >= high)
                    continue;

                // A close original-lane partner leaves the corridor early.
                bool departing = (!frontJoin || j != reference) &&
                    passingLane == otherLane && Mathf.Abs(otherLane - origin) <= LinkAlignment;
                float horizon = departing ? DepartureForecastSeconds : MergeForecastSeconds;
                float clearance = ContactLength + (departing ? .04f : j == reference ? bumperGap : .75f);
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

    private void ResetChase()
    {
        if (chaseReviewIn <= 0f)
            return;
        chaseReviewIn = 0f;
        chase.Reset();
    }

    private void Finish(float retrySeconds = .8f)
    {
        queueIndex = queueSize = 0;
        IsChaseMerge = false;
        IsFrontJoin = false;
        blockedFor = 0f;
        startedMoving = false;
        reviewIn = retrySeconds;
        Array.Clear(openingReviews, 0, openingReviews.Length);
    }
}
