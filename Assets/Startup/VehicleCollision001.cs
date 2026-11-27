using System;
using UnityEngine;

internal sealed class VehicleCollision001
{
    internal const float ContactLength = 2.9f;
    internal const float ContactWidth = 1.8f;
    internal const float DraftBumpMinimumSpeed = 145f * TrackDefinition.MetersPerMile / 3600f;
    internal const float DraftBumpAlignment = .4f;
    internal const float DraftBumpMaximumClosingSpeed = 1f;
    private const float DraftBumpTransferFraction = .6f;
    private const float ChasingDraftBumpTransferFraction = .84f;
    private readonly bool[,] touching, impactPartners;
    private readonly bool[,] gentleBumpContacts;
    private readonly float[] impactDamage;
    private readonly float[] crashImpulse;
    private readonly float[] draftBumpSpeedChange;
    private readonly bool[] gaveDraftBump, receivedDraftBump;
    private readonly float[] trackHalfWidths;

    internal readonly struct BodyImpact
    {
        internal readonly int First, Second;
        internal readonly Vector2 FirstOffset, SecondOffset;
        internal readonly bool FirstSide, SecondSide;
        internal readonly Vector2? FirstPanelContact, SecondPanelContact;
        internal readonly float Damage;
        internal bool SideToSide => FirstSide && SecondSide;

        internal BodyImpact(int first, int second, Vector2 firstOffset, Vector2 secondOffset,
            bool firstSide, bool secondSide, float damage, Vector2? firstPanelContact = null, Vector2? secondPanelContact = null)
        {
            First = first;
            Second = second;
            FirstOffset = firstOffset;
            SecondOffset = secondOffset;
            FirstSide = firstSide;
            SecondSide = secondSide;
            FirstPanelContact = firstPanelContact;
            SecondPanelContact = secondPanelContact;
            Damage = damage;
        }
    }

    internal Action<BodyImpact> ReportBodyImpact { get; set; }

    public VehicleCollision001(int count) : this(count, null) { }

    internal VehicleCollision001(int count, float[] trackHalfWidths)
    {
        this.trackHalfWidths = trackHalfWidths;
        touching = new bool[count, count];
        impactPartners = new bool[count, count];
        gentleBumpContacts = new bool[count, count];
        impactDamage = new float[count];
        crashImpulse = new float[count];
        draftBumpSpeedChange = new float[count];
        gaveDraftBump = new bool[count];
        receivedDraftBump = new bool[count];
    }

    internal bool HadImpactWith(int first, int second) => impactPartners[first, second];

    internal static float TrackContactWidth(float[] halfWidths, int first, int second) => halfWidths == null
        ? ContactWidth : TrackContactHalfWidth(halfWidths, first) + TrackContactHalfWidth(halfWidths, second);

    private static float TrackContactHalfWidth(float[] halfWidths, int index)
    {
        float width = halfWidths != null && (uint)index < (uint)halfWidths.Length ? halfWidths[index] : 0f;
        return width > 0f && !float.IsInfinity(width) ? width : ContactWidth * .5f;
    }

    public void ClearContacts()
    {
        Array.Clear(touching, 0, touching.Length);
        Array.Clear(gentleBumpContacts, 0, gentleBumpContacts.Length);
    }

    public float CrashImpulse(int index) => crashImpulse[index];

    public bool HadImpact(int index) => impactDamage[index] > 0f;

    public float DraftBumpSpeedChange(int index) => draftBumpSpeedChange[index];
    public bool GaveDraftBump(int index) => gaveDraftBump[index];
    public bool ReceivedDraftBump(int index) => receivedDraftBump[index];

