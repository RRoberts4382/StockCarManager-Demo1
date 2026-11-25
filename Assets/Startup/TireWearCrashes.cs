using UnityEngine;

public sealed partial class TrackPreview
{
    private struct StraightTireMotion
    {
        public Vector3 Position;
        public Vector3 Direction;
        public Quaternion Rotation;
        public float WallDistanceRemaining;
        public float DistanceTraveled;
    }

    private void StartTireWearFailure(int index, TrackPitTraffic.Car traffic)
    {
        int wornOut = SessionRun.ConsumeWornOutTireMask(index);
        if (wornOut == 0 || crashes[index].Active || SessionRun.Retired(index) || SessionRun.Finished(index) ||
            traffic?.InPit == true || traffic?.ApronEntryActive == true || traffic?.ApronExitActive == true)
            return;

        float distance = traffic?.TrackDistance ?? sessionDistances[index];
        float turn = SessionRun.TireHandlingTurnAt(distance);
        float speed = SessionRun.CurrentSpeed(index);
        if (turn == 0f || speed <= 0f)
            return;

        bool green = TrackState is null or SessionTrackState.TrackOpen or SessionTrackState.WhiteFlag;
        bool frontFailure = (wornOut & 0b0011) != 0 && green &&
            speed >= Mathf.Max(20f, .8f * SpeedAt(index, distance, out _, includeDraft: false));
        if (!frontFailure && (wornOut & 0b1100) == 0)
            return;

        float desiredLane = drivingLanesReady ? drivingLanes[index] : TargetDrivingLane(index, distance, traffic);
        float lane = TireHandlingLaneAt(index, distance, desiredLane, out float tireYaw);
        if (frontFailure)
        {
            BeginStraightTireCrash(index, distance, lane, speed);
            return;
        }

        float towardTurn = Mathf.Sign(turn);
        StartSpin(index, -14f * towardTurn);
        ref var crash = ref crashes[index];
        crash.Cause = CrashCause.RearTireWear;
        crash.Lane = lane;
        crash.Yaw = tireYaw;
        crash.Spin = Mathf.Abs(crash.Spin) * towardTurn;
    }

    private void BeginStraightTireCrash(int index, float distance, float lane, float speed)
    {
        var car = cars[index];
        Vector3 position = SampleTrackCarPose(distance, lane, out var trackForward, out var up);
        Quaternion beforeYaw = chasePoseBeforeYaw[index]?.rotation ?? car.localRotation;
        Vector3 direction = Vector3.ProjectOnPlane(beforeYaw * Vector3.forward, Vector3.up).normalized;
        if (Vector3.Dot(trackForward, direction) < .85f)
            direction = Vector3.ProjectOnPlane(trackForward, Vector3.up).normalized;

        float halfWidth = raceCarHalfWidths[index] > 0f ? raceCarHalfWidths[index] : VehicleCollision001.ContactWidth * .5f;
        Vector3 nose = position + direction * (VehicleCollision001.ContactLength * .5f);
        bool hitsWall = TryFindFrontTireWallContact(nose, direction, halfWidth, out float wallDistance);
        SessionRun.RecordWreck(index);
        crashes[index] = new CrashMotion
        {
            Active = true,
            Cause = CrashCause.FrontTireWear,
            ForwardSpeed = speed,
            Lane = lane,
            Straight = new StraightTireMotion
            {
                Position = position,
                Direction = direction,
                Rotation = StraightTireRotation(direction, up),
                WallDistanceRemaining = hitsWall ? wallDistance : float.PositiveInfinity
            }
        };
    }

    private void AdvanceStraightTireCrash(int index, TrackPitTraffic.Car traffic, float seconds)
    {
        ref var crash = ref crashes[index];
        ref var straight = ref crash.Straight;
        float before = traffic?.TrackDistance ?? sessionDistances[index];
        var road = RoadWidths(before);
        bool runoff = float.IsPositiveInfinity(straight.WallDistanceRemaining) &&
            (Mathf.Abs(crash.Lane) > road.x * .5f || straight.DistanceTraveled >= 150f);
        float nextSpeed = Mathf.MoveTowards(crash.ForwardSpeed, 0f, (runoff ? 9f : .75f) * seconds);
        float travel = Mathf.Min(straight.WallDistanceRemaining, (crash.ForwardSpeed + nextSpeed) * .5f * seconds);
        straight.DistanceTraveled += travel;
        straight.WallDistanceRemaining -= travel;
        straight.Position += straight.Direction * travel;

        // Project only onto the nearby route so the game can locate the wreck.
        // The projection can change height and banking, but never the straight XZ trajectory.
        float distance = before + travel;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Vector3 center = TrackPoint(distance, 0f, out var tangent, out _);
            Vector3 along = Vector3.ProjectOnPlane(tangent, Vector3.up).normalized;
            float correction = Vector3.Dot(straight.Position - center, along);
            distance = Mathf.Clamp(distance + correction, before - travel, before + travel * 2f);
        }

        Vector3 datum = TrackPoint(distance, 0f, out _, out _);
        Vector3 across = Vector3.ProjectOnPlane(TrackPoint(distance, 1f, out _, out _) - datum, Vector3.up);
        crash.Lane = Vector3.Dot(straight.Position - datum, across) / Mathf.Max(.01f, across.sqrMagnitude);
        float halfWidth = RoadWidths(distance).x * .5f;
        var surface = TrackPoint(distance, Mathf.Clamp(crash.Lane, -halfWidth, halfWidth), out _, out var up);
        straight.Position.y = surface.y;
        straight.Rotation = StraightTireRotation(straight.Direction, up);
        drivingLanes[index] = crash.Lane;
        if (traffic != null)
        {
            traffic.TrackDistance = distance;
            traffic.Odometer += travel;
        }
        else
            sessionDistances[index] = distance;

        SessionRun.RecordTravel(index, travel, nextSpeed, distance, reducedBurn: true,
            onTrack: Mathf.Abs(crash.Lane) <= halfWidth);
        crash.ForwardSpeed = nextSpeed;
        bool hitWall = straight.WallDistanceRemaining <= 0f;
        if (hitWall)
        {
            SessionRun.ApplyDamage(index, Mathf.Max(35f, nextSpeed * 1.75f));
            collisionFlashRemaining[index] = CollisionFlashSeconds;
        }
        if (hitWall || nextSpeed == 0f)
        {
            crash.ForwardSpeed = 0f;
            SessionRun.Retire(index);
            if (traffic != null)
                traffic.Retired = true;
        }
    }

    private static Quaternion StraightTireRotation(Vector3 direction, Vector3 up)
    {
        direction.y = -Vector3.Dot(direction, up) / Mathf.Max(.1f, up.y);
        return Quaternion.LookRotation(direction, up);
    }
}
