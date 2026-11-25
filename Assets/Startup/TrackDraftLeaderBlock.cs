using UnityEngine;

internal sealed class TrackDraftLeaderBlock
{
    private const float MinimumSpeed = 145f * TrackDefinition.MetersPerMile / 3600f;
    private const float MaximumCatchTime = 5f;
    private const float MaximumLaneMove = 3.8f;
    private const float MinimumRearGap = 6f * TrackDefinition.MetersPerFoot;
    private const float SteeringReserveSeconds = .25f;
    private const float EdgeClearance = 1.1f;
    private const float LaneSettled = .20f;
    private const float MaximumMoveTime = 2.5f;
    private const float CooldownSeconds = 3f;
    private const float ContactLength = VehicleCollision001.ContactLength;
    private const float ContactWidth = VehicleCollision001.ContactWidth;
    private const float CorridorReserve = VehicleBehavior003.DraftPassCorridorReserve;

    private int observedLeader = -1, observedThreat = -1;
    private float observedFor, movingFor, cooldown;

    internal int Leader { get; private set; } = -1;
    internal int Target { get; private set; } = -1;
    internal float TargetLane { get; private set; }

    internal bool Update(TrackDraft draft, float lapLength, float seconds, int leader, float aggression,
        bool[] onTrack, bool[] eligible, float[] distances, float[] lanes, float[] widths, float[] speeds,
        float[] lateralSpeeds, bool[] directed, float[] targets)
    {
        if (leader < 0 || leader != observedLeader)
        {
            Finish(0f);
            observedLeader = leader;
        }
        if (leader < 0 || !CanRace(leader) || directed?[leader] == true)
        {
            Finish(0f);
            return false;
        }

        float lane = lanes[leader];
        float edge = Mathf.Max(0f, widths[leader] * .5f - EdgeClearance);
        if (Leader == leader)
        {
            if (Mathf.Abs(lane - TargetLane) <= LaneSettled)
            {
                Finish(CooldownSeconds);
                return false;
            }

            movingFor += seconds;
            if (movingFor >= MaximumMoveTime || !CanRace(Target) || !HasClearance(TargetLane, out _))
            {
                Finish(CooldownSeconds);
                // Stop the lateral move if the opening closes. The ordinary driving target resumes next step.
                targets[leader] = lane;
                return true;
            }
            targets[leader] = TargetLane;
            return true;
        }

        cooldown = Mathf.Max(0f, cooldown - seconds);
        if (cooldown > 0f)
            return false;

        float minimumClosing = Mathf.Lerp(2.5f, 1.5f, aggression) * TrackDefinition.MetersPerMile / 3600f;
        int threat = -1;
        float threatLane = 0f;
        float bestArrival = float.PositiveInfinity;
        for (int i = 0; i < onTrack.Length; i++)
        {
            // Racing distances are unwrapped, so a lapped car will not be treated as a run for the lead.
            float gap = distances[leader] - distances[i] - ContactLength;
            float closing = speeds[i] - speeds[leader];
            float lateralDistance = Mathf.Abs(lanes[i] - lane);
            if (gap <= 0f || closing < minimumClosing || lateralDistance < ContactWidth ||
                lateralDistance > MaximumLaneMove || !CanRace(i) || !draft.LeadsDraftLine(i) ||
                directed?[i] == true && Mathf.Abs(targets[i] - lanes[i]) > LaneSettled)
                continue;

            float catchTime = gap / closing;
            // Keep tracking the same closing line when two runs are almost level.
            float arrival = catchTime - (i == observedThreat ? .20f : 0f);
            float destination = Mathf.Clamp(lanes[i], -edge, edge);
            if (catchTime > MaximumCatchTime || arrival >= bestArrival ||
                Mathf.Abs(destination - lanes[i]) > TrackDraft.CloseFollowAlignment ||
                Mathf.Abs(destination - lane) < ContactWidth || !HasClearance(destination, out float horizon) ||
                gap - closing * horizon < MinimumRearGap)
                continue;

            threat = i;
            threatLane = destination;
            bestArrival = arrival;
        }

        if (threat != observedThreat)
        {
            observedThreat = threat;
            observedFor = 0f;
        }
        if (threat < 0)
            return false;

        observedFor += seconds;
        if (observedFor < Mathf.Lerp(.65f, .35f, aggression))
            return false;

        Leader = leader;
        Target = threat;
        TargetLane = threatLane;
        movingFor = 0f;
        targets[leader] = threatLane;
        return true;

        bool CanRace(int i) => onTrack[i] && eligible[i] && speeds[i] >= MinimumSpeed;

        bool HasClearance(float destination, out float horizon)
        {
            var motion = new LateralMove(lane, destination, lateralSpeeds[leader]);
            horizon = motion.Duration + SteeringReserveSeconds;
            if (Mathf.Abs(destination) > edge)
                return false;

            for (int i = 0; i < onTrack.Length; i++)
            {
                if (i == leader || !onTrack[i])
                    continue;

                float gap = Mathf.Repeat(distances[i] - distances[leader] + lapLength * .5f, lapLength) - lapLength * .5f;
                if (!LongitudinalOverlap(gap, speeds[i] - speeds[leader], horizon, out float enter, out float exit))
                    continue;

                float before = motion.LaneAt(Mathf.Max(0f, enter - SteeringReserveSeconds));
                float after = motion.LaneAt(exit);
                float low = Mathf.Min(before, after) - ContactWidth - CorridorReserve;
                float high = Mathf.Max(before, after) + ContactWidth + CorridorReserve;
                float otherLane = lanes[i];
                float otherTarget = directed?[i] == true ? targets[i] : otherLane;
                if (Mathf.Max(otherLane, otherTarget) > low && Mathf.Min(otherLane, otherTarget) < high)
                    return false;
            }
            return true;
        }
    }

