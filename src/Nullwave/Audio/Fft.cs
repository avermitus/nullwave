namespace Nullwave.Audio;

public sealed class Fft
{
    private readonly int _n;
    private readonly float[] _window;
    private readonly float[] _re;
    private readonly float[] _im;
    private readonly int[] _reverse;

    public Fft(int n)
    {
        _n = n;
        _window = new float[n];
        _re = new float[n];
        _im = new float[n];
        _reverse = new int[n];
        for (int i = 0; i < n; i++)
            _window[i] = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / (n - 1));
        int bits = 0;
        while ((1 << bits) < n) bits++;
        for (int i = 0; i < n; i++)
        {
            int rev = 0;
            int v = i;
            for (int b = 0; b < bits; b++)
            {
                rev = (rev << 1) | (v & 1);
                v >>= 1;
            }
            _reverse[i] = rev;
        }
    }

    public int Size => _n;

    public void Magnitudes(ReadOnlySpan<float> input, float[] output)
    {
        int n = _n;
        for (int i = 0; i < n; i++)
        {
            _re[i] = (i < input.Length ? input[i] : 0f) * _window[i];
            _im[i] = 0f;
        }

        for (int i = 0; i < n; i++)
        {
            int j = _reverse[i];
            if (j > i)
            {
                (_re[i], _re[j]) = (_re[j], _re[i]);
                (_im[i], _im[j]) = (_im[j], _im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            float wLenR = (float)Math.Cos(angle);
            float wLenI = (float)Math.Sin(angle);
            for (int i = 0; i < n; i += len)
            {
                float wR = 1f, wI = 0f;
                int half = len >> 1;
                for (int j = 0; j < half; j++)
                {
                    int a = i + j;
                    int b = a + half;
                    float uR = _re[a], uI = _im[a];
                    float vR = _re[b] * wR - _im[b] * wI;
                    float vI = _re[b] * wI + _im[b] * wR;
                    _re[a] = uR + vR;
                    _im[a] = uI + vI;
                    _re[b] = uR - vR;
                    _im[b] = uI - vI;
                    float nwR = wR * wLenR - wI * wLenI;
                    wI = wR * wLenI + wI * wLenR;
                    wR = nwR;
                }
            }
        }

        float scale = 2f / n;
        for (int i = 0; i < n / 2; i++)
            output[i] = MathF.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) * scale;
    }
}
