using UnityEngine;
using UnityEngine.Rendering;

public sealed partial class TrackPreview
{
    private ParticleSystem tireSmoke;
    private ParticleSystem engineSmoke;
    private Material smokeMaterial;
    private float[] engineSmokeAge;
    private int[] tireSmokeCooldown;
    private int smokeStep;
    private uint smokeSeed;

    private ParticleSystem playerFire;
    private float playerFireRemaining;
    private const float PlayerFireSeconds = 7f;

    private Vector3 EngineSmokeOrigin(int index)
    {
        var frontAxle = raceWheels[index]?.FrontAxleCenter ??
            Vector3.forward * RearAxleHeading.CenterToRearAxle;
        return new Vector3(0f, .50f, frontAxle.z * .7f);
    }

    private void AdvancePlayerFire(float seconds)
    {
        if (playerFireRemaining <= 0f && playerFire == null)
            return;

        playerFireRemaining = Mathf.Max(0f, playerFireRemaining - seconds);
        bool burning = playerFireRemaining > 0f && raceCamera != null && raceCamera.enabled &&
            cars[0].gameObject.activeInHierarchy && trafficCars?[0]?.InGarage != true &&
            !NorthWilkesboroGarageOffPitRoute;
        if (burning && playerFire == null)
            CreatePlayerFire();
        if (playerFire == null)
            return;

        var emission = playerFire.emission;
        emission.enabled = burning;
        if (burning || playerFire.particleCount > 0)
            playerFire.Simulate(seconds, false, false, false);
    }

