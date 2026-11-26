using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class PracticeRunChart : VisualElement
{
    private readonly List<(GarageRunReview Run, int Number)> runs;
    internal enum LineStyle { Solid, Dashed, Dotted, DashDot }

    internal sealed class Selection
    {
        internal readonly HashSet<int> Runs = new HashSet<int>();
        internal readonly Color[] Colors = Series.Select((field, index) => field switch
        {
            (_, Axis.Pedals, 0) => Color.green,
            (_, Axis.Pedals, 1) => Color.red,
            _ when index < DefaultSeriesColors.Length => DefaultSeriesColors[index],
            _ => Color.HSVToRGB(Mathf.Repeat(index * 0.618034f, 1f), 0.52f, 1f)
        }).ToArray();
        internal readonly LineStyle[] Styles = Series.Select(field =>
            field.Axis == Axis.Pressure ? LineStyle.Dashed : LineStyle.Solid).ToArray();
        internal bool Initialized;
        internal bool RunTime;
    }

    private readonly HashSet<int> selected;
    private const string FieldsPreference = "StockCarManager.PracticeRunChart.Fields.v1";
    private readonly bool[] visibleSeries;
    private readonly Color[] seriesColors;
    private readonly LineStyle[] seriesStyles;
    private static readonly Color PettyBlue = new Color32(0, 155, 221, 255);
    private static readonly Color[] DefaultSeriesColors =
    {
        new Color(0.35f, 0.75f, 1f),
        new Color(1f, 0.48f, 0.35f),
        new Color(0.4f, 0.95f, 0.55f),
        new Color(0.85f, 0.55f, 1f),
        new Color(1f, 0.85f, 0.3f),
        new Color(0.60f, 0.85f, 1f),
        new Color(1f, 0.70f, 0.58f),
        new Color(0.65f, 1f, 0.75f),
        new Color(0.94f, 0.75f, 1f)
    };
    private readonly VisualElement plot = new VisualElement();
    private readonly VisualElement labels = new VisualElement();
    private readonly VisualElement legend = new VisualElement();
    private readonly ScrollView legendList = new ScrollView(ScrollViewMode.Vertical);
    private int runPage;
    private const int RunsPerPage = 5;
    private readonly Label notice = new Label();
    private readonly Label snapshotStatus = new Label();
    private readonly Button snapshotButton;
    private readonly Label pointTooltip = new Label { pickingMode = PickingMode.Ignore };
    private VisualElement chartArea;
    private int lapCount;
    private bool runTime;
    private float timeStart, timeEnd, totalTime;
    private readonly List<float> startFinishMarkers = new List<float>();
    private readonly Button resetZoom;
    private readonly Label axisTitle = new Label();
    private readonly Scroller timeScrollbar;
    private bool updatingScrollbar;
    private const float LegendWidth = 168f;
    private const float LegendGap = 8f;
    private const float Left = 65f;
    private const float AxisWidth = 70f;
    private const float Top = 45f;
    private const float Bottom = 32f;

    private enum Axis { Temperature, Time, Pressure, Wear, Fuel, Handling, Load, Steering, Pedals, Rpm, Speed }
    private static readonly string[] Corners = { "LF", "RF", "LR", "RR" };
    private static readonly (string Name, Axis Axis, int Channel)[] Series =
    {
        ("LF temp", Axis.Temperature, 0), ("RF temp", Axis.Temperature, 1),
        ("LR temp", Axis.Temperature, 2), ("RR temp", Axis.Temperature, 3),
        ("Lap time", Axis.Time, 0),
        ("LF pressure", Axis.Pressure, 0), ("RF pressure", Axis.Pressure, 1),
        ("LR pressure", Axis.Pressure, 2), ("RR pressure", Axis.Pressure, 3),
        ("LF wear", Axis.Wear, 0), ("RF wear", Axis.Wear, 1),
        ("LR wear", Axis.Wear, 2), ("RR wear", Axis.Wear, 3),
        ("Fuel remaining", Axis.Fuel, 0), ("Fuel used", Axis.Fuel, 1),
        ("Entry balance", Axis.Handling, 0), ("Middle balance", Axis.Handling, 1),
        ("Exit balance", Axis.Handling, 2),
        ("LF dynamic load", Axis.Load, 0), ("RF dynamic load", Axis.Load, 1),
        ("LR dynamic load", Axis.Load, 2), ("RR dynamic load", Axis.Load, 3),
        ("LF static load", Axis.Load, 4), ("RF static load", Axis.Load, 5),
        ("LR static load", Axis.Load, 6), ("RR static load", Axis.Load, 7),
        ("Steering angle", Axis.Steering, 0), ("Throttle", Axis.Pedals, 0), ("Brake", Axis.Pedals, 1),
        ("RPM", Axis.Rpm, 0), ("MPH", Axis.Speed, 0)
    };
    private readonly Dictionary<Axis, (float Min, float Max)> scales = new Dictionary<Axis, (float, float)>();
    private readonly List<Axis> chartAxes = new List<Axis>();
    private float RightAxisWidth => Mathf.Clamp((plot.contentRect.width - Left) * 0.6f /
        Math.Max(1, chartAxes.Count - 1), 50f, AxisWidth);
    private float Right => 12 + Mathf.Max(0, chartAxes.Count - 1) * RightAxisWidth;

    internal PracticeRunChart(RaceSession session, TrackDefinition track, Selection selection)
    {
        selected = selection.Runs;
        visibleSeries = LoadFields();
        seriesColors = selection.Colors;
        seriesStyles = selection.Styles;
        style.flexGrow = 1;
        style.flexBasis = 0;
        style.minHeight = 0;
        var content = new VisualElement();
        content.AddToClassList("practice-chart");
        content.AddToClassList("ui-default-font");
        Add(content);
        var header = new VisualElement { style = { flexDirection = FlexDirection.Row, flexShrink = 0, alignItems = Align.Center } };
        snapshotButton = SettingsFormStyle.Button(string.Empty, () =>
        {
            try
            {
                string path = GarageGraphSnapshot.Save(track, session,
                    selected.OrderBy(index => index).Select(index => runs[index]), visibleSeries, Series.Select(field => field.Name).ToArray());
                snapshotStatus.text = $"Saved {selected.Count} run(s). Path copied to clipboard.";
                snapshotStatus.tooltip = path;
                GUIUtility.systemCopyBuffer = path;
                Debug.Log("Graph snapshot saved: " + path);
            }
            catch (Exception error) when (error is System.IO.IOException ||
                error is UnauthorizedAccessException || error is InvalidOperationException)
            {
                snapshotStatus.text = "Could not save snapshot: " + error.Message;
            }
            snapshotStatus.style.display = DisplayStyle.Flex;
        });
        snapshotButton.tooltip = "Save a chart snapshot containing all data for the selected runs.";
        snapshotButton.style.width = 38;
        snapshotButton.style.flexShrink = 0;
        snapshotButton.style.marginRight = 0;
        snapshotButton.style.paddingLeft = snapshotButton.style.paddingRight = 0;
        snapshotButton.style.alignItems = Align.Center;
        snapshotButton.style.justifyContent = Justify.Center;
        var camera = new VisualElement
        {
            pickingMode = PickingMode.Ignore,
            style = { width = 22, height = 22 }
        };
        camera.AddToClassList("track-icon-art");
        snapshotButton.Add(camera);
        StartupMenu.DrawSnapshotButtonIcon(snapshotButton);
        var options = SettingsFormStyle.Button("Chart Options", null);
        options.tooltip = "Choose the runs and fields to display on the chart.";
        options.clicked += () => OpenOptions(content, options);
        var optionsColumn = new VisualElement
        {
            style = { width = LegendWidth + LegendGap, flexShrink = 0 }
        };
        options.style.marginLeft = 0;
        optionsColumn.Add(options);
        header.Add(optionsColumn);
        runTime = selection.RunTime;
        var byLap = SettingsFormStyle.Button("By Lap", null);
        byLap.tooltip = "Plot the saved samples for each completed lap.";
        var byTime = SettingsFormStyle.Button("Run Time", null);
        byTime.tooltip = "Plot samples taken about every tenth of a second. Scroll over the plot to zoom.";
        resetZoom = SettingsFormStyle.Button("Reset Zoom", ResetTimeZoom);
        resetZoom.tooltip = "Show the entire run again.";
        byLap.style.marginLeft = 0;
        header.Add(byLap);
        header.Add(byTime);
        header.Add(resetZoom);

        void UpdateModeButtons()
        {
            byLap.EnableInClassList("settings-tab-current", !runTime);
            byTime.EnableInClassList("settings-tab-current", runTime);
            resetZoom.style.display = runTime ? DisplayStyle.Flex : DisplayStyle.None;
        }
        void SelectMode(bool useRunTime)
        {
            if (runTime == useRunTime) return;
            runTime = selection.RunTime = useRunTime;
            UpdateModeButtons();
            Refresh();
        }
        byLap.clicked += () => SelectMode(false);
        byTime.clicked += () => SelectMode(true);
        UpdateModeButtons();
        SettingsFormStyle.AddHoverTooltips(content);
        var comparison = new Label(session.name + " · Run comparison")
        {
            style = { flexGrow = 1, minWidth = 0, overflow = Overflow.Hidden, textOverflow = TextOverflow.Ellipsis }
        };
        header.Add(comparison);
        header.Add(snapshotButton);
        content.Add(header);
        snapshotStatus.style.display = DisplayStyle.None;
        snapshotStatus.style.whiteSpace = WhiteSpace.Normal;
        snapshotStatus.style.flexShrink = 0;
        content.Add(snapshotStatus);
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexGrow = 1, minHeight = 0 } };
        content.Add(row);
        FramePanel(legend);
        legend.style.width = LegendWidth;
        legend.style.maxHeight = Length.Percent(100);
        legendList.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        legendList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        legend.Add(legendList);
        legend.style.alignSelf = Align.FlexStart;
        legend.style.paddingLeft = legend.style.paddingRight = 8;
        legend.style.paddingTop = legend.style.paddingBottom = 5;
        legend.style.flexShrink = 0;
        legend.style.marginRight = LegendGap;
        row.Add(legend);

        runs = (session.garageRuns ?? Enumerable.Empty<GarageRunReview>()).Select((run, i) => (Run: run, Number: i + 1))
            .Where(item => item.Run?.complete == true).ToList();
        runPage = Math.Max(0, (runs.Count - 1) / RunsPerPage);
        selected.RemoveWhere(index => index < 0 || index >= runs.Count);
        if (!selection.Initialized && runs.Count > 0)
        {
            selected.Add(runs.Count - 1);
            selection.Initialized = true;
        }
        var right = new VisualElement { style = { flexGrow = 1, minWidth = 0, minHeight = 0 } };
        row.Add(right);
        chartArea = right;
        notice.style.whiteSpace = WhiteSpace.Normal;
        notice.style.flexShrink = 0;
        right.Add(notice);
        plot.style.flexGrow = 1;
        plot.style.flexBasis = 0;
        plot.style.minHeight = 0;
        plot.style.minWidth = 0;
        plot.style.backgroundColor = new Color(0.045f, 0.065f, 0.09f);
        labels.style.position = Position.Absolute;
        labels.style.left = labels.style.right = labels.style.top = labels.style.bottom = 0;
        labels.pickingMode = PickingMode.Ignore;
        plot.Add(labels);
        var gridFrame = new VisualElement { style = { flexGrow = 1, flexBasis = 0, minHeight = 0 } };
        FramePanel(gridFrame);
        gridFrame.style.overflow = Overflow.Hidden;
        gridFrame.Add(plot);
        axisTitle.style.height = 24;
        axisTitle.style.flexShrink = 0;
        axisTitle.style.marginTop = 0;
        axisTitle.style.marginBottom = 4;
        axisTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
        axisTitle.style.fontSize = 14;
        axisTitle.pickingMode = PickingMode.Ignore;
        gridFrame.Add(axisTitle);

        timeScrollbar = new Scroller(0f, 1f, PanTime, SliderDirection.Horizontal)
        {
            tooltip = "Drag to move through the zoomed run."
        };
        timeScrollbar.style.display = DisplayStyle.None;
        timeScrollbar.style.height = 16;
        timeScrollbar.style.flexShrink = 0;
        timeScrollbar.style.marginBottom = 6;
        var thumb = timeScrollbar.slider.Q("unity-dragger");
        if (thumb != null)
            thumb.style.backgroundColor = PettyBlue;
        gridFrame.Add(timeScrollbar);
        right.Add(gridFrame);
        pointTooltip.style.position = Position.Absolute;
        pointTooltip.style.width = 290;
        pointTooltip.style.whiteSpace = WhiteSpace.Normal;
        pointTooltip.style.backgroundColor = new Color(0.02f, 0.03f, 0.045f, 0.98f);
        pointTooltip.style.color = Color.white;
        pointTooltip.style.paddingLeft = pointTooltip.style.paddingRight = 10;
        pointTooltip.style.paddingTop = pointTooltip.style.paddingBottom = 8;
        pointTooltip.style.display = DisplayStyle.None;
        right.Add(pointTooltip);
        plot.RegisterCallback<PointerMoveEvent>(ShowPointTooltip);
        plot.RegisterCallback<PointerLeaveEvent>(_ => HidePointTooltip());
        plot.RegisterCallback<WheelEvent>(ZoomTime);

        plot.generateVisualContent += Draw;
        plot.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            HidePointTooltip();
            BuildLabels();
            plot.MarkDirtyRepaint();
        });
        Refresh();
    }

    private static void FramePanel(VisualElement panel)
    {
        panel.style.borderTopColor = panel.style.borderBottomColor =
            panel.style.borderLeftColor = panel.style.borderRightColor = PettyBlue;
        panel.style.borderTopWidth = panel.style.borderBottomWidth =
            panel.style.borderLeftWidth = panel.style.borderRightWidth = 1;
        panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
            panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 8;
    }

    private void AddLegendEntry(int series, Color color)
    {
        var row = new VisualElement
        {
            style = { flexDirection = FlexDirection.Row, alignItems = Align.Center, height = 20 }
        };
        var label = new Label(Series[series].Name)
        {
            style =
            {
                color = color,
                fontSize = 14,
                unityTextAlign = TextAnchor.MiddleLeft,
                alignSelf = Align.Stretch,
                marginTop = 0,
                marginBottom = 0,
                paddingTop = 0,
                paddingBottom = 0,
                flexGrow = 1
            }
        };
        row.Add(label);
        row.Add(CreateLineSample(series, seriesStyles[series], color));
        legendList.Add(row);
    }

    private static VisualElement CreateLineSample(int series, LineStyle style, Color color)
    {
        var sample = new VisualElement
        {
            pickingMode = PickingMode.Ignore,
            style = { width = 24, height = 10, marginLeft = 6, flexShrink = 0 }
        };
        sample.generateVisualContent += context =>
        {
            var painter = context.painter2D;
            painter.strokeColor = color;
            painter.lineWidth = LineWidth(series);
            painter.BeginPath();
            float centerY = sample.contentRect.center.y;
            var start = new Vector2(0, centerY);
            var end = new Vector2(sample.contentRect.width, centerY);
            if (style == LineStyle.Solid)
            {
                painter.MoveTo(start);
                painter.LineTo(end);
            }
            else
            {
                float offset = 0f;
                DrawPatternSegment(painter, start, end, style, ref offset);
            }
            painter.Stroke();
        };
        return sample;
    }

    private static float LineWidth(int series) => Series[series].Axis == Axis.Time ? 3f : 1.8f;

    private static string LineStyleName(LineStyle style) =>
        style == LineStyle.DashDot ? "Dash-dot" : style.ToString();

    private static void DrawPatternSegment(Painter2D painter, Vector2 start, Vector2 end,
        LineStyle style, ref float offset)
    {
        var (period, dashLength) = style switch
        {
            LineStyle.Dotted => (4f, 1.5f),
            LineStyle.DashDot => (12f, 6.4f),
            _ => (8f, 6.4f)
        };
        Vector2 delta = end - start;
        float length = delta.magnitude;
        if (length <= 0f)
            return;

        Vector2 direction = delta / length;
        for (float dashStart = -offset; dashStart < length; dashStart += period)
        {
            DrawMark(dashStart, dashLength);
            if (style == LineStyle.DashDot)
                DrawMark(dashStart + 8f, 1.5f);
        }
        offset = (offset + length) % period;

        void DrawMark(float from, float markLength)
        {
            float first = Mathf.Max(0f, from);
            float last = Mathf.Min(from + markLength, length);
            if (last <= first)
                return;
            painter.MoveTo(start + direction * first);
            painter.LineTo(start + direction * last);
        }
    }

    private void UpdateLineStyleButton(Button button, int field)
    {
        var style = seriesStyles[field];
        button.tooltip = $"Choose the line style for {Series[field].Name}. Current style is {LineStyleName(style).ToLowerInvariant()}.";
        button.Clear();
        var preview = CreateLineSample(field, style, seriesColors[field]);
        preview.style.width = 44;
        preview.style.marginLeft = 0;
        button.Add(preview);
    }

    private void OpenLineStyles(VisualElement optionsOverlay, Button source, int field)
    {
        var overlay = new VisualElement();
        SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.width = 216;
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        FramePanel(dialog);
        var title = SettingsFormStyle.Label($"{Series[field].Name} line style", "settings-group-heading");
        title.style.whiteSpace = WhiteSpace.Normal;
        title.style.color = PettyBlue;
        dialog.Add(title);

        void Close()
        {
            overlay.RemoveFromHierarchy();
            optionsOverlay.SetEnabled(true);
            source.Focus();
        }

        Button current = null;
        foreach (LineStyle style in Enum.GetValues(typeof(LineStyle)))
        {
            var choice = SettingsFormStyle.Button(string.Empty, () =>
            {
                seriesStyles[field] = style;
                UpdateLineStyleButton(source, field);
                Refresh();
                Close();
            });
            choice.tooltip = $"Use the {LineStyleName(style).ToLowerInvariant()} line style.";
            choice.style.alignItems = Align.Center;
            choice.style.justifyContent = Justify.Center;
            var preview = CreateLineSample(field, style, seriesColors[field]);
            preview.style.width = 66;
            preview.style.marginLeft = 0;
            choice.Add(preview);
            if (seriesStyles[field] == style)
                FramePanel(choice);
            dialog.Add(choice);
            if (seriesStyles[field] == style)
                current = choice;
        }

        var actions = new VisualElement();
        actions.AddToClassList("settings-confirmation-actions");
        actions.style.borderTopWidth = 1;
        actions.style.borderTopColor = PettyBlue;
        var cancel = SettingsFormStyle.Button("Cancel", Close);
        cancel.tooltip = "Close the line style selector.";
        actions.Add(cancel);
        dialog.Add(actions);
        overlay.Add(dialog);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
        optionsOverlay.SetEnabled(false);
        Add(overlay);
        (current ?? cancel).Focus();
    }

    private void OpenOptions(VisualElement content, Button source)
    {
        HidePointTooltip();
        var overlay = new VisualElement();
        SettingsFormStyle.Apply(overlay);
        overlay.AddToClassList("settings-confirmation-overlay");
        var dialog = new VisualElement();
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.style.width = 548;
        dialog.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        dialog.style.borderTopColor = dialog.style.borderBottomColor =
            dialog.style.borderLeftColor = dialog.style.borderRightColor = PettyBlue;
        var title = SettingsFormStyle.Label("Chart Options", "settings-group-heading");
        title.style.color = PettyBlue;
        dialog.Add(title);

        var columns = new VisualElement { style = { flexDirection = FlexDirection.Row } };
        var runChoices = new VisualElement();
        var runPageLabel = new Label();
        var runColumn = new VisualElement { style = { flexGrow = 1, minWidth = 200, marginRight = 20 } };
        runColumn.Add(SettingsFormStyle.Label("Runs", "settings-group-heading"));
        runColumn.Add(runChoices);
        var pager = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        var previous = SettingsFormStyle.Button("‹", null);
        previous.tooltip = "Show the previous page of completed runs.";
        var next = SettingsFormStyle.Button("›", null);
        next.tooltip = "Show the next page of completed runs.";
        int lastPage = Math.Max(0, (runs.Count - 1) / RunsPerPage);
        pager.Add(previous);
        pager.Add(runPageLabel);
        pager.Add(next);
        runColumn.Add(pager);
        pager.style.display = lastPage > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        if (runs.Count == 0)
            runColumn.Add(new Label("No completed runs"));
        columns.Add(runColumn);

        void RefreshPage()
        {
            BuildRunChoices(runChoices);
            runPageLabel.text = $"{runPage + 1}/{lastPage + 1}";
            previous.SetEnabled(runPage > 0);
            next.SetEnabled(runPage < lastPage);
        }
        previous.clicked += () => { runPage--; RefreshPage(); };
        next.clicked += () => { runPage++; RefreshPage(); };
        RefreshPage();

        var fields = new VisualElement { style = { minWidth = 260, flexShrink = 0 } };
        fields.Add(SettingsFormStyle.Label("Fields", "settings-group-heading"));
        for (int field = 0; field < Series.Length; field++)
        {
            if (Series[field].Axis is Axis.Time or Axis.Rpm or Axis.Speed)
                fields.Add(BuildFieldRow(overlay, field, Series[field].Name));
        }
        var tires = AddFieldGroup(fields, "Tires");
        var temperature = AddFieldGroup(tires, "Temperature");
        var pressure = AddFieldGroup(tires, "Pressure");
        var wear = AddFieldGroup(tires, "Wear");
        var fuel = AddFieldGroup(fields, "Fuel");
        var handling = AddFieldGroup(fields, "Estimated handling balance");
        var loads = AddFieldGroup(fields, "Corner loads");
        var dynamicLoads = AddFieldGroup(loads, "Dynamic");
        var staticLoads = AddFieldGroup(loads, "Static");
        var controls = AddFieldGroup(fields, "Car controls");
        for (int field = 0; field < Series.Length; field++)
        {
            var (name, axis, channel) = Series[field];
            var group = axis switch
            {
                Axis.Temperature => temperature,
                Axis.Pressure => pressure,
                Axis.Wear => wear,
                Axis.Fuel => fuel,
                Axis.Handling => handling,
                Axis.Load => channel < 4 ? dynamicLoads : staticLoads,
                Axis.Steering or Axis.Pedals => controls,
                _ => null
            };
            group?.Add(BuildFieldRow(overlay, field, axis is Axis.Temperature or Axis.Pressure or Axis.Wear or Axis.Load
                ? Corners[channel % Corners.Length] : name));
        }
        columns.Add(fields);
        dialog.Add(columns);

        void Close()
        {
            overlay.RemoveFromHierarchy();
            content.SetEnabled(true);
            source.Focus();
        }
        var actions = new VisualElement();
        actions.AddToClassList("settings-confirmation-actions");
        actions.style.borderTopWidth = 1;
        actions.style.borderTopColor = dialog.style.borderTopColor;
        actions.style.paddingTop = 4;
        var done = SettingsFormStyle.Button("Done", Close, primary: true);
        done.tooltip = "Close chart options and return to the chart.";
        actions.Add(done);
        dialog.Add(actions);
        overlay.Add(dialog);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
        content.SetEnabled(false);
        Add(overlay);
        done.Focus();
    }

    private static bool[] LoadFields()
    {
        if (!PlayerPrefs.HasKey(FieldsPreference))
            return Series.Select(field => field.Axis is Axis.Temperature or Axis.Time).ToArray();

        var enabled = new HashSet<string>(PlayerPrefs.GetString(FieldsPreference).Split('|'), StringComparer.Ordinal);
        return Series.Select(field => enabled.Contains(field.Name)).ToArray();
    }

    private void SaveFields()
    {
        PlayerPrefs.SetString(FieldsPreference,
            string.Join("|", Series.Where((_, index) => visibleSeries[index]).Select(field => field.Name)));
        PlayerPrefs.Save();
    }

    private static Foldout AddFieldGroup(VisualElement parent, string title)
    {
        var group = new Foldout { text = title, value = false };
        group.Q<Toggle>().tooltip = $"Expand or collapse {title} fields.";
        group.RegisterValueChangedCallback(evt =>
        {
            if (evt.target != group || !evt.newValue)
                return;
            // Keep the field controls and footer within the dialog at small window sizes.
            foreach (var sibling in group.parent.Children().OfType<Foldout>())
                if (sibling != group)
                    sibling.value = false;
        });
        parent.Add(group);
        return group;
    }

    private VisualElement BuildFieldRow(VisualElement overlay, int field, string label)
    {
        var fieldSwitch = SettingsFormStyle.PillSwitch(label, visibleSeries[field]);
        fieldSwitch.tooltip = $"Show or hide {Series[field].Name} on the chart.";
        var row = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };
        fieldSwitch.style.flexGrow = 1;
        row.Add(fieldSwitch);

        var swatch = SettingsFormStyle.Button(string.Empty, null);
        swatch.tooltip = $"Change the chart line color for {Series[field].Name}.";
        swatch.style.width = 26;
        swatch.style.height = 19;
        swatch.style.minHeight = 19;
        swatch.style.flexShrink = 0;
        swatch.style.marginTop = swatch.style.marginBottom = 1;
        swatch.style.paddingTop = swatch.style.paddingBottom = 2;
        swatch.style.paddingLeft = swatch.style.paddingRight = 2;
        var fill = new VisualElement { pickingMode = PickingMode.Ignore };
        fill.style.flexGrow = 1;
        fill.style.alignSelf = Align.Stretch;
        fill.style.backgroundColor = seriesColors[field];
        swatch.Add(fill);
        swatch.SetEnabled(visibleSeries[field]);
        fieldSwitch.RegisterValueChangedCallback(evt =>
        {
            visibleSeries[field] = evt.newValue;
            SaveFields();
            swatch.SetEnabled(evt.newValue);
            Refresh();
        });
        row.Add(swatch);

        var lineStyle = SettingsFormStyle.Button(string.Empty, null);
        lineStyle.style.width = 56;
        lineStyle.style.alignItems = Align.Center;
        lineStyle.style.justifyContent = Justify.Center;
        lineStyle.style.minHeight = 24;
        lineStyle.style.flexShrink = 0;
        lineStyle.style.marginTop = lineStyle.style.marginBottom = 1;
        lineStyle.style.paddingTop = lineStyle.style.paddingBottom = 0;
        lineStyle.style.paddingLeft = lineStyle.style.paddingRight = 4;
        lineStyle.clicked += () => OpenLineStyles(overlay, lineStyle, field);
        UpdateLineStyleButton(lineStyle, field);
        swatch.clicked += () => GameColorPicker.Open(overlay, seriesColors[field], color =>
        {
            seriesColors[field] = color;
            fill.style.backgroundColor = (Color)color;
            UpdateLineStyleButton(lineStyle, field);
            Refresh();
        });
        row.Add(lineStyle);
        return row;
    }

    private void BuildRunChoices(VisualElement runChoices)
    {
        runChoices.Clear();
        int end = Math.Min(runs.Count, (runPage + 1) * RunsPerPage);
        for (int i = runPage * RunsPerPage; i < end; i++)
        {
            int index = i;
            var runSwitch = SettingsFormStyle.PillSwitch($"Run {runs[i].Number} ({runs[i].Run.lapTimes.Count} laps)", selected.Contains(i));
            runSwitch.tooltip = $"Show or hide run {runs[i].Number} on the chart.";
            runSwitch.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) selected.Add(index);
                else selected.Remove(index);
                Refresh();
            });
            runChoices.Add(runSwitch);
        }
    }

    private Color SeriesColor(int run, int series)
    {
        // The newest selected run is brightest. Older selected runs share its hue.
        int shade = selected.Count(index => index > run);
        float brightness = Mathf.Max(0.45f, Mathf.Pow(0.86f, shade));
        Color color = seriesColors[series] * brightness;
        color.a = 1f;
        return color;
    }

    private void Refresh()
    {
        HidePointTooltip();
        snapshotButton.SetEnabled(selected.Count > 0);
        var activeRuns = selected.OrderByDescending(index => index).ToList();
        legendList.Clear();
        chartAxes.Clear();
        scales.Clear();
        var bounds = new Dictionary<Axis, (float Min, float Max)>();
        bool missingSamples = false;
        for (int series = 0; series < Series.Length; series++)
        {
            if (!visibleSeries[series] || runTime && !SupportsRunTime(series))
                continue;
            var axis = Series[series].Axis;
            bool addedLegend = false;
            foreach (int index in activeRuns)
            {
                var run = runs[index].Run;
                int count = SampleCount(run);
                if (runTime && count == 0)
                    missingSamples = true;
                for (int sample = 0; sample < count; sample++)
                {
                    if (!TryChartValue(run, sample, series, out float value))
                    {
                        missingSamples = true;
                        continue;
                    }
                    if (!addedLegend)
                    {
                        AddLegendEntry(series, SeriesColor(index, series));
                        addedLegend = true;
                    }
                    if (bounds.TryGetValue(axis, out var range))
                        bounds[axis] = (Mathf.Min(range.Min, value), Mathf.Max(range.Max, value));
                    else
                        bounds.Add(axis, (value, value));
                }
            }
        }
        bool hasSamples = bounds.Count > 0;
        if (!runTime && Series.Where((_, index) => visibleSeries[index]).Any(field => field.Axis == Axis.Handling))
            bounds.TryAdd(Axis.Handling, (0f, 0f));

        foreach (var axis in bounds.Keys.OrderBy(axis => axis))
        {
            chartAxes.Add(axis);
            var (minimum, maximum) = bounds[axis];
            float signedLimit = Mathf.Max(5f, Mathf.Ceil(Mathf.Max(Mathf.Abs(minimum), Mathf.Abs(maximum)) / 5f) * 5f);
            scales[axis] = axis switch
            {
                Axis.Temperature => (75f, 325f),
                Axis.Time => (Mathf.Floor(minimum - 0.5f), Mathf.Ceil(maximum + 0.5f)),
                Axis.Pressure or Axis.Wear or Axis.Pedals => (0f, 100f),
                Axis.Fuel => (Mathf.Min(0f, Mathf.Floor(minimum / 5f) * 5f), Mathf.Max(5f, Mathf.Ceil(maximum / 5f) * 5f)),
                Axis.Handling or Axis.Steering => (-signedLimit, signedLimit),
                Axis.Rpm => (0f, Mathf.Max(1000f, Mathf.Ceil(maximum / 1000f) * 1000f)),
                Axis.Speed => (0f, Mathf.Max(25f, Mathf.Ceil(maximum / 25f) * 25f)),
                _ => (0f, Mathf.Max(250f, Mathf.Ceil(maximum / 250f) * 250f))
            };
        }

        legend.style.visibility = legendList.contentContainer.childCount == 0 ? Visibility.Hidden : Visibility.Visible;
        lapCount = activeRuns.Select(index => runs[index].Run.lapTimes.Count).DefaultIfEmpty(0).Max();
        float duration = 0f;
        startFinishMarkers.Clear();
        if (runTime)
        {
            foreach (int index in activeRuns)
            {
                var run = runs[index].Run;
                if (run.timeSamples?.Count > 0)
                {
                    duration = Mathf.Max(duration, run.timeSamples[run.timeSamples.Count - 1].elapsedSeconds);
                    var crossings = run.startFinishCrossingSeconds?.Count > 0
                        ? run.startFinishCrossingSeconds : run.lapFinishSeconds;
                    if (crossings?.Count > 0)
                    {
                        startFinishMarkers.AddRange(crossings);
                        duration = Mathf.Max(duration, crossings[crossings.Count - 1]);
                    }
                }
            }
        }
        if (duration != totalTime)
        {
            totalTime = duration;
            timeStart = 0f;
            timeEnd = duration;
        }
        UpdateTimeScrollbar();
        notice.text = runs.Count == 0 ? "Run data appears after an on-track run finishes." :
            selected.Count == 0 ? "Select a run to compare." :
            !visibleSeries.Any(visible => visible) ? "Select a line to display." :
            runTime && !activeRuns.Any(index => runs[index].Run.timeSamples?.Count > 0)
                ? "The selected runs have no Run Time samples. Record a new run or switch to By Lap." :
            runTime && !Series.Where((_, index) => visibleSeries[index] && SupportsRunTime(index)).Any()
                ? "Select RPM, MPH, a car control, tire temperature, tire pressure, or a dynamic corner load for Run Time." :
            !hasSamples ? "No recorded samples for the selected runs and fields." :
            missingSamples ? "Some runs lack recorded data for the selected fields." : "";
        notice.style.display = string.IsNullOrEmpty(notice.text) ? DisplayStyle.None : DisplayStyle.Flex;
        axisTitle.text = runTime ? "Run time (s)" : "Lap Number";
        axisTitle.style.display = runTime || lapCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        BuildLabels();
        plot.MarkDirtyRepaint();
    }

    private float PlotWidth => plot.contentRect.width - Left - Right;
    private float X(float position)
    {
        float fraction = runTime
            ? timeEnd > timeStart ? (position - timeStart) / (timeEnd - timeStart) : 0.5f
            : lapCount <= 1 ? 0.5f : position / (lapCount - 1);
        return Left + fraction * PlotWidth;
    }

    private void ResetTimeZoom()
    {
        timeStart = 0f;
        timeEnd = totalTime;
        RedrawTimeWindow();
    }

    private void ZoomTime(WheelEvent evt)
    {
        if (!runTime || totalTime <= 1f || PlotWidth <= 0f)
            return;
        float x = plot.WorldToLocal(evt.mousePosition).x;
        if (x < Left || x > Left + PlotWidth)
            return;
        float anchor = (x - Left) / PlotWidth;
        float duration = timeEnd - timeStart;
        float next = Mathf.Clamp(duration * Mathf.Pow(1.25f, evt.delta.y / 3f), 1f, totalTime);
        timeStart = Mathf.Clamp(timeStart + (duration - next) * anchor, 0f, totalTime - next);
        timeEnd = timeStart + next;
        RedrawTimeWindow();
        evt.StopPropagation();
    }

    private void PanTime(float start)
    {
        if (updatingScrollbar || !runTime)
            return;
        float duration = timeEnd - timeStart;
        timeStart = Mathf.Clamp(start, 0f, Mathf.Max(0f, totalTime - duration));
        timeEnd = timeStart + duration;
        RedrawTimeWindow();
    }

    private void UpdateTimeScrollbar()
    {
        float duration = timeEnd - timeStart;
        float range = Mathf.Max(0f, totalTime - duration);
        bool zoomed = runTime && range > 0.001f;
        resetZoom.SetEnabled(zoomed);
        timeScrollbar.style.display = zoomed ? DisplayStyle.Flex : DisplayStyle.None;
        if (!zoomed)
            return;

        updatingScrollbar = true;
        timeScrollbar.highValue = range;
        timeScrollbar.slider.SetValueWithoutNotify(timeStart);
        timeScrollbar.Adjust(duration / totalTime);
        updatingScrollbar = false;
    }

    private void RedrawTimeWindow()
    {
        UpdateTimeScrollbar();
        HidePointTooltip();
        BuildLabels();
        plot.MarkDirtyRepaint();
    }
    private float Y(float normalized) => Top + (1f - normalized) * (plot.contentRect.height - Top - Bottom);

    private void BuildLabels()
    {
        labels.Clear();
        axisTitle.style.marginLeft = timeScrollbar.style.marginLeft = Left;
        axisTitle.style.marginRight = timeScrollbar.style.marginRight = Right;
        if (plot.contentRect.width <= Left + Right)
            return;
        for (int index = 0; index < chartAxes.Count; index++)
        {
            var axis = chartAxes[index];
            var (minimum, maximum) = scales[axis];
            float axisWidth = index == 0 ? Left : RightAxisWidth;
            float x = index == 0 ? 5f : plot.contentRect.width - Right + 4 + (index - 1) * axisWidth;
            int fontSize = axisWidth < 65f ? 12 : 14;
            LabelAt(AxisLabel(axis), x, 5, axisWidth, fontSize);
            for (int tick = 0; tick <= 5; tick++)
            {
                float fraction = tick / 5f;
                string value = Mathf.Lerp(minimum, maximum, fraction).ToString(axis == Axis.Time ? "0.00" : "0.#");
                if (axis == Axis.Handling && (tick == 0 || tick == 5))
                    value = tick == 0 ? "Loose" : "Tight";
                LabelAt(value, x, Y(fraction) - 10, axisWidth, fontSize);
            }
            if (axis == Axis.Handling)
                LabelAt("0", x, Y(0.5f) - 10, axisWidth, fontSize);
        }
        if (runTime)
        {
            BuildTimeLabels();
            return;
        }
        if (lapCount == 0)
            return;

        int labelStep = Math.Max(1, Mathf.CeilToInt(lapCount * 28f / (plot.contentRect.width - Left - Right)));
        for (int lap = 0; lap < lapCount; lap++)
        {
            if (lap % labelStep == 0 || lap == lapCount - 1)
                LabelAt((lap + 1).ToString(), X(lap) - 12, Y(0) + 8, 28);
        }
    }

    private void BuildTimeLabels()
    {
        float duration = timeEnd - timeStart;
        if (duration > 0f)
        {
            float target = duration / Mathf.Max(1f, Mathf.Floor(PlotWidth / 64f));
            float power = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(target)));
            float step = target <= power * 2f ? power * 2f : target <= power * 5f ? power * 5f : power * 10f;
            for (float second = Mathf.Ceil(timeStart / step) * step; second <= timeEnd; second += step)
                LabelAt(second.ToString("0.##"), X(second) - 12, Y(0) + 8, 64);
        }
    }

    private void LabelAt(string text, float x, float y, float width, int fontSize = 14)
    {
        var label = new Label(text);
        label.style.position = Position.Absolute;
        label.style.left = x;
        label.style.top = y;
        label.style.width = width;
        label.style.color = Color.white;
        label.style.fontSize = fontSize;
        labels.Add(label);
    }

    private void HidePointTooltip() => pointTooltip.style.display = DisplayStyle.None;

    private void ShowPointTooltip(PointerMoveEvent evt)
    {
        Vector2 pointer = plot.WorldToLocal(evt.position);
        float width = plot.contentRect.width - Left - Right;
        if (width <= 0f || pointer.y < Top - 8f || pointer.y > Y(0) + 8f)
        {
            HidePointTooltip();
            return;
        }
        float fraction = (pointer.x - Left) / width;
        if (fraction < 0f || fraction > 1f)
        {
            HidePointTooltip();
            return;
        }
        float nearest = 64f;
        var hits = new List<string>();
        foreach (int index in selected.OrderBy(index => index))
        {
            var run = runs[index].Run;
            int sample = runTime ? NearestTimeSample(run, Mathf.Lerp(timeStart, timeEnd, fraction))
                : Mathf.RoundToInt(fraction * (lapCount - 1));
            if (sample < 0 || sample >= SampleCount(run))
                continue;
            float position = runTime ? run.timeSamples[sample].elapsedSeconds : sample;
            if (runTime && (position < timeStart || position > timeEnd))
                continue;
            for (int series = 0; series < Series.Length; series++)
            {
                if (!visibleSeries[series])
                    continue;
                if (!TryChartValue(run, sample, series, out float value)) continue;
                float normalized = Normalize(series, value);
                float distance = (pointer - new Vector2(X(position), Y(normalized))).sqrMagnitude;
                if (distance > 64f || distance > nearest + 0.01f)
                    continue;
                if (distance < nearest - 0.01f)
                    hits.Clear();
                nearest = distance;
                string units = Units(Series[series].Axis);
                string location = runTime ? $"{position:0.00}s" : $"Lap {sample + 1}";
                hits.Add($"Run {runs[index].Number} · {location}\n{Series[series].Name} {value:G6} {units}");
            }
        }
        if (hits.Count == 0)
        {
            HidePointTooltip();
            return;
        }
        pointTooltip.text = string.Join("\n\n", hits);
        pointTooltip.style.display = DisplayStyle.Flex;
        pointTooltip.BringToFront();
        Vector2 local = chartArea.WorldToLocal(evt.position);
        float tooltipWidth = Mathf.Min(290f, chartArea.contentRect.width);
        pointTooltip.style.width = tooltipWidth;
        pointTooltip.style.left = Mathf.Clamp(local.x + 14f, 0f,
            Mathf.Max(0f, chartArea.contentRect.width - tooltipWidth));
        float height = hits.Count * 52f + 16f;
        pointTooltip.style.top = Mathf.Clamp(local.y + 16f, 0f,
            Mathf.Max(0f, chartArea.contentRect.height - height));
    }

    private int SampleCount(GarageRunReview run) => runTime ? run.timeSamples?.Count ?? 0 : run.lapTimes.Count;

    private static int FindTimeSample(List<GarageRunTimeSample> samples, float time)
    {
        int low = 0, high = samples.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (samples[middle].elapsedSeconds < time) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private static int NearestTimeSample(GarageRunReview run, float time)
    {
        var samples = run.timeSamples;
        if (samples == null || samples.Count == 0) return -1;
        int next = FindTimeSample(samples, time);
        if (next == samples.Count || next > 0 &&
            time - samples[next - 1].elapsedSeconds < samples[next].elapsedSeconds - time)
            return next - 1;
        return next;
    }

    private static bool SupportsRunTime(int series)
    {
        var (_, axis, channel) = Series[series];
        return axis is Axis.Temperature or Axis.Pressure or Axis.Steering or Axis.Pedals or Axis.Rpm or Axis.Speed ||
            axis == Axis.Load && channel < 4;
    }

    private bool TryChartValue(GarageRunReview run, int index, int series, out float value)
    {
        if (!runTime) return TryValue(run, index, series, out value);
        var sample = run.timeSamples[index];
        var (_, axis, channel) = Series[series];
        value = axis switch
        {
            Axis.Temperature => sample.temperatures[channel],
            Axis.Pressure => sample.pressures[channel],
            Axis.Load when channel < 4 && sample.hasDynamicLoads => sample.dynamicLoads[channel],
            Axis.Steering when sample.hasSteering => sample.steeringAngleDegrees,
            Axis.Pedals => (channel == 0 ? sample.throttle : sample.brake) * 100f,
            Axis.Rpm when sample.hasPowertrainSample => sample.engineRpm,
            Axis.Speed when sample.hasPowertrainSample => sample.speedMph,
            _ => float.NaN
        };
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string AxisLabel(Axis axis) => axis switch
    {
        Axis.Temperature => "Temp (°F)",
        Axis.Time => "Time (s)",
        Axis.Pressure => "PSI",
        Axis.Wear => "Wear (%)",
        Axis.Fuel => "Fuel (gal)",
        Axis.Handling => "Balance",
        Axis.Steering => "Steer (°)",
        Axis.Pedals => "Pedal %",
        Axis.Rpm => "RPM",
        Axis.Speed => "MPH",
        _ => "Load (lb)"
    };

    private static string Units(Axis axis) => axis switch
    {
        Axis.Temperature => "°F",
        Axis.Time => "s",
        Axis.Pressure => "PSI",
        Axis.Wear => "%",
        Axis.Fuel => "gal",
        Axis.Handling => "points",
        Axis.Steering => "°",
        Axis.Pedals => "%",
        Axis.Rpm => "RPM",
        Axis.Speed => "MPH",
        _ => "lb"
    };

    internal static bool TryValue(GarageRunReview run, int lap, int series, out float value)
    {
        value = 0f;
        if (lap < 0 || lap >= run.lapTimes.Count) return false;
        var (_, axis, channel) = Series[series];
        if (axis == Axis.Time)
        {
            value = run.lapTimes[lap];
            return value > 0f && !float.IsInfinity(value);
        }
        if (run.lapTemperatures == null || lap >= run.lapTemperatures.Count) return false;
        var sample = run.lapTemperatures[lap];
        var diagnostics = sample?.diagnostics;
        switch (axis)
        {
            case Axis.Temperature:
                value = CornerValue(sample?.tires, channel);
                break;
            case Axis.Pressure:
                value = CornerValue(diagnostics?.runningPressuresPsi, channel);
                break;
            case Axis.Wear:
                value = CornerValue(diagnostics?.tireWear, channel) * 100f;
                break;
            case Axis.Fuel:
                value = diagnostics == null ? float.NaN : channel == 0
                    ? (float)diagnostics.fuelGallons
                    : (float)((run.startDiagnostics?.fuelGallons ?? double.NaN) - diagnostics.fuelGallons);
                break;
            case Axis.Handling:
                var phases = sample?.handling;
                var phase = phases?.Length == 3 ? phases[channel] : null;
                value = phase != null && phase.seconds > 0d ? phase.Average : float.NaN;
                break;
            case Axis.Steering:
                value = diagnostics?.hasSteeringAngle == true ? diagnostics.steeringAngleDegrees : float.NaN;
                break;
            case Axis.Rpm:
                value = diagnostics?.hasPowertrainSample == true ? diagnostics.engineRpm : float.NaN;
                break;
            case Axis.Speed:
                value = diagnostics?.hasPowertrainSample == true ? diagnostics.speedMph : float.NaN;
                break;
            case Axis.Pedals:
                value = diagnostics?.hasCarControls == true
                    ? (channel == 0 ? diagnostics.throttleInput : diagnostics.brakeInput) * 100f
                    : float.NaN;
                break;
            case Axis.Load:
                value = diagnostics?.weightDynamicsEnabled == true
                    ? channel < 4 ? CornerValue(diagnostics.dynamicCornerWeightsPounds, channel)
                        : CornerValue(diagnostics.cornerWeightsPounds, channel - 4)
                    : float.NaN;
                break;
        }
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float CornerValue(float[] values, int corner) =>
        values?.Length == 4 ? values[corner] : float.NaN;

    private float Normalize(int series, float value)
    {
        var (minimum, maximum) = scales[Series[series].Axis];
        return Mathf.InverseLerp(minimum, maximum, value);
    }

    private void DrawTimeTrace(Painter2D painter, GarageRunReview run, int series, Color color)
    {
        var samples = run.timeSamples;
        if (samples == null || samples.Count == 0)
            return;

        painter.strokeColor = color;
        painter.fillColor = color;
        painter.lineWidth = LineWidth(series);
        var style = seriesStyles[series];
        int column = int.MinValue;
        Vector2 low = default, high = default, previous = default;
        bool started = false;
        int pointCount = 0;
        float offset = 0f;
        painter.BeginPath();
        for (int index = FindTimeSample(samples, timeStart); index < samples.Count; index++)
        {
            float second = samples[index].elapsedSeconds;
            if (second > timeEnd) break;
            if (!TryChartValue(run, index, series, out float value))
            {
                FlushColumn();
                started = false;
                offset = 0f;
                continue;
            }
            var point = new Vector2(X(second), Y(Normalize(series, value)));
            int nextColumn = Mathf.FloorToInt(point.x);
            if (nextColumn != column)
            {
                FlushColumn();
                column = nextColumn;
                low = high = point;
            }
            else
            {
                // Preserve spikes when more than one 10 Hz sample falls in a pixel.
                if (point.y < low.y) low = point;
                if (point.y > high.y) high = point;
            }
        }
        FlushColumn();
        painter.Stroke();
        if (pointCount == 1)
        {
            painter.BeginPath();
            painter.MoveTo(previous + new Vector2(-2f, -2f));
            painter.LineTo(previous + new Vector2(2f, -2f));
            painter.LineTo(previous + new Vector2(2f, 2f));
            painter.LineTo(previous + new Vector2(-2f, 2f));
            painter.ClosePath();
            painter.Fill();
        }

        void FlushColumn()
        {
            if (column == int.MinValue) return;
            if (low.x <= high.x)
            {
                PlotPoint(low);
                if (high != low) PlotPoint(high);
            }
            else
            {
                PlotPoint(high);
                PlotPoint(low);
            }
            column = int.MinValue;
        }

        void PlotPoint(Vector2 point)
        {
            if (!started) painter.MoveTo(point);
            else if (style == LineStyle.Solid) painter.LineTo(point);
            else DrawPatternSegment(painter, previous, point, style, ref offset);
            previous = point;
            started = true;
            pointCount++;
        }
    }

    private void Draw(MeshGenerationContext context)
    {
        if (plot.contentRect.width <= Left + Right)
            return;
        var painter = context.painter2D;
        painter.lineWidth = 1;
        painter.strokeColor = new Color(0.25f, 0.3f, 0.36f);
        for (int tick = 0; tick <= 5; tick++)
        {
            painter.BeginPath();
            painter.MoveTo(new Vector2(Left, Y(tick / 5f)));
            painter.LineTo(new Vector2(plot.contentRect.width - Right, Y(tick / 5f)));
            painter.Stroke();
        }
        if (scales.ContainsKey(Axis.Handling))
        {
            float zeroY = Y(0.5f);
            painter.strokeColor = new Color(0.54f, 0.63f, 0.73f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(Left, zeroY));
            painter.LineTo(new Vector2(plot.contentRect.width - Right, zeroY));
            painter.Stroke();
        }
        if (runTime && startFinishMarkers.Count > 0)
        {
            painter.lineWidth = 1f;
            painter.strokeColor = new Color(0.38f, 0.54f, 0.62f);
            painter.BeginPath();
            foreach (float marker in startFinishMarkers)
            {
                if (marker < timeStart || marker > timeEnd) continue;
                float x = X(marker);
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, plot.contentRect.height));
            }
            painter.Stroke();
        }
        foreach (int index in selected.OrderBy(index => index))
        {
            var run = runs[index].Run;
            for (int series = 0; series < Series.Length; series++)
            {
                if (!visibleSeries[series] || !scales.ContainsKey(Series[series].Axis))
                    continue;
                if (runTime)
                {
                    if (SupportsRunTime(series))
                        DrawTimeTrace(painter, run, series, SeriesColor(index, series));
                    continue;
                }
                int count = Series[series].Axis == Axis.Time ? run.lapTimes.Count :
                    Math.Min(run.lapTimes.Count, run.lapTemperatures?.Count ?? 0);
                painter.strokeColor = SeriesColor(index, series);
                painter.fillColor = painter.strokeColor;
                painter.lineWidth = LineWidth(series);
                var lineStyle = seriesStyles[series];
                bool started = false;
                Vector2 previous = default;
                float dashOffset = 0f;
                painter.BeginPath();
                for (int lap = 0; lap < count; lap++)
                {
                    if (!TryValue(run, lap, series, out float value))
                    {
                        started = false;
                        dashOffset = 0f;
                        continue;
                    }
                    var point = new Vector2(X(lap), Y(Normalize(series, value)));
                    if (!started)
                        painter.MoveTo(point);
                    else if (lineStyle != LineStyle.Solid)
                        DrawPatternSegment(painter, previous, point, lineStyle, ref dashOffset);
                    else
                        painter.LineTo(point);
                    previous = point;
                    started = true;
                }
                painter.Stroke();
                // Mark each sample so one-lap runs are visible too.
                for (int lap = 0; lap < count; lap++)
                {
                    if (!TryValue(run, lap, series, out float value)) continue;
                    float normalized = Normalize(series, value);
                    float x = X(lap), y = Y(normalized);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x - 2, y - 2));
                    painter.LineTo(new Vector2(x + 2, y - 2));
                    painter.LineTo(new Vector2(x + 2, y + 2));
                    painter.LineTo(new Vector2(x - 2, y + 2));
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }
    }
}
