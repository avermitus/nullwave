namespace Nullwave.Render;

/// <summary>
/// Remembers which backdrop world each song section had, per song. A section heard again brings
/// its world back; a new section gets a fresh one. Any other world change (act change, key
/// change) re-assigns the current section's world.
/// </summary>
public sealed class SectionWorldMemory
{
    private readonly Dictionary<int, BackdropGenome> _worlds = new();
    private int _epoch = int.MinValue;
    private int _label;

    public int Count => _worlds.Count;
    public bool Active => _epoch != int.MinValue;

    /// <summary>The world to show for a section that just started.</summary>
    public BackdropGenome Choose(int label, bool repeat, int songEpoch, BackdropGenome current, Func<BackdropGenome> fresh)
    {
        if (songEpoch != _epoch) { _worlds.Clear(); _epoch = songEpoch; _label = 0; }
        if (!_worlds.ContainsKey(_label)) _worlds[_label] = current;
        _label = label;
        if (repeat && _worlds.TryGetValue(label, out var known)) return known;
        var next = fresh();
        _worlds[label] = next;
        return next;
    }

    /// <summary>The current section's world was replaced for another reason.</summary>
    public void Replace(BackdropGenome g)
    {
        if (Active) _worlds[_label] = g;
    }
}
