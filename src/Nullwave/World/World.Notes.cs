using Nullwave.Audio;
using Nullwave.Core;

namespace Nullwave.World;

/// <summary>
/// In-key guns: kills ring out as notes of the chord that is playing (a rising arpeggio as your
/// chain grows, landing on the next sixteenth note) and hits tick softly in key.
/// </summary>
public sealed partial class World
{
    private int _harmonyRoot = -1;
    private bool _harmonyMinor;
    private int _keyRoot;
    private float _hitNoteCooldown;

    private void CacheHarmony(AudioState audio)
    {
        _harmonyRoot = audio.ChordRoot;
        _harmonyMinor = audio.ChordMinor;
        _keyRoot = audio.Key >= 0 ? audio.Key % 12 : 0;
        _hitNoteCooldown -= 1f / 60f;
    }

    /// <summary>Chord tone <paramref name="degree"/> (0 root, 1 third, 2 fifth, 3 octave) as a MIDI note near C5.</summary>
    public static int ChordTone(int root, bool minor, int degree, int octaveShift)
    {
        int[] steps = minor ? [0, 3, 7, 12] : [0, 4, 7, 12];
        int r = root < 0 ? 0 : root;
        return 72 + r + steps[((degree % 4) + 4) % 4] + 12 * octaveShift - (r >= 6 ? 12 : 0);
    }

    /// <summary>Seconds until the next sixteenth note on the beat clock.</summary>
    private float DelayToSixteenth()
    {
        double sixteenths = _beatPos * 4.0;
        double next = Math.Ceiling(sixteenths + 1e-6);
        float d = (float)((next - sixteenths) / 4.0 * 60.0 / Math.Max(40f, Bpm));
        return d < 0.012f ? 0f : d;
    }

    private void PlayKillNote(Enemy e)
    {
        if (Sfx == null || Demo || !GameSettings.Current.InKeySounds) return;
        int root = _harmonyRoot >= 0 ? _harmonyRoot : _keyRoot;
        float pan = M.Clamp((e.Pos.X - Player.Pos.X) / 700f, -1f, 1f) * 0.5f;
        float delay = DelayToSixteenth();
        if (e.Boss)
        {
            // a full chord arpeggio for a boss
            for (int k = 0; k < 4; k++) Sfx.PlayNote(ChordTone(root, _harmonyMinor, k, 0), 0.24f, pan, delay + k * 0.06f);
            return;
        }
        int step = Math.Max(0, Combo);
        Sfx.PlayNote(ChordTone(root, _harmonyMinor, step, Math.Min(2, step / 8)), 0.2f, pan, delay);
    }

    private void PlayHitNote()
    {
        if (Sfx == null || Demo || _hitNoteCooldown > 0f || !GameSettings.Current.InKeySounds) return;
        _hitNoteCooldown = 0.09f;
        int root = _harmonyRoot >= 0 ? _harmonyRoot : _keyRoot;
        Sfx.PlayNote(ChordTone(root, _harmonyMinor, (int)(Time * 7f) % 3, -1), 0.07f, 0f, 0f, 1);
    }
}