    public void Resolve(RaceSessionRun run, float lapLength, float seconds,
        bool[] onTrack, float[] startDistances, float[] endDistances, float[] startLanes, float[] endLanes,
        bool wrap = true, float[] startYaw = null, float[] endYaw = null, bool[] draftBumpEligible = null,
        int[] strongDraftBumpTargets = null, CornerYawSweep[] cornerYaw = null)
    {
        Array.Clear(draftBumpSpeedChange, 0, draftBumpSpeedChange.Length);
        Array.Clear(gaveDraftBump, 0, gaveDraftBump.Length);
        Array.Clear(receivedDraftBump, 0, receivedDraftBump.Length);
        if (seconds <= 0f)
            return;

        Array.Clear(impactDamage, 0, impactDamage.Length);
        Array.Clear(crashImpulse, 0, crashImpulse.Length);
        Array.Clear(impactPartners, 0, impactPartners.Length);
        for (int first = 0; first < onTrack.Length; first++)
        {
            for (int second = first + 1; second < onTrack.Length; second++)
            {
                if (!onTrack[first] || !onTrack[second])
                {
                    touching[first, second] = false;
                    gentleBumpContacts[first, second] = false;
                    continue;
                }

                float gap = startDistances[second] - startDistances[first];
                if (wrap)
                    gap = Mathf.Repeat(gap + lapLength * 0.5f, lapLength) - lapLength * 0.5f;
                float longitudinal = (endDistances[second] - startDistances[second]) -
                    (endDistances[first] - startDistances[first]);
                float side = startLanes[second] - startLanes[first];
                float lateral = endLanes[second] - endLanes[first] - side;
                float contactWidth = TrackContactWidth(trackHalfWidths, first, second);
                float enter = 0f;
                float exit = 1f;
                bool oriented = startYaw != null && endYaw != null;
                bool contact;
                if (oriented)
                {
                    contact = false;
                    // Subdivide swept translation and yaw, retaining collision damage
                    // during stall maneuvers instead of exempting parked cars.
                    int samples = Mathf.Clamp(Mathf.CeilToInt((Mathf.Abs(longitudinal) + Mathf.Abs(lateral)) / .1f) + 4, 4, 64);
                    for (int sample = 0; sample <= samples && !contact; sample++)
                    {
                        float t = sample / (float)samples;
                        contact = PitFootprint.Overlaps(gap + longitudinal * t, side + lateral * t,
                            Mathf.LerpAngle(startYaw[first], endYaw[first], t), Mathf.LerpAngle(startYaw[second], endYaw[second], t));
                        if (contact) enter = t;
                    }
                }
                else contact = OverlapsDuringStep(gap, longitudinal, ContactLength, ref enter, ref exit) &&
                    OverlapsDuringStep(side, lateral, contactWidth, ref enter, ref exit);
                bool yawTouching = false;
                YawHit yawHit = default;
                bool yawContact = !oriented && cornerYaw != null &&
                    (cornerYaw[first].Active || cornerYaw[second].Active) &&
                    SweepCornerYaw(cornerYaw[first], cornerYaw[second], new Vector2(side, gap), new Vector2(lateral, longitudinal),
                        seconds, touching[first, second], !contact, out yawHit, out yawTouching);
                if (yawContact)
                {
                    contact = true;
                    enter = yawHit.Time;
                }
                bool continuingBump = gentleBumpContacts[first, second];
                bool gentleBump = false;
                if (contact && (!touching[first, second] || continuingBump))
                {
                    if (!yawContact && draftBumpEligible != null && wrap && !oriented &&
                        draftBumpEligible[first] && draftBumpEligible[second] &&
                        Mathf.Abs(gap) >= ContactLength - .05f && gap * (gap + longitudinal) > 0f &&
                        Mathf.Abs(side) <= DraftBumpAlignment && Mathf.Abs(side + lateral) <= DraftBumpAlignment &&
                        Mathf.Abs(lateral) <= .55f * seconds)
                    {
                        int leader = gap > 0f ? second : first;
                        int follower = gap > 0f ? first : second;
                        float leaderSpeed = run.CurrentSpeed(leader);
                        float followerSpeed = run.CurrentSpeed(follower);
                        // Relative travel inferred from float distances can overstate the
                        // closing speed after many laps, so use the recorded speeds.
                        float closingSpeed = followerSpeed - leaderSpeed;
                        gentleBump = leaderSpeed >= DraftBumpMinimumSpeed && followerSpeed >= DraftBumpMinimumSpeed &&
                            Mathf.Abs(closingSpeed) <= DraftBumpMaximumClosingSpeed &&
                            (continuingBump || closingSpeed >= .03f);
                        if (gentleBump && !continuingBump)
                        {
                            float transfer = strongDraftBumpTargets?[follower] == leader
                                ? Mathf.Min(DraftBumpTransferFraction * DraftBumpMaximumClosingSpeed,
                                    ChasingDraftBumpTransferFraction * closingSpeed)
                                : DraftBumpTransferFraction * closingSpeed;
                            draftBumpSpeedChange[leader] += transfer;
                            draftBumpSpeedChange[follower] -= transfer;
                            receivedDraftBump[leader] = true;
                            gaveDraftBump[follower] = true;
                        }
                    }

                    float relativeSpeed = yawContact ? yawHit.ClosingSpeed :
                        gentleBump ? 0f : Mathf.Sqrt(longitudinal * longitudinal + lateral * lateral) / seconds;
                    float damage = Mathf.Clamp(relativeSpeed * 1.2f, .5f, 100f);
                    // A gentle draft bump leaves a scuff on first contact without adding race damage.
                    if (ReportBodyImpact != null && (!gentleBump || !continuingBump))
                    {
                        var offset = new Vector2(side + lateral * enter, gap + longitudinal * enter);
                        Vector2 firstOffset = offset, secondOffset = -offset;
                        if (oriented)
                        {
                            firstOffset = LocalOffset(offset, Mathf.LerpAngle(startYaw[first], endYaw[first], enter));
                            secondOffset = LocalOffset(-offset, Mathf.LerpAngle(startYaw[second], endYaw[second], enter));
                        }
                        if (yawContact)
                            ReportBodyImpact(new BodyImpact(first, second, firstOffset, secondOffset,
                                yawHit.FirstSide, yawHit.SecondSide, damage, yawHit.FirstPoint, yawHit.SecondPoint));
                        else
                            ReportBodyImpact(new BodyImpact(first, second, firstOffset, secondOffset,
                                IsSideContact(firstOffset, contactWidth), IsSideContact(secondOffset, contactWidth), damage));
                    }
                    if (!gentleBump)
                    {
                        run.RecordContact(first, second, damage);
                        impactPartners[first, second] = impactPartners[second, first] = true;
                        // Hard contact breaks traction. Opposite impulses send the cars apart.
                        if (relativeSpeed >= 8f)
                        {
                            float direction = side >= 0f ? 1f : -1f;
                            float impulse = Mathf.Min(14f, relativeSpeed * 0.6f);
                            crashImpulse[first] -= direction * impulse;
                            crashImpulse[second] += direction * impulse;
                        }
                        impactDamage[first] += damage;
                        impactDamage[second] += damage;
                    }
                }

                // Small release margin avoids repeated impacts from contact projection jitter.
                float releaseMargin = touching[first, second] || gentleBump || impactDamage[first] > 0f ? .05f : 0f;
                touching[first, second] = yawTouching || (oriented
                    ? PitFootprint.Overlaps(gap + longitudinal, side + lateral, endYaw[first], endYaw[second], releaseMargin * .5f)
                    : Mathf.Abs(gap + longitudinal) < ContactLength + releaseMargin && Mathf.Abs(side + lateral) < contactWidth + releaseMargin);
                gentleBumpContacts[first, second] = touching[first, second] && (contact ? gentleBump : continuingBump);
            }
        }

        for (int i = 0; i < impactDamage.Length; i++)
        {
            if (impactDamage[i] > 0f)
                run.ApplyDamage(i, impactDamage[i]);
        }
    }

