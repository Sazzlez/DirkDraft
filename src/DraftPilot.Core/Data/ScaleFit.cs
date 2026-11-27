namespace DraftPilot.Core.Data;

/// <summary>
/// The two fits behind the scales the export measures (<see cref="LaneScaleModel"/>,
/// <see cref="TeamfightScaleModel"/>), the same as the measurement tools' (app/tools/measure): a
/// logistic regression without a side, and champion strengths drawn to 0 on top of it.
/// </summary>
internal static class ScaleFit
{
    /// <summary>
    /// Logistic regression without intercept on the rows and their mirror images (−x, 1 − y): Newton
    /// steps; the mirror gives the same weight and twice the gradient, so it needs no copy.
    /// </summary>
    public static double[] MirroredLogistic(IReadOnlyList<double[]> x, IReadOnlyList<double> y, int columns)
    {
        var beta = new double[columns];
        if (x.Count == 0)
            return beta;
        for (var iteration = 0; iteration < 50; iteration++)
        {
            var gradient = new double[columns];
            var hessian = new double[columns, columns];
            for (var i = 0; i < x.Count; i++)
            {
                var row = x[i];
                var z = 0.0;
                for (var j = 0; j < columns; j++)
                    z += beta[j] * row[j];
                var p = Sigmoid(z);
                var w = 2 * p * (1 - p);
                var r = 2 * (y[i] - p);
                for (var a = 0; a < columns; a++)
                {
                    gradient[a] += r * row[a];
                    for (var b = a; b < columns; b++)
                        hessian[a, b] += w * row[a] * row[b];
                }
            }
            for (var a = 0; a < columns; a++)
            {
                for (var b = 0; b < a; b++)
                    hessian[a, b] = hessian[b, a];
                // A column that never varies must not break the solve.
                hessian[a, a] += 1e-6;
            }
            var step = Solve(hessian, gradient);
            var largest = 0.0;
            for (var j = 0; j < columns; j++)
            {
                beta[j] += step[j];
                largest = Math.Max(largest, Math.Abs(step[j]));
            }
            if (largest < 1e-9)
                break;
        }
        return beta;
    }

    /// <summary>
    /// Strengths on top of an offset: coordinate-wise Newton steps, each drawn to 0 with spread
    /// <paramref name="tau"/> (as <c>fitEffects</c> in app/tools/measure/duels.ts). A row's keys in
    /// <c>Plus</c> speak for its side, those in <c>Minus</c> against.
    /// </summary>
    public static double[] RidgeStrengths(int keys, IReadOnlyList<(int[] Plus, int[] Minus, double Y)> rows, double[] offset, double tau, int sweeps = 10)
    {
        var theta = new double[keys];
        var z = (double[])offset.Clone();
        var members = new List<(int Row, int Sign)>[keys];
        for (var k = 0; k < keys; k++)
            members[k] = [];
        for (var i = 0; i < rows.Count; i++)
        {
            foreach (var k in rows[i].Plus)
                members[k].Add((i, 1));
            foreach (var k in rows[i].Minus)
                members[k].Add((i, -1));
        }

        var penalty = 1 / (tau * tau);
        for (var sweep = 0; sweep < sweeps; sweep++)
        {
            for (var k = 0; k < keys; k++)
            {
                if (members[k].Count == 0)
                    continue;
                var gradient = -theta[k] * penalty;
                var hessian = penalty;
                foreach (var (i, sign) in members[k])
                {
                    var p = Sigmoid(z[i]);
                    gradient += sign * (rows[i].Y - p);
                    hessian += p * (1 - p);
                }
                var step = gradient / hessian;
                theta[k] += step;
                foreach (var (i, sign) in members[k])
                    z[i] += sign * step;
            }
        }
        return theta;
    }

    public static double Sigmoid(double z) => 1 / (1 + Math.Exp(-z));

    /// <summary>Gaussian elimination with partial pivoting; a singular system gives no step.</summary>
    private static double[] Solve(double[,] matrix, double[] vector)
    {
        var n = vector.Length;
        var a = (double[,])matrix.Clone();
        var b = (double[])vector.Clone();
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
            {
                if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col]))
                    pivot = r;
            }
            if (Math.Abs(a[pivot, col]) < 1e-300)
                return new double[n];
            if (pivot != col)
            {
                for (var c = 0; c < n; c++)
                    (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                (b[col], b[pivot]) = (b[pivot], b[col]);
            }
            for (var r = col + 1; r < n; r++)
            {
                var factor = a[r, col] / a[col, col];
                if (factor == 0)
                    continue;
                for (var c = col; c < n; c++)
                    a[r, c] -= factor * a[col, c];
                b[r] -= factor * b[col];
            }
        }
        var x = new double[n];
        for (var r = n - 1; r >= 0; r--)
        {
            var sum = b[r];
            for (var c = r + 1; c < n; c++)
                sum -= a[r, c] * x[c];
            x[r] = sum / a[r, r];
        }
        return x;
    }
}