    private void Finish(float wait)
    {
        Leader = Target = observedThreat = -1;
        observedFor = movingFor = 0f;
        cooldown = wait;
    }

    private static bool LongitudinalOverlap(float gap, float relativeSpeed, float horizon, out float enter, out float exit)
    {
        const float clearance = ContactLength + CorridorReserve;
        enter = 0f;
        exit = horizon;
        if (Mathf.Abs(relativeSpeed) < .0001f)
            return Mathf.Abs(gap) < clearance;

        float first = (-clearance - gap) / relativeSpeed;
        float second = (clearance - gap) / relativeSpeed;
        enter = Mathf.Max(0f, Mathf.Min(first, second));
        exit = Mathf.Min(horizon, Mathf.Max(first, second));
        return enter < exit;
    }

    private readonly struct LateralMove
    {
        private const float acceleration = VehicleBehavior003.TrafficLateralAcceleration;
        private const float maximumSpeed = VehicleBehavior003.MaximumLateralSpeed;
        private readonly float from, direction, distance, startSpeed, rampTime;
        internal float Duration { get; }

        internal LateralMove(float from, float to, float lateralSpeed)
        {
            this.from = from;
            direction = Mathf.Sign(to - from);
            distance = Mathf.Abs(to - from);
            startSpeed = Mathf.Clamp(direction * lateralSpeed, 0f, maximumSpeed);
            rampTime = (maximumSpeed - startSpeed) / acceleration;
            float rampDistance = (startSpeed + maximumSpeed) * .5f * rampTime;
            Duration = distance <= rampDistance
                ? (Mathf.Sqrt(startSpeed * startSpeed + 2f * acceleration * distance) - startSpeed) / acceleration
                : rampTime + (distance - rampDistance) / maximumSpeed;
        }

        internal float LaneAt(float time)
        {
            float ramp = Mathf.Min(time, rampTime);
            float travel = (startSpeed + .5f * acceleration * ramp) * ramp + maximumSpeed * (time - ramp);
            return from + direction * Mathf.Min(distance, travel);
        }
    }
}
