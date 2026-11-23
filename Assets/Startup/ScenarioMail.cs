using System;
using System.Collections.Generic;
using System.Linq;

internal static class ScenarioMail
{
    private const string CareerStartId = "scenario/career-start";
    private const string TestId = "scenario/test";
    private const string FollowUpTestId = "scenario/test-follow-up";
    private const string SponsorshipTestId = "scenario/primary-sponsor-test";
    private const double PostRaceChance = 0.08;

    public static bool HasPending(Career career) => career.inbox?.Any(mail => mail.BlocksAdvancement) == true;

    public static void SendCareerStart(Career career) =>
        Send(career, CareerStartId, "Choose your first-season priority",
            "Before we start the season, what should the crew prioritize at our first team meeting?",
            "Reliable finishes", "Qualifying pace");

    public static bool SendTestToExistingCareer(Career career)
    {
        if (TeamCommunications.HasReceivedMail(career, CareerStartId))
            return false;

        if (!TeamCommunications.HasReceivedMail(career, TestId))
            return Send(career, TestId, "Test scenario · Response required",
                "This is a test of scenario emails. Select OK to complete the scenario and unlock date advancement.", "OK");

        if (career.inbox?.Any(mail => mail.eventId == TestId && mail.NeedsResponse) == true)
            return false;

        return Send(career, FollowUpTestId, "Follow-up test scenario · Response required",
            "Try Next Day with this scenario still pending. Then select OK to satisfy it and unlock date advancement.", "OK");
    }

    public static void SendSeasonStart(Career career) =>
        Send(career, $"scenario/season/{career.schedule.season}", "Set the new season's priority",
            "The new schedule is ready. Which part of the team's performance should we review first?",
            "Race consistency", "Fast-lap pace");

    public static bool HasSeasonSponsorship(Career career) =>
        TeamCommunications.HasReceivedMail(career, SeasonSponsorshipId(career.schedule.season)) ||
        career.inbox?.Any(mail => mail.eventId == SponsorshipTestId &&
            mail.scenarioSponsorOffers?.FirstOrDefault()?.seasonYear == career.schedule.season) == true;

    private static string SeasonSponsorshipId(int year) => $"scenario/primary-sponsor/{year}";

    public static bool SendSeasonSponsorship(Career career, int rank, int fieldSize, bool test = false)
    {
        int year = career.schedule.season;
        string id = test ? SponsorshipTestId : SeasonSponsorshipId(year);
        if (HasSeasonSponsorship(career) ||
            (test && (TeamCommunications.HasReceivedMail(career, SponsorshipTestId) ||
                      TeamCommunications.HasReceivedMail(career, CareerStartId))))
            return false;

        int raceCount = career.schedule.races.Count(race => !race.completed);
        if (raceCount == 0)
            return false;

        var offers = Sponsorships.CreateSeasonOffers(career.PrimarySponsor, year, raceCount, rank, fieldSize, new Random(), career.SeriesRules, career.Driver);
        var names = offers.Select(offer => Sponsorships.CompanyName(offer.companyId)).ToArray();
        string period = test ? $"{raceCount} remaining events in the {year} season" : $"the full {year} season ({raceCount} events)";
        string terms = string.Join("\n\n", offers.Select((offer, index) =>
            $"{names[index]}\n{RaceFinances.Dollars(offer.signingPayment)} on signing\n" +
            $"{RaceFinances.Dollars(offer.perRacePayment)} after each event"));
        string basis = fieldSize > 1 ? $"The offers reflect last season's P{rank} in the final points standings."
            : "The offers use the starting-team rate because there is no prior season ranking.";
        if (career.Driver != null)
            basis += " Your driver's Charisma and Social Media XP also affect the signing payment and per-race rate.";
        Send(career, id, test ? "Test scenario · Primary sponsorship offers" : $"{year} primary sponsorship offers",
            $"Two companies are offering to become your primary sponsor for {period}. Select one agreement.\n\n" +
            $"{basis}\n\n{terms}\n\nBoth agreements pay regardless of the team's results.", names);
        career.inbox.Last().scenarioSponsorOffers = offers;
        return true;
    }

    internal static void ResetSeasonSponsorship(Career career)
    {
        career.ResetSponsorships();
        career.inbox?.RemoveAll(mail => mail.scenarioSponsorOffers?.Length > 0);
        career.deletedMailIds?.RemoveAll(id => id != null &&
            (id.StartsWith("scenario/primary-sponsor/", StringComparison.Ordinal) || id == SponsorshipTestId));
        SendSeasonSponsorship(career, career.previousSeasonRank, career.previousSeasonFieldSize);
    }

