namespace CowColonySim.Sim.Time;

public enum SimSpeed
{
    Paused = 0,
    Normal = 1,
    Fast = 2,
    VeryFast = 3,
    UltraFast = 6,
}

public sealed class SpeedController
{
    private volatile int _current = (int)SimSpeed.Normal;

    public SimSpeed Current
    {
        get => (SimSpeed)_current;
        set => _current = (int)value;
    }

    public bool IsPaused => Current == SimSpeed.Paused;

    public int Multiplier => (int)Current;

    public int TargetTicksPerSecond => SimConstants.TickRateHz * Multiplier;

    public void Set(SimSpeed speed) => Current = speed;

    public void TogglePause()
    {
        if (Current == SimSpeed.Paused)
        {
            Current = SimSpeed.Normal;
        }
        else
        {
            Current = SimSpeed.Paused;
        }
    }
}
