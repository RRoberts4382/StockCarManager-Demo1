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
    private const long MerchandisingPerSeason = 40_000;

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
        List<RaceExpenses> chargedExpenses, long upcomingPayroll, long winnings)
    {
        var season = career.schedule;
        var completed = races.Where(race => race.completed).ToArray();
        int finished = completed.Length;
        int entered = completed.Count(race => !race.playerSkipped);

        long? Average(long total) => finished == 0 ? null
            : (long)Math.Round((decimal)total / finished, MidpointRounding.AwayFromZero);

        Row RaceCost(string name, long baseCost, long charged) =>
            new Row(name, Average(-charged) ?? -baseCost, -charged);

        var expenses = new Row("Operating Expenses",
            RaceCost("Salaries", upcomingPayroll, season.payrollPaid),
            new Row("Vehicles",
                new Row("Car Purchases", Average(-season.carPurchases), -season.carPurchases),
                RaceCost("Engine Lease", RaceExpenses.EngineLeaseBase, chargedExpenses.Sum(item => (long)item.engineLease)),
                RaceCost("Tires", RaceExpenses.TiresBase, chargedExpenses.Sum(item => (long)item.tires)),
                RaceCost("Parts and Maintenance", RaceExpenses.WearAndMaterialsBase,
                    chargedExpenses.Sum(item => (long)item.wearAndMaterials) + season.componentRepairs),
                new Row("Consumables (est.)", 0, 0),
                RaceCost("Crash Damage (est.)", CrashDamagePerEntry, CrashDamagePerEntry * entered),
                new Row("R&D", null, -season.componentUpgrades)),
            RaceCost("Travel and Logistics", RaceExpenses.TravelAndLogisticsBase, chargedExpenses.Sum(item => (long)item.travelAndLogistics)),
            RaceCost("Hospitality (est.)", HospitalityPerEntry, HospitalityPerEntry * entered),
            new Row("Facility Maintenance (est.)", null, -FacilityMaintenancePerSeason));

        var payments = completed.SelectMany(race => race.sponsorPayments ?? Enumerable.Empty<SponsorRacePayment>()).ToArray();
        var sponsors = new List<Row>();
        for (int slot = 0; slot < Sponsorships.Slots; slot++)
        {
            var historical = payments.Where(payment => payment.slot == slot)
                .GroupBy(payment => payment.companyId).ToDictionary(group => group.Key, group => group.Sum(payment => payment.amount));
            var contract = career.sponsors?.Find(item => item.slot == slot);
            if (contract != null)
                historical.TryAdd(contract.offer.companyId, 0L);
            string slotName = $"Sponsor {slot + 1}";
            var companies = historical.Select(item =>
                new Row(Sponsorships.CompanyName(item.Key), Average(item.Value), item.Value)).ToArray();
            if (companies.Length == 0)
                sponsors.Add(new Row($"{slotName} · Open", Average(0), 0));
            else if (companies.Length == 1)
                sponsors.Add(new Row($"{slotName} · {companies[0].Name}", companies[0].PerRace, companies[0].SeasonTotal));
            else
                sponsors.Add(new Row(slotName, companies));
        }
        long earlierIncome = season.sponsorIncome - payments.Sum(payment => payment.amount);
        if (earlierIncome != 0)
            sponsors.Add(new Row("Earlier sponsorship payments", Average(earlierIncome), earlierIncome));

        var revenue = new Row("Revenue",
            new Row("Sponsorship", sponsors.ToArray()),
            new Row("Event Prize Money", Average(winnings), winnings),
            new Row("Manufacturer Support (est.)", null, ManufacturerSupportPerSeason),
            new Row("Merchandising (est.)", null, MerchandisingPerSeason),
            new Row("Vehicle Sales", null, season.carSales));

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

        toggle.AddToClassList("nav-button");
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
