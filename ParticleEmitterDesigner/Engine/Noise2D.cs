namespace ParticleEmitterDesigner.Engine;

/// <summary>
/// Lightweight, allocation-free 2D Perlin noise. The permutation table is built
/// once from a fixed seed so results are deterministic (important for the
/// sprite-sheet baker).
/// </summary>
public static class Noise2D
{
    private const int TableSize = 256;
    private const int TableMask = TableSize - 1;

    // 8 unit gradient directions (Perlin's improved gradient set).
    private static readonly double[,] Gradients =
    {
        { 1, 1 }, { -1, 1 }, { 1, -1 }, { -1, -1 },
        { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 }
    };

    private static readonly int[] Perm = new int[TableSize * 2];

    static Noise2D()
    {
        var values = new int[TableSize];
        for (int i = 0; i < TableSize; i++)
        {
            values[i] = i;
        }

        // Deterministic Fisher-Yates shuffle.
        var random = new Random(1337);
        for (int i = TableSize - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }

        for (int i = 0; i < TableSize * 2; i++)
        {
            Perm[i] = values[i & TableMask];
        }
    }

    /// <summary>Samples the noise field. Returns roughly [-1, 1].</summary>
    public static double Sample(double x, double y)
    {
        int xi = (int)Math.Floor(x) & TableMask;
        int yi = (int)Math.Floor(y) & TableMask;

        double xf = x - Math.Floor(x);
        double yf = y - Math.Floor(y);

        double u = Fade(xf);
        double v = Fade(yf);

        int aa = Perm[Perm[xi] + yi];
        int ab = Perm[Perm[xi] + yi + 1];
        int ba = Perm[Perm[xi + 1] + yi];
        int bb = Perm[Perm[xi + 1] + yi + 1];

        double x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
        double x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);

        return Lerp(x1, x2, v);
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double Grad(int hash, double x, double y)
    {
        int h = hash & 7;
        return Gradients[h, 0] * x + Gradients[h, 1] * y;
    }
}