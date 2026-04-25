using CowColonySim.Sim.Climate;
using CowColonySim.Sim.Time;

namespace CowColonySim.Sim.Snapshots;

public sealed record SimSnapshot(
    long TickNumber,
    DateTime GameTime,
    double DayFraction,
    int DayIndex,
    SimSpeed Speed,
    ClimateSnapshot Climate)
{
    public static SimSnapshot Empty { get; } = new(
        TickNumber: 0,
        GameTime: CalendarConstants.Epoch,
        DayFraction: GameClock.DayFraction(0),
        DayIndex: 0,
        Speed: SimSpeed.Normal,
        Climate: ClimateSnapshot.Empty);

    public static SimSnapshot FromTick(long tickNumber, SimSpeed speed, ClimateSnapshot climate) => new(
        TickNumber: tickNumber,
        GameTime: GameClock.ToDateTime(tickNumber),
        DayFraction: GameClock.DayFraction(tickNumber),
        DayIndex: GameClock.DayIndex(tickNumber),
        Speed: speed,
        Climate: climate);
}
