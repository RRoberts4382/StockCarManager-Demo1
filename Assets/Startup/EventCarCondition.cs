using System;

[Serializable]
internal sealed class EventCarCondition
{
    public string chassis;
    public float damage;
    public float[] wear;
    public float[] temperature;
    public float[] coldPressures;

    public float Wear(int tire) => wear?.Length == 4 ? wear[tire] : 0f;
    public float Temperature(int tire) => temperature?.Length == 4 ? temperature[tire] : 0f;

    public void Apply(TeamCar car)
    {
        car.ResetTrackCondition();
        car.Damage = damage;
        for (int i = 0; i < 4; i++)
        {
            car.GetTire(i).wear = Wear(i);
            car.GetTire(i).temperature = Temperature(i);
            car.GetTire(i).coldPressurePsi = coldPressures?.Length == 4 ? coldPressures[i] : 0f;
        }
    }

    public static EventCarCondition Capture(TeamCar car, float damage)
    {
        var condition = new EventCarCondition
        {
            chassis = car.chassisNumber, damage = damage, wear = new float[4], temperature = new float[4], coldPressures = new float[4]
        };
        for (int i = 0; i < 4; i++)
        {
            condition.wear[i] = car.GetTire(i).wear;
            condition.temperature[i] = car.GetTire(i).temperature;
            condition.coldPressures[i] = car.GetTire(i).coldPressurePsi;
        }
        return condition;
    }
}
