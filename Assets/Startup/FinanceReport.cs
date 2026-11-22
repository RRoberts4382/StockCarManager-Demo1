using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

internal static class FinanceReport
{
    // Until these lines are tracked, use report-only allowances.
    private const long CrashDamagePerEntry = 4_000;
    private const long HospitalityPerEntry = 3_000;
    private const long FacilityMaintenancePerSeason = 60_000;
    private const long ManufacturerSupportPerSeason = 100_000;

    private sealed class Row
    {
        public readonly string Name;
        public readonly long? PerRace;
        public readonly long SeasonTotal;
        public readonly Row[] Children;

        public Row(string name, long? perRace, long seasonTotal)
        {
            Name = name;
            PerRace = perRace;
            SeasonTotal = seasonTotal;
            Children = Array.Empty<Row>();
        }

        public Row(string name, params Row[] children)
            : this(name, children.Any(row => row.PerRace.HasValue) ? children.Sum(row => row.PerRace.GetValueOrDefault()) : (long?)null,
                children.Sum(row => row.SeasonTotal))
        {
            Children = children;
        }
    }

    public static void Render(VisualElement grid, Career career, List<ScheduledRace> races,
        List<RaceExpenses> chargedExpenses, long upcomingPayroll, long winnings, RaceExpenses forecast)
    {
        var season = career.schedule;
        var completed = races.Where(race => race.completed).ToArray();
        int finished = completed.Length;
        int entered = completed.Count(race => !race.playerSkipped);

        long? Average(long total) => finished == 0 ? null
            : (long)Math.Round((decimal)total / finished, MidpointRounding.AwayFromZero);

        Row RaceCost(string name, long baseCost, long charged) =>
            new Row(name, Average(-charged) ?? -baseCost, -charged);

        var chargedItems = chargedExpenses.SelectMany(expense => expense.Items).ToArray();
        var perEvent = forecast.Items.Concat(chargedItems).GroupBy(item => item.id).Select(group => group.First())
            .Select(item => RaceCost(item.name, forecast.Items.FirstOrDefault(cost => cost.id == item.id)?.amount ?? 0,
                chargedItems.Where(cost => cost.id == item.id).Sum(cost => (long)cost.amount))).ToArray();

        var merchandise = Merchandising.Totals(career, season.season);
        bool showMerchandise = merchandise.Inventory > 0 || merchandise.Sales > 0;
        var expenseRows = new List<Row>
        {
            RaceCost("Salaries", upcomingPayroll, season.payrollPaid),
            new Row("Race Entry Expenses", perEvent),
            new Row("Vehicles",
                new Row("Car Purchases", Average(-season.carPurchases), -season.carPurchases),
                new Row("Component Repairs", Average(-season.componentRepairs), -season.componentRepairs),
                new Row("Consumables (est.)", 0, 0),
                RaceCost("Crash Damage (est.)", CrashDamagePerEntry, CrashDamagePerEntry * entered),
                new Row("R&D", null, -season.componentUpgrades)),
            new Row("Mystery Pallet Purchases", null, -season.mysteryPalletPurchases),
            RaceCost("Hospitality (est.)", HospitalityPerEntry, HospitalityPerEntry * entered),
            new Row("Facility Maintenance (est.)", null, -FacilityMaintenancePerSeason)
        };
        if (showMerchandise)
            expenseRows.Add(new Row("Merchandise Inventory Purchases", null, -merchandise.Inventory));
        var expenses = new Row("Operating Expenses", expenseRows.ToArray());

        var payments = completed.SelectMany(race => race.sponsorPayments ?? Enumerable.Empty<SponsorRacePayment>()).ToArray();
        var sponsors = new List<Row>();
        var historical = payments.Where(payment => payment.slot == 0)
            .GroupBy(payment => payment.companyId).ToDictionary(group => group.Key, group => group.Sum(payment => payment.amount));
        if (career.PrimarySponsor is SponsorContract contract)
            historical.TryAdd(contract.offer.companyId, 0L);
        var companies = historical.Select(item =>
            new Row(Sponsorships.CompanyName(item.Key), Average(item.Value), item.Value)).ToArray();
        if (companies.Length == 0)
            sponsors.Add(new Row("Primary sponsor · Open", Average(0), 0));
        else if (companies.Length == 1)
            sponsors.Add(new Row($"Primary sponsor · {companies[0].Name}", companies[0].PerRace, companies[0].SeasonTotal));
        else
            sponsors.Add(new Row("Primary sponsor", companies));
        long legacyIncome = payments.Where(payment => payment.slot != 0).Sum(payment => payment.amount);
        if (legacyIncome != 0)
            sponsors.Add(new Row("Historical sponsorship payments", Average(legacyIncome), legacyIncome));
        if (season.sponsorSigningIncome != 0)
            sponsors.Add(new Row("Primary sponsor signing payments", null, season.sponsorSigningIncome));
        long earlierIncome = season.sponsorIncome - season.sponsorSigningIncome - payments.Sum(payment => payment.amount);
        if (earlierIncome != 0)
            sponsors.Add(new Row("Earlier sponsorship payments", Average(earlierIncome), earlierIncome));

        var revenueRows = new List<Row>
        {
            new Row("Sponsorship", sponsors.ToArray()),
            new Row("Event Prize Money", Average(winnings), winnings),
            new Row("Season Points Fund Prizes", null, SeasonPrizes.Income(career)),
            new Row("Manufacturer Support (est.)", null, ManufacturerSupportPerSeason),
            new Row("Vehicle Sales", null, season.carSales),
            new Row("Mystery Pallet Sale Proceeds", null, season.mysteryPalletSaleProceeds)
        };
        if (showMerchandise)
            revenueRows.Add(new Row("Merchandise Sales", null, merchandise.Sales));
        var revenue = new Row("Revenue", revenueRows.ToArray());

        grid.Clear();
        var header = new VisualElement();
        header.AddToClassList("results-grid-row");
        header.AddToClassList("results-grid-header");
        AddCell(header, $"{season.season} Season", "finance-description");
        AddCell(header, "Per Race", "finance-number").tooltip =
            "Average per completed scheduled event for tracked amounts. Recurring base costs are shown before the first result.";
        AddCell(header, "Season Total", "finance-number").tooltip =
            "Recorded year-to-date transactions and labeled report estimates.";
        grid.Add(header);
        AddRow(grid, expenses, 0);
        AddRow(grid, revenue, 0);
        var note = new Label("Estimates are included in the report subtotals. Balance and average profit use recorded transactions.")
        {
            style = { whiteSpace = WhiteSpace.Normal }
        };
        note.AddToClassList("garage-detail");
        note.AddToClassList("finance-estimates-note");
        grid.Add(note);
    }

