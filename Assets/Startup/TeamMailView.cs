using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class TeamMailView
{
    private TeamMailMessage selected;

    public void Show(VisualElement page, Career career, Action repaint, Func<IEnumerable<string>, string> deletePosts,
        Func<DriverContact, Texture2D> driverPortrait)
    {
        page.Clear();
        var email = Column(page, "mail-email");
        AddLabel(email, "Mail", "mail-title");
        var layout = Column(email, "mail-email-layout");
        var inbox = Column(layout, "mail-inbox");
        AddLabel(inbox, "Inbox", "mail-subheading");
        var rows = AddScroll(inbox);
        var reader = Column(layout, "mail-reader");
        var message = AddScroll(reader);

        var messages = career.inbox?.OrderByDescending(item => item.date, StringComparer.Ordinal).ToArray()
            ?? Array.Empty<TeamMailMessage>();
        if (!messages.Contains(selected))
            selected = messages.FirstOrDefault();

        foreach (var item in messages)
        {
            var button = new Button(() =>
            {
                selected = item;
                foreach (var row in rows.Children())
                    row.EnableInClassList("nav-button-selected", ReferenceEquals(row.userData, selected));
                Read();
                repaint();
            }) { userData = item };
            button.AddToClassList("nav-button");
            button.AddToClassList("mail-message-row");
            button.EnableInClassList("nav-button-selected", ReferenceEquals(item, selected));
            AddLabel(button, item.subject, "mail-subject", PickingMode.Ignore);
            AddLabel(button, item.sender, "mail-sender", PickingMode.Ignore);
            rows.Add(button);
        }
        Read();

        var social = Column(page, "mail-social");
        var feedHeader = Column(social, "mail-post-header");
        AddLabel(feedHeader, "Social feed", "mail-title");
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

        void Read()
        {
            message.Clear();
            message.scrollOffset = Vector2.zero;
            if (selected == null)
            {
                AddLabel(message, "Your inbox is empty.", "mail-copy");
                return;
            }
            var logo = BroadcastCoverage.Logo(selected.mediaOutletId);
            if (logo != null)
                message.Add(logo);
            AddLabel(message, selected.subject, "mail-message-title");
            AddLabel(message, $"From {selected.sender} · {DateText(selected.date)}", "mail-meta");
            AddLabel(message, selected.body, "mail-copy");
        }
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
        button.AddToClassList("nav-button");
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
        var scroll = new ScrollView();
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
