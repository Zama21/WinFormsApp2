using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinFormsApp2
{
    public static class SampleTerrainGenerator
    {
        // Generate with signature: cols, rows (to match earlier call)
        public static double[][] Generate(int cols, int rows)
        {
            var rnd = new Random(12345);
            double[][] arr = new double[rows][];
            for (int r = 0; r < rows; r++) arr[r] = new double[cols];

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    double nx = x / (double)cols;
                    double ny = y / (double)rows;

                    double h = 0;
                    h += 50.0 * Math.Sin(2 * Math.PI * (nx * 2.0 + ny * 1.5));
                    h += 30.0 * Math.Sin(2 * Math.PI * (nx * 6.0 - ny * 4.0));
                    h += 8.0 * Math.Sin(2 * Math.PI * (nx * 20.0 + ny * 10.0));

                    for (int i = 0; i < 6; i++)
                    {
                        double cx = (i * 37 % cols) / (double)cols;
                        double cy = ((i * 57 + 17) % rows) / (double)rows;
                        double dx = nx - cx;
                        double dy = ny - cy;
                        double dist2 = dx * dx + dy * dy;
                        h += 200.0 * Math.Exp(-dist2 * 80.0) * (0.6 + 0.8 * Math.Sin(i + nx * 6.28));
                    }

                    h += 6.0 * (rnd.NextDouble() - 0.5);
                    arr[y][x] = h;
                }
            }

            // normalize to 50..800 meters
            double min = double.MaxValue, max = double.MinValue;
            foreach (var row in arr) foreach (var v in row) { if (v < min) min = v; if (v > max) max = v; }
            double span = Math.Max(1e-9, max - min);
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    arr[y][x] = 50.0 + 750.0 * (arr[y][x] - min) / span;

            return arr;
        }
    }
}