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
    public int seasonYear;
    public long signingPayment;
    public long perRacePayment;

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
        if (offer.seasonYear > 0)
            return offer.perRacePayment;
        if (!offer.Qualifies(finishPosition))
            return 0;
        if (offer.lumpSum)
            return offer.totalValue - paid;

        int race = offer.duration - racesRemaining + 1;
        return offer.totalValue * race / offer.duration - offer.totalValue * (race - 1) / offer.duration;
    }
}

[Serializable]
internal sealed class SponsorRacePayment
{
    public int slot;
    public int companyId;
    public long amount;
}

internal static class Sponsorships
{
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
    // Replace contracts so callers can restore the previous state if writing the save fails.
    public static SponsorContract Settle(SponsorContract contract,
        int finishPosition, int seasonYear, out long income, out List<SponsorRacePayment> payments)
    {
        income = 0;
        payments = new List<SponsorRacePayment>();
        if (contract?.offer == null || contract.racesRemaining <= 0 ||
            (contract.offer.seasonYear > 0 && contract.offer.seasonYear != seasonYear))
            return null;

        income = contract.Payout(finishPosition);
        if (income != 0)
            payments.Add(new SponsorRacePayment { companyId = contract.offer.companyId, amount = income });
        return contract.racesRemaining > 1 ? new SponsorContract
        {
            offer = contract.offer,
            racesRemaining = contract.racesRemaining - 1,
            paid = contract.paid + income
        } : null;
    }

    public static SponsorOffer[] CreateSeasonOffers(SponsorContract contract, int seasonYear,
        int raceCount, int rank, int fieldSize, Random random, RacingSeries series, Driver driver = null)
    {
        int? signedCompany = contract?.offer?.companyId;
        var companies = Enumerable.Range(0, Brands.Length * Industries.Length).Where(id => id != signedCompany).ToList();
        double standing = fieldSize > 1 ? Math.Clamp((fieldSize - rank) / (double)(fieldSize - 1), 0, 1) : 0;
        double audience = (Math.Clamp(driver?.SocialMediaXp ?? .5f, 0f, 1f) +
            Math.Clamp(driver?.Charisma ?? .5f, 0f, 1f)) / 2d;
        var offers = new SponsorOffer[2];
        for (int i = 0; i < offers.Length; i++)
        {
            int choice = random.Next(companies.Count);
            double variation = 0.9 + random.NextDouble() * 0.2;
            long signing = OfferAmount(series.minimumSponsorshipSigning, series.maximumSponsorshipSigning, variation);
            long perRace = OfferAmount(series.minimumSponsorshipPerRace, series.maximumSponsorshipPerRace, 2 - variation);
            offers[i] = new SponsorOffer
            {
                companyId = companies[choice],
                seasonYear = seasonYear,
                duration = raceCount,
                signingPayment = signing,
                perRacePayment = perRace,
                totalValue = signing + perRace * raceCount
            };
            companies.RemoveAt(choice);
        }
        return offers;

        long OfferAmount(long min, long max, double variation)
        {
            double baseAmount = Math.Clamp((min + (max - min) * standing) * variation, min, max);
            double adjustment = (audience - .5d) * .4d;
            double room = adjustment >= 0 ? max - baseAmount : baseAmount - min;
            return (long)Math.Round(baseAmount + adjustment * room);
        }
    }
}
