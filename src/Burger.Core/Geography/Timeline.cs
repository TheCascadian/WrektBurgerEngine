namespace Burger.Core.Geography;

/// <summary>
/// Values that change on given days. A value holds from its day until the next point. Lookups are binary searches, so a query
/// costs O(log n) however long the history is. Two different values on the same day are a conflict and are refused.
/// </summary>
public sealed class Timeline<T>
{
    private readonly List<(int Day, T Value)> _points = new();
    public int Count => _points.Count;
    public IReadOnlyList<(int Day, T Value)> Points => _points;
    public int FirstDay => _points.Count == 0 ? HistoricalCalendar.Forever : _points[0].Day;
    public int LastDay => _points.Count == 0 ? HistoricalCalendar.Beginning : _points[^1].Day;

    /// <summary>Adds a point. Returns false (and changes nothing) if the day already holds a different value.</summary>
    public bool TrySet(int day, T value)
    {
        int i = IndexAtOrBefore(day);
        if (i >= 0 && _points[i].Day == day)
            return EqualityComparer<T>.Default.Equals(_points[i].Value, value);
        _points.Insert(i + 1, (day, value));
        return true;
    }

    /// <summary>Replaces whatever the day holds.</summary>
    public void Force(int day, T value)
    {
        int i = IndexAtOrBefore(day);
        if (i >= 0 && _points[i].Day == day) _points[i] = (day, value);
        else _points.Insert(i + 1, (day, value));
    }

    public bool TryAt(int day, out T value)
    {
        int i = IndexAtOrBefore(day);
        if (i < 0) { value = default!; return false; }
        value = _points[i].Value;
        return true;
    }

    public bool HasPointOn(int day)
    {
        int i = IndexAtOrBefore(day);
        return i >= 0 && _points[i].Day == day;
    }

    public void RemoveFrom(int day) => _points.RemoveAll(p => p.Day >= day);

    private int IndexAtOrBefore(int day)
    {
        int lo = 0, hi = _points.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_points[mid].Day <= day) { found = mid; lo = mid + 1; } else hi = mid - 1;
        }
        return found;
    }
}