    private static void AddRow(VisualElement container, Row data, int depth)
    {
        bool hasChildren = data.Children.Length > 0;
        var row = hasChildren ? new Button() : new VisualElement();
        row.AddToClassList("results-grid-row");
        row.EnableInClassList("results-grid-header", hasChildren);
        var description = AddCell(row, data.Name, "finance-description");
        description.style.paddingLeft = 8 + depth * 16;
        AddAmount(row, data.PerRace);
        AddAmount(row, data.SeasonTotal);
        container.Add(row);
        if (row is not Button toggle)
            return;

        toggle.AddToClassList("settings-button");
        toggle.AddToClassList("finance-group-toggle");
        var children = new VisualElement();
        container.Add(children);
        foreach (var child in data.Children)
            AddRow(children, child, depth + 1);

        bool expanded = depth == 0;
        UpdateGroup();
        toggle.clicked += () =>
        {
            expanded = !expanded;
            UpdateGroup();
        };

        void UpdateGroup()
        {
            children.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            description.text = (expanded ? "▾ " : "▸ ") + data.Name;
            toggle.tooltip = (expanded ? "Collapse " : "Expand ") + data.Name;
        }
    }

    private static Label AddCell(VisualElement row, string text, string column)
    {
        var label = new Label(text) { pickingMode = row is Button ? PickingMode.Ignore : PickingMode.Position };
        label.AddToClassList("results-grid-cell");
        label.AddToClassList(column);
        row.Add(label);
        return label;
    }

    private static void AddAmount(VisualElement row, long? amount)
    {
        string text = amount.HasValue
            ? (amount < 0 ? "-" : string.Empty) + RaceFinances.Dollars(Math.Abs(amount.Value))
            : "—";
        var label = AddCell(row, text, "finance-number");
        label.EnableInClassList("finance-positive", amount > 0);
        label.EnableInClassList("finance-negative", amount < 0);
    }
}
