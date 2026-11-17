using System;
using System.Collections.Generic;

[Serializable]
internal sealed class EngineSupplier
{
    public string name;
    public CarManufacturer manufacturer;
    public float power;
    public float reliability;
    public int maintenancePerRace;

    public static Dictionary<CarManufacturer, EngineSupplier[]> CreateChoices(Random random) => new()
    {
        [CarManufacturer.Ford] = Complete(CarManufacturer.Ford, random,
            ("Roush-Yates Racing Engines", 0.97f, 0.94f)),
        [CarManufacturer.Chevrolet] = Complete(CarManufacturer.Chevrolet, random,
            ("Hendrick Racing Engines", 0.99f, 0.93f), ("RCR Engines", 0.94f, 0.96f)),
        [CarManufacturer.Dodge] = Complete(CarManufacturer.Dodge, random,
            ("RCR Engines", 0.94f, 0.95f)),
        [CarManufacturer.Toyota] = Complete(CarManufacturer.Toyota, random,
            ("TRD Racing Engines", 0.96f, 0.98f))
    };

    private static EngineSupplier[] Complete(CarManufacturer manufacturer, Random random,
        params (string Name, float Power, float Reliability)[] established)
    {
        var choices = new EngineSupplier[3];
        for (int i = 0; i < established.Length; i++)
        {
            var supplier = established[i];
            choices[i] = Create(manufacturer, supplier.Name, supplier.Power, supplier.Reliability, 60_000);
        }

        string[] names = manufacturer switch
        {
            CarManufacturer.Ford => new[] { "Piedmont Competition Engines", "Red Oak Motor Works" },
            CarManufacturer.Chevrolet => new[] { "Blue Ridge Race Engines" },
            CarManufacturer.Dodge => new[] { "Iron Bend Racing Engines", "Catawba Performance" },
            _ => new[] { "Oakline Power Systems", "Summit Creek Engine Works" }
        };
        for (int i = 0; i < names.Length; i++)
        {
            choices[established.Length + i] = Create(manufacturer, names[i],
                random.Next(73, 89) / 100f, random.Next(73, 89) / 100f,
                random.Next(40_000 + i * 9_000, 49_000 + i * 9_000));
        }
        return choices;
    }

    private static EngineSupplier Create(CarManufacturer manufacturer, string name, float power, float reliability, int cost) =>
        new EngineSupplier
        {
            manufacturer = manufacturer, name = name, power = power, reliability = reliability, maintenancePerRace = cost
        };
}