    internal static bool SendSeriesChangeIfDue(Career career, DateTime date)
    {
        var award = career.seasonPrizeAwards?.Where(item => item.paid && item.season + 1 >= career.schedule.season)
            .OrderByDescending(item => item.season).FirstOrDefault();
        if (award == null || (Career.TryReadDate(award.paymentDate, out var paidDate) && date.Date <= paidDate.Date)) return false;
        int year = award.season + 1;
        if (!Send(career, "scenario/series-change/" + year, "Choose your series for " + year,
            "Your season points fund prize has been paid. Would you like to change racing series for " + year + "?\n\n" +
            "Change Series opens team and car setup with your current values. You will review chassis sales, the replacement car purchase, and any employee departures before committing. " +
            "Stay in " + career.seriesName + " keeps your current cars and employees and continues into the next season.", "Stay in " + career.seriesName, "Change Series")) return false;
        var mail = career.inbox.Last(); mail.sender = career.seriesName; mail.seriesChangeYear = year;
        return true;
    }

    internal static bool RefreshSeriesChangeChoices(Career career)
    {
        bool changed = false;
        foreach (var mail in career.inbox ?? Enumerable.Empty<TeamMailMessage>())
        {
            if (mail.seriesChangeYear <= 0 || !mail.NeedsResponse) continue;
            var choices = new[] { "Stay in " + career.seriesName, "Change Series" };
            if (mail.scenarioOptions.SequenceEqual(choices)) continue;
            mail.scenarioOptions = choices;
            mail.body = "Would you like to stay in " + career.seriesName + " for " + mail.seriesChangeYear +
                ", keeping your current cars and employees, or change series?\n\nChange Series opens team and car setup with your current values. " +
                "You will review all chassis sales, the replacement purchase, employee departures, and sponsorship reset before committing.";
            changed = true;
        }
        return changed;
    }

    public static void SendNewYear(Career career, DateTime previousDate, DateTime date)
    {
        if (date.Year > previousDate.Year)
            Send(career, $"scenario/year/{date.Year}", "Choose a focus for the new year",
                "The crew is updating its plans for the new year. Which topic should open our planning meeting?",
                "Shop readiness", "Track preparation");
    }

    public static void MaybeSendPostRace(Career career, ScheduledRace race)
    {
        if (new Random().NextDouble() >= PostRaceChance)
            return;
        Send(career, $"scenario/post-race/{race.date}/{race.trackId}", $"Choose a debrief topic after {race.trackName}",
            "The crew has time for one topic at the start of our race debrief. Which should we cover first?",
            "Pit strategy", "Long-run pace");
    }

    private static readonly string[] Celebrations =
        { "birthday", "wedding anniversary", "family bar mitzvah", "shop anniversary", "new baby celebration" };

    private static readonly string[] Charities =
    {
        "Three-Legged Dogs' Sock Fund", "Foundation for Unclaimed Left Turns",
        "Retirement Home for Worn-Out Traffic Cones", "Society for Pigeons Without GPS",
        "Institute for the Preservation of Bent Spoons", "National Fund for Lonely Lug Nuts"
    };

