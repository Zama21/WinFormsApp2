using HelixToolkit.Wpf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace WinFormsApp2
{
    // Small struct for geocoords
    public readonly struct GeoCoord
    {
        public double Latitude { get; }
        public double Longitude { get; }
        public GeoCoord(double lat, double lon) { Latitude = lat; Longitude = lon; }
    }

    public class TerrainWpfControl : System.Windows.Controls.UserControl
    {
        private readonly HelixViewport3D _view;
        private readonly Model3DGroup _rootModel;
        private GeometryModel3D? _terrainModel;
        private double[][]? _heights;
        private GeoCoord _start = new GeoCoord(0, 0);
        private GeoCoord _end = new GeoCoord(0, 0);
        private double _widthMeters = 1000, _heightMeters = 1000;
        private bool _curvatureEnabled = false;
        private double _verticalExag = 1.0;
        private int _colorMapIndex = 0;

        public TerrainWpfControl()
        {
            // Build UI
            _view = new HelixViewport3D
            {
                ShowCoordinateSystem = true,
                ShowViewCube = true,
                IsHeadLightEnabled = true,
                Background = Brushes.LightSkyBlue
            };

            _view.Camera = new PerspectiveCamera
            {
                Position = new Point3D(0, -_heightMeters * 1.5, _widthMeters * 0.6),
                LookDirection = new Vector3D(0, _heightMeters * 1.5, -_widthMeters * 0.6),
                UpDirection = new Vector3D(0, 0, 1),
                FieldOfView = 45
            };

            _rootModel = new Model3DGroup();
            _rootModel.Children.Add(new AmbientLight(Color.FromScRgb(1f, 0.35f, 0.35f, 0.35f)));
            _rootModel.Children.Add(new DirectionalLight(Color.FromScRgb(1f, 0.9f, 0.9f, 0.8f), new Vector3D(-0.6, -0.5, -1.0)));
            _rootModel.Children.Add(new DirectionalLight(Color.FromScRgb(0.7f, 0.6f, 0.6f, 0.65f), new Vector3D(0.4, 0.3, -0.5)));

            var modelVisual = new ModelVisual3D { Content = _rootModel };
            _view.Children.Add(modelVisual);
            _view.Children.Add(new DefaultLights());

            Content = _view;
        }

        /// <summary>
        /// Set height data and bounding box in meters.
        /// heights: jagged array [rows][cols] where rows->Y, cols->X
        /// start and end: geo coords of midpoints of left and right edges (used for curvature)
        /// widthMeters, heightMeters: horizontal extents in meters
        /// </summary>
        public void SetData(double[][] heights, GeoCoord start, GeoCoord end, double widthMeters, double heightMeters)
        {
            _heights = heights ?? throw new ArgumentNullException(nameof(heights));
            _start = start;
            _end = end;
            _widthMeters = widthMeters;
            _heightMeters = heightMeters;
            BuildTerrain();
            ResetCamera();
        }

        public void SetVerticalExaggeration(double exag)
        {
            _verticalExag = Math.Max(1e-9, exag);
            BuildTerrain();
        }

        public void SetCurvatureEnabled(bool enabled)
        {
            _curvatureEnabled = enabled;
            BuildTerrain();
        }

        public void SetColorMap(int idx)
        {
            _colorMapIndex = idx;
            BuildTerrain();
        }

        public void ResetCamera()
        {
            var bounds = _rootModel.Bounds;
            var center = new Point3D(bounds.X + bounds.SizeX / 2.0, bounds.Y + bounds.SizeY / 2.0, bounds.Z + bounds.SizeZ / 2.0);
            double diag = Math.Sqrt(bounds.SizeX * bounds.SizeX + bounds.SizeY * bounds.SizeY + bounds.SizeZ * bounds.SizeZ);
            if (_view.Camera is ProjectionCamera cam)
            {
                cam.Position = new Point3D(center.X - diag * 1.2, center.Y - diag * 1.2, center.Z + diag * 0.7);
                cam.LookDirection = new Vector3D(center.X - cam.Position.X, center.Y - cam.Position.Y, center.Z - cam.Position.Z);
                cam.UpDirection = new Vector3D(0, 0, 1);
            }
            _view.ZoomExtents();
        }

        private void BuildTerrain()
        {
            if (_heights == null) return;

            int rows = _heights.Length;
            int cols = _heights[0].Length;
            for (int r = 0; r < rows; r++)
                if (_heights[r].Length != cols) throw new ArgumentException("All rows must have same length.");

            var mesh = new MeshGeometry3D();

            double dx = _widthMeters / (cols - 1);
            double dy = _heightMeters / (rows - 1);

            // find min/max height from input heights (before vertical exaggeration)
            double minH = double.MaxValue, maxH = double.MinValue;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    double h = _heights[r][c];
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;
                }
            if (maxH - minH < 1e-9) maxH = minH + 1e-9;

            // build positions & uv
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    double x = (c - (cols - 1) / 2.0) * dx;
                    double y = ((rows - 1) / 2.0 - r) * dy;
                    double z = _heights[r][c] * _verticalExag;

                    if (_curvatureEnabled)
                    {
                        double fx = c / (double)(cols - 1);
                        double lat = _start.Latitude * (1 - fx) + _end.Latitude * fx;
                        double lon = _start.Longitude * (1 - fx) + _end.Longitude * fx;

                        double metersPerDegLat = 111132.0;
                        double metersPerDegLon = 111319.0 * Math.Cos(lat * Math.PI / 180.0);

                        double lonPoint = lon + x / metersPerDegLon;
                        double latPoint = lat + y / metersPerDegLat;

                        double R = 6_371_000.0;
                        double phi = (90.0 - latPoint) * Math.PI / 180.0;
                        double theta = (lonPoint + 180.0) * Math.PI / 180.0;
                        double radius = R + z;

                        double px = radius * Math.Sin(phi) * Math.Cos(theta);
                        double py = radius * Math.Sin(phi) * Math.Sin(theta);
                        double pz = radius * Math.Cos(phi);

                        mesh.Positions.Add(new Point3D(px, py, pz));
                    }
                    else
                    {
                        mesh.Positions.Add(new Point3D(x, y, z));
                    }

                    mesh.TextureCoordinates.Add(new System.Windows.Point(c / (double)(cols - 1), r / (double)(rows - 1)));
                }
            }

            // triangles
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    int i0 = r * cols + c;
                    int i1 = i0 + 1;
                    int i2 = i0 + cols;
                    int i3 = i2 + 1;

                    mesh.TriangleIndices.Add(i0);
                    mesh.TriangleIndices.Add(i2);
                    mesh.TriangleIndices.Add(i1);

                    mesh.TriangleIndices.Add(i1);
                    mesh.TriangleIndices.Add(i2);
                    mesh.TriangleIndices.Add(i3);
                }
            }

            // normals (accumulate per triangle)
            var normals = new Vector3D[mesh.Positions.Count];
            for (int t = 0; t < mesh.TriangleIndices.Count; t += 3)
            {
                int ia = mesh.TriangleIndices[t];
                int ib = mesh.TriangleIndices[t + 1];
                int ic = mesh.TriangleIndices[t + 2];

                var a = mesh.Positions[ia];
                var b = mesh.Positions[ib];
                var c = mesh.Positions[ic];

                var n = Vector3D.CrossProduct(b - a, c - a);
                normals[ia] += n;
                normals[ib] += n;
                normals[ic] += n;
            }

            var nc = new Vector3DCollection(normals.Length);
            foreach (var v in normals)
            {
                var nv = v;
                if (nv.LengthSquared > 1e-9) nv.Normalize();
                nc.Add(nv);
            }
            mesh.Normals = nc;

            // --- Create color bitmap (texture) based on heights (pre-exaggeration) ---
            var bmp = CreateHeightColorBitmap(_heights, minH, maxH, _colorMapIndex);

            // Create ImageBrush from bitmap and apply to material
            var imageBrush = new ImageBrush(bmp)
            {
                // map brush directly to mesh UVs (u=0..1, v=0..1)
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = Stretch.Fill
            };

            var mat = new DiffuseMaterial(imageBrush);
            var back = new DiffuseMaterial(Brushes.Gray);

            if (_terrainModel != null) _rootModel.Children.Remove(_terrainModel);

            _terrainModel = new GeometryModel3D(mesh, mat) { BackMaterial = back };
            _rootModel.Children.Add(_terrainModel);
        }

        // Build a bitmap where pixel (x,y) corresponds to height at [row=y][col=x]
        private BitmapSource CreateHeightColorBitmap(double[][] heights, double minH, double maxH, int cmapIndex)
        {
            int rows = heights.Length;
            int cols = heights[0].Length;

            // We'll create BGRA32 bitmap
            int stride = cols * 4;
            byte[] pixels = new byte[rows * stride];

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    double h = heights[r][c];
                    double t = (h - minH) / (maxH - minH);
                    if (t < 0) t = 0;
                    if (t > 1) t = 1;

                    var col = ColorFromGradient(t, cmapIndex);

                    int idx = r * stride + c * 4;
                    pixels[idx + 0] = col.B; // B
                    pixels[idx + 1] = col.G; // G
                    pixels[idx + 2] = col.R; // R
                    pixels[idx + 3] = 255;   // A
                }
            }

            var bmp = BitmapSource.Create(cols, rows, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            bmp.Freeze();
            return bmp;
        }

        private Color ColorFromGradient(double t, int cmap)
        {
            if (cmap == 0)
            {
                if (t < 0.35) return Lerp(Colors.DarkGreen, Colors.Green, t / 0.35);
                if (t < 0.6) return Lerp(Colors.Green, Color.FromRgb(210, 180, 140), (t - 0.35) / 0.25);
                if (t < 0.75) return Lerp(Color.FromRgb(210, 180, 140), Colors.Sienna, (t - 0.6) / 0.15);
                return Lerp(Colors.Sienna, Colors.White, (t - 0.75) / 0.25);
            }
            else if (cmap == 1)
            {
                if (t < 0.2) return Lerp(Colors.Blue, Colors.Cyan, t / 0.2);
                if (t < 0.45) return Lerp(Colors.Cyan, Colors.Green, (t - 0.2) / 0.25);
                if (t < 0.75) return Lerp(Colors.Green, Colors.Sienna, (t - 0.45) / 0.30);
                return Lerp(Colors.Sienna, Colors.White, (t - 0.75) / 0.25);
            }

            byte v = (byte)(t * 255);
            return Color.FromRgb(v, v, v);
        }

        private static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t)
            );
        }
    }
}
