using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class RaceWheelAnimation
{
    private readonly Wheel[] wheels;
    private Vector3 previousPosition;
    private Quaternion previousRotation;
    private bool initialized;

    private RaceWheelAnimation(Wheel[] wheels) => this.wheels = wheels;

    public static RaceWheelAnimation Bind(Transform car)
    {
        var renderers = car.GetComponentsInChildren<MeshRenderer>();
        var wheels = new List<Wheel>(4);
        foreach (var renderer in renderers)
        {
            if (renderer.name.IndexOf(".Wheel.", StringComparison.Ordinal) < 0)
                continue;

            Bounds bounds = BoundsInCar(car, renderer);
            if (bounds.extents.y <= 0.01f)
                continue;

            bool front = renderer.name.IndexOf(".Wheel.Ft.", StringComparison.Ordinal) >= 0;
            var steer = new GameObject(renderer.name + " Steering").transform;
            steer.SetParent(car, false);
            steer.localPosition = bounds.center;
            var spin = new GameObject(renderer.name + " Spin").transform;
            spin.SetParent(steer, false);
            renderer.transform.SetParent(spin, true);

            if (front)
            {
                string brakeName = renderer.name.Replace(".Wheel.", ".WheelBrake.");
                var brake = Array.Find(renderers, item => item.name == brakeName);
                if (brake != null)
                    brake.transform.SetParent(steer, true);
            }

            wheels.Add(new Wheel(steer, spin, bounds.extents.y, front));
        }

        return wheels.Count == 0 ? null : new RaceWheelAnimation(wheels.ToArray());
    }

    public void Reset()
    {
        initialized = false;
        foreach (var wheel in wheels)
        {
            wheel.Angle = 0f;
            wheel.SteeringAngle = 0f;
        }
        Present();
    }

    public void Sample(Transform car, bool sliding)
    {
        Vector3 position = car.localPosition;
        Quaternion rotation = car.localRotation;
        Vector3 movement = position - previousPosition;
        Quaternion oldRotation = previousRotation;
        previousPosition = position;
        previousRotation = rotation;
        // Ignore grid placement, pit resets and other discontinuities.
        if (!initialized || movement.sqrMagnitude > 100f)
        {
            initialized = true;
            foreach (var wheel in wheels)
                wheel.SteeringAngle = 0f;
            return;
        }

        if (movement.sqrMagnitude < 0.00000001f)
            return;

        Quaternion toMidpoint = Quaternion.Inverse(Quaternion.Slerp(oldRotation, rotation, 0.5f));
        var delta = toMidpoint * movement;
        var from = toMidpoint * oldRotation;
        var to = toMidpoint * rotation;
        foreach (var wheel in wheels)
        {
            Vector3 center = wheel.Center;
            Vector3 travel = delta + to * center - from * center;
            float distance = travel.z;
            if (wheel.Front)
            {
                float direction = Mathf.Sign(distance);
                // Sample each front contact patch separately. The inside
                // wheel takes a tighter arc and needs more steering lock.
                wheel.SteeringAngle = sliding ? 0f : Mathf.Clamp(
                    Mathf.Atan2(travel.x * direction, Mathf.Abs(distance)) * Mathf.Rad2Deg, -40f, 40f);
                if (!sliding)
                    distance = new Vector2(travel.x, distance).magnitude * direction;
            }
            wheel.Angle = Mathf.Repeat(wheel.Angle + distance / wheel.Radius * Mathf.Rad2Deg, 360f);
        }
    }

    public float FrontSteeringAngle
    {
        get
        {
            float sum = 0f;
            int count = 0;
            foreach (var wheel in wheels)
            {
                if (!wheel.Front)
                    continue;
                sum += wheel.SteeringAngle;
                count++;
            }
            return count == 0 ? 0f : sum / count;
        }
    }

    public void Present()
    {
        foreach (var wheel in wheels)
        {
            if (wheel.Front && wheel.SteeringAngle != wheel.PresentedSteering)
            {
                wheel.Steer.localRotation = Quaternion.Euler(0f, wheel.SteeringAngle, 0f);
                wheel.PresentedSteering = wheel.SteeringAngle;
            }
            if (wheel.Angle != wheel.PresentedAngle)
            {
                wheel.Spin.localRotation = Quaternion.Euler(wheel.Angle, 0f, 0f);
                wheel.PresentedAngle = wheel.Angle;
            }
        }
    }

    private static Bounds BoundsInCar(Transform car, MeshRenderer renderer)
    {
        var meshBounds = renderer.localBounds;
        var toCar = car.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        var bounds = new Bounds(toCar.MultiplyPoint3x4(meshBounds.min), Vector3.zero);
        for (int corner = 1; corner < 8; corner++)
        {
            var point = new Vector3((corner & 1) == 0 ? meshBounds.min.x : meshBounds.max.x,
                (corner & 2) == 0 ? meshBounds.min.y : meshBounds.max.y,
                (corner & 4) == 0 ? meshBounds.min.z : meshBounds.max.z);
            bounds.Encapsulate(toCar.MultiplyPoint3x4(point));
        }
        return bounds;
    }

    private sealed class Wheel
    {
        public readonly Transform Steer;
        public readonly Transform Spin;
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly bool Front;
        public float Angle;
        public float SteeringAngle;
        public float PresentedAngle;
        public float PresentedSteering;

        public Wheel(Transform steer, Transform spin, float radius, bool front)
        {
            Steer = steer;
            Spin = spin;
            Center = steer.localPosition;
            Radius = radius;
            Front = front;
        }
    }
}