    public static void SendSolicitations(Career career, DateTime previous, DateTime date)
    {
        if (date <= previous) return;

        for (var day = previous.Date; day < date.Date;)
        {
            day = day.AddDays(1);
            if (day.DayOfWeek != DayOfWeek.Monday) continue;
            string id = FormattableString.Invariant($"scenario/charity/{day:yyyy-MM-dd}");
            var random = ScenarioRandom(career, id);
            string charity = Charities[random.Next(Charities.Length)];
            int amount = random.Next(100, 2501);
            SendPurchase(career, id, charity, "Could you spare a donation?",
                $"Our latest fundraising drive is underway. Would your team donate {RaceFinances.Dollars(amount)} to help {charity}?",
                amount, "Donate", "Decline");
        }

        for (int year = previous.Year; year <= date.Year; year++)
        {
            var yearStart = new DateTime(year, 1, 1);
            int yearDays = DateTime.IsLeapYear(year) ? 366 : 365;
            string id = $"scenario/warranty/{year}";
            var random = ScenarioRandom(career, id);
            var warrantyDay = yearStart.AddDays(random.Next(yearDays));
            if (warrantyDay > previous && warrantyDay <= date)
                SendPurchase(career, id, "Priority Motor Warranty Bureau", "FINAL NOTICE: We need to talk about your car",
                    "We have been trying to reach you about your car's extended warranty. Stop what you're doing and respond today. " +
                    "We need your decision before you take another lap. Purchase the warranty now for $10,000. We are standing by for your answer.",
                    10000, "Purchase Warranty", "Decline");

            SendShopOffers(career, previous, date, yearStart, yearDays);

            foreach (var employee in career.employees ?? Enumerable.Empty<Employee>())
                SendCelebrations(employee.FullName, EmployeeGiftKey(employee), yearStart, yearDays);
            foreach (var driver in career.drivers ?? Enumerable.Empty<Driver>())
                SendCelebrations(driver.FullName, DriverGiftKey(driver), yearStart, yearDays);
        }

        void SendCelebrations(string name, string recipient, DateTime yearStart, int yearDays)
        {
            int half = yearDays / 2;
            for (int period = 0; period < 2; period++)
            {
                string id = $"scenario/team-gift/{yearStart.Year}/{period}/{recipient}";
                var random = ScenarioRandom(career, id);
                var day = yearStart.AddDays(period == 0 ? random.Next(half) : random.Next(half, yearDays));
                if (day <= previous || day > date) continue;

                string occasion = Celebrations[random.Next(Celebrations.Length)];
                int amount = random.Next(100, 2501);
                SendPurchase(career, id, "Shop Office", $"Gift for {name}",
                    $"We're collecting for a gift for {name}'s {occasion}. Could the team contribute {RaceFinances.Dollars(amount)}?",
                    amount, "Buy Gift", "Refuse", recipient);
            }
        }
    }

    private static void SendShopOffers(Career career, DateTime previous, DateTime date, DateTime yearStart, int yearDays)
    {
        SendOffer(TeamMailOffer.MascotSearch, "mascot", 0, "Shop Office", "Has anyone seen Lug Nut?",
            "Lug Nut, our shop cat, slipped out during load-in. Can we spend $500 on posters and a reward?",
            500, "Fund Search");
        SendOffer(TeamMailOffer.PartsPallet, "parts-pallet", 1, "Backstretch Freight Salvage", "Mystery pallet for sale",
            "We have an unclaimed pallet marked RACE PARTS. It could be good spares or a box of mismatched lug nuts. Take it sight unseen for $1,250. The shop can resell whatever turns up.",
            1250, "Buy Pallet");
        SendOffer(TeamMailOffer.Infomercial, "infomercial", 2, "Graveyard Shift Productions", "Your driver on late-night TV",
            "We will pay the team $2,000 for your driver to demonstrate our self-tightening ratchet on local TV. Can they say 'It practically tightens itself' with a straight face?",
            0, "Accept Spot");
        SendOffer(TeamMailOffer.ChiliCookOff, "chili", 3, "Turn Four Racing", "You call that chili?",
            "Bring your best pot to the rival teams' chili cook-off. Entry is $300. The winner takes $1,500 and bragging rights in the garage.",
            300, "Enter Cook-Off");

        void SendOffer(TeamMailOffer offer, string slug, int quarter, string sender, string subject, string body, int cost, string accept)
        {
            string id = $"scenario/{slug}/{yearStart.Year}";
            var random = ScenarioRandom(career, id);
            var day = yearStart.AddDays(random.Next(quarter * yearDays / 4, (quarter + 1) * yearDays / 4));
            if (day <= previous || day > date || !Send(career, id, subject, body, accept, "Decline")) return;
            var mail = career.inbox.Last();
            mail.sender = sender;
            mail.scenarioOffer = offer;
            mail.scenarioPurchaseCost = cost;
        }
    }

