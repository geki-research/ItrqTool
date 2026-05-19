namespace ItrqTool.Tasks.TemplateDiff;

public static class HungarianAlgorithm
{
    /// <summary>
    /// Solves the maximum-weight bipartite assignment problem.
    /// Given a profit matrix where matrix[i,j] is the value
    /// of assigning row i to column j, returns the assignment
    /// that maximizes total profit.
    /// </summary>
    /// <param name="profitMatrix">M rows × N cols. Values are
    /// expected to be in [0.0, 1.0] but the algorithm does
    /// not assume this.</param>
    /// <returns>Array of length M where result[i] = j means
    /// row i is assigned to column j, or -1 if row i has no
    /// assignment (only possible when M > N).</returns>
    public static int[] SolveMaximumAssignment(double[,] profitMatrix)
    {
        int rows = profitMatrix.GetLength(0);
        int cols = profitMatrix.GetLength(1);
        if (rows == 0 || cols == 0) return [];

        int n = Math.Max(rows, cols);

        // Convert max-profit to min-cost; padding cells (beyond real rows/cols) have cost 0
        double Cost(int i, int j) =>
            i < rows && j < cols ? -profitMatrix[i, j] : 0.0;

        // Standard O(n³) Hungarian via shortest augmenting path with Johnson potentials.
        // p[j] = 1-based row currently assigned to column j  (j=0 is the virtual source)
        // u[i] = row potential, v[j] = column potential
        var p   = new int[n + 1];
        var u   = new double[n + 1];
        var v   = new double[n + 1];
        var way = new int[n + 1];

        for (int i = 1; i <= n; i++)
        {
            p[0] = i;
            int j0 = 0;
            var minD = new double[n + 1];
            var used = new bool[n + 1];
            Array.Fill(minD, double.MaxValue);

            do
            {
                used[j0] = true;
                int i0    = p[j0];
                int j1    = -1;
                double delta = double.MaxValue;

                for (int j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    double cur = Cost(i0 - 1, j - 1) - u[i0] - v[j];
                    if (cur < minD[j]) { minD[j] = cur; way[j] = j0; }
                    if (minD[j] < delta) { delta = minD[j]; j1 = j; }
                }

                for (int j = 0; j <= n; j++)
                {
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                    else          minD[j] -= delta;
                }

                j0 = j1;
            }
            while (p[j0] != 0);

            do
            {
                int j1 = way[j0];
                p[j0]  = p[j1];
                j0     = j1;
            }
            while (j0 != 0);
        }

        // p[j] = 1-based row assigned to real column j.
        // result[row] = column  (-1 when assigned to a virtual column, i.e. M > N)
        var result = new int[rows];
        Array.Fill(result, -1);

        for (int j = 1; j <= cols; j++)
        {
            int row = p[j] - 1; // convert to 0-based
            if (row >= 0 && row < rows)
                result[row] = j - 1; // convert to 0-based column
        }

        return result;
    }
}