    internal readonly struct CornerYawSweep
    {
        private readonly float start, end, halfWidth;
        private readonly Vector2 pivot;
        internal bool Active => start != 0f || end != 0f;
        internal float HalfWidth => halfWidth > 0f ? halfWidth : ContactWidth * .5f;
        internal float Reach => Mathf.Max(Mathf.Abs(start), Mathf.Abs(end)) * Mathf.Deg2Rad *
            (ContactLength * .5f + Mathf.Abs(pivot.y) + HalfWidth + Mathf.Abs(pivot.x));

        internal CornerYawSweep(float start, float end, Vector2 pivot, float halfWidth)
        {
            this.start = start;
            this.end = end;
            this.pivot = pivot;
            this.halfWidth = halfWidth;
        }

        internal YawFootprint At(Vector2 position, float time)
        {
            float yaw = Mathf.Lerp(start, end, time) * Mathf.Deg2Rad;
            var forward = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
            var right = new Vector2(forward.y, -forward.x);
            Vector2 center = position + pivot - right * pivot.x - forward * pivot.y;
            return new YawFootprint(center, forward, right, ContactLength * .5f, HalfWidth);
        }
    }

    internal readonly struct YawFootprint
    {
        internal readonly Vector2 Center, Forward, Right;
        private readonly float halfLength, halfWidth;

        internal YawFootprint(Vector2 center, Vector2 forward, Vector2 right, float halfLength, float halfWidth)
        {
            Center = center;
            Forward = forward;
            Right = right;
            this.halfLength = halfLength;
            this.halfWidth = halfWidth;
        }

        internal YawFootprint Rear => new YawFootprint(Center - Forward * (halfLength * .5f),
            Forward, Right, halfLength * .5f, halfWidth);
        internal float RadiusAlong(Vector2 axis) =>
            halfWidth * Mathf.Abs(Vector2.Dot(Right, axis)) + halfLength * Mathf.Abs(Vector2.Dot(Forward, axis));
        internal Vector2 LocalPoint(Vector2 point) => new Vector2(Vector2.Dot(point - Center, Right), Vector2.Dot(point - Center, Forward));
        internal bool IsSide(Vector2 normal) =>
            Mathf.Abs(Vector2.Dot(Right, normal)) >= Mathf.Abs(Vector2.Dot(Forward, normal));
    }