    private static string ApplyShopOffer(Career career, TeamMailMessage mail, out Action rollback)
    {
        rollback = null;
        if (mail.scenarioOffer == TeamMailOffer.Infomercial && career.Driver == null)
            return "Hire or assign a driver before accepting the TV spot.";

        var random = ScenarioRandom(career, mail.eventId + "/outcome");
        int payout = 0;
        float moraleBoost = 0f;
        string outcome;
        switch (mail.scenarioOffer)
        {
            case TeamMailOffer.MascotSearch:
                if (random.NextDouble() < .75)
                {
                    moraleBoost = .03f;
                    outcome = "Lug Nut turned up asleep in a neighbor's tire rack. The whole team gained 3 morale points.";
                }
                else outcome = "The crew put up the posters and checked every trailer. There's still no sign of Lug Nut.";
                break;
            case TeamMailOffer.PartsPallet:
                bool hasSpares = random.NextDouble() < .45;
                payout = hasSpares ? 3000 : 100;
                outcome = hasSpares
                    ? "The pallet held boxed race spares. The shop sold them for $3,000."
                    : "The pallet held 200 mismatched lug nuts. A scrap dealer paid $100 for the lot.";
                break;
            case TeamMailOffer.Infomercial:
                payout = 2000;
                outcome = "Your driver made it through the ratchet demo without laughing. The station paid the team $2,000.";
                break;
            case TeamMailOffer.ChiliCookOff:
                bool won = random.NextDouble() < .30;
                payout = won ? 1500 : 0;
                moraleBoost = won ? .03f : .01f;
                outcome = won
                    ? "Your chili took first place. The team earned $1,500 and everyone gained 3 morale points."
                    : "The judges picked another team. Everyone still gained 1 morale point for the afternoon out.";
                break;
            default:
                return null;
        }

        long previousBalance = career.teamBalance;
        career.teamBalance += payout - mail.scenarioPurchaseCost;
        var season = career.schedule;
        bool isPallet = mail.scenarioOffer == TeamMailOffer.PartsPallet;
        if (isPallet)
        {
            season.mysteryPalletPurchases += mail.scenarioPurchaseCost;
            season.mysteryPalletSaleProceeds += payout;
        }
        var undoMorale = new List<Action>();
        if (moraleBoost > 0)
        {
            foreach (var employee in career.employees ?? Enumerable.Empty<Employee>())
            {
                float previousMorale = employee.morale;
                employee.morale = Math.Min(1f, previousMorale + moraleBoost);
                undoMorale.Add(() => employee.morale = previousMorale);
            }
            foreach (var driver in career.drivers ?? Enumerable.Empty<Driver>())
            {
                float previousMorale = driver.morale;
                driver.morale = Math.Min(1f, previousMorale + moraleBoost);
                undoMorale.Add(() => driver.morale = previousMorale);
            }
        }
        var result = new TeamMailMessage
        {
            eventId = mail.eventId + "/outcome",
            date = career.currentDate,
            sender = mail.sender,
            subject = "Update: " + mail.subject,
            body = outcome
        };
        career.inbox.Add(result);
        rollback = () =>
        {
            career.inbox.Remove(result);
            career.teamBalance = previousBalance;
            if (isPallet)
            {
                season.mysteryPalletPurchases -= mail.scenarioPurchaseCost;
                season.mysteryPalletSaleProceeds -= payout;
            }
            foreach (var undo in undoMorale) undo();
        };
        return null;
    }

    private static readonly string[] AppearanceBusinesses =
        { "Turn Two Tire & Auto", "Corner Flag Hardware", "Main Street Barbecue", "Victory Lane Coffee", "Backstretch Sporting Goods" };

    private static readonly string[] LocalPodcasts =
        { "The Short Track Hour", "Pit Pass After Dark", "The Saturday Night Racing Show" };

    public static void SendDriverAppearances(Career career, DateTime previous, DateTime date)
    {
        var driver = career.Driver;
        if (driver == null || previous >= date || driver.arrest?.IsActiveOn(career.currentDate) == true) return;

        string recipient = DriverGiftKey(driver);
        double publicChance = .30 + .40 * Math.Clamp(driver.SocialMediaXp, 0f, 1f);
        ConsiderInvites(7, TeamMailOffer.PublicAppearance, "public-appearance", publicChance);
        ConsiderInvites(56, TeamMailOffer.PodcastAppearance, "podcast", 1);

        void ConsiderInvites(int intervalDays, TeamMailOffer offer, string slug, double chance)
        {
            int firstDay = (previous.Date - DateTime.MinValue).Days + 1;
            int lastDay = (date.Date - DateTime.MinValue).Days;
            for (int window = firstDay / intervalDays; window <= lastDay / intervalDays; window++)
            {
                string id = $"scenario/{slug}/{window}/{recipient}";
                var random = ScenarioRandom(career, id);
                int day = window * intervalDays + random.Next(intervalDays);
                if (day < firstDay || day > lastDay || random.NextDouble() >= chance) continue;

                bool podcast = offer == TeamMailOffer.PodcastAppearance;
                var hosts = podcast ? LocalPodcasts : AppearanceBusinesses;
                string host = hosts[random.Next(hosts.Length)];
                int pay = podcast ? random.Next(10, 31) * 25 : random.Next(6, 19) * 25;
                string payText = $"We will pay {driver.FullName} {RaceFinances.Dollars(pay)} directly.";
                string body = podcast
                    ? $"Would {driver.FullName} join our local racing podcast for an episode about life at the track? {payText} Accepting builds Social Media XP."
                    : $"Could {driver.FullName} stop by for an autograph session and photos with fans? {payText} Accepting builds Charisma. Declining lowers it slightly.";
                if (!Send(career, id, podcast ? "Come on our racing podcast" : "Local autograph session invitation",
                    body, podcast ? "Accept Podcast" : "Accept Appearance", "Decline")) continue;
                var mail = career.inbox.Last();
                mail.sender = host;
                mail.date = DateTime.MinValue.AddDays(day).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                mail.scenarioOffer = offer;
                mail.scenarioInvitedDriver = recipient;
            }
        }
    }

