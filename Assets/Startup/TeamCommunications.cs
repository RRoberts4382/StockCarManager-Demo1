using System;
using System.Collections.Generic;
using System.Linq;

internal enum TeamMailOffer { None, MascotSearch, PartsPallet, Infomercial, ChiliCookOff, Merchandising, PublicAppearance, PodcastAppearance, StaffArrest }

[Serializable]
internal sealed class TeamMailMessage
{
    public string eventId;
    public string sender;
    public string mediaOutletId;
    public string date;
    public string subject;
    public string body;
    public bool isRead;
    public bool requiresRead;
    public int seriesChangeYear;
    public string[] scenarioOptions;
    public string scenarioResponse;
    public SponsorOffer[] scenarioSponsorOffers;
    public int scenarioPurchaseCost;
    public TeamMailOffer scenarioOffer;
    public string scenarioGiftRecipient;
    public string scenarioInvitedDriver;

    public bool IsScenario => scenarioOptions?.Length > 0;
    public bool BlocksAdvancement => (NeedsResponse && scenarioPurchaseCost <= 0 && scenarioOffer == TeamMailOffer.None) || (requiresRead && !isRead);
    public bool NeedsResponse => IsScenario && string.IsNullOrEmpty(scenarioResponse);
}

internal enum SocialSentiment { Positive, Neutral, Negative }

internal sealed class TeamSocialPost
{
    public string Id { get; }
    public string UserName { get; }
    public string Source { get; }
    public string Date { get; }
    public string Track { get; }
    public int Position { get; }
    public int FieldSize { get; }
    public SocialSentiment Sentiment { get; }
    public string Text { get; }
    public DriverContact DriverContact { get; }

    public TeamSocialPost(string id, string source, string userName, string date, string track, int position, int fieldSize,
        SocialSentiment sentiment, string text, DriverContact driverContact = null)
    {
        Id = id;
        UserName = userName;
        Source = source;
        Date = date;
        Track = track;
        Position = position;
        FieldSize = fieldSize;
        Sentiment = sentiment;
        Text = text;
        DriverContact = driverContact;
    }
}

internal static class TeamCommunications
{
    private const int MaximumPosts = 30;
    private static readonly string[] Sources = { "Apex Wire", "Pit Wall Journal", "Garage Signal", "Grandstand Chatter" };
    private static readonly string[] HandleStarts = { "LateBrake", "Backstretch", "PitLane", "TurnFour", "GridSide", "HighLine" };
    private static readonly string[] HandleEnds = { "Scout", "Racer", "Fan", "Watcher", "Reporter", "Nomad" };
    private static readonly string[] PositiveEmoji = { "🏁", "👏", "🎉" };
    private static readonly string[] NeutralEmoji = { "🏎", "⏱", "📝" };
    private static readonly string[] NegativeEmoji = { "😬", "😕", "🔧" };
    private static readonly string[] LightContact =
    {
        "I felt that tap at {1}, {0}. Next restart, I'm taking your lane before you can blink.",
        "You leaned on me at {1}, {0}. Next time we're door to door, I'm keeping the groove.",
        "Remember that little nudge at {1}, {0}? I'll give you the same welcome next time around."
    };
    private static readonly string[] ModerateContact =
    {
        "{0}, that hit at {1} cost me positions. Next time I get to your bumper, you're getting moved up the track.",
        "You tore up my fender at {1}, {0}. Next time we go into a corner together, you're taking the long way around.",
        "Enjoy the spot you took at {1}, {0}. Next race I'm using my bumper to take it back."
    };
    private static readonly string[] HeavyContact =
    {
        "You wrecked my car at {1}, {0}. Next time I'm on your bumper in a turn, I'm sending you up the track and taking the spot.",
        "{0}, my crew is still pulling bent metal off the car after {1}. When I catch you in a race, I'm moving you out of the damn groove.",
        "I had a car worth racing until you piled into it at {1}, {0}. Next green flag we share, expect the bumper and a trip to the high lane."
    };
    private static readonly string[] Positive =
    {
        "{0} had something to celebrate at {1}. P{2} is a strong result.",
        "The crew at {0} earned this one. P{2} at {1}.",
        "Keep an eye on {0}. That P{2} finish at {1} caught our attention.",
        "Big cheers for {0} after a P{2} run at {1}."
    };
    private static readonly string[] Neutral =
    {
        "{0} came home P{2} at {1}. On to the next round.",
        "The final timing sheet puts {0} P{2} at {1}.",
        "A middle-of-the-pack finish for {0} at {1}. They end the day P{2}.",
        "{0} leaves {1} with P{2}. Let's see what next week brings."
    };
    private static readonly string[] Negative =
    {
        "A rough day at {1} for {0}. They finished P{2}.",
        "P{2} at {1} leaves {0} with work to do before the next race.",
        "{0} was well down the order at {1}. P{2} is a tough result to take.",
        "Hard to cheer for P{2} at {1}. Hoping {0} bounces back next time."
    };

