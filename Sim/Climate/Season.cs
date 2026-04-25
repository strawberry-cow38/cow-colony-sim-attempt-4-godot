namespace CowColonySim.Sim.Climate;

public enum Season : byte
{
    Spring = 0,
    Summer,
    Autumn,
    Winter,
}

public static class SeasonHelper
{
    // Northern meteorological seasons by month: Spring=Mar-May, Summer=Jun-Aug,
    // Autumn=Sep-Nov, Winter=Dec-Feb. Southern hemisphere flips.
    public static Season FromDate(DateTime gameTime, double latitude)
    {
        var month = gameTime.Month;
        var northern = NorthernByMonth(month);
        return latitude < 0 ? Flip(northern) : northern;
    }

    private static Season NorthernByMonth(int month) => month switch
    {
        3 or 4 or 5 => Season.Spring,
        6 or 7 or 8 => Season.Summer,
        9 or 10 or 11 => Season.Autumn,
        _ => Season.Winter,
    };

    private static Season Flip(Season s) => s switch
    {
        Season.Spring => Season.Autumn,
        Season.Summer => Season.Winter,
        Season.Autumn => Season.Spring,
        Season.Winter => Season.Summer,
        _ => s,
    };
}