    private readonly struct YawHit
    {
        internal readonly float Time, ClosingSpeed;
        internal readonly Vector2 FirstPoint, SecondPoint;
        internal readonly bool FirstSide, SecondSide;

        internal YawHit(float time, float closingSpeed, YawFootprint first, YawFootprint second, Vector2 point, Vector2 normal)
        {
            Time = time;
            ClosingSpeed = closingSpeed;
            FirstPoint = first.LocalPoint(point);
            SecondPoint = second.LocalPoint(point);
            FirstSide = first.IsSide(normal);
            SecondSide = second.IsSide(normal);
        }
    }

    private static bool SweepCornerYaw(CornerYawSweep first, CornerYawSweep second, Vector2 offset, Vector2 movement,
        float seconds, bool wasTouching, bool checkImpact, out YawHit hit, out bool touchingAtEnd)
    {
        hit = default;
        touchingAtEnd = false;
        const float release = .025f;
        float reach = first.Reach + second.Reach + release;
        float enter = 0f, exit = 1f;
        if (!OverlapsDuringStep(offset.x, movement.x, first.HalfWidth + second.HalfWidth + reach, ref enter, ref exit) ||
            !OverlapsDuringStep(offset.y, movement.y, ContactLength + reach, ref enter, ref exit))
            return false;

        touchingAtEnd = ClearanceAt(1f, out _, out _, out _) <= (wasTouching ? release : 0f);
        if (wasTouching || !checkImpact) return false;

        int samples = Mathf.Clamp(Mathf.CeilToInt((movement.magnitude * (exit - enter) + reach) / .025f), 4, 32);
        float previousTime = enter;
        float previous = ClearanceAt(enter, out _, out _, out _);
        for (int step = 1; step <= samples; step++)
        {
            float time = Mathf.Lerp(enter, exit, step / (float)samples);
            float clearance = ClearanceAt(time, out _, out _, out _);
            if (clearance <= 0f && (previous >= 0f || step == 1))
            {
                float duration = (time - previousTime) * seconds;
                float speed = duration > 0f ? (previous - clearance) / duration : 0f;
                if (speed > .01f)
                {
                    float impactTime = previous > 0f ? Mathf.Lerp(previousTime, time, previous / (previous - clearance)) : previousTime;
                    ClearanceAt(impactTime, out var contactFirst, out var contactSecond, out var normal);
                    Vector2 point = FootprintContactPoint(contactFirst, contactSecond, normal);
                    hit = new YawHit(impactTime, speed, first.At(Vector2.zero, impactTime),
                        second.At(offset + movement * impactTime, impactTime), point, normal);
                    return true;
                }
            }
            previousTime = time;
            previous = clearance;
        }
        return false;

        float ClearanceAt(float time, out YawFootprint contactFirst, out YawFootprint contactSecond, out Vector2 normal)
        {
            YawFootprint a = first.At(Vector2.zero, time);
            YawFootprint b = second.At(offset + movement * time, time);
            contactFirst = first.Active ? a.Rear : a;
            contactSecond = b;
            normal = default;
            float minimum = float.PositiveInfinity;
            if (first.Active) minimum = FootprintClearance(contactFirst, contactSecond, out normal);
            if (second.Active)
            {
                var rear = b.Rear;
                float otherClearance = FootprintClearance(a, rear, out var otherNormal);
                if (otherClearance < minimum)
                {
                    minimum = otherClearance;
                    contactFirst = a;
                    contactSecond = rear;
                    normal = otherNormal;
                }
            }
            return minimum;
        }
    }

