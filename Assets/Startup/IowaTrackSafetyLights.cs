using System;
using UnityEngine;

public sealed partial class TrackPreview
{
    private enum TrackSafetySignal { Off, Green, Amber, Red }

    private Renderer[] iowaSafetyFaces;
    private Material iowaSafetyLightsOff;
    private Material[] iowaSafetyLightsOn;
    private TrackSafetySignal iowaSafetySignal;
    private double iowaSafetySignalSince;
    private int iowaSafetyFaceMask = -1;

    private bool TrackCautionLampsActive => TrackState switch
    {
        SessionTrackState.StageComplete or SessionTrackState.Caution or SessionTrackState.StageEndingUnderCaution => true,
        SessionTrackState.FinishUnderCaution => !SessionComplete,
        SessionTrackState.WhiteFlag => stages?.ReducedFuelBurn == true,
        _ => false
    };

    private void SetIowaSafetyFaces(Renderer leftAmber, Renderer rightAmber, Renderer green, Renderer red)
    {
        iowaSafetyFaces = new[] { leftAmber, rightAmber, green, red };
        iowaSafetyLightsOff = MakeMaterial(new Color32(49, 51, 51, 255));
        iowaSafetyLightsOff.name = "Safety signal dark lens";
        iowaSafetyLightsOff.SetFloat("_Smoothness", .41f);
        Material amber = Lamp("Amber caution lamps", new Color32(255, 182, 20, 255));
        iowaSafetyLightsOn = new[]
        {
            amber, amber,
            Lamp("Green track-open lamps", new Color32(29, 235, 78, 255)),
            Lamp("Red track-closed lamps", new Color32(247, 33, 27, 255))
        };
        iowaSafetyFaceMask = -1;
        UpdateIowaSafetyLights();

        Material Lamp(string name, Color color)
        {
            var material = MakeMaterial(color);
            material.name = name;
            material.SetFloat("_Smoothness", .27f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2.4f);
            return material;
        }
    }

    private TrackSafetySignal ReadTrackSafetySignal()
    {
        if (OneToGoBeforeGreen)
            return TrackSafetySignal.Off;

        var state = TrackState;
        // Stop signalling movement once the last car has returned to its stall.
        if ((state is SessionTrackState.Checkered or SessionTrackState.FinishUnderCaution) && SessionComplete)
            return TrackSafetySignal.Red;
        if (TrackCautionLampsActive)
            return TrackSafetySignal.Amber;

        return state switch
        {
            SessionTrackState.TrackClosed or SessionTrackState.ReturnToPits or SessionTrackState.Complete => TrackSafetySignal.Red,
            SessionTrackState.Pacing => TrackSafetySignal.Amber,
            SessionTrackState.TrackOpen or SessionTrackState.WhiteFlag or SessionTrackState.Checkered => TrackSafetySignal.Green,
            _ => TrackSafetySignal.Off
        };
    }

    private void UpdateIowaSafetyLights()
    {
        if (iowaSafetyFaces == null)
            return;

        double now = Time.unscaledTimeAsDouble;
        TrackSafetySignal signal = ReadTrackSafetySignal();
        if (signal != iowaSafetySignal)
        {
            iowaSafetySignal = signal;
            iowaSafetySignalSince = now;
        }

        int mask = IowaSafetyLensMask(signal, now - iowaSafetySignalSince);
        if (iowaSafetyFaceMask == mask)
            return;

        iowaSafetyFaceMask = mask;
        for (int i = 0; i < iowaSafetyFaces.Length; i++)
            iowaSafetyFaces[i].sharedMaterial = (mask & (1 << i)) != 0 ? iowaSafetyLightsOn[i] : iowaSafetyLightsOff;
    }

    private static int IowaSafetyLensMask(TrackSafetySignal signal, double seconds)
    {
        if (signal == TrackSafetySignal.Green)
            return 1 << 2;
        if (signal == TrackSafetySignal.Red)
            return 1 << 3;
        if (signal != TrackSafetySignal.Amber)
            return 0;

        // The bank changes every half second and completes two visible pulses in each slot.
        double slot = Math.Max(0d, seconds) * 2d;
        double bank = Math.Floor(slot);
        bool blinking = (slot - bank) % .5d < .25d;
        return blinking ? 1 << (int)(bank % 2d) : 0;
    }
}
