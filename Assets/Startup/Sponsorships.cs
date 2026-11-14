using System;
using System.Collections.Generic;
using System.Linq;

[Serializable]
internal sealed class SponsorOffer
{
    public int companyId;
    public long totalValue;
    public int duration;
    public bool lumpSum;
    public int maxFinishPosition;

    public bool Qualifies(int finishPosition) =>
        finishPosition > 0 && (maxFinishPosition == 0 || finishPosition <= maxFinishPosition);
}

[Serializable]
internal sealed class SponsorContract
{
    public SponsorOffer offer;
    public int slot;
    public int racesRemaining;
    public long paid;

    public long Payout(int finishPosition)
    {
        if (!offer.Qualifies(finishPosition))
            return 0;
        if (offer.lumpSum)
            return offer.totalValue - paid;

        int race = offer.duration - racesRemaining + 1;
        return offer.totalValue * race / offer.duration - offer.totalValue * (race - 1) / offer.duration;
    }
}

internal static class Sponsorships
{
    public const int Slots = 3;

    private static readonly string[] Brands =
    {
        "Aldercrest", "Brindle Peak", "Copper Heron", "Driftmere", "Ember Hollow",
        "Foxglen", "Granite Lark", "Harbor Loom", "Iron Fern", "Juniper Vale",
        "Kestrel Forge", "Lantern Ridge", "Maple Circuit", "North Finch", "Oak Meridian",
        "Pine Lantern", "Quartz Hollow", "Redwillow", "Silver Badger", "Thistle Point"
    };

    private static readonly string[] Industries =
    {
        "Tools", "Freight", "Energy", "Foods", "Wireless", "Outfitters", "Materials", "Beverages",
        "Systems", "Lubricants"
    };

    public static string CompanyName(int id) => Brands[id / Industries.Length] + " " + Industries[id % Industries.Length];
    public static string LogoText(int id) => string.Concat(Brands[id / Industries.Length][0], Industries[id % Industries.Length][0]);
    public static string SlotName(int slot) => slot == 0 ? "Primary" : $"Associate {slot}";

    // Replace contracts so callers can restore the previous state if writing the save fails.
    public static List<SponsorContract> Settle(IEnumerable<SponsorContract> contracts,
        int finishPosition, out long income)
    {
        var remaining = new List<SponsorContract>();
        income = 0;
        foreach (var contract in contracts)
        {
            long payout = contract.Payout(finishPosition);
            income += payout;
            if (contract.racesRemaining > 1)
                remaining.Add(new SponsorContract
                {
                    offer = contract.offer,
                    slot = contract.slot,
                    racesRemaining = contract.racesRemaining - 1,
                    paid = contract.paid + payout
                });
        }
        return remaining;
    }

    public static List<SponsorOffer> CreateOffers(IReadOnlyCollection<SponsorContract> contracts,
        int position, int fieldSize, int remainingRaces, int seasonRaces, Random random)
    {
        var offers = new List<SponsorOffer>();
        if (contracts.Count >= Slots || remainingRaces <= 0)
            return offers;

        var signed = contracts.Select(contract => contract.offer.companyId).ToHashSet();
        var companies = Enumerable.Range(0, Brands.Length * Industries.Length).Where(id => !signed.Contains(id)).ToList();
        double standing = fieldSize > 1 ? Math.Max(0, Math.Min(1, (fieldSize - position) / (double)(fieldSize - 1))) : 0;
        for (int i = 0; i < Slots; i++)
        {
            int choice = random.Next(companies.Count);
            // The first offer matches the standing. The other two trade some value for easier terms.
            double quality = standing * (1 - 0.12 * i);
            int duration = 1 + (int)Math.Round((remainingRaces - 1) * quality);
            double seasonShare = duration / (double)Math.Max(seasonRaces, duration);
            int finishTarget = 10 + (int)Math.Round(10 * (1 - quality) / 0.5);
            offers.Add(new SponsorOffer
            {
                companyId = companies[choice],
                duration = duration,
                totalValue = 5000 + (long)Math.Round(9995000 * quality * seasonShare),
                lumpSum = random.Next(2) == 0,
                maxFinishPosition = quality < 0.5 || finishTarget >= fieldSize ? 0 : finishTarget
            });
            companies.RemoveAt(choice);
        }
        return offers;
    }
}
