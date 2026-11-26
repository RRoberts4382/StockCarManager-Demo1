using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class SeriesRaceCalendar
{
    internal sealed class Clipboard
    {
        internal SeriesCalendarRace[] Races = Array.Empty<SeriesCalendarRace>();
    }

    private const string CalendarHint = "Drag to move or swap. Ctrl-click individual dates or Shift-click a range, then right-click to copy, paste, or remove races.";
    private static readonly int[] LapOptions = { 25, 50, 75, 100, 125, 150, 175, 200, 300, 400, 500, 600 };
    private readonly VisualElement screen, frame;
    private readonly RacingSeries series;
    private readonly Clipboard clipboard;
    private readonly TrackCatalog catalog;
    private readonly Action<DateTime, DateTime> rememberView;
    private readonly Action changed;
    private readonly bool listView;
    private readonly VisualElement panel = new();
    private readonly Dictionary<Button, DateTime> dates = new();
    private readonly HashSet<string> pasteDates = new(StringComparer.Ordinal);
    private DateTime firstMonth, selectedDate, selectionAnchor;
    private Label dragHint;
    private Button dragSource, dropTarget;
    private SeriesCalendarRace draggedRace;
    private int dragPointer;
    private Vector2 dragStart;
    private bool isDragging;
    private StyleColor targetBackground;

    internal static VisualElement CreatePanel(VisualElement screen, VisualElement frame, TrackCatalog catalog,
        RacingSeries series, Clipboard clipboard, DateTime month, DateTime selected, bool listView,
        Action<DateTime, DateTime> rememberView, Action changed)
    {
        var calendar = new SeriesRaceCalendar(screen, frame, catalog, series, clipboard, month, selected, listView, rememberView, changed);
        calendar.Render();
        return calendar.panel;
    }

    private SeriesRaceCalendar(VisualElement screen, VisualElement frame, TrackCatalog catalog,
        RacingSeries series, Clipboard clipboard, DateTime month, DateTime selected, bool listView,
        Action<DateTime, DateTime> rememberView, Action changed)
    {
        this.screen = screen;
        this.frame = frame;
        this.catalog = catalog;
        this.series = series;
        this.clipboard = clipboard;
        this.listView = listView;
        this.rememberView = rememberView;
        this.changed = changed;
        panel.style.flexGrow = 1;
        var firstDate = FirstScheduledDate(DateTime.Today.Year);
        firstMonth = month == default ? MonthOf(firstDate) : month;
        selectedDate = selected == default ? firstDate : selected;
        selectionAnchor = selectedDate;
        panel.RegisterCallback<PointerMoveEvent>(UpdateDrag);
        panel.RegisterCallback<PointerUpEvent>(FinishDrag);
        panel.RegisterCallback<PointerCancelEvent>(evt => CancelDrag(evt.pointerId));
        panel.RegisterCallback<PointerCaptureOutEvent>(evt => CancelDrag(evt.pointerId));
    }

    private SeriesCalendarRace[] Races => series.calendarRaces ?? Array.Empty<SeriesCalendarRace>();
    private DateTime FirstScheduledDate(int year)
    {
        var firstRace = Races.OrderBy(race => race.date, StringComparer.Ordinal).FirstOrDefault();
        return SeasonSchedule.TryScheduleDate(firstRace?.date, year, out var date)
            ? date : new DateTime(year, 1, 1);
    }

    private static DateTime MonthOf(DateTime date) => new(date.Year, date.Month, 1);

    private static string DateText(DateTime date) => date.ToString("MM/dd", CultureInfo.InvariantCulture);
    private static string TimeText(string time) =>
        SessionWeatherForecast.TryStartTime(time, out var parsed)
            ? DateTime.Today.Add(parsed).ToString("h:mm tt", CultureInfo.InvariantCulture)
            : time;

    private void Render(bool focusDay = false, int navigationDirection = 0)
    {
        rememberView(firstMonth, selectedDate);
        dates.Clear();
        panel.Clear();
        if (listView)
        {
            DrawRaceList(focusDay);
            return;
        }
        var calendar = new VisualElement();
        calendar.style.alignSelf = Align.Center;
        calendar.style.width = Length.Percent(100);
        calendar.style.flexGrow = 1;
        panel.Add(calendar);

        var navigation = Row(calendar);
        navigation.style.justifyContent = Justify.SpaceBetween;
        var leading = Row(navigation);
        var firstDate = FirstScheduledDate(firstMonth.Year);
        leading.Add(NavigationButton("|\u25C2", "Return to the first scheduled race month, or January if there are no races.",
            () => JumpToDate(firstDate)));
        var previous = NavigationButton("\u25C2", "Previous month", () => ShiftMonth(-1));
        previous.SetEnabled(firstMonth.Month > 1);
        leading.Add(previous);

        var trailing = Row(navigation);
        var next = NavigationButton("\u25B8", "Next month", () => ShiftMonth(1));
        next.SetEnabled(firstMonth.Month < 12);
        trailing.Add(next);
        var lastRace = Races.OrderByDescending(race => race.date, StringComparer.Ordinal).FirstOrDefault();
        bool hasLastRace = SeasonSchedule.TryScheduleDate(lastRace?.date, firstMonth.Year, out var lastDate);
        var last = NavigationButton("\u25B8|", "Jump to the last scheduled race.", () => JumpToDate(lastDate, atEnd: true));
        last.SetEnabled(hasLastRace);
        trailing.Add(last);

        var months = Row(calendar);
        months.style.alignItems = Align.Stretch;
        months.style.flexGrow = 1;
        Button selectedButton = null;
        for (int i = 0; i < 3; i++)
        {
            var monthPanel = new VisualElement();
            monthPanel.style.flexGrow = 1;
            monthPanel.style.flexBasis = 0;
            monthPanel.style.minWidth = 0;
            monthPanel.style.marginRight = i < 2 ? 12 : 0;
            monthPanel.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
            monthPanel.style.borderLeftWidth = monthPanel.style.borderRightWidth = 1;
            monthPanel.style.borderTopWidth = monthPanel.style.borderBottomWidth = 1;
            var border = new StyleColor(new Color32(98, 120, 140, 255));
            monthPanel.style.borderLeftColor = monthPanel.style.borderRightColor = border;
            monthPanel.style.borderTopColor = monthPanel.style.borderBottomColor = border;
            monthPanel.style.paddingBottom = 4;
            months.Add(monthPanel);
            var day = DrawMonth(monthPanel, firstMonth.AddMonths(i));
            selectedButton ??= day;
        }
        dragHint = SettingsFormStyle.Label(CalendarHint, "settings-note");
        calendar.Add(dragHint);
        if (navigationDirection < 0)
            (previous.enabledSelf ? previous : next).Focus();
        else if (navigationDirection > 0)
            (next.enabledSelf ? next : previous).Focus();
        else if (focusDay)
            selectedButton?.Focus();
    }

    private void DrawRaceList(bool focusDay)
    {
        if (Races.Length == 0)
        {
            panel.Add(SettingsFormStyle.Label("No races scheduled. Switch to Calendar View to add a race.", "settings-note"));
            return;
        }

        var heading = Row(panel);
        heading.style.paddingLeft = heading.style.paddingRight = 13;
        AddRaceColumns(heading, "Date", "Track", "Laps", "Start time");

        var list = new ScrollView(ScrollViewMode.Vertical);
        list.style.flexGrow = 1;
        list.style.flexBasis = 0;
        list.style.minHeight = 100;
        list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        list.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        panel.Add(list);
        Button selected = null;
        foreach (var race in Races.OrderBy(entry => entry.date, StringComparer.Ordinal))
        {
            if (!SeasonSchedule.TryScheduleDate(race.date, firstMonth.Year, out var date))
                continue;
            string track = catalog.Find(race.trackId)?.Definition.name ?? "Unavailable track";
            string label = date.ToString("MMM d", CultureInfo.InvariantCulture);
            Button row = null;
            row = Button("", $"Edit the {label} race at {track}.", () =>
            {
                if (date < firstMonth || date >= firstMonth.AddMonths(3))
                    firstMonth = MonthOf(date);
                SelectRaceDate(date, row);
            });
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.flexShrink = 0;
            row.style.marginLeft = row.style.marginRight = 0;
            row.style.marginTop = row.style.marginBottom = 1;
            row.style.minHeight = 34;
            AddRaceColumns(row, label, track, race.laps.ToString(CultureInfo.InvariantCulture), TimeText(race.startTime));
            list.Add(row);
            if (race.date == DateText(selectedDate))
            {
                row.AddToClassList("settings-tab-current");
                selected = row;
            }
        }
        if (focusDay && selected != null)
        {
            list.ScrollTo(selected);
            selected.Focus();
        }
    }

    private static void AddRaceColumns(VisualElement row, string date, string track, string laps, string time)
    {
        AddColumn(date, 90);
        AddColumn(track, 0);
        AddColumn(laps, 80);
        AddColumn(time, 112);

        void AddColumn(string value, int width)
        {
            var label = SettingsFormStyle.Label(value, "settings-note");
            label.pickingMode = PickingMode.Ignore;
            label.style.marginTop = label.style.marginBottom = 0;
            if (width == 0)
            {
                label.style.flexGrow = 1;
                label.style.flexBasis = 0;
                label.style.minWidth = 0;
            }
            else
            {
                label.style.width = width;
                label.style.flexShrink = 0;
            }
            row.Add(label);
        }
    }

    private Button DrawMonth(VisualElement monthPanel, DateTime first)
    {
        var bar = Row(monthPanel);
        bar.style.height = 27;
        bar.style.backgroundColor = (Color)new Color32(37, 52, 72, 255);

        var heading = SettingsFormStyle.Label(first.ToString("MMMM yyyy", CultureInfo.InvariantCulture), "settings-note");
        heading.style.flexGrow = 1;
        heading.style.marginTop = heading.style.marginBottom = 0;
        heading.style.color = (Color)new Color32(245, 245, 245, 255);
        heading.style.unityTextAlign = TextAnchor.MiddleCenter;
        bar.Add(heading);

        var weekdays = Row(monthPanel);
        weekdays.style.height = 26;
        weekdays.style.flexShrink = 0;
        foreach (string weekday in CultureInfo.InvariantCulture.DateTimeFormat.AbbreviatedDayNames)
        {
            var header = SettingsFormStyle.Label(weekday.Substring(0, 2), "settings-note");
            SizeDay(header);
            header.style.unityTextAlign = TextAnchor.MiddleCenter;
            weekdays.Add(header);
        }

        int dayCount = DateTime.DaysInMonth(first.Year, first.Month);
        int offset = (int)first.DayOfWeek;
        Button selectedButton = null;
        for (int week = 0; week < 6; week++)
        {
            var row = Row(monthPanel);
            row.style.alignItems = Align.Stretch;
            row.style.flexGrow = 1;
            row.style.minHeight = 26;
            for (int weekday = 0; weekday < 7; weekday++)
            {
                int day = week * 7 + weekday - offset + 1;
                if (day < 1 || day > dayCount)
                {
                    var spillover = SettingsFormStyle.Label(first.AddDays(day - 1).Day.ToString(), "settings-note");
                    SizeDay(spillover);
                    spillover.style.color = (Color)new Color32(135, 151, 167, 255);
                    spillover.style.unityTextAlign = TextAnchor.MiddleCenter;
                    spillover.pickingMode = PickingMode.Ignore;
                    row.Add(spillover);
                    continue;
                }
                var date = first.AddDays(day - 1);
                var race = Races.FirstOrDefault(entry => entry.date == DateText(date));
                string trackName = race == null ? null : catalog.Find(race.trackId)?.Definition.name;
                Button cell = null;
                cell = CalendarButton(race == null ? day.ToString() : $"{day} <color=#009BDD><noparse>{TrackInitials(trackName)}</noparse></color>",
                    race == null ? $"Set up a race on {DateText(date)}." :
                        $"{DateText(date)}: {trackName ?? "Unavailable track"}, {race.laps} laps, {TimeText(race.startTime)}.",
                    () => SelectRaceDate(date, cell), pasteDates.Contains(DateText(date)) ||
                        (pasteDates.Count == 0 && date == selectedDate));
                SizeDay(cell);
                if (date.Month == 2 && date.Day == 29)
                {
                    cell.SetEnabled(false);
                    cell.tooltip = "Choose a date that occurs every season.";
                }
                else
                {
                    dates.Add(cell, date);
                    cell.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        if (evt.shiftKey || evt.ctrlKey)
                        {
                            if (evt.shiftKey)
                                SelectRange(date, extend: evt.ctrlKey);
                            else
                            {
                                string key = DateText(date);
                                if (!pasteDates.Add(key)) pasteDates.Remove(key);
                                selectionAnchor = date;
                            }
                            selectedDate = date;
                            evt.StopImmediatePropagation();
                            Render(focusDay: true);
                        }
                        else if (race != null)
                            StartDrag(evt, cell, race);
                    }, TrickleDown.TrickleDown);
                    cell.RegisterCallback<PointerUpEvent>(evt =>
                    {
                        if (evt.button != 1 || draggedRace != null) return;
                        evt.StopImmediatePropagation();
                        OpenContextMenu(cell, date, evt.position);
                    });
                }
                if (date == selectedDate) selectedButton = cell;
                row.Add(cell);
            }
        }

        return selectedButton;
    }

    private void SelectRange(DateTime date, bool extend)
    {
        if (!extend)
            pasteDates.Clear();
        var first = selectionAnchor < date ? selectionAnchor : date;
        var last = selectionAnchor > date ? selectionAnchor : date;
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (day.Month != 2 || day.Day != 29)
                pasteDates.Add(DateText(day));
        }
    }

    private void SelectRaceDate(DateTime date, Button cell)
    {
        pasteDates.Clear();
        selectionAnchor = selectedDate = date;
        rememberView(firstMonth, selectedDate);
        OpenRace(cell);
    }

    private void StartDrag(PointerDownEvent evt, Button source, SeriesCalendarRace race)
    {
        if (evt.button != 0 || draggedRace != null)
            return;
        dragSource = source;
        draggedRace = race;
        dragPointer = evt.pointerId;
        dragStart = evt.position;
        panel.CapturePointer(dragPointer);
        evt.StopImmediatePropagation();
    }

    private void UpdateDrag(PointerMoveEvent evt)
    {
        if (draggedRace == null || evt.pointerId != dragPointer)
            return;
        if (!isDragging && ((Vector2)evt.position - dragStart).sqrMagnitude < 25)
            return;
        isDragging = true;
        var target = DayAt(evt.position);
        bool available = target != null && target != dragSource;
        SetDropTarget(available ? target : null);
        if (available)
        {
            var date = dates[target];
            dragHint.text = RaceOn(date) == null ? $"Move race to {DateText(date)}." :
                $"Swap races on {draggedRace.date} and {DateText(date)}.";
        }
        else
            dragHint.text = "Drop the race on another date.";
        evt.StopPropagation();
    }

    private void FinishDrag(PointerUpEvent evt)
    {
        if (draggedRace == null || evt.pointerId != dragPointer || evt.button != 0)
            return;
        var race = draggedRace;
        var source = dragSource;
        var target = DayAt(evt.position);
        bool movedPointer = isDragging || ((Vector2)evt.position - dragStart).sqrMagnitude >= 25;
        ClearDrag();
        evt.StopImmediatePropagation();

        if (!movedPointer)
        {
            if (target == source)
                SelectRaceDate(dates[source], source);
            return;
        }
        if (target == null || target == source)
            return;
        selectedDate = dates[target];
        var other = RaceOn(selectedDate);
        if (other != null)
            other.date = race.date;
        race.date = DateText(selectedDate);
        series.calendarRaces = Races.OrderBy(entry => entry.date, StringComparer.Ordinal).ToArray();
        rememberView(firstMonth, selectedDate);
        changed();
    }

    private Button DayAt(Vector2 position)
    {
        for (var element = panel.panel?.Pick(position); element != null && element != panel; element = element.parent)
            if (element is Button button && dates.ContainsKey(button))
                return button;
        return null;
    }

    private SeriesCalendarRace RaceOn(DateTime day)
    {
        string key = DateText(day);
        return Races.FirstOrDefault(race => race.date == key);
    }

    private void SetDropTarget(Button target)
    {
        if (dropTarget == target) return;
        if (dropTarget != null)
            dropTarget.style.backgroundColor = targetBackground;
        dropTarget = target;
        if (dropTarget == null) return;
        targetBackground = dropTarget.style.backgroundColor;
        dropTarget.style.backgroundColor = (Color)new Color32(45, 74, 96, 255);
    }

    private void CancelDrag(int pointerId)
    {
        if (draggedRace != null && pointerId == dragPointer)
            ClearDrag();
    }

    private void ClearDrag()
    {
        SetDropTarget(null);
        draggedRace = null;
        dragSource = null;
        isDragging = false;
        dragHint.text = CalendarHint;
        if (panel.HasPointerCapture(dragPointer))
            panel.ReleasePointer(dragPointer);
    }

    private void OpenContextMenu(Button source, DateTime date, Vector2 position)
    {
        string key = DateText(date);
        var targets = pasteDates.Contains(key) ? new HashSet<string>(pasteDates, StringComparer.Ordinal) :
            new HashSet<string>(StringComparer.Ordinal) { key };
        var selectedRaces = Races.Where(entry => targets.Contains(entry.date))
            .OrderBy(entry => entry.date, StringComparer.Ordinal).ToArray();
        var (pastedRaces, pasteError) = PlanPaste(targets);
        var pasteKeys = pastedRaces.Select(entry => entry.date).ToHashSet(StringComparer.Ordinal);
        bool replaces = Races.Any(entry => pasteKeys.Contains(entry.date));
        var overlay = new VisualElement();
        overlay.style.position = Position.Absolute;
        overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0;
        var menu = new VisualElement();
        menu.style.position = Position.Absolute;
        menu.style.backgroundColor = (Color)new Color32(16, 21, 30, 255);
        const int width = 245;
        var localPosition = screen.WorldToLocal(position);
        menu.style.width = width;
        menu.style.left = Mathf.Clamp(localPosition.x, 0, Mathf.Max(0, screen.contentRect.width - width));
        menu.style.top = Mathf.Clamp(localPosition.y, 0, Mathf.Max(0, screen.contentRect.height - 136));
        overlay.Add(menu);

        string copyLabel = selectedRaces.Length <= 1 ? "Copy Race" : $"Copy {selectedRaces.Length} Races";
        var copy = Button(copyLabel, "Copy the selected races, including their tracks, laps, and start times.", () =>
        {
            clipboard.Races = selectedRaces.Select(race => CopyRace(race, race.date)).ToArray();
            Close();
            dragHint.text = clipboard.Races.Length == 1
                ? "Race copied. Right-click a date or selected dates to paste."
                : $"Copied {clipboard.Races.Length} races. Paste onto one start date or select one destination per race.";
        });
        copy.SetEnabled(selectedRaces.Length > 0);
        menu.Add(copy);
        string pasteLabel = pastedRaces.Length <= 1 ? "Paste Race" : $"Paste {pastedRaces.Length} Races";
        if (replaces) pasteLabel += " (Replace)";
        var paste = Button(pasteLabel, pasteError ?? "Paste the copied races to the indicated dates.", () =>
        {
            series.calendarRaces = Races.Where(entry => !pasteKeys.Contains(entry.date))
                .Concat(pastedRaces)
                .OrderBy(entry => entry.date, StringComparer.Ordinal).ToArray();
            selectedDate = date;
            rememberView(firstMonth, selectedDate);
            Close();
            changed();
        });
        paste.SetEnabled(pasteError == null);
        menu.Add(paste);
        string removeLabel = selectedRaces.Length <= 1 ? "Remove Race" : $"Remove {selectedRaces.Length} Races";
        var remove = Button(removeLabel, "Remove all races on the selected dates.", () =>
        {
            series.calendarRaces = Races.Where(entry => !targets.Contains(entry.date)).ToArray();
            selectedDate = date;
            rememberView(firstMonth, selectedDate);
            Close();
            changed();
        });
        remove.SetEnabled(selectedRaces.Length > 0);
        menu.Add(remove);

        overlay.focusable = true;
        overlay.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.target != overlay) return;
            Close();
            evt.StopPropagation();
        });
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            Close();
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
        frame.SetEnabled(false);
        screen.Add(overlay);
        (copy.enabledSelf ? (VisualElement)copy : paste.enabledSelf ? paste : overlay).Focus();

        void Close()
        {
            overlay.RemoveFromHierarchy();
            frame.SetEnabled(true);
            source.Focus();
        }
    }

    private (SeriesCalendarRace[] Races, string Error) PlanPaste(HashSet<string> targets)
    {
        var copied = clipboard.Races;
        var empty = Array.Empty<SeriesCalendarRace>();
        if (copied.Length == 0)
            return (empty, "Copy a race before pasting.");
        if (copied.Any(race =>
        {
            var track = catalog.Find(race.trackId)?.Definition;
            return track == null || !Eligible(track);
        }))
            return (empty, "The copied races must use installed tracks within the series track length limit.");

        var orderedTargets = targets.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        if (copied.Length == 1)
            return (orderedTargets.Select(key => CopyRace(copied[0], key)).ToArray(), null);
        if (orderedTargets.Length == copied.Length)
            return (copied.Zip(orderedTargets, CopyRace).ToArray(), null);
        if (orderedTargets.Length != 1)
            return (empty, $"Select one start date or {copied.Length} destination dates.");

        const int recurringYear = 2001;
        if (!SeasonSchedule.TryScheduleDate(orderedTargets[0], recurringYear, out var anchor) ||
            !SeasonSchedule.TryScheduleDate(copied[0].date, recurringYear, out var first))
            return (empty, "Choose a valid calendar date.");
        var result = new SeriesCalendarRace[copied.Length];
        for (int i = 0; i < copied.Length; i++)
        {
            if (!SeasonSchedule.TryScheduleDate(copied[i].date, recurringYear, out var source))
                return (empty, "A copied race has an invalid date.");
            var destination = anchor.AddDays((source - first).Days);
            if (destination.Year != recurringYear)
                return (empty, "Choose an earlier start date so the copied races fit before the end of the year.");
            result[i] = CopyRace(copied[i], DateText(destination));
        }
        return (result, null);
    }

    private static SeriesCalendarRace CopyRace(SeriesCalendarRace race, string date) => new()
    {
        date = date,
        trackId = race.trackId,
        laps = race.laps,
        startTime = race.startTime
    };

    private static string TrackInitials(string trackName)
    {
        if (string.IsNullOrWhiteSpace(trackName)) return "?";
        var words = trackName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
            return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
        return string.Concat(words[0][0], words[1][0]).ToUpperInvariant();
    }

    private static Button NavigationButton(string icon, string tooltip, Action action)
    {
        var button = Button(icon, tooltip, action);
        button.style.width = 36;
        button.style.paddingLeft = button.style.paddingRight = 0;
        button.style.fontSize = 16;
        return button;
    }

    private void JumpToDate(DateTime date, bool atEnd = false)
    {
        firstMonth = atEnd ? new DateTime(date.Year, Math.Max(1, date.Month - 2), 1) : MonthOf(date);
        pasteDates.Clear();
        selectionAnchor = selectedDate = date;
        Render(focusDay: true);
    }

    private void ShiftMonth(int direction)
    {
        firstMonth = firstMonth.AddMonths(direction);
        if (selectedDate < firstMonth || selectedDate >= firstMonth.AddMonths(3))
            selectedDate = firstMonth;
        Render(navigationDirection: direction);
    }

    private void OpenRace(Button source)
    {
        var overlay = new VisualElement();
        var raceDialog = new VisualElement();
        SetupDialog(overlay, raceDialog, 465, Cancel);
        raceDialog.Add(SettingsFormStyle.Label($"Race on {DateText(selectedDate)}", "settings-group-heading"));
        var existing = Races.FirstOrDefault(entry => entry.date == DateText(selectedDate));
        var tracks = catalog.Entries.Where(entry => Eligible(entry.Definition)).ToArray();
        var oldTrack = existing == null ? null : catalog.Find(existing.trackId);
        var trackNames = tracks.Select(entry => entry.ToString()).ToList();
        var trackField = SettingsFormStyle.StyleField(new DropdownField("Track", trackNames, -1));
        trackField.tooltip = "Choose an installed track within the series track length limit.";
        trackField.SetValueWithoutNotify(oldTrack != null && tracks.Contains(oldTrack) ? oldTrack.ToString() : null);
        trackField.SetEnabled(tracks.Length > 0);
        raceDialog.Add(trackField);
        if (tracks.Length == 0)
            raceDialog.Add(SettingsFormStyle.Label("No installed tracks meet the series track length limit.", "settings-note"));
        var fields = Row(raceDialog);
        var lapChoices = LapOptions.Select(count => count.ToString(CultureInfo.InvariantCulture)).ToList();
        var laps = SettingsFormStyle.StyleField(new DropdownField("Laps", lapChoices, -1));
        laps.tooltip = "Choose the number of laps for this race.";
        laps.style.minWidth = 0;
        if (existing != null)
            laps.SetValueWithoutNotify(existing.laps.ToString(CultureInfo.InvariantCulture));
        fields.Add(laps);
        var startTimes = Enumerable.Range(0, 19).Select(index => TimeSpan.FromHours(12).Add(TimeSpan.FromMinutes(index * 30))).ToArray();
        var timeChoices = startTimes.Select(value => DateTime.Today.Add(value).ToString("h:mm tt", CultureInfo.InvariantCulture)).ToList();
        var time = SettingsFormStyle.StyleField(new DropdownField("Start time", timeChoices, -1));
        time.tooltip = "Choose a start time from noon to 9:00 PM.";
        time.style.minWidth = 0;
        time.SetValueWithoutNotify(TimeText(existing?.startTime ?? "15:00"));
        fields.Add(time);
        trackField.RegisterValueChangedCallback(_ =>
        {
            int index = trackField.index;
            if (index >= 0 && string.IsNullOrWhiteSpace(laps.value))
            {
                int defaultLaps = tracks[index].Definition.raceLaps;
                laps.value = LapOptions.OrderBy(count => Math.Abs(count - defaultLaps))
                    .First().ToString(CultureInfo.InvariantCulture);
            }
        });

        var error = SettingsFormStyle.Label("", "settings-validation-summary");
        error.style.minHeight = 34;
        error.style.whiteSpace = WhiteSpace.Normal;
        raceDialog.Add(error);
        var actions = Row(raceDialog);
        actions.AddToClassList("settings-confirmation-actions");
        actions.style.borderTopWidth = 1;
        actions.style.borderTopColor = (Color)new Color32(0, 155, 221, 255);
        actions.style.paddingTop = 6;
        actions.Add(Button("Cancel", "Discard changes to this race.", Cancel));

        if (existing != null)
        {
            actions.Add(Button("Remove Race", "Remove the race from this date.", () =>
            {
                series.calendarRaces = Races.Where(entry => entry != existing).ToArray();
                Close();
                changed();
            }));
        }
        actions.Add(Button("Apply Race", "Add this race or apply changes to it.", Apply));
        frame.SetEnabled(false);
        screen.Add(overlay);
        (trackField.enabledSelf ? (VisualElement)trackField : laps).Focus();

        void Apply()
        {
            string key = DateText(selectedDate);
            int trackIndex = trackField.index;
            var track = trackIndex >= 0 ? tracks[trackIndex].Definition : null;
            string message = track == null
                ? "Choose an installed track within the series track length limit." : null;
            if (!int.TryParse(laps.value, NumberStyles.None, CultureInfo.InvariantCulture, out int lapCount) || lapCount <= 0)
                message ??= "Choose a lap count.";
            int timeIndex = time.index;
            string storedTime = timeIndex >= 0
                ? startTimes[timeIndex].ToString(@"hh\:mm", CultureInfo.InvariantCulture)
                : existing?.startTime;
            if (SeriesSettingsStore.CalendarTimeError(storedTime) != null)
                message ??= "Enter a start time from 12:00 PM to 9:00 PM.";
            SettingsFormStyle.SetValidationMessage(error, message, message != null);
            if (message != null) return;
            series.calendarRaces = Races.Where(entry => entry != existing)
                .Append(new SeriesCalendarRace { date = key, trackId = track.id, laps = lapCount, startTime = storedTime })
                .OrderBy(entry => entry.date, StringComparer.Ordinal).ToArray();

            Close();
            changed();
        }

        void Close()
        {
            overlay.RemoveFromHierarchy();
            frame.SetEnabled(true);
        }

        void Cancel()
        {
            Close();
            if (source.parent != null)
                Render(focusDay: true);
        }
    }

    private bool Eligible(TrackDefinition track) =>
        series.maximumTrackLengthMiles <= 0 || track.lengthMiles <= series.maximumTrackLengthMiles;

    private static void SetupDialog(VisualElement overlay, VisualElement dialog, int width, Action close)
    {
        overlay.AddToClassList("settings-confirmation-overlay");
        dialog.AddToClassList("settings-confirmation-dialog");
        dialog.AddToClassList("settings-msaa-dialog");
        dialog.style.width = width;
        overlay.Add(dialog);
        overlay.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode != KeyCode.Escape) return;
            close();
            evt.StopPropagation();
        }, TrickleDown.TrickleDown);
    }

    private static void SizeDay(VisualElement day)
    {
        day.style.flexGrow = 1;
        day.style.flexBasis = 0;
        day.style.minWidth = 0;
        day.style.minHeight = 24;
        day.style.fontSize = 13;
        day.style.marginLeft = day.style.marginRight = 1;
        day.style.marginTop = day.style.marginBottom = 0;
        day.style.paddingLeft = day.style.paddingRight = 0;
        day.style.paddingTop = day.style.paddingBottom = 0;
    }

    private static Button CalendarButton(string label, string tooltip, Action action, bool selected = false)
    {
        var button = new Button(action) { text = label, tooltip = tooltip, enableRichText = true };
        button.style.color = (Color)new Color32(245, 245, 245, 255);
        button.style.borderLeftWidth = button.style.borderRightWidth = 0;
        button.style.borderTopWidth = button.style.borderBottomWidth = 0;
        button.style.marginLeft = button.style.marginRight = 0;
        button.style.marginTop = button.style.marginBottom = 0;
        var idle = selected ? (Color)new Color32(45, 74, 96, 255) : Color.clear;
        var highlight = (Color)new Color32(58, 94, 120, 255);
        button.style.backgroundColor = idle;
        bool hovered = false, focused = false;
        button.RegisterCallback<PointerEnterEvent>(_ => { hovered = true; Refresh(); });
        button.RegisterCallback<PointerLeaveEvent>(_ => { hovered = false; Refresh(); });
        button.RegisterCallback<FocusInEvent>(_ => { focused = true; Refresh(); });
        button.RegisterCallback<FocusOutEvent>(_ => { focused = false; Refresh(); });
        return button;

        void Refresh() => button.style.backgroundColor = hovered || focused ? highlight : idle;
    }

    private static VisualElement Row(VisualElement parent)
    {
        var row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        parent.Add(row);
        return row;
    }

    private static Button Button(string label, string tooltip, Action action)
    {
        var button = SettingsFormStyle.Button(label, action);
        button.tooltip = tooltip;
        return button;
    }
}