    public static bool AddDriverWelcome(Career career)
    {
        const string eventId = "driver-welcome";
        if (career.Driver == null || HasReceivedMail(career, eventId))
            return false;

        string sender = career.Driver.FullName;
        career.inbox ??= new List<TeamMailMessage>();
        career.inbox.Add(new TeamMailMessage
        {
            eventId = eventId,
            sender = sender,
            date = career.currentDate,
            subject = "Glad to be on board",
            body = $"Hi team,\n\nI'm thrilled to join {career.teamName}. I've been looking forward to meeting the crew and getting my first laps in the car. Thanks for giving me the chance to race with you.\n\nSee you at the shop,\n{sender}"
        });
        return true;
    }

    public static bool HasReceivedMail(Career career, string eventId) =>
        career.inbox?.Any(message => message.eventId == eventId) == true ||
        career.deletedMailIds?.Contains(eventId) == true;

    public static IEnumerable<TeamSocialPost> RaceFeed(Career career)
    {
        var deleted = new HashSet<string>(career.deletedSocialPostIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        return RetainedPosts(career).Where(post => !deleted.Contains(post.Id));
    }

    public static List<string> DeletePosts(Career career, IEnumerable<string> postIds)
    {
        var deleted = new HashSet<string>(career.deletedSocialPostIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        deleted.UnionWith(postIds);
        return RetainedPosts(career).Where(post => deleted.Contains(post.Id)).Select(post => post.Id).ToList();
    }

    private static IEnumerable<TeamSocialPost> RetainedPosts(Career career) =>
        DailyPosts(career).Concat(GeneratePosts(career).Take(MaximumPosts))
            .OrderByDescending(post => post.Date, StringComparer.Ordinal).Take(MaximumPosts);

    private static IEnumerable<TeamSocialPost> DailyPosts(Career career)
    {
        if (!Career.TryReadDate(career.currentDate, out var today))
            yield break;
        if (!Career.TryReadDate(career.socialStartDate, out var start))
            start = today;

        int days = Math.Min(MaximumPosts, (today - start).Days + 1);
        for (int offset = 0; offset < days; offset++)
        {
            string date = today.AddDays(-offset).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var random = new Random(SeedForDate(date, 67));
            string text = $"{career.teamName} — {SocialInsults.Texts[random.Next(SocialInsults.Texts.Length)]}";
            yield return new TeamSocialPost($"daily/{date}", Sources[random.Next(Sources.Length)], RandomUser(random),
                date, null, 0, 0, SocialSentiment.Negative, MaybeEmoji(random, text, NegativeEmoji));
        }
    }

    private static IEnumerable<TeamSocialPost> GeneratePosts(Career career)
    {
        var recent = (career.completedSeasons ?? Enumerable.Empty<SeasonSchedule>())
            .Append(career.schedule)
            .SelectMany(season => season?.races ?? Enumerable.Empty<ScheduledRace>())
            .OrderByDescending(race => race.date, StringComparer.Ordinal);

        foreach (var race in recent)
        {
            foreach (var post in ContactPosts(career.teamName, race))
                yield return post;
            if (!race.completed || race.playerSkipped)
                continue;
            var results = race.sessions?.Find(session => session.kind == RaceSessionKind.Race)?.results;
            var result = results?.Find(race.IsPlayerResult);
            if (result == null || result.position < 1 || result.position > results.Count)
                continue;

            foreach (var post in RacePosts(career.teamName, race, result.position, results.Count, result.wrecked))
                yield return post;
        }
    }

    private static IEnumerable<TeamSocialPost> ContactPosts(string team, ScheduledRace race)
    {
        if (race.sessions == null)
            yield break;

        var usedMessages = new HashSet<string>(StringComparer.Ordinal);
        for (int sessionIndex = race.sessions.Count - 1; sessionIndex >= 0; sessionIndex--)
        {
            var session = race.sessions[sessionIndex];
            if (!session.Completed || session.playerContacts == null)
                continue;
            foreach (var contact in session.playerContacts.OrderByDescending(item => item.damage))
            {
                var lines = contact.damage >= 22f ? HeavyContact : contact.damage >= 8f ? ModerateContact : LightContact;
                var random = new Random(SeedForDate(race.date, unchecked(race.trackId * 31 + contact.carNumber + sessionIndex)));
                string author = string.IsNullOrWhiteSpace(contact.driverName)
                    ? $"Driver of #{contact.carNumber}" : $"{contact.driverName} · #{contact.carNumber}";
                int firstLine = random.Next(lines.Length);
                string text = UniqueContactMessage(lines, firstLine, session, contact);
                yield return new TeamSocialPost($"{race.date}/{race.trackId}/contact/{sessionIndex}/{contact.carNumber}",
                    Sources[random.Next(Sources.Length)], author, race.date, race.trackName, 0, 0, SocialSentiment.Negative, text, contact);
            }
        }

        string UniqueContactMessage(string[] lines, int firstLine, RaceSession session, DriverContact contact)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string candidate = string.Format(lines[(firstLine + i) % lines.Length], team, race.trackName);
                if (usedMessages.Add(candidate))
                    return candidate;
            }

            string sessionName = string.IsNullOrWhiteSpace(session.name) ? session.kind.ToString() : session.name;
            string baseText = string.Format(lines[firstLine], team, race.trackName);
            string detail = $"My No. {contact.carNumber} still has the marks from {sessionName}.";
            string text = $"{baseText} {detail}";
            for (int occurrence = 2; !usedMessages.Add(text); occurrence++)
                text = $"{baseText} That's hit number {occurrence} my No. {contact.carNumber} has to deal with after {sessionName}.";
            return text;
        }
    }

