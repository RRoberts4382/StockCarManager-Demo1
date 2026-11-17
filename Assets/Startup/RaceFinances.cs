using System;
using System.Globalization;
using System.Linq;

[Serializable]
internal sealed class RaceExpenses
{
    public const int WearAndMaterialsBase = 50_000;
    public const int EngineLeaseBase = 50_000;
    public const int TravelAndLogisticsBase = 25_000;
    public const int TiresBase = 15_000;
    public const int BaseTotal = WearAndMaterialsBase + EngineLeaseBase + TravelAndLogisticsBase + TiresBase;

    public int wearAndMaterials;
    public int engineLease;
    public int travelAndLogistics;
    public int tires;

    public int Total => wearAndMaterials + engineLease + travelAndLogistics + tires;

    public static RaceExpenses Generate(Random random, EngineSupplier supplier) => new RaceExpenses
    {
        wearAndMaterials = Vary(WearAndMaterialsBase, random),
        engineLease = supplier?.maintenancePerRace ?? Vary(EngineLeaseBase, random),
        travelAndLogistics = Vary(TravelAndLogisticsBase, random),
        tires = Vary(TiresBase, random)
    };

    private static int Vary(int amount, Random random)
    {
        int variation = amount / 10;
        return amount + random.Next(-variation, variation + 1);
    }
}

internal static class RaceFinances
{
    public const long StartingBalance = 100_000;
    private const long StandardPurse = 11_233_037;

    // Cup charter payout weights, in thousandths of a percentage point.
    private static readonly int[] PayoutWeights =
    {
        5160, 4067, 3976, 3885, 3794, 3704, 3613, 3522, 3432, 3341,
        3250, 3165, 3079, 2993, 2908, 2822, 2736, 2651, 2565, 2479,
        2399, 2318, 2237, 2157, 2076, 1995, 1915, 1834, 1753, 1672,
        1597, 1521, 1445, 1370, 1294, 1218, 1143, 1057, 971, 886
    };

    public static long PurseForTrack(float miles)
    {
        // The benchmark is a 1.5-mile race. Each additional mile adds 10% of that benchmark.
        decimal multiplier = 1m + ((decimal)miles - 1.5m) * 0.10m;
        return (long)Math.Round(StandardPurse * multiplier, MidpointRounding.AwayFromZero);
    }

    public static long PayrollInstallment(long annualPayroll, int raceCount, int completedRaces)
    {
        decimal perRace = (decimal)annualPayroll / raceCount;
        return (long)(Math.Round(perRace * completedRaces, MidpointRounding.AwayFromZero) -
            Math.Round(perRace * (completedRaces - 1), MidpointRounding.AwayFromZero));
    }

    public static long[] Payouts(long purse, int fieldSize)
    {
        var payouts = new long[fieldSize];
        if (fieldSize == 0 || purse <= 0)
            return payouts;

        int Weight(int index) => PayoutWeights[Math.Min(index, PayoutWeights.Length - 1)];
        int totalWeight = Enumerable.Range(0, fieldSize).Sum(Weight);
        var remainders = new decimal[fieldSize];
        long remaining = purse;
        for (int i = 0; i < fieldSize; i++)
        {
            decimal share = (decimal)purse * Weight(i) / totalWeight;
            payouts[i] = (long)share;
            remainders[i] = share - payouts[i];
            remaining -= payouts[i];
        }

        foreach (int index in Enumerable.Range(0, fieldSize).OrderByDescending(i => remainders[i]).Take((int)remaining))
            payouts[index]++;
        return payouts;
    }

    public static string Dollars(long amount) => amount.ToString("C0", CultureInfo.GetCultureInfo("en-US"));
}
