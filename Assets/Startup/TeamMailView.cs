using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class TeamMailView
{
    private TeamMailMessage selected;

    public void Show(VisualElement page, Career career, Action repaint, Func<IEnumerable<string>, string> deletePosts,
        Func<TeamMailMessage, string> markMailRead, Func<TeamMailMessage[], string> deleteMail,
        Func<TeamMailMessage, string, string> respond,
        Func<DriverContact, Texture2D> driverPortrait, TeamMailMessage openMessage = null)
    {
        page.Clear();
        page.AddToClassList("google-sans");
        var email = Column(page, "mail-email");
        email.AddToClassList("team-home-panel");
        SettingsFormStyle.StylePanel(email);
        AddLabel(email, "Mail", "team-home-heading");
        var layout = Column(email, "mail-email-layout");
        var inbox = Column(layout, "mail-inbox");
        var inboxHeader = Column(inbox, "mail-inbox-header");
        AddLabel(inboxHeader, "Inbox", "mail-subheading");
        var scenarioAlert = new Label("!") { tooltip = "Read required emails and respond to pending scenarios before advancing the date." };
        scenarioAlert.AddToClassList("mail-scenario-alert");
        inboxHeader.Add(scenarioAlert);
        var clearInbox = TrashButton("Delete all emails");
        inboxHeader.Add(clearInbox);
        var mailStatus = new Label { style = { display = DisplayStyle.None } };
        mailStatus.AddToClassList("mail-copy");
        inbox.Add(mailStatus);
        var rows = AddScroll(inbox);
        var reader = Column(layout, "mail-reader");
        var message = AddScroll(reader);

        clearInbox.clicked += () => DeleteMail(career.inbox?.Where(item => !item.BlocksAdvancement && (!item.IsScenario || item.NeedsResponse)).ToArray()
            ?? Array.Empty<TeamMailMessage>());
        if (openMessage != null && career.inbox?.Contains(openMessage) == true)
        {
            selected = openMessage;
            ShowMailStatus(markMailRead(openMessage));
        }
        DrawInbox();
        Read();

        var social = Column(page, "mail-social");
        social.AddToClassList("team-home-panel");
        SettingsFormStyle.StylePanel(social);
        AddLabel(social, "Social feed", "team-home-heading");
        var feedHeader = Column(social, "mail-post-header");
        feedHeader.AddToClassList("mail-feed-actions");
        var status = new Label { style = { display = DisplayStyle.None } };
        status.AddToClassList("mail-copy");
        social.Add(status);
        var posts = AddScroll(social);
        var feed = TeamCommunications.RaceFeed(career).ToArray();
        int visiblePosts = feed.Length;
        var clearFeed = TrashButton("Delete all posts");
        clearFeed.clicked += () =>
        {
            if (!Delete(feed.Select(post => post.Id)))
                return;
            posts.Clear();
            visiblePosts = 0;
            ShowEmptyFeed();
        };
        feedHeader.Add(clearFeed);
        clearFeed.SetEnabled(visiblePosts > 0);
        if (feed.Length == 0)
            AddLabel(posts, career.deletedSocialPostIds?.Count > 0 ? "No posts to show."
                : "Race reactions will appear here after your team finishes a race.", "mail-copy");
        foreach (var post in feed)
        {
            var card = Column(posts, "mail-post");
            Color sourceColor = post.Source switch
            {
                "Apex Wire" => new Color32(74, 164, 220, 255),
                "Pit Wall Journal" => new Color32(224, 164, 75, 255),
                "Garage Signal" => new Color32(73, 180, 143, 255),
                "Grandstand Chatter" => new Color32(183, 133, 217, 255),
                _ => new Color32(117, 139, 155, 255)
            };
            card.style.borderLeftColor = sourceColor;
            var header = Column(card, "mail-post-header");
            header.Add(CreateAvatar(post.UserName, driverPortrait(post.DriverContact)));
            var author = Column(header, "mail-post-author");
            AddLabel(author, post.Source, "mail-subheading");
            AddLabel(author, post.UserName, "mail-sender");
            header.Add(TrashButton("Delete post", () =>
            {
                if (!Delete(new[] { post.Id }))
                    return;
                card.RemoveFromHierarchy();
                if (--visiblePosts == 0)
                    ShowEmptyFeed();
            }));
            AddLabel(card, DateText(post.Date), "mail-meta");
            AddLabel(card, post.Text, "mail-copy");
        }

        bool Delete(IEnumerable<string> postIds)
        {
            string error = deletePosts(postIds);
            status.text = error ?? string.Empty;
            status.style.display = error == null ? DisplayStyle.None : DisplayStyle.Flex;
            return error == null;
        }

        void ShowEmptyFeed()
        {
            clearFeed.SetEnabled(false);
            AddLabel(posts, "No posts to show.", "mail-copy");
        }

        void DrawInbox()
        {
            rows.Clear();
            var messages = career.inbox?.Where(item => !item.IsScenario || item.NeedsResponse).Reverse().ToArray()
                ?? Array.Empty<TeamMailMessage>();
            bool hasPending = messages.Any(item => item.BlocksAdvancement);
            scenarioAlert.style.display = hasPending ? DisplayStyle.Flex : DisplayStyle.None;
            clearInbox.tooltip = hasPending ? "Delete emails that do not require reading or a response" : "Delete all emails";
            clearInbox.SetEnabled(messages.Any(item => !item.BlocksAdvancement));
            if (!messages.Contains(selected))
                selected = null;

            foreach (var item in messages)
            {
                var row = Column(rows, "mail-inbox-row");
                var button = new Button(() =>
                {
                    selected = item;
                    ShowMailStatus(markMailRead(item));
                    foreach (var entry in rows.Children())
                    {
                        var open = entry.Q<Button>(className: "mail-message-row");
                        var mail = (TeamMailMessage)open.userData;
                        open.EnableInClassList("settings-tab-current", ReferenceEquals(mail, selected));
                        open.EnableInClassList("mail-unread", !mail.isRead);
                    }
                    Read();
                    scenarioAlert.style.display = career.inbox.Any(mail => mail.BlocksAdvancement) ? DisplayStyle.Flex : DisplayStyle.None;
                    repaint();
                }) { userData = item };
                button.AddToClassList("settings-button");
                button.AddToClassList("mail-message-row");
                button.tooltip = "Open email: " + item.subject;
                button.EnableInClassList("settings-tab-current", ReferenceEquals(item, selected));
                button.EnableInClassList("mail-unread", !item.isRead);
                button.EnableInClassList("mail-scenario", item.IsScenario || item.requiresRead);
                AddLabel(button, item.subject, "mail-subject", PickingMode.Ignore);
                AddLabel(button, item.sender, "mail-sender", PickingMode.Ignore);
                row.Add(button);
                if (!item.BlocksAdvancement)
                {
                    var trash = TrashButton("Delete email", () => DeleteMail(new[] { item }));
                    trash.AddToClassList("mail-inbox-delete");
                    row.Add(trash);
                }
            }
        }

        void DeleteMail(TeamMailMessage[] items)
        {
            string error = deleteMail(items);
            ShowMailStatus(error);
            if (error != null)
                return;
            DrawInbox();
            Read();
            repaint();
        }

        void ShowMailStatus(string error)
        {
            mailStatus.text = error ?? string.Empty;
            mailStatus.style.display = error == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void Read()
        {
            message.Clear();
            message.scrollOffset = Vector2.zero;
            if (selected == null)
            {
                AddLabel(message, rows.childCount > 0 ? "Select an email to read it." : "Your inbox is empty.", "mail-copy");
                return;
            }
            var logo = BroadcastCoverage.Logo(selected.mediaOutletId);
            if (logo != null)
                message.Add(logo);
            AddLabel(message, selected.subject, "mail-message-title");
            AddLabel(message, $"From {selected.sender} · {DateText(selected.date)}", "mail-meta");
            if (RaceReportMail.TryRead(selected, career, out var report))
                AddRaceReport(message, report);
            else if (RacePrepMail.TryRead(selected, out var prep))
                AddRacePrep(message, prep);
            else
                AddLabel(message, selected.body, "mail-copy");
            if (selected.NeedsResponse)
            {
                var scenario = selected;
                var responseActions = new VisualElement();
                responseActions.AddToClassList("mail-response-actions");
                message.Add(responseActions);
                foreach (string option in scenario.scenarioOptions)
                {
                    var choice = new Button(() =>
                    {
                        string error = respond(scenario, option);
                        ShowMailStatus(error);
                        if (error != null)
                            return;
                        DrawInbox();
                        Read();
                        repaint();
                    }) { text = option };
                    choice.AddToClassList("settings-button");
                    choice.AddToClassList("mail-scenario-choice");
                    choice.tooltip = "Respond to this scenario: " + option;
                    responseActions.Add(choice);
                }
            }
        }
    }

    private static void AddRaceReport(VisualElement parent, RaceReportMail report)
    {
        var content = Column(parent, "mail-race-report");
        content.AddToClassList("ui-default-font");
        AddReportText(content, report.Introduction, "mail-race-intro");

        var results = AddReportSection(content, "Session results");
        var grid = Column(results, "mail-race-results");
        grid.AddToClassList("results-grid");
        AddReportSession(grid, "Session", "Pos", "Laps", "Best lap", null, header: true);
        foreach (var session in report.Sessions)
            AddReportSession(grid, session.Name, session.Position, session.Laps, session.BestLap, session.Status);

        var condition = AddReportSection(content, "Car condition and service");
        AddReportText(condition, report.ConditionNote, "mail-race-note");
        foreach (var chassis in report.Chassis)
        {
            var group = Column(condition, "mail-race-chassis");
            AddReportText(group, chassis.Heading, "mail-race-chassis-heading");
            AddReportText(group, chassis.Summary, "mail-race-note");

            AddReportComponents(group, chassis.Components);
            foreach (string note in chassis.Notes)
                AddReportText(group, note, "mail-race-note");
        }
    }

    private static void AddRacePrep(VisualElement parent, RacePrepMail prep)
    {
        var content = Column(parent, "mail-race-report");
        content.AddToClassList("mail-race-prep");
        content.AddToClassList("ui-default-font");
        AddReportText(content, prep.Introduction, "mail-race-intro");

        var track = AddReportSection(content, "Track brief");
        foreach (string note in prep.TrackNotes)
            AddReportText(track, note, "mail-race-note");
        if (prep.Weights != null)
        {
            var chart = Column(track, "track-ratings");
            chart.AddToClassList("mail-race-track");
            StartupMenu.DisplayTrackRatings(chart, prep.Weights);
            foreach (var (column, title) in chart.Children().Zip(TrackRatings.DisplayAttributes(prep.Weights),
                (column, rating) => (column, rating.Title)))
                AddLabel(column, title, "mail-race-track-caption");
        }

        var chassis = AddReportSection(content, "Chassis recommendation");
        AddReportText(chassis, prep.Recommendation, "mail-race-chassis-heading");
        AddReportComponents(chassis, prep.Components);
        foreach (string note in prep.ChassisNotes)
            AddReportText(chassis, note, "mail-race-note");

        AddReportText(content, prep.Closing, "mail-race-intro");
    }

    private static void AddReportComponents(VisualElement parent, IReadOnlyList<RaceReportMail.ComponentRow> components)
    {
        if (components.Count == 0)
            return;
        var cards = Column(parent, "mail-race-components");
        foreach (var component in components)
        {
            var slot = Column(cards, "mail-race-component-slot");
            var card = Column(slot, "mail-race-component");
            card.EnableInClassList("mail-race-component-needs-repair", component.RequiresRepair);
            var heading = Column(card, "mail-race-component-header");
            var iconSlot = Column(heading, "home-component-icon-slot");
            iconSlot.Add(ShopComponentPanel.ComponentIcon(component.Component));
            AddLabel(heading, CarComponentDisplay.Name(component.Component), "mail-race-component-name");
            var wear = Column(card, "mail-race-wear");
            TeamCarCard.AddComponentWear(wear, component.Wear, component.WearText);
            if (!string.IsNullOrWhiteSpace(component.AddedWearText))
                AddLabel(card, component.AddedWearText + " this event", "mail-race-wear-change");
            if (component.RequiresRepair)
                AddLabel(card, "Repair required", "mail-race-repair-label");
        }
    }

    private static VisualElement AddReportSection(VisualElement parent, string title)
    {
        var section = Column(parent, "mail-race-section");
        section.AddToClassList("team-home-panel");
        SettingsFormStyle.StylePanel(section);
        AddLabel(section, title, "team-home-heading");
        return section;
    }

    private static void AddReportSession(VisualElement grid, string session, string position, string laps,
        string bestLap, string status, bool header = false)
    {
        var row = Column(grid, "mail-race-session-row");
        row.AddToClassList("results-grid-row");
        row.EnableInClassList("results-grid-header", header);
        var nameCell = Column(row, "mail-race-session-cell");
        nameCell.AddToClassList("results-grid-cell");
        AddLabel(nameCell, session, "mail-race-session-name");
        AddReportText(nameCell, status, "mail-race-session-status");
        Stat(position, "mail-race-pos");
        Stat(laps, "mail-race-laps");
        Stat(bestLap, "mail-race-best-lap");

        void Stat(string text, string column)
        {
            var cell = new Label(string.IsNullOrWhiteSpace(text) ? "\u2014" : text);
            cell.AddToClassList("results-grid-cell");
            cell.AddToClassList("mail-race-stat");
            cell.AddToClassList(column);
            row.Add(cell);
        }
    }

    private static void AddReportText(VisualElement parent, string text, string styleClass)
    {
        if (!string.IsNullOrWhiteSpace(text))
            AddLabel(parent, text, styleClass);
    }

    private static VisualElement CreateAvatar(string name, Texture2D portrait)
    {
        var avatar = new VisualElement { pickingMode = PickingMode.Ignore };
        avatar.AddToClassList("mail-post-avatar");
        if (portrait != null)
        {
            avatar.style.backgroundImage = new StyleBackground(portrait);
            return avatar;
        }

        uint hash = 2166136261;
        foreach (char character in name)
            hash = unchecked((hash ^ character) * 16777619);
        avatar.style.backgroundColor = Color.HSVToRGB((hash % 360) / 360f, 0.63f, 0.43f);
        string initials = new string(name.Where(char.IsUpper).Take(2).ToArray());
        if (initials.Length == 0)
            initials = name.TrimStart('@').Substring(0, Math.Min(2, name.TrimStart('@').Length)).ToUpperInvariant();
        AddLabel(avatar, initials, "mail-avatar-initials", PickingMode.Ignore);
        return avatar;
    }

    private static Button TrashButton(string tooltip, Action action = null)
    {
        var button = new Button(action) { tooltip = tooltip };
        button.AddToClassList("settings-button");
        button.AddToClassList("mail-post-delete");
        var icon = new VisualElement { pickingMode = PickingMode.Ignore };
        icon.AddToClassList("mail-trash-icon");
        icon.generateVisualContent += DrawTrashCan;
        button.Add(icon);
        return button;
    }

    private static void DrawTrashCan(MeshGenerationContext context)
    {
        var painter = context.painter2D;
        painter.strokeColor = context.visualElement.resolvedStyle.color;
        painter.lineWidth = 1.4f;
        painter.BeginPath();
        painter.MoveTo(new Vector2(2, 4.5f));
        painter.LineTo(new Vector2(14, 4.5f));
        painter.MoveTo(new Vector2(6, 4.5f));
        painter.LineTo(new Vector2(6, 2));
        painter.LineTo(new Vector2(10, 2));
        painter.LineTo(new Vector2(10, 4.5f));
        painter.MoveTo(new Vector2(3.5f, 4.5f));
        painter.LineTo(new Vector2(4.5f, 14));
        painter.LineTo(new Vector2(11.5f, 14));
        painter.LineTo(new Vector2(12.5f, 4.5f));
        painter.Stroke();
    }

    private static string DateText(string date) => Career.TryReadDate(date, out var parsed) ? parsed.ToString("MMM d, yyyy") : date;

    private static ScrollView AddScroll(VisualElement parent)
    {
        var scroll = new ScrollView(ScrollViewMode.Vertical)
        {
            verticalScrollerVisibility = ScrollerVisibility.Hidden,
            horizontalScrollerVisibility = ScrollerVisibility.Hidden
        };
        scroll.AddToClassList("mail-scroll");
        parent.Add(scroll);
        return scroll;
    }

    private static VisualElement Column(VisualElement parent, string styleClass)
    {
        var element = new VisualElement();
        element.AddToClassList(styleClass);
        parent.Add(element);
        return element;
    }

    private static void AddLabel(VisualElement parent, string text, string styleClass, PickingMode picking = PickingMode.Position)
    {
        var label = new Label(text) { pickingMode = picking };
        label.AddToClassList(styleClass);
        parent.Add(label);
    }
}
