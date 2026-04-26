using CowColonySim.Sim;
using CowColonySim.Sim.Time;
using Godot;

namespace CowColonySim.Game.Time;

// Drives sun rotation, sun colour/energy, ambient colour, and procedural
// sky colours from the in-game clock. Uses phase keyframes (midnight, dawn,
// noon, dusk) so transitions hold flat through deep night/day and turn
// orange in narrow dawn/dusk windows. 24 in-game hours = one revolution.
public partial class DayNightCycle : Node3D
{
    private const float SunYawDegrees = 35f;

    private SimRuntime _runtime = null!;
    private DirectionalLight3D _sun = null!;
    private Godot.Environment _env = null!;
    private ProceduralSkyMaterial _sky = null!;

    private readonly struct Palette
    {
        public Palette(Color skyTop, Color skyHorizon, Color groundHorizon,
                       Color sunColor, float sunEnergy,
                       Color ambient, float ambientEnergy)
        {
            SkyTop = skyTop;
            SkyHorizon = skyHorizon;
            GroundHorizon = groundHorizon;
            SunColor = sunColor;
            SunEnergy = sunEnergy;
            Ambient = ambient;
            AmbientEnergy = ambientEnergy;
        }
        public readonly Color SkyTop;
        public readonly Color SkyHorizon;
        public readonly Color GroundHorizon;
        public readonly Color SunColor;
        public readonly float SunEnergy;
        public readonly Color Ambient;
        public readonly float AmbientEnergy;

        public static Palette Lerp(Palette a, Palette b, float t) => new(
            a.SkyTop.Lerp(b.SkyTop, t),
            a.SkyHorizon.Lerp(b.SkyHorizon, t),
            a.GroundHorizon.Lerp(b.GroundHorizon, t),
            a.SunColor.Lerp(b.SunColor, t),
            Mathf.Lerp(a.SunEnergy, b.SunEnergy, t),
            a.Ambient.Lerp(b.Ambient, t),
            Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, t));
    }

    private static readonly Palette Night = new(
        skyTop: new Color(0.015f, 0.025f, 0.075f),
        skyHorizon: new Color(0.05f, 0.07f, 0.15f),
        groundHorizon: new Color(0.04f, 0.05f, 0.10f),
        sunColor: new Color(0.55f, 0.65f, 0.85f),
        sunEnergy: 0.05f,
        ambient: new Color(0.05f, 0.07f, 0.14f),
        ambientEnergy: 0.10f);

    private static readonly Palette Dawn = new(
        skyTop: new Color(0.30f, 0.32f, 0.55f),
        skyHorizon: new Color(1.00f, 0.55f, 0.35f),
        groundHorizon: new Color(0.55f, 0.40f, 0.30f),
        sunColor: new Color(1.00f, 0.55f, 0.30f),
        sunEnergy: 1.6f,
        ambient: new Color(0.55f, 0.45f, 0.45f),
        ambientEnergy: 0.25f);

    private static readonly Palette Day = new(
        skyTop: new Color(0.18f, 0.42f, 0.82f),
        skyHorizon: new Color(0.70f, 0.82f, 0.95f),
        groundHorizon: new Color(0.70f, 0.78f, 0.85f),
        sunColor: new Color(1.00f, 0.97f, 0.92f),
        sunEnergy: 2.5f,
        ambient: new Color(0.55f, 0.60f, 0.70f),
        ambientEnergy: 0.35f);

    private static readonly Palette Dusk = new(
        skyTop: new Color(0.22f, 0.18f, 0.40f),
        skyHorizon: new Color(1.00f, 0.42f, 0.28f),
        groundHorizon: new Color(0.45f, 0.28f, 0.28f),
        sunColor: new Color(1.00f, 0.42f, 0.22f),
        sunEnergy: 1.4f,
        ambient: new Color(0.55f, 0.38f, 0.42f),
        ambientEnergy: 0.22f);

    // Phase keyframes (phase = hour/24). Plateau night/day, narrow dawn/dusk.
    private static readonly (float Phase, Palette P)[] Keys =
    {
        (0.00f, Night),
        (0.20f, Night),
        (0.27f, Dawn),
        (0.40f, Day),
        (0.60f, Day),
        (0.73f, Dusk),
        (0.80f, Night),
        (1.00f, Night),
    };

    public void Configure(SimRuntime runtime, DirectionalLight3D sun,
                          Godot.Environment env, ProceduralSkyMaterial sky)
    {
        _runtime = runtime;
        _sun = sun;
        _env = env;
        _sky = sky;
    }

    public override void _Process(double delta)
    {
        var dt = GameClock.DateTimeAt(_runtime.TickNumber);
        var hours = dt.Hour + dt.Minute / 60f + dt.Second / 3600f;
        var phase = hours / 24f;

        // Sun pitch sweeps continuously through the day; angle is 0 at 06:00
        // (sunrise), π/2 at 12:00 (noon), π at 18:00 (sunset), 3π/2 at 00:00.
        var altitudeAngle = (phase - 0.25f) * Mathf.Tau;
        _sun.Rotation = new Vector3(-altitudeAngle, Mathf.DegToRad(SunYawDegrees), 0f);

        var p = Sample(phase);
        _sun.LightColor = p.SunColor;
        _sun.LightEnergy = p.SunEnergy;
        _env.AmbientLightColor = p.Ambient;
        _env.AmbientLightEnergy = p.AmbientEnergy;
        _sky.SkyTopColor = p.SkyTop;
        _sky.SkyHorizonColor = p.SkyHorizon;
        _sky.GroundHorizonColor = p.GroundHorizon;
    }

    private static Palette Sample(float phase)
    {
        for (var i = 0; i < Keys.Length - 1; i++)
        {
            var a = Keys[i];
            var b = Keys[i + 1];
            if (phase >= a.Phase && phase <= b.Phase)
            {
                var span = b.Phase - a.Phase;
                var t = span <= 0f ? 0f : (phase - a.Phase) / span;
                return Palette.Lerp(a.P, b.P, t);
            }
        }
        return Keys[Keys.Length - 1].P;
    }
}
