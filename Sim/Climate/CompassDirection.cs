namespace CowColonySim.Sim.Climate;

public enum CompassDirection : byte
{
    N = 0,
    NE,
    E,
    SE,
    S,
    SW,
    W,
    NW,
}

public static class CompassHelper
{
    // Wind direction convention: degrees indicate the direction the wind is
    // coming FROM, measured clockwise from north. 0 = from N, 90 = from E.
    public static CompassDirection FromDegrees(double degrees)
    {
        var d = ((degrees % 360.0) + 360.0) % 360.0;
        var sector = (int)Math.Floor((d + 22.5) / 45.0) % 8;
        return (CompassDirection)sector;
    }
}
