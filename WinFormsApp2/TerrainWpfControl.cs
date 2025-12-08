
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

    public class TerrainTile
    {
        public double[][] Heights;
        public GeoCoord Start;
        public GeoCoord End;
        public double WidthMeters;
        public double HeightMeters;

        public TerrainTile(double[][] heights, GeoCoord start, GeoCoord end, double widthMeters, double heightMeters)
        {
            Heights = heights ?? throw new ArgumentNullException(nameof(heights));
            Start = start;
            End = end;
            WidthMeters = widthMeters;
            HeightMeters = heightMeters;
        }
    }
    public class RoutePoint
    {
        public int Id { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        // высота над поверхностью рельефа в метрах
        public double HeightAboveTerrain { get; set; } = 0.0;
    }


    public class TerrainWpfControl : System.Windows.Controls.UserControl
    {
        private readonly HelixViewport3D _view;
        private readonly Model3DGroup _rootModel;

        // теперь — несколько тайлов
        private readonly List<TerrainTile> _tiles = new();
        // для каждого тайла — соответствующий GeometryModel3D
        private readonly List<GeometryModel3D> _tileModels = new();

        private bool _curvatureEnabled = false;
        private double _verticalExag = 1.0;
        private int _colorMapIndex = 0;

        // route visualization (как раньше)
        private List<RoutePoint>? _routeCache = null;
        private ModelVisual3D? _routePointsVisual;
        private ModelVisual3D? _routeLinesVisual;
        private BillboardTextGroupVisual3D? _routeLabelsVisual;
        private ModelVisual3D? _routePoints;
        private LinesVisual3D? _routeLines;
        private List<BillboardTextVisual3D> _routeLabels = new();

        // shared origin for curvature-based conversion (so different tiles stay aligned)
        private Vector3D? _curvatureOrigin = null;

        public TerrainWpfControl()
        {
            _view = new HelixViewport3D
            {
                ShowCoordinateSystem = true,
                ShowViewCube = true,
                IsHeadLightEnabled = true,
                Background = Brushes.LightSkyBlue
            };

            _view.Camera = new PerspectiveCamera
            {
                Position = new Point3D(0, -1500, 600),
                LookDirection = new Vector3D(0, 1500, -600),
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

        // --- API ---

        // Заменяет все тайлы новыми
        public void SetTiles(List<TerrainTile> tiles)
        {
            if (tiles == null) throw new ArgumentNullException(nameof(tiles));
            _tiles.Clear();
            _tiles.AddRange(tiles);
            RebuildAllTiles();
            ResetCamera();
        }

        // Добавляет один тайл (вместе с уже существующими)
        public void AddTile(TerrainTile tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            _tiles.Add(tile);
            RebuildAllTiles();
        }

        public void ClearTiles()
        {
            _tiles.Clear();
            RebuildAllTiles();
        }

        public void SetVerticalExaggeration(double exag)
        {
            _verticalExag = Math.Max(1e-9, exag);
            RebuildAllTiles();
        }

        public void SetCurvatureEnabled(bool enabled)
        {
            _curvatureEnabled = enabled;
            RebuildAllTiles();
        }

        public void SetColorMap(int idx)
        {
            _colorMapIndex = idx;
            RebuildAllTiles();
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

        // --- Tiles building ---

        private void RebuildAllTiles()
        {
            // remove existing tile models from root
            foreach (var m in _tileModels)
                _rootModel.Children.Remove(m);
            _tileModels.Clear();

            _curvatureOrigin = null;
            if (_curvatureEnabled && _tiles.Count > 0)
            {
                // Compute a shared curvature origin to keep all curved tiles aligned
                // We'll compute approximate center of all tiles in geodetic meters and map to sphere center.
                double avgLat = 0.0, avgLon = 0.0;
                foreach (var t in _tiles)
                {
                    avgLat += (t.Start.Latitude + t.End.Latitude) * 0.5;
                    avgLon += (t.Start.Longitude + t.End.Longitude) * 0.5;
                }
                avgLat /= (_tiles.Count);
                avgLon /= (_tiles.Count);

                // choose nominal center at avgLat/avgLon and surface radius R
                const double R = 6_371_000.0;
                double phi = (90.0 - avgLat) * Math.PI / 180.0;
                double theta = (avgLon + 180.0) * Math.PI / 180.0;
                double px = R * Math.Sin(phi) * Math.Cos(theta);
                double py = R * Math.Sin(phi) * Math.Sin(theta);
                double pz = R * Math.Cos(phi);
                _curvatureOrigin = new Vector3D(px, py, pz);
            }

            // Build each tile as its own mesh
            for (int ti = 0; ti < _tiles.Count; ti++)
            {
                var tile = _tiles[ti];

                if (tile.Heights == null) continue;
                int rows = tile.Heights.Length;
                if (rows == 0) continue;
                int cols = tile.Heights[0].Length;
                for (int r = 0; r < rows; r++)
                    if (tile.Heights[r].Length != cols) throw new ArgumentException("All rows in tile must have same length.");

                var mesh = new MeshGeometry3D();

                double dx = tile.WidthMeters / (cols - 1);
                double dy = tile.HeightMeters / (rows - 1);

                // find min/max height from input heights (before vertical exaggeration)
                double minH = double.MaxValue, maxH = double.MinValue;
                for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        double h = tile.Heights[r][c];
                        if (h < minH) minH = h;
                        if (h > maxH) maxH = h;
                    }
                if (maxH - minH < 1e-9) maxH = minH + 1e-9;

                // build positions & uv
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        double xLocal = (c - (cols - 1) / 2.0) * dx;
                        double yLocal = ((rows - 1) / 2.0 - r) * dy;
                        double z = tile.Heights[r][c] * _verticalExag;

                        if (_curvatureEnabled)
                        {
                            // compute geographic lat/lon for this column and row similar to previous logic:
                            double fx = c / (double)(cols - 1);
                            double lat = tile.Start.Latitude * (1 - fx) + tile.End.Latitude * fx;
                            double lon = tile.Start.Longitude * (1 - fx) + tile.End.Longitude * fx;

                            double metersPerDegLat = 111132.0;
                            double metersPerDegLon = 111319.0 * Math.Cos(lat * Math.PI / 180.0);

                            double lonPoint = lon + xLocal / metersPerDegLon;
                            double latPoint = lat + yLocal / metersPerDegLat;

                            const double R = 6_371_000.0;
                            double phi = (90.0 - latPoint) * Math.PI / 180.0;
                            double theta = (lonPoint + 180.0) * Math.PI / 180.0;
                            double radius = R + z;

                            double px = radius * Math.Sin(phi) * Math.Cos(theta);
                            double py = radius * Math.Sin(phi) * Math.Sin(theta);
                            double pz = radius * Math.Cos(phi);

                            if (_curvatureOrigin != null)
                            {
                                px -= _curvatureOrigin.Value.X;
                                py -= _curvatureOrigin.Value.Y;
                                pz -= _curvatureOrigin.Value.Z;
                            }

                            mesh.Positions.Add(new Point3D(px, py, pz));
                        }
                        else
                        {
                            // flat local coordinates (x,y,z) — but note: different tiles may have different local centers.
                            // To align tiles consistently in local XY we position them by mapping tile center to world origin offset:
                            // For simplicity we compute an XY offset based on the tile's longitudinal middle vs a reference (0,0) — but to keep compatibility
                            // with your prior single-tile code we place each tile centered around its local (0,0). This keeps relative positions correct
                            // when tiles are defined consistently in the same coordinate frame (Width/Height).
                            mesh.Positions.Add(new Point3D(xLocal + TileWorldOffsetX(tile), yLocal + TileWorldOffsetY(tile), z));
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

                // normals
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

                // texture/colormap
                var bmp = CreateHeightColorBitmap(tile.Heights, minH, maxH, _colorMapIndex);
                var imageBrush = new ImageBrush(bmp)
                {
                    ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                    Stretch = Stretch.Fill
                };

                var mat = new DiffuseMaterial(imageBrush);
                var back = new DiffuseMaterial(Brushes.Gray);

                var model = new GeometryModel3D(mesh, mat) { BackMaterial = back };
                _rootModel.Children.Add(model);
                _tileModels.Add(model);
            }

            // rebuild route visuals so they follow new geometry
            RebuildRoute();
        }

        // Compute a consistent XY offset for a tile when curvature is off.
        // We'll map tiles into a shared flat local XY using the tile center projected to meters around an arbitrary origin (first tile center).
        // For simplicity & determinism we compute tile offsets relative to first tile in list.
        private double TileWorldOffsetX(TerrainTile tile)
        {
            if (_tiles == null || _tiles.Count == 0) return 0.0;
            var refTile = _tiles[0];
            // compute approximate longitudinal center for both tiles in meters offset
            double refLat = (refTile.Start.Latitude + refTile.End.Latitude) * 0.5;
            double metersPerDegLonRef = 111319.0 * Math.Cos(refLat * Math.PI / 180.0);
            double refLonCenter = (refTile.Start.Longitude + refTile.End.Longitude) * 0.5;

            double tileLat = (tile.Start.Latitude + tile.End.Latitude) * 0.5;
            double tileLonCenter = (tile.Start.Longitude + tile.End.Longitude) * 0.5;

            double dxDeg = (tileLonCenter - refLonCenter);
            double dxMeters = dxDeg * metersPerDegLonRef;

            return dxMeters;
        }

        private double TileWorldOffsetY(TerrainTile tile)
        {
            if (_tiles == null || _tiles.Count == 0) return 0.0;
            var refTile = _tiles[0];
            double refLat = (refTile.Start.Latitude + refTile.End.Latitude) * 0.5;
            double refLatCenter = refLat;
            double tileLat = (tile.Start.Latitude + tile.End.Latitude) * 0.5;

            double metersPerDegLat = 111132.0;
            double dyDeg = (tileLat - refLatCenter);
            double dyMeters = dyDeg * metersPerDegLat;

            return dyMeters;
        }

        // --- Color bitmap creation (как раньше) ---
        private BitmapSource CreateHeightColorBitmap(double[][] heights, double minH, double maxH, int cmapIndex)
        {
            int rows = heights.Length;
            int cols = heights[0].Length;

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
                    pixels[idx + 0] = col.B;
                    pixels[idx + 1] = col.G;
                    pixels[idx + 2] = col.R;
                    pixels[idx + 3] = 255;
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

        // --- Route / labels / points handling ---
        public void SetRoute(List<RoutePoint> route)
        {
            _routeCache = route;
            RebuildRoute();
        }

        private void RebuildRoute()
        {
            // clear existing visuals
            ClearRoute();

            if (_routeCache == null) return;

            _routePoints = new ModelVisual3D();
            _routeLines = new LinesVisual3D()
            {
                Color = Colors.Yellow,
                Thickness = 2
            };

            Point3D? prev = null;
            foreach (var p in _routeCache)
            {
                var pos = ConvertGeoToPoint(p.Latitude, p.Longitude, p.HeightAboveTerrain);

                if (prev != null)
                {
                    _routeLines.Points.Add(prev.Value);
                    _routeLines.Points.Add(pos);
                }
                prev = pos;

                var sphere = new SphereVisual3D()
                {
                    Center = pos,
                    Radius = 3,
                    Material = new DiffuseMaterial(
                        new SolidColorBrush(p.Id == _routeCache[0].Id ? Colors.Lime : Colors.Red))
                };

                _routePoints.Children.Add(sphere);

                var label = new BillboardTextVisual3D()
                {
                    Text = $"#{p.Id}\nLat={p.Latitude:F5}\nLon={p.Longitude:F5}\nΔH={p.HeightAboveTerrain:F1}",
                    Position = new Point3D(pos.X, pos.Y + 10, pos.Z),
                    Background = Brushes.Black,
                    Foreground = Brushes.White
                };

                _routeLabels.Add(label);
                _view.Children.Add(label);
            }

            if (_routePoints != null) _view.Children.Add(_routePoints);
            if (_routeLines != null) _view.Children.Add(_routeLines);
        }

        private void ClearRoute()
        {
            if (_routePoints != null)
                _view.Children.Remove(_routePoints);

            if (_routeLines != null)
                _view.Children.Remove(_routeLines);

            foreach (var label in _routeLabels)
                _view.Children.Remove(label);

            _routeLabels.Clear();
            _routePoints = null;
            _routeLines = null;
        }

        // --- Geo -> Point3D conversion (multi-tile aware) ---
        // If a point lies within a tile, use that tile's heights for interpolation.
        // If none found, place at surface radius (if curvature) or at flat XY with terrainBase = 0.
        private Point3D ConvertGeoToPoint(double lat, double lon, double extraHeight)
        {
            for (int ti = 0; ti < _tiles.Count; ti++)
            {
                var tile = _tiles[ti];
                if (IsGeoInsideTile(tile, lat, lon, out double fx, out double fy))
                {
                    int rows = tile.Heights.Length;
                    int cols = tile.Heights[0].Length;

                    // colPos fraction along width
                    double colPos = fx * (cols - 1);

                    // --- FIX #1: correct rowPos sign ---
                    double dy = tile.HeightMeters / (rows - 1);
                    double rowCenter = (rows - 1) / 2.0;
                    // north = fy positive → row decreases → sign must be +:
                    double rowPos = rowCenter + (fy / dy);
                    // ----------------------------------

                    colPos = Math.Max(0.0, Math.Min(cols - 1, colPos));
                    rowPos = Math.Max(0.0, Math.Min(rows - 1, rowPos));

                    int c0 = (int)Math.Floor(colPos);
                    int c1 = Math.Min(cols - 1, c0 + 1);
                    int r0 = (int)Math.Floor(rowPos);
                    int r1 = Math.Min(rows - 1, r0 + 1);

                    double s = colPos - c0;
                    double t = rowPos - r0;

                    double h00 = tile.Heights[r0][c0];
                    double h10 = tile.Heights[r0][c1];
                    double h01 = tile.Heights[r1][c0];
                    double h11 = tile.Heights[r1][c1];

                    double h0 = h00 * (1 - s) + h10 * s;
                    double h1 = h01 * (1 - s) + h11 * s;
                    double terrainBase = h0 * (1 - t) + h1 * t;

                    double finalH = terrainBase * _verticalExag + extraHeight;

                    double dx = tile.WidthMeters / (cols - 1);
                    double xLocal = (colPos - (cols - 1) / 2.0) * dx;
                    double yLocal = ((rows - 1) / 2.0 - rowPos) * dy;

                    if (!_curvatureEnabled)
                    {
                        double x = xLocal + TileWorldOffsetX(tile);
                        double y = yLocal + TileWorldOffsetY(tile);
                        return new Point3D(x, y, finalH);
                    }

                    // curvature
                    double fxCol = colPos / (cols - 1);
                    double latAtCol = tile.Start.Latitude * (1 - fxCol) + tile.End.Latitude * fxCol;

                    double metersPerDegLat = 111132.0;
                    double metersPerDegLon = 111319.0 * Math.Cos(latAtCol * Math.PI / 180.0);

                    double lonPoint = (tile.Start.Longitude * (1 - fxCol) + tile.End.Longitude * fxCol)
                                       + xLocal / metersPerDegLon;
                    double latPoint = latAtCol + yLocal / metersPerDegLat;

                    const double R = 6_371_000.0;
                    double phi = (90.0 - latPoint) * Math.PI / 180.0;
                    double theta = (lonPoint + 180.0) * Math.PI / 180.0;
                    double radius = R + finalH;

                    double px = radius * Math.Sin(phi) * Math.Cos(theta);
                    double py = radius * Math.Sin(phi) * Math.Sin(theta);
                    double pz = radius * Math.Cos(phi);

                    if (_curvatureOrigin != null)
                    {
                        px -= _curvatureOrigin.Value.X;
                        py -= _curvatureOrigin.Value.Y;
                        pz -= _curvatureOrigin.Value.Z;
                    }

                    return new Point3D(px, py, pz);
                }
            }

            // point outside tiles
            double finalNoTile = extraHeight;

            if (!_curvatureEnabled)
            {
                double metersPerDegLon = 111319.0 * Math.Cos(lat * Math.PI / 180.0);
                double metersPerDegLat2 = 111132.0;

                double refLon = (_tiles.Count > 0) ? (_tiles[0].Start.Longitude + _tiles[0].End.Longitude) * 0.5 : 0.0;
                double refLat = (_tiles.Count > 0) ? (_tiles[0].Start.Latitude + _tiles[0].End.Latitude) * 0.5 : 0.0;

                double dxMeters = (lon - refLon) * metersPerDegLon;
                double dyMeters = (lat - refLat) * metersPerDegLat2;

                return new Point3D(dxMeters, dyMeters, finalNoTile);
            }
            else
            {
                const double R = 6_371_000.0;
                double phi = (90.0 - lat) * Math.PI / 180.0;
                double theta = (lon + 180.0) * Math.PI / 180.0;
                double radius = R + finalNoTile;

                double px = radius * Math.Sin(phi) * Math.Cos(theta);
                double py = radius * Math.Sin(phi) * Math.Sin(theta);
                double pz = radius * Math.Cos(phi);

                if (_curvatureOrigin != null)
                {
                    px -= _curvatureOrigin.Value.X;
                    py -= _curvatureOrigin.Value.Y;
                    pz -= _curvatureOrigin.Value.Z;
                }

                return new Point3D(px, py, pz);
            }
        }


        // Проверяет попадает ли геокоордината в тайл (приближённо, в метрах)
        // Возвращает fx (0..1) вдоль ширины и fy (метры от центра по север/юг, положительное на север)
        private bool IsGeoInsideTile(TerrainTile tile, double lat, double lon, out double fx, out double fyMeters)
        {
            fx = 0;
            fyMeters = 0;

            double metersPerDegLat = 111132.0;

            double totalLonDiff = tile.End.Longitude - tile.Start.Longitude;
            if (Math.Abs(totalLonDiff) < 1e-12)
                return false;

            fx = (lon - tile.Start.Longitude) / totalLonDiff;

            // tolerance
            if (fx < -0.01 || fx > 1.01)
                return false;

            // clamp
            fx = Math.Max(0, Math.Min(1, fx));

            // interpolate latitude on center axis
            double latAtCol = tile.Start.Latitude * (1 - fx) + tile.End.Latitude * fx;

            // vertical offset in meters
            fyMeters = (lat - latAtCol) * metersPerDegLat;

            // check height half-span
            return Math.Abs(fyMeters) <= tile.HeightMeters / 2.0 + 1e-6;
        }


    }
}
