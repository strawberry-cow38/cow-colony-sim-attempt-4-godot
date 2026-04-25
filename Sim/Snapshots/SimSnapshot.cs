using CowColonySim.Sim.Time;

namespace CowColonySim.Sim.Snapshots;

public sealed record SimSnapshot(
    long TickNumber,
    DateTime GameTime,
    double DayFraction,
    int DayIndex,
    SimSpeed Speed)
{
    public static SimSnapshot Empty { get; } = new(
        TickNumber: 0,
        GameTime: CalendarConstants.Epoch,
        DayFraction: GameClock.DayFraction(0),
        DayIndex: 0,
        Speed: SimSpeed.Normal);

    public static SimSnapshot FromTick(long tickNumber, SimSpeed speed) => new(
        TickNumber: tickNumber,
        GameTime: GameClock.ToDateTime(tickNumber),
        DayFraction: GameClock.DayFraction(tickNumber),
        DayIndex: GameClock.DayIndex(tickNumber),
        Speed: speed);
}
