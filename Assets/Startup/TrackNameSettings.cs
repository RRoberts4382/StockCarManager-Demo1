using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class TrackNameSettings : DefinitionSettings<TrackSettingsValues>
{
    private readonly TrackCatalog catalog;

    internal TrackNameSettings(TrackCatalog catalog, Action back)
        : base("Track Settings", "Track", back, selectorInTabRow: true)
    {
        this.catalog = catalog;
    }

    protected override IEnumerable<Entry> LoadEntries()
    {
        catalog.Refresh();
        foreach (var entry in catalog.Entries)
        {
            var track = entry.Definition;
            var values = new TrackSettingsValues { name = track.name, logo = track.logo, weights = track.weights };
            yield return new Entry(track.id.ToString(CultureInfo.InvariantCulture), track.name, values,
                draft => catalog.SaveSettings(track.id, draft));
        }
    }

    protected override IEnumerable<(int Tab, string Error)> ValidationErrors(TrackSettingsValues draft)
    {
        if (TrackCatalog.DisplayNameError(draft.name?.Trim()) is string nameError)
            yield return (0, nameError);
        if (!TrackDefinition.ValidWeights(draft.weights))
            yield return (0, "Enter attribute values from 0.00 to 1.00.");
    }

    protected override void Draw(TrackSettingsValues draft, int tab)
    {
        var panel = Row(Group(null));
        panel.style.alignItems = Align.FlexStart;

        var details = new VisualElement();
        details.style.flexGrow = 1;
        details.style.flexBasis = 0;
        details.style.minWidth = 300;
        details.style.marginRight = 32;
        panel.Add(details);
        SettingsFormStyle.NameAndLogo(details, "Track Name", "Track Logo", draft.name, draft.logo,
            value => TrackCatalog.DisplayNameError(value?.Trim()),
            value => { draft.name = value; Changed(); },
            value => { draft.logo = value; Changed(); });

        var attributes = new VisualElement();
        attributes.style.flexGrow = 1;
        attributes.style.flexBasis = 0;
        attributes.style.minWidth = 300;
        panel.Add(attributes);
        DrawAttributes(attributes, draft.weights);
    }

    private void DrawAttributes(VisualElement panel, TrackWeights values)
    {
        panel.Add(SettingsFormStyle.Label("Track attributes", "settings-group-heading"));
        foreach (var (title, value, component) in TrackRatings.DisplayAttributes(values))
        {
            var row = Row(panel);
            row.style.alignItems = Align.Center;
            row.style.minHeight = 41;
            string help = component.HasValue
                ? $"Set how much {title.ToLowerInvariant()} matters at this track."
                : "Set how quickly this track wears tires.";
            var icon = StartupMenu.TrackAttributeIcon(component);
            icon.tooltip = help;
            icon.style.width = icon.style.height = 40;
            icon.style.flexShrink = 0;
            icon.style.marginRight = 16;
            row.Add(icon);
            var label = SettingsFormStyle.Label(title, "settings-note");
            label.style.minWidth = 101;
            label.style.marginBottom = 0;
            row.Add(label);

            var slider = new Slider(0f, 1f) { tooltip = help };
            slider.style.flexGrow = 1;
            slider.style.marginRight = 12;
            slider.SetValueWithoutNotify(value);
            var handle = slider.Q<VisualElement>("unity-dragger");
            if (handle != null)
                handle.style.backgroundColor = (Color)new Color32(0, 155, 221, 255);
            row.Add(slider);

            var number = SettingsFormStyle.Label(value.ToString("F2"), "settings-note");
            number.style.minWidth = 38;
            number.style.marginBottom = 0;
            number.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(number);
            slider.RegisterValueChangedCallback(evt =>
            {
                float snapped = Mathf.Round(evt.newValue * 100f) / 100f;
                slider.SetValueWithoutNotify(snapped);
                TrackRatings.SetAttribute(values, component, snapped);
                number.text = snapped.ToString("F2");
                Changed();
            });
        }
    }
}
