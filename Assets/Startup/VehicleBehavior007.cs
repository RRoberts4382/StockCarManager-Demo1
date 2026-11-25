using System;
using UnityEngine;

internal sealed class VehicleBehavior007 : VehicleBehavior006
{
    internal new const string ModelId = "vehicle-behavior-007";
    internal static bool OvalPassingEnabled => false;

    public override string Id => ModelId;
    public override string Name => "VehicleBehavior007";

    internal static Vector2 PassingLaneRange(float preferredLane, float width)
    {
        float halfWidth = width * .5f;
        float roadEdge = Mathf.Max(0f, halfWidth - PassEdgeClearance);
        return new Vector2(Mathf.Max(-roadEdge, preferredLane - halfWidth),
            Mathf.Min(roadEdge, preferredLane + halfWidth));
    }

    internal static float ForecastPass(
        (int Index, float Station, float Lane, float Speed) subject,
        (int Index, float Station, float Lane, float Speed) blocker,
        float destinationLane, float initialGap,
        Func<int, float, float, (float TargetSpeed, float BrakingRate)> paceAt,
        Func<int, float, float> blockerLaneAt = null)
    {
        const float step = .25f;
        const int steps = 12;
        const int closingWindow = 4;
        const float minimumGain = .75f;
        const float minimumClosingSpeed = .25f;
        const float overlapTolerance = .5f;
        const float arrivalToleranceSeconds = 1f;
        const float contactLength = VehicleCollision001.ContactLength;

        float subjectTravel = 0f, blockerTravel = 0f;
        float subjectSpeed = subject.Speed, blockerSpeed = blocker.Speed;
        float lane = subject.Lane, lateralSpeed = 0f;
        float blockerLane = blocker.Lane;
        float gainBeforeClosingWindow = 0f;
        for (int i = 0; i < steps; i++)
        {
            if (i == steps - closingWindow)
                gainBeforeClosingWindow = subjectTravel - blockerTravel;

            lateralSpeed = Mathf.Min(MaximumLateralSpeed, lateralSpeed + TrafficLateralAcceleration * step);
            float nextLane = Mathf.MoveTowards(lane, destinationLane, lateralSpeed * step);
            float sampleLane = (lane + nextLane) * .5f;
            float subjectStation = subject.Station + subjectTravel + subjectSpeed * step * .5f;
            float blockerStation = blocker.Station + blockerTravel + blockerSpeed * step * .5f;
            float nextBlockerLane = blockerLaneAt == null ? blockerLane : Mathf.MoveTowards(blockerLane,
                blockerLaneAt(blocker.Index, blockerStation), MaximumLateralSpeed * step);
            float sampleBlockerLane = (blockerLane + nextBlockerLane) * .5f;
            float nextSubjectSpeed = AdvanceSpeed(subjectSpeed, paceAt(subject.Index, subjectStation, sampleLane), step);
            float nextBlockerSpeed = AdvanceSpeed(blockerSpeed, paceAt(blocker.Index, blockerStation, sampleBlockerLane), step);
            float subjectStep = (subjectSpeed + nextSubjectSpeed) * step * .5f;
            float blockerStep = (blockerSpeed + nextBlockerSpeed) * step * .5f;

            // Until it moves out, the closing car can use only the bumper gap ahead of it.
            if (Mathf.Abs(sampleLane - sampleBlockerLane) < VehicleCollision001.ContactWidth)
            {
                float room = Mathf.Max(0f, initialGap + blockerTravel - subjectTravel - contactLength);
                if (subjectStep > blockerStep + room)
                {
                    subjectStep = blockerStep + room;
                    nextSubjectSpeed = Mathf.Min(nextSubjectSpeed, nextBlockerSpeed);
                }
            }

            subjectTravel += subjectStep;
            blockerTravel += blockerStep;
            subjectSpeed = nextSubjectSpeed;
            blockerSpeed = nextBlockerSpeed;
            lane = nextLane;
            blockerLane = nextBlockerLane;
        }

        float gain = subjectTravel - blockerTravel;
        float remainingGap = initialGap - gain;
        float closingSpeed = (gain - gainBeforeClosingWindow) / (closingWindow * step);
        bool cleared = remainingGap <= -contactLength - overlapTolerance;
        // A pass can be worthwhile before it is complete if the car is still closing near overlap.
        bool approachingOverlap = closingSpeed >= minimumClosingSpeed &&
            remainingGap <= contactLength + overlapTolerance + closingSpeed * arrivalToleranceSeconds;
        return gain >= minimumGain && (cleared || approachingOverlap) ? gain : float.NegativeInfinity;
    }

    private static float AdvanceSpeed(float speed, (float TargetSpeed, float BrakingRate) pace, float seconds)
    {
        float acceleration = RaceSessionRun.ReferenceAcceleration(speed);
        if (pace.BrakingRate <= 0f)
            return RaceSessionRun.FullThrottleSpeed(speed, pace.TargetSpeed, acceleration, seconds);
        float rate = pace.TargetSpeed > speed ? acceleration : pace.BrakingRate;
        return Mathf.MoveTowards(speed, pace.TargetSpeed, rate * seconds);
    }
}