    public static string ApplyDriverAppearance(Career career, TeamMailMessage mail, int choice, out Action rollback)
    {
        rollback = null;
        var driver = career.drivers?.Find(item => DriverGiftKey(item) == mail.scenarioInvitedDriver);
        if (driver == null)
            return choice == 0 ? "The invited driver has left the team. Select Decline to clear the invitation." : null;

        if (mail.scenarioOffer == TeamMailOffer.PublicAppearance)
        {
            float previous = driver.Charisma;
            driver.Charisma += choice == 0 ? .02f : -.01f;
            rollback = () => driver.Charisma = previous;
        }
        else if (choice == 0)
        {
            float previous = driver.SocialMediaXp;
            driver.SocialMediaXp += .03f;
            rollback = () => driver.SocialMediaXp = previous;
        }

        return null;
    }

    private static readonly string[] DriverEmbarrassments =
    {
        "a clip of them waving back at someone who was waving at the person behind them",
        "a video of them climbing into the wrong rental car at the airport",
        "a clip of them confidently taking a victory bow at a karaoke contest before the winner was announced",
        "a photo of them walking around the grocery store with a price tag stuck to the back of their jacket",
        "a video of them dropping three ice cream cones in a row at a fan event"
    };

    public static Action SendDriverEmbarrassment(Career career, DateTime previous, DateTime destination)
    {
        var driver = career.Driver;
        if (driver == null || destination <= previous) return null;

        float priorScore = driver.SocialMediaXp;
        bool applied = false;
        for (int year = previous.Year; year <= destination.Year; year++)
        {
            var start = new DateTime(year, 1, 2);
            var middle = new DateTime(year, 7, 1);
            SendInPeriod(start, (middle - start).Days, 0);
            SendInPeriod(middle, 184, 1);
        }
        return applied ? () => driver.SocialMediaXp = priorScore : null;

        void SendInPeriod(DateTime start, int days, int period)
        {
            string id = $"scenario/driver-embarrassment/{start.Year}/{period}";
            var random = ScenarioRandom(career, id);
            var day = start.AddDays(random.Next(days));
            if (day <= previous || day > destination) return;
            string incident = DriverEmbarrassments[random.Next(DriverEmbarrassments.Length)];
            float drop = Math.Min(.04f, driver.SocialMediaXp);
            if (!Send(career, id, $"{driver.FullName} had an awkward moment online",
                $"Local fans have been sharing {incident}. {driver.FullName}'s Social Media XP dropped by {drop:0.00}.")) return;

            var mail = career.inbox.Last();
            mail.sender = "Team Communications";
            mail.date = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            driver.SocialMediaXp -= drop;
            applied = true;
        }
    }

    private static readonly string[] CrewArguments =
    {
        "whether a hot dog counts as a sandwich",
        "whose turn it was to refill the shop coffee pot",
        "whether the shop radio should play the same song twice in one day",
        "whether a taped line on the floor reserves a parking spot",
        "who moved the lucky wrench"
    };