    private void CreatePlayerFire()
    {
        var shader = Resources.Load<Shader>("Startup/TrackFire");
        if (shader == null || !shader.isSupported)
            return;

        var material = new Material(shader);
        materials.Add(material);
        var emitter = CreateObject("Player engine fire", cars[0]);
        emitter.SetActive(false);
        emitter.transform.localPosition = EngineSmokeOrigin(0);
        emitter.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        playerFire = emitter.AddComponent<ParticleSystem>();
        var main = playerFire.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 48;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.19f, .38f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.45f, 1.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(.34f, .62f);
        var shape = playerFire.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 17f;
        shape.radius = .14f;
        var emission = playerFire.emission;
        emission.rateOverTime = 48f;
        var fade = playerFire.colorOverLifetime;
        fade.enabled = true;
        fade.color = new Gradient
        {
            alphaKeys = new[] { new GradientAlphaKey(.75f, 0f), new GradientAlphaKey(1f, .18f),
                new GradientAlphaKey(0f, 1f) },
            colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }
        };
        var renderer = emitter.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        emitter.SetActive(true);
    }

    private void ResetTrackSmoke()
    {
        tireSmoke?.Clear();
        engineSmoke?.Clear();
        playerFire?.Clear();
        playerFireRemaining = 0f;
        engineSmokeAge = new float[cars.Length];
        tireSmokeCooldown = new int[cars.Length];
        smokeStep = 0;
        smokeSeed = 0;
    }

    private void SampleTrackSmoke(int index, TrackPitTraffic.Car traffic, bool spinning, bool lockup, bool emit,
        float extremeLooseSlip)
    {
        var car = cars[index];
        string failure = SessionRun.MechanicalFailure(index);
        bool blownEngine = failure == "Blown Engine";
        bool engineFailure = blownEngine || failure == "Overheating Engine";
        if (engineFailure)
            engineSmokeAge[index] += SessionStep;

        if (tireSmokeCooldown[index] > 0)
            tireSmokeCooldown[index]--;

        if (raceCamera == null || !raceCamera.enabled || !car.gameObject.activeSelf || traffic?.InGarage == true ||
            (isQualifying && index != 0) || (index == 0 && NorthWilkesboroGarageOffPitRoute))
            return;

        bool onFire = index == 0 && playerFireRemaining > 0f;
        if (emit && (onFire || engineFailure && engineSmokeAge[index] < (blownEngine ? 9f : 6f)))
        {
            var position = car.TransformPoint(EngineSmokeOrigin(index));
            bool darkSmoke = blownEngine || onFire;
            var drift = Vector3.up * (darkSmoke ? 1.25f : .85f) - car.forward * .8f;
            var color = darkSmoke ? new Color(.38f, .38f, .39f, .54f) : new Color(.80f, .81f, .82f, .46f);
            EmitTrackSmoke(ref engineSmoke, "Engine smoke", position, drift, color, true);
        }

        // Pit traffic has no driven-wheel slip value. Use the first few meters of
        // a moving stall departure as a visual burnout cue.
        bool burnout = traffic != null && traffic.InPit && traffic.Maneuver && !traffic.ArrivalManeuver &&
            traffic.ActualPitSpeed > .5f && traffic.PitDistance - traffic.ManeuverStart < 3f &&
            !SessionRun.Finished(index) && !SessionRun.Retired(index) && !engineFailure;
        bool sliding = spinning && (Mathf.Abs(crashes[index].ForwardSpeed) > 1f ||
            Mathf.Abs(crashes[index].SideSpeed) > 1f || SessionRun.CurrentSpeed(index) > 1f);
        bool looseSmoke = extremeLooseSlip >= .15f;
        if (tireSmokeCooldown[index] > 0 || (!sliding && !lockup && !burnout && !looseSmoke))
            return;
        tireSmokeCooldown[index] = 3 * Mathf.Max(1, sessionSpeed);

        float station = traffic?.TrackDistance ?? sessionDistances[index];
        for (int tire = 0; tire < 4; tire++)
        {
            bool front = tire < 2;
            if (!sliding && (front ? !lockup : !burnout && !looseSmoke))
                continue;

            float side = tire % 2 == 0 ? -1f : 1f;
            Vector3 position;
            if (burnout)
            {
                var contact = raceWheels[index]?.TireContact(front, side) ??
                    new Vector3(side * FormationHalfWheelTrack, 0f, -RearAxleHeading.CenterToRearAxle);
                position = car.TransformPoint(contact);
            }
            else
            {
                float setupYaw = !front && looseSmoke ? looseSetupYawAngles[index] : 0f;
                var contact = SkidTireContact(index, station, front, side, setupYaw);
                if (!contact.HasValue)
                    continue;
                position = transform.TransformPoint(contact.Value);
            }

            var drift = Vector3.up * .6f - car.forward * .6f + car.right * (side * .22f);
            float alpha = !front && looseSmoke && !sliding && !burnout ? .24f * extremeLooseSlip : .56f;
            EmitTrackSmoke(ref tireSmoke, "Tire smoke", position + Vector3.up * .09f, drift,
                new Color(.85f, .85f, .84f, alpha), false);
        }
    }

    private void EmitTrackSmoke(ref ParticleSystem system, string name, Vector3 position,
        Vector3 velocity, Color color, bool engine, float startSize = 0f)
    {
        if (system == null)
        {
            if (smokeMaterial == null)
            {
                var shader = Resources.Load<Shader>("Startup/TrackSmoke");
                if (shader == null || !shader.isSupported)
                    return;

                smokeMaterial = new Material(shader);
                materials.Add(smokeMaterial);
            }

            var emitter = CreateObject(name, transform);
            emitter.SetActive(false);
            system = emitter.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = engine ? 160 : 600;
            main.startLifetime = engine ? new ParticleSystem.MinMaxCurve(1.8f, 2.7f) :
                new ParticleSystem.MinMaxCurve(1.1f, 1.9f);
            main.startSize = engine ? .48f : .52f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .75f, 1f, 3.4f));
            var fade = system.colorOverLifetime;
            fade.enabled = true;
            fade.color = new Gradient
            {
                alphaKeys = new[] { new GradientAlphaKey(.56f, 0f), new GradientAlphaKey(1f, .16f),
                    new GradientAlphaKey(.55f, .55f), new GradientAlphaKey(0f, 1f) },
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }
            };
            var renderer = emitter.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = smokeMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            emitter.SetActive(true);
        }

        var particle = new ParticleSystem.EmitParams
        {
            position = position,
            velocity = velocity,
            startColor = color,
            randomSeed = ++smokeSeed
        };
        if (startSize > 0f)
            particle.startSize = startSize;
        system.Emit(particle, 1);
    }
}
