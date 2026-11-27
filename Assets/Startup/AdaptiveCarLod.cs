using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class TrackPreview
{
    // Approximate car length as a fraction of the camera's vertical view.
    private static readonly float[] CarLodThresholds = { .04f, .075f, .12f, .18f };
    private const float CarLodHysteresis = .15f;
    private const float RenderedCarLength = 2.9f;
    private const double TargetCarLodFps = 60d;
    private const double CarLodReductionDelay = 5d;
    private const double CarLodRecoveryDelay = 20d;
    private const double CarLodRecoveryTrial = 10d;
    private const double CarLodRecoveryRetryDelay = 60d;
    private int carLodReduction, carLodFrameCount;
    private double carLodSampleTime, carLodSlowTime, carLodHealthyTime;
    private double carLodRecoveryTrialTime;
    private bool carLodRecoveryBackoff, forceLowestCarLod;

    private int EffectiveMaximumCarLod => forceLowestCarLod ? 1 : CarDetailIndex + 1 - carLodReduction;
    private readonly CarVisualLods[] carVisualLods = new CarVisualLods[CarCount];
    private readonly HashSet<string> loadedCarLodResources = new();
    private readonly HashSet<string> missingCarLodResources = new();
    private ResourceRequest carLodRequest;
    private string requestedCarLodResource;

    private sealed class CarVisual
    {
        public string Resource;
        public Transform Root, Body;
        public Transform FallbackRearTires;
        public Vector3 FallbackRearTiresCenter;
        public bool SupportsBodyTravel;
        public RaceWheelAnimation Wheels;
    }

    private sealed class CarVisualLods
    {
        public string[] Resources;
        public Action<Transform, string, bool> Build;
        public CarVisual Current;
        public Vector3 BodyYawPivot;
        // Keep one previous visual so returning to the last level is cheap.
        public CarVisual Previous;
        public int Level, CoverageLevel;
    }

    private void InitializeCarVisualLods(int index, string modelResource, int number, string numberText,
        Material hoodPaint, Material bodyPaint, Material glass, Material rubber, string[] sponsors, CarPaintStyle? paint)
    {
        var resources = new string[CarDetailLabels.Length];
        for (int i = 0; i < resources.Length; i++)
            resources[i] = definition.CarModelForTrack(modelResource, i + 1);
        string reference = carTypes[index].HasTrackModelLods ? resources[^1] : resources[CarDetailIndex];
        Texture2D generatedPaint = null;

        var state = new CarVisualLods
        {
            Resources = resources,
            CoverageLevel = CarDetailIndex + 1,
            Build = (root, resource, first) =>
                BuildCar(root, number, hoodPaint, bodyPaint, glass, rubber, sponsors, paint, texture =>
                {
                    generatedPaint = texture;
                    carPaintTextures.Add(texture);
                }, modelResource,
                    measureHalfWidth: first ? width => raceCarHalfWidths[index] = width : null,
                    visualModelResource: resource, numberText: numberText, referenceModel: reference,
                    generatedPaintTexture: generatedPaint)
        };
        carVisualLods[index] = state;
        ApplyCarVisualLod(index, CarDetailIndex + 1);
        loadedCarLodResources.Add(state.Current.Resource);
    }

    private bool ApplyCarVisualLod(int index, int level)
    {
        var state = carVisualLods[index];
        string resource = state.Resources[level - 1];
        if (state.Current?.Resource == resource)
        {
            state.Level = level;
            return false;
        }

        var car = cars[index];
        var next = state.Previous;
        if (next?.Resource != resource)
        {
            var discarded = next;
            bool wasActive = car.gameObject.activeSelf;
            car.gameObject.SetActive(true);
            var root = CreateObject("Car visuals", car).transform;
            try
            {
                var body = CreateObject("Sprung body", root).transform;
                state.Build(body, resource, state.Current == null);
                // Wheel pivots are attached to the unsprung root and continue to follow the track pose.
                var wheels = RaceWheelAnimation.Bind(root, detachRearBrakes: true);
                var frontTires = body.Find("Front tires");
                var rearTires = body.Find("Rear tires");
                frontTires?.SetParent(root, false);
                rearTires?.SetParent(root, false);
                next = new CarVisual
                {
                    Resource = resource,
                    Root = root,
                    Body = body,
                    Wheels = wheels,
                    FallbackRearTires = rearTires,
                    FallbackRearTiresCenter = rearTires?.localPosition ?? Vector3.zero,
                    SupportsBodyTravel = wheels?.HasFourCorners == true || (frontTires != null && rearTires != null)
                };
            }
            catch
            {
                root.gameObject.SetActive(false);
                Destroy(root.gameObject);
                throw;
            }
            finally
            {
                car.gameObject.SetActive(wasActive);
            }

            if (discarded != null)
            {
                discarded.Root.SetParent(null, true);
                Destroy(discarded.Root.gameObject);
            }
        }

        var old = state.Current;
        if (old != null)
            old.Root.gameObject.SetActive(false);
        if (old == null)
            state.BodyYawPivot = next.Wheels?.FrontAxleCenter ?? Vector3.forward * RearAxleHeading.CenterToRearAxle;
        next.Wheels?.CopyMotionFrom(old?.Wheels);
        PresentBodySuspension(index, next);
        next.Root.gameObject.SetActive(true);
        state.Previous = old;
        state.Current = next;
        state.Level = level;
        raceWheels[index] = next.Wheels;

        if (old != null)
        {
            ConfigureOnboardCamera(index);
            if (iowaVehicleLighting.ContainsKey(car))
                CacheIowaVehicleLighting(car);
            else if (bristolSun != null)
                AssignBristolLighting(next.Root);

            if (cockpitGlassCar == car)
            {
                cockpitGlassCar = null;
                UpdateCockpitGlass();
            }
        }
        return true;
    }

    private bool UsesMaximumCarLod(int index) =>
        ViewAngle == CameraAngle.InCar && (index == 0 || cars[index] == CameraSubject);

    private void ResetCarLodSampling()
    {
        carLodFrameCount = 0;
        carLodSampleTime = carLodSlowTime = carLodHealthyTime = 0d;
        carLodRecoveryTrialTime = 0d;
    }

    private void ResetCarLodBudget()
    {
        carLodReduction = 0;
        carLodRecoveryBackoff = false;
        ResetCarLodSampling();
    }

    private void ApplyCarLodPlaybackOverride(int speed)
    {
        if (speed == 0 || forceLowestCarLod == (speed > 1)) return;
        forceLowestCarLod = speed > 1;
        ResetCarLodSampling();
        if (!forceLowestCarLod || !SupportsCarDetail) return;

        // Apply the override before advancing the simulation at the higher speed.
        for (int i = 0; i < cars.Length; i++)
            if (carVisualLods[i] != null)
                ApplyCarVisualLod(i, 1);
        ApplyCameraPose();
    }

    private void SampleCarLodFrameRate(double frameSeconds)
    {
        if (sessionSpeed != 1 || SessionRun == null || !Application.isFocused)
        {
            ResetCarLodSampling();
            return;
        }

        carLodFrameCount++;
        carLodSampleTime += frameSeconds;
        if (carLodSampleTime < 1d) return;

        double sampled = carLodSampleTime;
        // Allow sub-millisecond timing noise around a display locked to 60 Hz.
        bool healthy = sampled <= carLodFrameCount / TargetCarLodFps + .0001d;
        carLodSampleTime = 0d;
        carLodFrameCount = 0;
        bool testingRecovery = carLodRecoveryTrialTime > 0d;
        carLodRecoveryTrialTime = Math.Max(0d, carLodRecoveryTrialTime - sampled);

        if (!healthy)
        {
            carLodHealthyTime = 0d;
            carLodSlowTime += sampled;
            if (carLodSlowTime < CarLodReductionDelay || carLodReduction >= CarDetailIndex) return;

            carLodReduction++;
            carLodSlowTime = 0d;
            carLodRecoveryBackoff |= testingRecovery;
            carLodRecoveryTrialTime = 0d;
            return;
        }

        carLodSlowTime = 0d;
        if (testingRecovery && carLodRecoveryTrialTime == 0d)
            carLodRecoveryBackoff = false;
        if (carLodReduction == 0) return;

        carLodHealthyTime += sampled;
        double recoveryDelay = carLodRecoveryBackoff ? CarLodRecoveryRetryDelay : CarLodRecoveryDelay;
        if (carLodHealthyTime < recoveryDelay) return;

        carLodReduction--;
        carLodHealthyTime = 0d;
        carLodRecoveryTrialTime = CarLodRecoveryTrial;
    }

    private void KeepOnboardCarLod()
    {
        if (ViewAngle != CameraAngle.InCar || carVisualLods[0] == null)
            return;

        int maximum = EffectiveMaximumCarLod;
        ApplyCarVisualLod(0, maximum);
        int subject = Array.IndexOf(cars, CameraSubject);
        if (subject > 0)
            ApplyCarVisualLod(subject, maximum);
    }

    private void UpdateAdaptiveCarLods()
    {
        if (raceCamera == null || !raceCamera.enabled || carVisualLods[0] == null)
            return;

        SampleCarLodFrameRate(Time.unscaledDeltaTime);
        if (carLodRequest != null && carLodRequest.isDone)
        {
            if (carLodRequest.asset != null)
                loadedCarLodResources.Add(requestedCarLodResource);
            else
                missingCarLodResources.Add(requestedCarLodResource);
            carLodRequest = null;
            requestedCarLodResource = null;
        }

        int bestCar = -1, bestLevel = 0;
        float bestPriority = -1f, loadPriority = -1f;
        string loadResource = null;
        int maximum = EffectiveMaximumCarLod;
        int configuredMaximum = CarDetailIndex + 1;
        for (int i = 0; i < cars.Length; i++)
        {
            var state = carVisualLods[i];
            if (!cars[i].gameObject.activeSelf) continue;

            float coverage = 0f;
            int level = maximum;
            if (maximum > 1 && !UsesMaximumCarLod(i))
            {
                Vector3 center = cars[i].position + Vector3.up * .5f;
                coverage = CarCameraCoverage(raceCamera, center);
                if (rearViewCamera != null && rearViewCamera.enabled)
                    coverage = Mathf.Max(coverage, CarCameraCoverage(rearViewCamera, center));

                int coverageLevel = CarLodForCoverage(coverage, state.CoverageLevel, configuredMaximum);
                state.CoverageLevel = coverageLevel;
                level = Mathf.Max(1, coverageLevel - carLodReduction);
            }
            string resource = state.Resources[level - 1];
            if (resource == state.Current.Resource)
            {
                state.Level = level;
                continue;
            }

            float priority = level > state.Level ? 2f + coverage : 1f - Mathf.Clamp01(coverage);
            if (state.Previous?.Resource == resource || loadedCarLodResources.Contains(resource))
            {
                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestCar = i;
                    bestLevel = level;
                }
            }
            else if (carLodRequest == null && priority > loadPriority && !missingCarLodResources.Contains(resource))
            {
                loadPriority = priority;
                loadResource = resource;
            }
        }

        if (loadResource != null)
        {
            requestedCarLodResource = loadResource;
            carLodRequest = Resources.LoadAsync<GameObject>("Startup/" + loadResource);
        }

        // A camera cut can change the requested detail for the whole field.
        // Process only one nonessential visual swap per frame.
        if (bestCar >= 0 && ApplyCarVisualLod(bestCar, bestLevel) &&
            (ViewAngle is CameraAngle.Hood or CameraAngle.InCar) && cars[bestCar] == CameraSubject)
            ApplyCameraPose();
    }

    private static float CarCameraCoverage(Camera camera, Vector3 center)
    {
        Vector3 position = camera.transform.InverseTransformPoint(center);
        float radius = RenderedCarLength * .5f;
        if (position.z + radius < camera.nearClipPlane || position.z - radius > camera.farClipPlane)
            return 0f;

        float depth = Mathf.Max(position.z, camera.nearClipPlane);
        float halfHeight = camera.orthographic ? camera.orthographicSize :
            depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
        if (Mathf.Abs(position.x) > halfHeight * camera.aspect + radius ||
            Mathf.Abs(position.y) > halfHeight + radius)
            return 0f;

        return Mathf.Clamp01(radius / Mathf.Max(.01f, halfHeight));
    }

    private static int CarLodForCoverage(float coverage, int level, int maximum)
    {
        level = Mathf.Clamp(level, 1, maximum);
        while (level < maximum && coverage >= CarLodThresholds[level - 1] * (1f + CarLodHysteresis))
            level++;
        while (level > 1 && coverage < CarLodThresholds[level - 2] * (1f - CarLodHysteresis))
            level--;
        return level;
    }
}
