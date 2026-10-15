namespace KebuzPlayer.Infrastructure;

public sealed class LaunchBatcher
{
    public static readonly TimeSpan Gap = TimeSpan.FromMilliseconds(150);

    private DateTime _first = DateTime.MinValue;
    private DateTime _last  = DateTime.MinValue;

    public bool Joins(DateTime startedUtc)
    {
        bool joins = _first != DateTime.MinValue
                     && startedUtc >= _first - Gap
                     && startedUtc <= _last + Gap;
        if (joins)
        {
            if (startedUtc < _first) _first = startedUtc;
            if (startedUtc > _last) _last = startedUtc;
        }
        else
        {
            _first = _last = startedUtc;
        }
        return joins;
    }
}
