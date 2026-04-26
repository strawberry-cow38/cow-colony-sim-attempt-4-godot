using CowColonySim.Sim.Time;

namespace CowColonySim.Sim.Lighting;

// Maps the current tick onto a 0..1 sun-light fraction. Plateaus at 0
// through deep night and at 1 through deep day; ramps in narrow dawn
// and dusk windows. Phase boundaries match the colour keyframes used
// by Game/Time/DayNightCycle so the visual sky and the gameplay light
// value cross zero/one at the same instant.
public static class SunCurve
{
    private const float NightEnd = 0.20f;   // 04:48 — sun starts rising
    private const float DayStart = 0.30f;   // 07:12 — sun fully up
    private const float DayEnd   = 0.70f;   // 16:48 — sun starts setting
    private const float NightStart = 0.80f; // 19:12 — sun fully down

    public static float Phase(long tickNumber)
    {
        var seconds = GameClock.InGameSecondsAt(tickNumber);
        var dayFraction = (seconds / 86400.0) % 1.0;
        if (dayFraction < 0) dayFraction += 1.0;
        // Game epoch is 06:00, so add 0.25 to align phase=0 with midnight.
        var phase = (dayFraction + 0.25) % 1.0;
        return (float)phase;
    }

    public static float FractionAtPhase(float phase)
    {
        if (phase <= NightEnd) return 0f;
        if (phase < DayStart) return Smoothstep((phase - NightEnd) / (DayStart - NightEnd));
        if (phase <= DayEnd) return 1f;
        if (phase < NightStart) return 1f - Smoothstep((phase - DayEnd) / (NightStart - DayEnd));
        return 0f;
    }

    public static float FractionAtTick(long tickNumber) => FractionAtPhase(Phase(tickNumber));

    private static float Smoothstep(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        return t * t * (3f - 2f * t);
    }
}
