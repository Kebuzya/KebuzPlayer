namespace KebuzPlayer.Infrastructure;

public static class RuPlural
{
    public static string Tracks(int n) => $"{n} {Form(n, "трек", "трека", "треков")}";

    public static string Form(int n, string one, string few, string many)
    {
        int abs = Math.Abs(n);
        int mod100 = abs % 100;
        int mod10  = abs % 10;
        if (mod100 is >= 11 and <= 14) return many;
        if (mod10 == 1) return one;
        if (mod10 is >= 2 and <= 4) return few;
        return many;
    }
}
