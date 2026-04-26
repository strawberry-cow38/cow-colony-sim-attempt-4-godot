using CowColonySim.Sim;
using CowColonySim.Sim.Time;
using Godot;

namespace CowColonySim.Game.Time;

// Drives sun rotation, sun energy, and ambient colour from the in-game
// clock. 24 in-game hours = full revolution of the sun. Noon → sun straight
// down (-Y); midnight → sun straight up (+Y). Energy + ambient lerp from
// daytime values to night values across a soft dawn/dusk window.
public partial class DayNightCycle : Node3D
{
    private const float SunYawDegrees = 35f;
    private const float DaySunEnergy = 2.5f;
    private const float NightSunEnergy = 0.05f;

    private static readonly Color DayAmbient = new(0.55f, 0.6f, 0.7f);
    private static readonly Color NightAmbient = new(0.04f, 0.05f, 0.10f);
    private const float DayAmbientEnergy = 0.35f;
    private const float NightAmbientEnergy = 0.08f;

    private SimRuntime _runtime = null!;
    private DirectionalLight3D _sun = null!;
    private Godot.Environment _env = null!;

    public void Configure(SimRuntime runtime, DirectionalLight3D sun, Godot.Environment env)
    {
        _runtime = runtime;
        _sun = sun;
        _env = env;
    }

    public override void _Process(double delta)
    {
        var dt = GameClock.DateTimeAt(_runtime.TickNumber);
        var hours = dt.Hour + dt.Minute / 60f + dt.Second / 3600f;
        var phase = hours / 24f;

        // altitudeAngle: 0 at 06:00, π/2 at 12:00 (noon), π at 18:00, 3π/2 at 00:00.
        var altitudeAngle = (phase - 0.25f) * Mathf.Tau;
        var pitch = -altitudeAngle;
        _sun.Rotation = new Vector3(pitch, Mathf.DegToRad(SunYawDegrees), 0f);

        // Sun height above horizon, -1..1.
        var altitude = MathF.Sin(altitudeAngle);
        var dayFactor = Mathf.Clamp((altitude + 0.1f) / 0.4f, 0f, 1f);

        _sun.LightEnergy = Mathf.Lerp(NightSunEnergy, DaySunEnergy, dayFactor);
        _env.AmbientLightColor = NightAmbient.Lerp(DayAmbient, dayFactor);
        _env.AmbientLightEnergy = Mathf.Lerp(NightAmbientEnergy, DayAmbientEnergy, dayFactor);
    }
}
