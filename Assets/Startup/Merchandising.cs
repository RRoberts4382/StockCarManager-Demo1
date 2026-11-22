using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

[Serializable]
internal sealed class MerchandiseProgram
{
    public int offerYear;
    public int inventoryCost;
    public string firstDeposit;
}

[Serializable]
internal sealed class MerchandiseMonth
{
    public int year;
    public int month;
    public long sales;
}

internal static class Merchandising
{
    private const int DepositCount = 52;
    private static readonly int[] BuyIns = { 1000, 5000, 20000, 50000 };

    public static bool SendOffer(Career career, DateTime day)
    {
        string id = $"scenario/merchandising/{day.Year}";
        if (TeamCommunications.HasReceivedMail(career, id)) return false;

        AddMail(career, id, day, "Let us run your merchandise booth",
            "We stock, sell, and ship your team's T-shirts, hats, keychains, and koozies. Choose an up-front inventory budget. " +
            "We send 52 weekly deposits beginning next month, plus a monthly sales report. At typical driver ratings, " +
            "the program pays out 2.5 times the inventory cost over the full run. Social Media XP and Charisma affect sales.",
            BuyIns.Select(amount => "Invest " + RaceFinances.Dollars(amount)).Append("Decline").ToArray());
        return true;
    }

    public static string Accept(Career career, TeamMailMessage offer, int choice, out Action undo)
    {
        undo = null;
        if (choice < 0 || choice >= BuyIns.Length) return null;
        if (!Career.TryReadDate(career.currentDate, out var day) ||
            !Career.TryReadDate(offer.date, out var offeredOn) || offeredOn.Year != day.Year)
            return "This year's merchandising offer has expired. Select Decline to clear it.";
        if (career.merchandisePrograms?.Any(program => program.offerYear == day.Year) == true)
            return "There is already a merchandising program for this offer year.";

        int cost = BuyIns[choice];
        if (career.teamBalance < cost) return "The team budget cannot cover this inventory purchase.";
        if (day.Year == 9999 && day.Month == 12) return "There is no next month to start this program.";

        var previousPrograms = career.merchandisePrograms;
        long previousBalance = career.teamBalance;
        var start = new DateTime(day.Year, day.Month, 1).AddMonths(1);
        var program = new MerchandiseProgram { offerYear = day.Year, inventoryCost = cost, firstDeposit = DateText(start) };
        career.merchandisePrograms = new List<MerchandiseProgram>(previousPrograms ?? Enumerable.Empty<MerchandiseProgram>()) { program };
        career.teamBalance -= cost;
        undo = () =>
        {
            career.merchandisePrograms = previousPrograms;
            career.teamBalance = previousBalance;
        };
        return null;
    }

    public static Action Advance(Career career, DateTime previous, DateTime destination)
    {
        if (destination.Date <= previous.Date) return null;

        var previousMonths = career.merchandiseMonths;
        var months = new List<MerchandiseMonth>(previousMonths ?? Enumerable.Empty<MerchandiseMonth>());
        long previousBalance = career.teamBalance;
        int inboxCount = career.inbox?.Count ?? 0;
        decimal factor = SalesFactor(career.Driver);
        var programs = (career.merchandisePrograms ?? Enumerable.Empty<MerchandiseProgram>())
            .Select(program => (Program: program, Start: Career.TryReadDate(program.firstDeposit, out var start) ? (DateTime?)start : null))
            .Where(entry => entry.Start.HasValue && entry.Start.Value <= destination &&
                (previous - entry.Start.Value).Days < DepositCount * 7).ToArray();

        for (var day = previous.Date; day < destination.Date;)
        {
            day = day.AddDays(1);
            if (day.Day == 1)
            {
                var lastMonth = day.AddMonths(-1);
                var statement = months.Find(entry => entry.year == lastMonth.Year && entry.month == lastMonth.Month);
                if (statement != null)
                {
                    string name = lastMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
                    AddMail(career, $"merchandise-report/{lastMonth:yyyy-MM}", day, "Merchandising report for " + name,
                        $"Merchandise sales deposited {RaceFinances.Dollars(statement.sales)} into the team budget during {name}.");
                }
                if (day.Month == 1) SendOffer(career, day);
            }

            long sales = 0;
            foreach (var entry in programs)
            {
                int elapsed = (day - entry.Start.Value).Days;
                if (elapsed < 0 || elapsed % 7 != 0 || elapsed / 7 >= DepositCount) continue;
                int week = elapsed / 7;
                decimal weekly = entry.Program.inventoryCost * 2.5m * factor / DepositCount;
                sales += (long)(Math.Round((week + 1) * weekly) - Math.Round(week * weekly));
            }
            if (sales == 0) continue;

            int index = months.FindIndex(entry => entry.year == day.Year && entry.month == day.Month);
            long priorSales = index < 0 ? 0 : months[index].sales;
            var month = new MerchandiseMonth { year = day.Year, month = day.Month, sales = priorSales + sales };
            if (index < 0) months.Add(month);
            else months[index] = month;
            career.teamBalance += sales;
        }

        career.merchandiseMonths = months;
        return () =>
        {
            career.merchandiseMonths = previousMonths;
            career.teamBalance = previousBalance;
            CrewChiefMail.UndoNewMessages(career, inboxCount);
        };
    }

    public static (long Inventory, long Sales) Totals(Career career, int year) =>
        ((career.merchandisePrograms ?? Enumerable.Empty<MerchandiseProgram>())
            .Where(program => program.offerYear == year).Sum(program => (long)program.inventoryCost),
         (career.merchandiseMonths ?? Enumerable.Empty<MerchandiseMonth>())
            .Where(month => month.year == year).Sum(month => month.sales));

    private static decimal SalesFactor(Driver driver)
    {
        double social = Math.Clamp(driver?.SocialMediaXp ?? .5f, 0f, 1f);
        double charisma = Math.Clamp(driver?.Charisma ?? .5f, 0f, 1f);
        return (decimal)(.75 + .25 * social + .25 * charisma);
    }

    private static string DateText(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static void AddMail(Career career, string id, DateTime day, string subject, string body, string[] choices = null)
    {
        if (TeamCommunications.HasReceivedMail(career, id)) return;
        career.inbox ??= new List<TeamMailMessage>();
        career.inbox.Add(new TeamMailMessage
        {
            eventId = id,
            date = DateText(day),
            sender = "Gridline Merchandise & Fulfillment",
            subject = subject,
            body = body,
            scenarioOptions = choices,
            scenarioOffer = choices == null ? TeamMailOffer.None : TeamMailOffer.Merchandising
        });
    }
}

