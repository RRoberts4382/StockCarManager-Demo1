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

    public static bool HasPending(Career career) => career.inbox?.Any(mail => mail.NeedsResponse) == true;

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

        var offers = Sponsorships.CreateSeasonOffers(career.PrimarySponsor, year, raceCount, rank, fieldSize, new Random());
        var names = offers.Select(offer => Sponsorships.CompanyName(offer.companyId)).ToArray();
        string period = test ? $"{raceCount} remaining events in the {year} season" : $"the full {year} season ({raceCount} events)";
        string terms = string.Join("\n\n", offers.Select((offer, index) =>
            $"{names[index]}\n{RaceFinances.Dollars(offer.signingPayment)} on signing\n" +
            $"{RaceFinances.Dollars(offer.perRacePayment)} after each event"));
        string basis = fieldSize > 1 ? $"The offers reflect last season's P{rank} in the final points standings."
            : "The offers use the starting-team rate because there is no prior season ranking.";
        Send(career, id, test ? "Test scenario · Primary sponsorship offers" : $"{year} primary sponsorship offers",
            $"Two companies are offering to become your primary sponsor for {period}. Select one agreement.\n\n" +
            $"{basis}\n\n{terms}\n\nBoth agreements pay regardless of the team's results.", names);
        career.inbox.Last().scenarioSponsorOffers = offers;
        return true;
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