    private static float FootprintClearance(YawFootprint first, YawFootprint second, out Vector2 normal)
    {
        // The axis with the greatest separation becomes the contact normal at first touch.
        Vector2 delta = second.Center - first.Center;
        normal = first.Right;
        float separation = SeparationOn(first.Right);
        float other = SeparationOn(first.Forward);
        if (other > separation) { separation = other; normal = first.Forward; }
        other = SeparationOn(second.Right);
        if (other > separation) { separation = other; normal = second.Right; }
        other = SeparationOn(second.Forward);
        if (other > separation) { separation = other; normal = second.Forward; }
        if (Vector2.Dot(delta, normal) < 0f) normal = -normal;
        return separation;

        float SeparationOn(Vector2 axis) =>
            Mathf.Abs(Vector2.Dot(delta, axis)) - first.RadiusAlong(axis) - second.RadiusAlong(axis);
    }

    private static Vector2 FootprintContactPoint(YawFootprint first, YawFootprint second, Vector2 normal)
    {
        var along = new Vector2(normal.y, -normal.x);
        float firstEdge = Vector2.Dot(first.Center, normal) + first.RadiusAlong(normal);
        float secondEdge = Vector2.Dot(second.Center, normal) - second.RadiusAlong(normal);
        float firstAlong = Vector2.Dot(first.Center, along), firstSpan = first.RadiusAlong(along);
        float secondAlong = Vector2.Dot(second.Center, along), secondSpan = second.RadiusAlong(along);
        float contactAlong = (Mathf.Max(firstAlong - firstSpan, secondAlong - secondSpan) +
            Mathf.Min(firstAlong + firstSpan, secondAlong + secondSpan)) * .5f;
        return normal * ((firstEdge + secondEdge) * .5f) + along * contactAlong;
    }

    // Split pit routes do not share a distance coordinate. Use the actual
    // swept poses, including cars on different connectors and track/pit merges.
    internal void ResolveSpatial(RaceSessionRun run, float seconds, bool[] active, bool[] pitInvolved,
        Vector3[] start, Vector3[] end, float[] yawStart, float[] yawEnd)
    {
        Array.Clear(impactDamage, 0, impactDamage.Length);
        Array.Clear(crashImpulse, 0, crashImpulse.Length);
        if (seconds <= 0f) return;
        for (int i = 0; i < active.Length; i++) for (int j = i + 1; j < active.Length; j++)
        {
            if (!active[i] || !active[j] || !(pitInvolved[i] || pitInvolved[j]))
            { touching[i, j] = false; continue; }
            bool contact = SpatialContact(start[i], end[i], yawStart[i], yawEnd[i], start[j], end[j], yawStart[j], yawEnd[j],
                out float impactTime);
            if (contact && !touching[i, j])
            {
                Vector3 relative = (end[j] - start[j]) - (end[i] - start[i]);
                float damage = Mathf.Clamp(relative.magnitude / seconds * 1.2f, .5f, 100f);
                if (ReportBodyImpact != null)
                {
                    Vector3 gapAtContact = start[j] - start[i] + relative * impactTime;
                    var offset = new Vector2(gapAtContact.x, gapAtContact.z);
                    var firstOffset = LocalOffset(offset, Mathf.LerpAngle(yawStart[i], yawEnd[i], impactTime));
                    var secondOffset = LocalOffset(-offset, Mathf.LerpAngle(yawStart[j], yawEnd[j], impactTime));
                    ReportBodyImpact(new BodyImpact(i, j, firstOffset, secondOffset,
                        IsSideContact(firstOffset, ContactWidth), IsSideContact(secondOffset, ContactWidth), damage));
                }
                run.RecordContact(i, j, damage);
                impactDamage[i] += damage; impactDamage[j] += damage;
            }
            Vector3 delta = end[j] - end[i];
            float margin = touching[i, j] || contact ? .025f : 0f;
            touching[i, j] = Mathf.Abs(delta.y) < 1.5f && PitFootprint.Overlaps(delta.z, delta.x, yawEnd[i], yawEnd[j], margin);
        }
        for (int i = 0; i < active.Length; i++) if (impactDamage[i] > 0f) run.ApplyDamage(i, impactDamage[i]);
    }

    internal static bool SpatialContact(Vector3 a0, Vector3 a1, float yawA0, float yawA1,
        Vector3 b0, Vector3 b1, float yawB0, float yawB1)
        => SpatialContact(a0, a1, yawA0, yawA1, b0, b1, yawB0, yawB1, out _);

