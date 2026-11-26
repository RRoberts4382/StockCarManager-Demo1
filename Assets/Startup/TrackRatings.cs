using System;
using System.Collections.Generic;
using UnityEngine;

internal static class TrackRatings
{
    private const float MaxAeroLength = 2.5f;

    public static IEnumerable<(string Title, float Value, CarComponent? Component)> DisplayAttributes(TrackWeights weights)
    {
        foreach (var component in CarComponentDisplay.Order)
        {
            var (title, value) = component switch
            {
                CarComponent.Engine => ("Horsepower", weights.horsepower),
                CarComponent.Transmission => ("Transmission", weights.transmission),
                CarComponent.Suspension => ("Suspension", weights.suspension),
                CarComponent.Brakes => ("Brakes", weights.brakes),
                CarComponent.Aerodynamics => ("Aero", weights.aerodynamics),
                _ => throw new ArgumentOutOfRangeException(nameof(component), component, "Unknown track attribute component.")
            };
            yield return (title, value, component);
        }
        yield return ("Tire Wear", weights.surfaceRoughness, null);
    }

    internal static void SetAttribute(TrackWeights weights, CarComponent? component, float value)
    {
        switch (component)
        {
            case CarComponent.Engine: weights.horsepower = value; break;
            case CarComponent.Transmission: weights.transmission = value; break;
            case CarComponent.Suspension: weights.suspension = value; break;
            case CarComponent.Brakes: weights.brakes = value; break;
            case CarComponent.Aerodynamics: weights.aerodynamics = value; break;
            case null: weights.surfaceRoughness = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(component));
        }
    }

    public static float TransmissionWeight(TrackDefinition track) =>
        track.majorStyle == "Road" ? 1f : track.lengthMiles <= 1f ? 0.25f : 0f;

    public static void Apply(TrackDefinition track, float shortest)
    {
        float length = track.lengthMiles;
        var weights = track.weights;
        weights.transmission = TransmissionWeight(track);
        weights.aerodynamics = track.majorStyle == "Road" ? 0.25f :
            length >= MaxAeroLength ? 1f : Mathf.InverseLerp(shortest, MaxAeroLength, length);
        weights.brakes = track.majorStyle == "Road" || Mathf.Approximately(length, shortest) ? 1f : 0f;
    }
}
