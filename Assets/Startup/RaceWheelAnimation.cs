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

            wheels.Add(new Wheel(renderer.name, steer, spin, bounds.extents.y, front));
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
            wheel.FilteredSteering = 0f;
        }
        Present();
    }

    public void Sample(Transform car, bool sliding, float steeringBlend = 0f)
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
            {
                wheel.SteeringAngle = 0f;
                wheel.FilteredSteering = 0f;
            }
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
                float steering = sliding ? 0f : Mathf.Clamp(
                    Mathf.Atan2(travel.x * direction, Mathf.Abs(distance)) * Mathf.Rad2Deg, -40f, 40f);
                // Filter brief pose changes over traveled distance so playback
                // speed has no effect on the steering presentation.
                wheel.FilteredSteering = sliding || steeringBlend <= 0f ? steering :
                    Mathf.Lerp(wheel.FilteredSteering, steering, 1f - Mathf.Exp(-movement.magnitude / 16f));
                wheel.SteeringAngle = Mathf.Lerp(steering, wheel.FilteredSteering, steeringBlend);
                if (!sliding)
                    distance = new Vector2(travel.x, distance).magnitude * direction;
            }
            wheel.Angle = Mathf.Repeat(wheel.Angle + distance / wheel.Radius * Mathf.Rad2Deg, 360f);
        }
    }

    public void CopyMotionFrom(RaceWheelAnimation source)
    {
        if (source == null)
        {
            Reset();
            return;
        }

        initialized = source.initialized;
        previousPosition = source.previousPosition;
        previousRotation = source.previousRotation;
        foreach (var wheel in wheels)
        {
            var prior = Array.Find(source.wheels, candidate => candidate.Name == wheel.Name);
            wheel.Angle = prior?.Angle ?? 0f;
            wheel.SteeringAngle = prior?.SteeringAngle ?? 0f;
            wheel.FilteredSteering = prior?.FilteredSteering ?? 0f;
        }
        Present();
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
        public readonly string Name;
        public readonly Transform Steer;
        public readonly Transform Spin;
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly bool Front;
        public float Angle;
        public float SteeringAngle;
        public float FilteredSteering;
        public float PresentedAngle;
        public float PresentedSteering;

        public Wheel(string name, Transform steer, Transform spin, float radius, bool front)
        {
            Name = name;
            Steer = steer;
            Spin = spin;
            Center = steer.localPosition;
            Radius = radius;
            Front = front;
        }
    }
}