    private static bool SpatialContact(Vector3 a0, Vector3 a1, float yawA0, float yawA1,
        Vector3 b0, Vector3 b1, float yawB0, float yawB1, out float impactTime)
    {
        impactTime = 0f;
        Vector3 delta0 = b0 - a0, change = (b1 - a1) - delta0;
        // Broad phase on the swept relative position; avoids sampling distant
        // cars with identical progress values on Bristol's opposite pit roads.
        Vector2 planar = new Vector2(delta0.x, delta0.z), motion = new Vector2(change.x, change.z);
        float closest = motion.sqrMagnitude > .000001f ? Mathf.Clamp01(-Vector2.Dot(planar, motion) / motion.sqrMagnitude) : 0f;
        if ((planar + motion * closest).sqrMagnitude > 12.25f) return false;
        float rotationTravel = (Mathf.Abs(Mathf.DeltaAngle(yawA0, yawA1)) + Mathf.Abs(Mathf.DeltaAngle(yawB0, yawB1))) * Mathf.Deg2Rad * 1.71f;
        int samples = Mathf.Clamp(Mathf.CeilToInt((motion.magnitude + rotationTravel) / .05f), 4, 256);
        for (int i = 0; i <= samples; i++)
        {
            float t = i / (float)samples;
            Vector3 delta = delta0 + change * t;
            if (Mathf.Abs(delta.y) < 1.5f && PitFootprint.Overlaps(delta.z, delta.x,
                Mathf.LerpAngle(yawA0, yawA1, t), Mathf.LerpAngle(yawB0, yawB1, t)))
            {
                impactTime = t;
                return true;
            }
        }
        return false;
    }

    private static Vector2 LocalOffset(Vector2 offset, float yaw)
    {
        Vector3 local = Quaternion.Euler(0f, -yaw, 0f) * new Vector3(offset.x, 0f, offset.y);
        return new Vector2(local.x, local.z);
    }

    internal static bool IsSideContact(Vector2 offset, float width) =>
        Mathf.Abs(offset.x) >= width * .42f && Mathf.Abs(offset.y) < ContactLength * .82f;

    // Position projection resolves penetration without changing commanded velocities.
    // Trailing cars move backward, never forward through another car or across a finish line.
    internal static void Separate(float lapLength, bool[] active, float[] distances, float[] lanes, float[] widths,
        float[] trackHalfWidths = null)
    {
        for (int pass = 0; pass < active.Length; pass++)
        {
            bool changed = false;
            for (int i=0;i<active.Length;i++) for(int j=i+1;j<active.Length;j++)
            {
                if (!active[i] || !active[j]) continue;
                float gap = Mathf.Repeat(distances[j]-distances[i]+lapLength*.5f,lapLength)-lapLength*.5f;
                float side = lanes[j]-lanes[i];
                float halfWidthI = TrackContactHalfWidth(trackHalfWidths, i);
                float halfWidthJ = TrackContactHalfWidth(trackHalfWidths, j);
                float contactWidth = halfWidthI + halfWidthJ;
                float longitudinal = ContactLength-Mathf.Abs(gap);
                float lateral = contactWidth-Mathf.Abs(side);
                if (longitudinal <= 0f || lateral <= 0f) continue;
                float direction = side >= 0f ? 1f : -1f;
                float halfI = Mathf.Max(0f,widths[i]*.5f-halfWidthI);
                float halfJ = Mathf.Max(0f,widths[j]*.5f-halfWidthJ);
                float moveI = Mathf.Clamp(lanes[i]-direction*(lateral+.002f)*.5f,-halfI,halfI);
                float moveJ = Mathf.Clamp(lanes[j]+direction*(lateral+.002f)*.5f,-halfJ,halfJ);
                if (lateral < longitudinal && Mathf.Abs(moveJ-moveI) >= contactWidth)
                { lanes[i]=moveI; lanes[j]=moveJ; }
                else if (gap >= 0f) distances[i] -= longitudinal+.002f;
                else distances[j] -= longitudinal+.002f;
                changed = true;
            }
            if (!changed) break;
        }
    }

    private static bool OverlapsDuringStep(float separation, float change, float limit, ref float enter, ref float exit)
    {
        if (Mathf.Abs(change) < 0.00001f)
            return Mathf.Abs(separation) < limit;

        float first = (-limit - separation) / change;
        float second = (limit - separation) / change;
        enter = Mathf.Max(enter, Mathf.Min(first, second));
        exit = Mathf.Min(exit, Mathf.Max(first, second));
        return enter < exit;
    }
}
