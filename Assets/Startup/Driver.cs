using System;
using UnityEngine;

[Serializable]
internal sealed class PersonalityTraits
{
    [Range(0f, 1f)] public float aggression = 0.5f;
}

[Serializable]
internal sealed class Driver
{
    public string firstName;
    public string lastName;
    public int age;
    public string gender;
    public string hometownCity;
    public string hometownState;
    public long annualSalary;
    public PersonalityTraits personalityTraits = new PersonalityTraits();

    public string FullName => (firstName + " " + lastName).Trim();

    public static Driver Create(System.Random random, string name, string city = null, string state = null) =>
        FromEmployee(Employee.Create(EmployeeRole.Driver, random, name, city, state), random);

    public static Driver FromEmployee(Employee employee, System.Random random) => new Driver
    {
        firstName = employee.firstName,
        lastName = employee.lastName,
        age = employee.age,
        gender = employee.gender,
        hometownCity = employee.hometownCity,
        hometownState = employee.hometownState,
        annualSalary = employee.annualSalary,
        personalityTraits = new PersonalityTraits { aggression = (float)random.NextDouble() }
    };
}
