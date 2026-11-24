using System.Collections.Generic;
using UnityEngine;

internal sealed class CarPaintTemplateRenderAssets : MonoBehaviour
{
    private CarPaintTemplate template;
    private readonly List<Object> assets = new();

    internal void Own(CarPaintTemplate paint) => template = paint;

    internal void Retain(Object asset) => assets.Add(asset);

    private void OnDestroy()
    {
        foreach (var asset in assets)
            Destroy(asset);
        template?.ReleaseAssigned();
    }
}