    public static Action SendCrewArgument(Career career, DateTime previous, DateTime destination)
    {
        if ((career.employees?.Count ?? 0) < 2 || destination <= previous) return null;

        var previousDates = new Dictionary<Employee, string>();
        for (int year = previous.Year; year <= destination.Year; year++)
        {
            var start = new DateTime(year, 1, 2);
            var middle = new DateTime(year, 7, 1);
            SendInPeriod(start, (middle - start).Days, 0);
            SendInPeriod(middle, 184, 1);
        }
        return previousDates.Count == 0 ? null : () =>
        {
            foreach (var entry in previousDates) entry.Key.argumentMoraleUntil = entry.Value;
        };

        void SendInPeriod(DateTime start, int days, int period)
        {
            string id = $"scenario/crew-argument/{start.Year}/{period}";
            var random = ScenarioRandom(career, id);
            var day = start.AddDays(random.Next(days));
            if (day <= previous || day > destination) return;

            string dayText = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var crew = career.employees.Where(employee => employee.arrest?.IsActiveOn(dayText) != true).ToList();
            if (crew.Count < 2) return;
            var first = crew[random.Next(crew.Count)];
            crew.Remove(first);
            var second = crew[random.Next(crew.Count)];
            string topic = CrewArguments[random.Next(CrewArguments.Length)];
            if (!Send(career, id, "An argument in the shop",
                $"{first.FullName} and {second.FullName} got into an argument about {topic}. " +
                "Both lose 5 morale points for one week.")) return;

            var mail = career.inbox.Last();
            mail.sender = "Shop Office";
            mail.date = dayText;
            var until = day.AddDays(Math.Min(7, (DateTime.MaxValue.Date - day).Days))
                .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            previousDates.TryAdd(first, first.argumentMoraleUntil);
            previousDates.TryAdd(second, second.argumentMoraleUntil);
            first.argumentMoraleUntil = until;
            second.argumentMoraleUntil = until;
        }
    }

    internal static Random ScenarioRandom(Career career, string id)
    {
        int seed = 17;
        foreach (char character in career.teamName + "/" + id)
            seed = unchecked(seed * 31 + character);
        return new Random(seed);
    }

    private static string EmployeeGiftKey(Employee employee) =>
        $"employee/{employee.FullName}/{employee.hometownCity}/{employee.hometownState}";

    private static string DriverGiftKey(Driver driver) =>
        $"driver/{driver.FullName}/{driver.hometownCity}/{driver.hometownState}";

    private static void SendPurchase(Career career, string id, string sender, string subject, string body,
        int amount, string accept, string decline, string recipient = null)
    {
        if (!Send(career, id, subject, body, accept, decline)) return;
        var mail = career.inbox.Last();
        mail.sender = sender;
        mail.scenarioPurchaseCost = amount;
        mail.scenarioGiftRecipient = recipient;
    }

    public static string ApplyPurchase(Career career, TeamMailMessage mail, int choice, out Action rollback)
    {
        rollback = null;
        if (choice != 0 || (mail.scenarioPurchaseCost <= 0 && mail.scenarioOffer == TeamMailOffer.None))
            return null;

        if (mail.scenarioPurchaseCost > 0 && career.teamBalance < mail.scenarioPurchaseCost)
            return "The team budget cannot cover this purchase.";

        if (mail.scenarioOffer != TeamMailOffer.None)
            return ApplyShopOffer(career, mail, out rollback);

        Action undoMorale = null;
        if (!string.IsNullOrEmpty(mail.scenarioGiftRecipient))
        {
            var employee = career.employees?.Find(item => EmployeeGiftKey(item) == mail.scenarioGiftRecipient);
            var driver = career.drivers?.Find(item => DriverGiftKey(item) == mail.scenarioGiftRecipient);
            if (employee == null && driver == null)
                return "The gift recipient has left the team.";

            if (employee != null)
            {
                float previousMorale = employee.morale;
                employee.morale = Math.Min(1f, previousMorale + .05f);
                undoMorale = () => employee.morale = previousMorale;
            }
            else
            {
                float previousMorale = driver.morale;
                driver.morale = Math.Min(1f, previousMorale + .05f);
                undoMorale = () => driver.morale = previousMorale;
            }
        }

        long previousBalance = career.teamBalance;
        career.teamBalance -= mail.scenarioPurchaseCost;
        rollback = () =>
        {
            career.teamBalance = previousBalance;
            undoMorale?.Invoke();
        };
        return null;
    }

    private static bool Send(Career career, string id, string subject, string body, params string[] options)
    {
        if (TeamCommunications.HasReceivedMail(career, id))
            return false;
        career.inbox ??= new List<TeamMailMessage>();
        career.inbox.Add(new TeamMailMessage
        {
            eventId = id,
            sender = "Crew Chief",
            date = career.currentDate,
            subject = subject,
            body = body,
            scenarioOptions = options
        });
        return true;
    }
}