    private static IEnumerable<TeamSocialPost> RacePosts(string team, ScheduledRace race, int position, int fieldSize, bool wrecked)
    {
        // Seed from saved race data so reopening Mail doesn't rewrite the feed.
        var random = new Random(SeedForDate(race.date, race.trackId));
        var sources = Sources.OrderBy(_ => random.Next()).ToArray();
        var sentiment = RaceSession.ClassifyFinish(position, fieldSize) switch
        {
            RaceFinishBand.Good => SocialSentiment.Positive,
            RaceFinishBand.Bad => SocialSentiment.Negative,
            _ => SocialSentiment.Neutral
        };
        var templates = sentiment == SocialSentiment.Positive ? Positive
            : sentiment == SocialSentiment.Negative ? Negative : Neutral;
        var lines = templates.OrderBy(_ => random.Next()).ToArray();
        var emoji = sentiment == SocialSentiment.Positive ? PositiveEmoji
            : sentiment == SocialSentiment.Negative ? NegativeEmoji : NeutralEmoji;
        string raceId = $"{race.date}/{race.trackId}";
        int insultStart = random.Next(SocialInsults.Texts.Length);
        if (wrecked)
        {
            yield return Reaction("wreck/0", sources[0], 0, $"{team} wrecked at {race.trackName}.");
            yield return Reaction("wreck/1", sources[1], 1, $"After that wreck at {race.trackName}, here's the word on {team}.");
        }
        if (sentiment == SocialSentiment.Negative)
        {
            yield return Reaction("finish/0", sources[2], 2, $"{team} limped home P{position} at {race.trackName}.");
            yield return Reaction("finish/1", sources[3], 3, $"P{position} at {race.trackName} for {team}.");
        }
        for (int i = 0; i < sources.Length; i++)
            yield return new TeamSocialPost($"{raceId}/{sources[i]}", sources[i], RandomUser(random),
                race.date, race.trackName, position, fieldSize, sentiment,
                MaybeEmoji(random, string.Format(lines[i], team, race.trackName, position), emoji));

        TeamSocialPost Reaction(string suffix, string source, int offset, string context)
        {
            string insult = SocialInsults.Texts[(insultStart + offset) % SocialInsults.Texts.Length];
            return new TeamSocialPost($"{raceId}/{suffix}", source, RandomUser(random),
                race.date, race.trackName, position, fieldSize, SocialSentiment.Negative,
                MaybeEmoji(random, $"{context} {insult}", NegativeEmoji));
        }
    }

    private static int SeedForDate(string date, int seed)
    {
        foreach (char character in date ?? string.Empty)
            seed = unchecked(seed * 31 + character);
        return seed;
    }

    private static string RandomUser(Random random) =>
        $"@{HandleStarts[random.Next(HandleStarts.Length)]}{HandleEnds[random.Next(HandleEnds.Length)]}{random.Next(10, 100)}";

    private static string MaybeEmoji(Random random, string text, string[] emoji) =>
        random.Next(3) == 0 ? $"{text} {emoji[random.Next(emoji.Length)]}" : text;
}
