using System.Numerics;

namespace Nullwave.Core;

public sealed class Rng
{
    private ulong _state;

    public Rng(ulong seed)
    {
        _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        for (int i = 0; i < 4; i++) NextULong();
    }

    public ulong State => _state;

    public ulong NextULong()
    {
        ulong x = _state;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        _state = x;
        return x * 2685821657736338717UL;
    }

    public uint NextUInt() => (uint)(NextULong() >> 32);

    public float NextFloat() => (NextULong() >> 40) * (1f / 16777216f);

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    public int Range(int min, int maxExclusive)
    {
        if (maxExclusive <= min) return min;
        return min + (int)(NextULong() % (ulong)(maxExclusive - min));
    }

    public bool Chance(float probability) => NextFloat() < probability;

    public float Signed() => NextFloat() * 2f - 1f;

    public Vector2 InCircle(float radius)
    {
        float a = NextFloat() * M.Tau;
        float r = MathF.Sqrt(NextFloat()) * radius;
        return new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
    }

    public Vector2 InRing(float min, float max)
    {
        float a = NextFloat() * M.Tau;
        float r = M.Lerp(min, max, NextFloat());
        return new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
    }

    public T Pick<T>(IReadOnlyList<T> list) => list[Range(0, list.Count)];

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public float Weighted(params float[] weights)
    {
        float sum = 0;
        for (int i = 0; i < weights.Length; i++) sum += MathF.Max(0, weights[i]);
        if (sum <= 0) return 0;
        float r = NextFloat() * sum;
        for (int i = 0; i < weights.Length; i++)
        {
            r -= MathF.Max(0, weights[i]);
            if (r <= 0) return i;
        }
        return weights.Length - 1;
    }

    public Rng Fork(ulong salt) => new(NextULong() ^ salt * 0x9E3779B97F4A7C15UL);
}

public static class SeedCode
{
    public const int CodeLength = 8;
    private const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>
    /// Normalizes a character to the 34-char seed alphabet (0-9, A-Z with I-&gt;1 and O-&gt;0).
    /// Returns '\0' if the character is not valid.
    /// </summary>
    public static char NormalizeChar(char ch)
    {
        ch = char.ToUpperInvariant(ch);
        if (ch == 'I') ch = '1';
        else if (ch == 'O') ch = '0';
        return Alphabet.IndexOf(ch) >= 0 ? ch : '\0';
    }

    public static string NormalizeCode(string? raw) =>
        TryParse(raw, out _, out string canonical) ? canonical : "";

    public static bool TryParse(string? raw, out ulong seed, out string canonicalCode)
    {
        seed = 0;
        canonicalCode = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        string s = raw.Trim().ToUpperInvariant();
        if (s.StartsWith("NW-", StringComparison.Ordinal)) s = s[3..];
        Span<char> cleaned = stackalloc char[CodeLength];
        int count = 0;
        foreach (char c in s)
        {
            char norm = NormalizeChar(c);
            if (norm == '\0') continue;
            cleaned[count++] = norm;
            if (count == CodeLength) break;
        }
        if (count == 0) return false;
        ulong v = 0;
        for (int i = 0; i < count; i++)
        {
            int idx = Alphabet.IndexOf(cleaned[i]);
            v = v * (ulong)Alphabet.Length + (ulong)idx;
        }
        seed = v == 0 ? 1UL : v;
        canonicalCode = ToCode(seed);
        return true;
    }

    public static string ToCode(ulong seed)
    {
        Span<char> buf = stackalloc char[9];
        ulong v = seed;
        for (int i = 7; i >= 0; i--)
        {
            buf[i] = Alphabet[(int)(v % (ulong)Alphabet.Length)];
            v /= (ulong)Alphabet.Length;
        }
        return new string(buf[..8]);
    }

    public static ulong FromCode(string? code) =>
        TryParse(code, out ulong seed, out _) ? seed : 1UL;

    public static ulong NewSeed()
    {
        Span<byte> bytes = stackalloc byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        ulong v = BitConverter.ToUInt64(bytes);
        return v == 0 ? 0xC0FFEEUL : v;
    }

    public static ulong DailySeed(DateTime date)
    {
        ulong v = (ulong)(date.Year * 10000 + date.Month * 100 + date.Day);
        v ^= 0xA5A5F00DDEADBEEFUL;
        v *= 0x9E3779B97F4A7C15UL;
        v ^= v >> 29;
        return v == 0 ? 1 : v;
    }

    public static string DailyLabel(DateTime date) => date.ToString("yyyy.MM.dd");
}
