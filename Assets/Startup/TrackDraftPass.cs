using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class TrackDraftPass
{
    private enum PassPhase { OpeningGap, BuildingRun, Passing }

    private const float PreferredSetupGap = 6.6f;
    private const float MinimumSetupGap = 5.1f;
    private const float SetupGapStep = .5f;
    private const float SetupRunEndGap = 1.65f;
    private const float MaximumPullOutGap = 15f * TrackDefinition.MetersPerFoot;
    private const float RunBumperGap = .20f;
    private const float MinimumReserve = 2f;
    private const float MinimumSetupClosingSpeed = 1.32f;
    private const float MinimumPullOutClosingSpeed = 2f * TrackDefinition.MetersPerMile / 3600f;
    private const float MinimumPace = 145f * TrackDefinition.MetersPerMile / 3600f;
    private const float LaneSpacing = 2.65f;
    private const float LaneChangeSeconds = 1.15f;
    private const float ReviewInterval = .30f;
    private const float DecisiveLineAdvantage = 2f;
    private const float ContactLength = VehicleCollision001.ContactLength;
    private const float ContactWidth = VehicleCollision001.ContactWidth;

    private sealed class Attempt
    {
        public readonly int Attacker;
        public readonly int Leader;
        public readonly float SetupGap;
        public readonly float OriginalLane;
        public readonly float[] LineChoiceRolls;
        public readonly List<int> Followers = new List<int>();
        public PassPhase Phase = PassPhase.OpeningGap;
        public int Helper = -1, JoinCandidate = -1;
        public float PhaseTime, PassingLane, WillingFor;

        public Attempt(int attacker, int leader, float setupGap, float originalLane, int count, System.Random random)
        {
            Attacker = attacker;
            Leader = leader;
            SetupGap = setupGap;
            OriginalLane = originalLane;
            LineChoiceRolls = new float[count];
            // Draw once per attempt so frame-to-frame speed noise cannot reroll a driver's preference.
            for (int i = 0; i < count; i++)
                LineChoiceRolls[i] = (float)random.NextDouble();
        }

        public bool IsSupporting(int index) => index != Attacker && index != Leader && index != Helper;
    }

    private readonly List<Attempt> attempts = new List<Attempt>();
    private readonly Attempt[] owners;
    private readonly float[] opportunity;
    private readonly float[] retryAt;
    private readonly List<(int Attacker, int Leader, float Gap, float Score)> candidates =
        new List<(int Attacker, int Leader, float Gap, float Score)>();
    private readonly System.Random lineChoiceRandom = new System.Random(61904);
    private float elapsed;
    private float reviewIn;

    internal bool Active => attempts.Count > 0;
    internal TrackDraftLineMerge LineMerge { get; set; }
    internal bool[] LaneChanges { get; }
    internal float[] BumperGaps { get; }

    public TrackDraftPass(int count)
    {
        owners = new Attempt[count];
        opportunity = new float[count];
        retryAt = new float[count];
        LaneChanges = new bool[count];
        BumperGaps = new float[count];
        Array.Fill(BumperGaps, -1f);
    }

    internal bool SuppressBump(int car, int leader)
    {
        var attempt = owners[car];
        return (attempt != null && (attempt.Attacker == car || attempt.IsSupporting(car))) ||
            (leader >= 0 && owners[leader]?.Attacker == leader);
    }

    internal int LeaderForAttacker(int index) => owners[index]?.Attacker == index ? owners[index].Leader : -1;

    internal bool OwnsCar(int index) => owners[index] != null;

    internal float CommittedLane(int index, float currentLane, float width)
    {
        var attempt = owners[index];
        return attempt != null && attempt.Phase == PassPhase.Passing && index != attempt.Leader && index != attempt.Helper
            ? BoundedPassingLane(attempt.PassingLane, width) : currentLane;
    }

    internal int SnapshotTarget(int index) => owners[index]?.Leader ?? -1;

    internal string SnapshotPhase(int index)
    {
        var attempt = owners[index];
        if (attempt == null)
            return null;
        return attempt.Attacker == index ? attempt.Phase.ToString() :
            attempt.IsSupporting(index) ? "Supporting" : null;
    }

    internal void Update(TrackDraft draft, float lapLength, float seconds, bool enabled,
        bool[] onTrack, bool[] eligible, float[] distances, float[] lanes, float[] widths, float[] speeds,
        float[] paces, float[] targets, IReadOnlyList<SessionCarResult> drivers = null)
    {
        Array.Clear(LaneChanges, 0, LaneChanges.Length);
        Array.Fill(BumperGaps, -1f);
        if (!enabled)
        {
            attempts.Clear();
            Array.Clear(owners, 0, owners.Length);
            Array.Clear(opportunity, 0, opportunity.Length);
            Array.Clear(retryAt, 0, retryAt.Length);
            reviewIn = 0f;
            return;
        }

        elapsed += seconds;
        for (int i = 0; i < attempts.Count;)
        {
            var attempt = attempts[i];
            if (AdvanceAttempt(attempt))
            {
                i++;
                continue;
            }
            End(attempt);
            attempts.RemoveAt(i);
        }

        reviewIn -= seconds;
        if (reviewIn > 0f)
            return;

        reviewIn = ReviewInterval;
        candidates.Clear();
        for (int i = 0; i < owners.Length; i++)
        {
            int leader = draft.CloseFollowLeaders[i];
            float gap = owners[i] == null && AvailableForPass(i) && AvailableForPass(leader) && owners[leader] == null
                ? Gap(i, leader) - ContactLength : -1f;
            float reserve = 0f;
            float usableGap = 0f;
            if (elapsed >= retryAt[i] && gap >= -.05f && gap <= 2.5f &&
                Mathf.Abs(speeds[i] - speeds[leader]) <= .8f)
            {
                int helper = FindSupport(i, lanes[i]);
                if (helper >= 0 && FindPassingLane(i, helper, out _))
                    usableGap = FindSetupGap(i, leader, gap, out reserve);
            }

            if (usableGap <= 0f)
            {
                opportunity[i] = 0f;
                continue;
            }
            float aggression = Aggression(drivers, i);
            float rearPosition = RearPosition(i);
            opportunity[i] += ReviewInterval;
            if (opportunity[i] >= AttemptWait(aggression, rearPosition))
                candidates.Add((i, leader, usableGap, reserve + .25f * (aggression + rearPosition)));
        }

        candidates.Sort((left, right) =>
        {
            int byScore = right.Score.CompareTo(left.Score);
            return byScore != 0 ? byScore : left.Attacker.CompareTo(right.Attacker);
        });
        foreach (var candidate in candidates)
        {
            int attacker = candidate.Attacker;
            if (owners[attacker] != null || owners[candidate.Leader] != null ||
                !AvailableForPass(attacker) || !AvailableForPass(candidate.Leader))
                continue;

            int helper = FindSupport(attacker, lanes[attacker]);
            if (helper < 0 || !FindPassingLane(attacker, helper, out _))
                continue;

            var attempt = new Attempt(attacker, candidate.Leader, candidate.Gap, lanes[attacker],
                owners.Length, lineChoiceRandom);
            opportunity[attacker] = 0f;
            owners[attacker] = owners[candidate.Leader] = attempt;
            SetHelper(attempt, helper);
            if (AdvanceAttempt(attempt))
                attempts.Add(attempt);
            else
                End(attempt);
        }

        bool AdvanceAttempt(Attempt attempt)
        {
            int attacker = attempt.Attacker;
            int leader = attempt.Leader;
            attempt.PhaseTime += seconds;
            if (!IsRunning(attacker) || !IsRunning(leader))
                return false;

            float bumperGap = Gap(attacker, leader) - ContactLength;
            if (attempt.Phase != PassPhase.Passing)
            {
                if (IsRegrouping(attacker) || IsRegrouping(leader) || IsRegrouping(attempt.Helper))
                    return false;
                int helper = FindSupport(attacker, attempt.OriginalLane, attempt);
                if (helper < 0 || draft.CloseFollowLeaders[attacker] != leader || bumperGap > attempt.SetupGap + 1.35f ||
                    OpenPace(attacker, leader, bumperGap, attempt.SetupGap) - speeds[leader] < MinimumReserve - .25f)
                    return false;

                SetHelper(attempt, helper);
                if (attempt.Phase == PassPhase.OpeningGap)
                {
                    if (bumperGap >= attempt.SetupGap - .65f)
                    {
                        attempt.Phase = PassPhase.BuildingRun;
                        attempt.PhaseTime = 0f;
                    }
                    else if (attempt.PhaseTime >= 12f)
                    {
                        return false;
                    }
                }

                if (attempt.Phase == PassPhase.BuildingRun)
                {
                    if (bumperGap <= VehicleBehavior003.DraftPassCorridorReserve || attempt.PhaseTime >= 8f)
                        return false;

                    float closing = speeds[attacker] - speeds[leader];
                    if (bumperGap <= MaximumPullOutGap && closing >= MinimumPullOutClosingSpeed &&
                        TryGetPassingLane(attempt, bumperGap, out float destination, out int passingHelper))
                    {
                        SetHelper(attempt, passingHelper);
                        attempt.Phase = PassPhase.Passing;
                        attempt.PhaseTime = 0f;
                        attempt.PassingLane = destination;
                    }
                }

                if (attempt.Phase != PassPhase.Passing)
                {
                    BumperGaps[attacker] = attempt.Phase == PassPhase.OpeningGap ? attempt.SetupGap : RunBumperGap;
                    return true;
                }
            }

            for (int i = attempt.Followers.Count - 1; i >= 0; i--)
            {
                int follower = attempt.Followers[i];
                if (IsRunning(follower))
                    continue;
                owners[follower] = null;
                attempt.Followers.RemoveAt(i);
            }

            int supporter = attempt.Followers.Count > 0 ? attempt.Followers[0] : -1;
            bool settled = Mathf.Abs(lanes[attacker] - attempt.PassingLane) <= .20f;
            if ((settled && (Gap(attacker, leader) <= -ContactLength - .75f ||
                    attempt.PhaseTime >= 2.25f && supporter < 0)) || attempt.PhaseTime >= 9f)
                return false;

            int tail = supporter >= 0 ? attempt.Followers[attempt.Followers.Count - 1] : attacker;
            if (attempt.Helper >= 0 && !CanSupport(attempt.Helper, tail, attempt.OriginalLane,
                    Gap(attempt.Helper, tail), attempt))
                SetHelper(attempt, -1);

            int join = FindJoiner(attempt, tail, supporter);
            if (join != attempt.JoinCandidate)
            {
                attempt.JoinCandidate = join;
                attempt.WillingFor = 0f;
            }
            if (join >= 0)
            {
                attempt.WillingFor += seconds;
                if (attempt.WillingFor >= JoinWait(Aggression(drivers, join)))
                {
                    SetHelper(attempt, -1);
                    owners[join] = attempt;
                    attempt.Followers.Add(join);
                    attempt.JoinCandidate = -1;
                    attempt.WillingFor = 0f;
                }
            }

            SetPassingTarget(attacker, attempt);
            foreach (int follower in attempt.Followers)
                SetPassingTarget(follower, attempt);
            if (bumperGap >= 0f && Mathf.Abs(lanes[attacker] - lanes[leader]) < ContactWidth + .10f)
                BumperGaps[attacker] = RunBumperGap;

            return true;
        }

        void SetPassingTarget(int index, Attempt attempt)
        {
            targets[index] = PassingTarget(index, attempt);
            LaneChanges[index] = true;
        }

        float PassingTarget(int index, Attempt attempt) => BoundedPassingLane(attempt.PassingLane, widths[index]);

        bool IsRunning(int i) => i >= 0 && eligible[i] && speeds[i] >= MinimumPace;

        bool IsRegrouping(int i) => i >= 0 && LineMerge?.Regrouping[i] == true;

        bool AvailableForPass(int i) => IsRunning(i) && !IsRegrouping(i) && LineMerge?.ReservesCar(i) != true;

        float RearPosition(int index)
        {
            int ahead = 0, others = 0;
            for (int i = 0; i < owners.Length; i++)
            {
                if (i == index || !IsRunning(i))
                    continue;
                others++;
                if (distances[i] > distances[index] || distances[i] == distances[index] && i < index)
                    ahead++;
            }
            return others > 0 ? (float)ahead / others : 0f;
        }

        float Gap(int from, int to) =>
            Mathf.Repeat(distances[to] - distances[from] + lapLength * .5f, lapLength) - lapLength * .5f;

        int FindSupport(int front, float sourceLane, Attempt attempt = null)
        {
            int nearest = -1;
            float smallest = ContactLength + 4.3f;
            for (int i = 0; i < owners.Length; i++)
            {
                float gap = Gap(i, front);
                if (gap < smallest && CanSupport(i, front, sourceLane, gap, attempt))
                {
                    nearest = i;
                    smallest = gap;
                }
            }
            return nearest;
        }

        bool CanSupport(int car, int front, float sourceLane, float gap, Attempt attempt) =>
            car != front && (owners[car] == null || attempt?.Helper == car) && AvailableForPass(car) &&
            gap >= ContactLength - .06f && gap < ContactLength + 4.3f &&
            Mathf.Abs(lanes[car] - sourceLane) <= 1f && Mathf.Abs(speeds[car] - speeds[front]) <= 2f;

        int FindJoiner(Attempt attempt, int tail, int supporter)
        {
            int candidate = -1;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < owners.Length; i++)
            {
                float gap = Gap(i, tail);
                if (gap >= nearest || !CanSupport(i, tail, attempt.OriginalLane, gap, attempt))
                    continue;

                float passingSpeed = PredictedPassingSpeed(attempt.Attacker, supporter >= 0 ? supporter : i);
                if (tail != attempt.Attacker)
                    passingSpeed = Mathf.Min(passingSpeed, Mathf.Lerp(speeds[tail], speeds[attempt.Attacker], .45f));
                if (PrefersPassingLine(passingSpeed - StayingSpeed(i, attempt), Aggression(drivers, i),
                        attempt.LineChoiceRolls[i]) && CanMove(i, PassingTarget(i, attempt), attempt))
                {
                    candidate = i;
                    nearest = gap;
                }
            }
            return candidate;
        }

        float OpenPace(int i, int leader, float gap, float openGap) =>
            draft.EstimatePassSetupPace(i, leader, gap, openGap, Mathf.Abs(lanes[i] - lanes[leader]), paces[i]);

        float FindSetupGap(int index, int leader, float gap, out float reserve)
        {
            float lineSpeed = speeds[leader];
            float drive = RaceSessionRun.ReferenceAcceleration(lineSpeed);
            for (float open = PreferredSetupGap; open >= MinimumSetupGap - .01f; open -= SetupGapStep)
            {
                reserve = OpenPace(index, leader, gap, open) - lineSpeed;
                if (reserve < MinimumReserve)
                    continue;

                // The wake strengthens while closing. Check that the chosen
                // run-up still has room for the planned closing speed.
                float halfway = (open + SetupRunEndGap) * .5f;
                float terminal = OpenPace(index, leader, gap, halfway);
                float acceleration = RaceSessionRun.FullThrottleSpeed(lineSpeed, terminal, drive, 1f) - lineSpeed;
                float closingSquared = 2f * acceleration * (open - SetupRunEndGap);
                if (closingSquared >= MinimumSetupClosingSpeed * MinimumSetupClosingSpeed)
                    return open;
            }
            reserve = 0f;
            return 0f;
        }

        float SupportedPace(int i, int helper) =>
            draft.EstimateSplitPace(i, helper, Gap(helper, i) - ContactLength, paces[i]);

        float PredictedPassingSpeed(int i, int helper)
        {
            float speed = speeds[i];
            float terminal = IsRunning(helper) ? SupportedPace(i, helper) : paces[i] - draft.SpeedBoost(i);
            return RaceSessionRun.FullThrottleSpeed(speed, terminal, RaceSessionRun.ReferenceAcceleration(speed), 1.35f);
        }

        float StayingSpeed(int car, Attempt attempt)
        {
            float nearest = float.PositiveInfinity;
            float pace = speeds[attempt.Leader];
            for (int i = 0; i < owners.Length; i++)
            {
                if (i == car || i == attempt.Attacker || owners[i] == attempt && attempt.IsSupporting(i) ||
                    !IsRunning(i) || Mathf.Abs(lanes[i] - attempt.OriginalLane) > 1f)
                    continue;
                float gap = Gap(car, i);
                if (gap > 0f && gap < nearest)
                {
                    nearest = gap;
                    pace = speeds[i];
                }
            }
            return pace;
        }

        bool TryGetPassingLane(Attempt attempt, float bumperGap, out float destination, out int passingHelper)
        {
            int attacker = attempt.Attacker;
            destination = 0f;
            passingHelper = -1;
            float speed = speeds[attacker];
            float clean = paces[attacker] - draft.SpeedBoost(attacker);
            float acceleration = RaceSessionRun.ReferenceAcceleration(speed);
            float atSplit = RaceSessionRun.FullThrottleSpeed(speed, clean, acceleration, LaneChangeSeconds);
            float leaderSpeed = speeds[attempt.Leader];
            float nearest = float.PositiveInfinity;
            for (int helper = 0; helper < owners.Length; helper++)
            {
                float gap = Gap(helper, attacker);
                if (gap >= nearest || !CanSupport(helper, attacker, attempt.OriginalLane, gap, attempt))
                    continue;

                float withHelp = RaceSessionRun.FullThrottleSpeed(atSplit, SupportedPace(attacker, helper), acceleration, 1.4f);
                float gain = ((speed + atSplit) * .5f - leaderSpeed) * LaneChangeSeconds +
                    ((atSplit + withHelp) * .5f - leaderSpeed) * 1.4f;
                if (gain >= bumperGap &&
                    PrefersPassingLine(withHelp - StayingSpeed(helper, attempt), Aggression(drivers, helper),
                        attempt.LineChoiceRolls[helper]) && FindPassingLane(attacker, helper, out float lane, attempt))
                {
                    nearest = gap;
                    passingHelper = helper;
                    destination = lane;
                }
            }
            return passingHelper >= 0;
        }

        bool FindPassingLane(int attacker, int helper, out float destination, Attempt attempt = null)
        {
            float towardCenter = lanes[attacker] > 0f ? -1f : 1f;
            float edge = Mathf.Max(0f, Mathf.Min(widths[attacker], widths[helper]) * .5f - 1.1f);
            for (int side = 0; side < 2; side++)
            {
                float direction = side == 0 ? towardCenter : -towardCenter;
                float lane = Mathf.Clamp(lanes[attacker] + direction * LaneSpacing, -edge, edge);
                if (Mathf.Abs(lane - lanes[attacker]) < ContactWidth + .35f ||
                    !CanMove(attacker, lane, attempt, attempt?.Leader ?? -1) || !CanMove(helper, lane, attempt))
                    continue;
                destination = lane;
                return true;
            }
            destination = 0f;
            return false;
        }

        bool CanMove(int index, float lane, Attempt attempt, int leaderToClear = -1)
        {
            if (Mathf.Abs(lane) > widths[index] * .5f - 1.1f)
                return false;

            if (leaderToClear >= 0 && !CanClearLeader(lanes[index], lane, lanes[leaderToClear],
                Gap(index, leaderToClear) - ContactLength, speeds[index] - speeds[leaderToClear]))
                return false;

            float low = Mathf.Min(lanes[index], lane) - ContactWidth;
            float high = Mathf.Max(lanes[index], lane) + ContactWidth;
            for (int j = 0; j < owners.Length; j++)
            {
                if (j == index || j == leaderToClear || !onTrack[j])
                    continue;
                float otherLane = lanes[j];
                var other = owners[j];
                // A different pass may have already committed to a lane the car has not reached yet.
                float otherTarget = other != null && other != attempt && other.Phase == PassPhase.Passing &&
                    j != other.Leader ? PassingTarget(j, other) : LineMerge?.LaneChanges[j] == true ? targets[j] : otherLane;
                if (Mathf.Max(otherLane, otherTarget) <= low || Mathf.Min(otherLane, otherTarget) >= high)
                    continue;
                float gap = Gap(index, j);
                float future = gap + (speeds[j] - speeds[index]) * LaneChangeSeconds;
                if (Mathf.Min(gap, future) < ContactLength && Mathf.Max(gap, future) > -ContactLength)
                    return false;
            }
            return true;
        }
    }

    private static float BoundedPassingLane(float lane, float width)
    {
        float edge = Mathf.Max(0f, width * .5f - 1.1f);
        return Mathf.Clamp(lane, -edge, edge);
    }

    private static bool CanClearLeader(float lane, float destination, float leaderLane, float bumperGap, float closingSpeed)
    {
        const float reserve = VehicleBehavior003.DraftPassCorridorReserve;
        const float acceleration = VehicleBehavior003.TrafficLateralAcceleration;
        const float lateralSpeed = VehicleBehavior003.MaximumLateralSpeed;
        float direction = Mathf.Sign(destination - lane);
        float moveToClear = Mathf.Max(0f, ContactWidth + reserve + (leaderLane - lane) * direction);
        if (moveToClear > Mathf.Abs(destination - lane))
            return false;

        // A car leaving the draft line starts lateral acceleration from rest.
        // It only needs to stay behind the leader until their sides are clear.
        float rampTime = lateralSpeed / acceleration;
        float rampDistance = lateralSpeed * rampTime * .5f;
        float clearTime = moveToClear <= rampDistance
            ? Mathf.Sqrt(2f * moveToClear / acceleration)
            : rampTime + (moveToClear - rampDistance) / lateralSpeed;
        return bumperGap >= Mathf.Max(0f, closingSpeed) * clearTime + reserve;
    }

    private void SetHelper(Attempt attempt, int helper)
    {
        if (attempt.Helper == helper)
            return;
        if (attempt.Helper >= 0)
            owners[attempt.Helper] = null;
        attempt.Helper = helper;
        if (helper >= 0)
            owners[helper] = attempt;
    }

    private void End(Attempt attempt)
    {
        retryAt[attempt.Attacker] = elapsed + 2f;
        owners[attempt.Attacker] = owners[attempt.Leader] = null;
        SetHelper(attempt, -1);
        foreach (int follower in attempt.Followers)
            owners[follower] = null;
    }

    private static float Aggression(IReadOnlyList<SessionCarResult> drivers, int index)
    {
        float value = drivers?[index].driver?.personalityTraits?.aggression ?? .5f;
        return float.IsNaN(value) ? .5f : Mathf.Clamp01(value);
    }

    private static float AttemptWait(float aggression, float rearPosition) =>
        Mathf.Lerp(7.8f, 2.4f, aggression) * Mathf.Lerp(1f, .6f, rearPosition);

    private static float JoinWait(float aggression) => Mathf.Lerp(.80f, .25f, aggression);

    internal static bool PrefersPassingLine(float speedAdvantage, float aggression, float roll)
    {
        if (speedAdvantage >= DecisiveLineAdvantage)
            return true;
        if (speedAdvantage <= -DecisiveLineAdvantage)
            return false;

        float chance = .25f + .45f * Mathf.Clamp01(aggression) + .18f * speedAdvantage / DecisiveLineAdvantage;
        return roll < chance;
    }
}
