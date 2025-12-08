using System.Windows.Forms.Integration;

namespace WinFormsApp2
{
    public partial class Form1 : Form
    {
        private ElementHost _host;
        private TerrainWpfControl _terrainControl;
        private Button _btnReset;
        private TrackBar _tbVerticalExaggeration;
        private CheckBox _cbCurvature;
        private Label _lblExag;
        private ComboBox _cbColorMap;
        private Button _btnAddTile;

        public Form1()
        {
            InitializeComponent();
            Text = "Terrain viewer — HelixToolkit (WPF in WinForms)";
            Width = 1200;
            Height = 800;

            _host = new ElementHost { Dock = DockStyle.Fill };
            Controls.Add(_host);

            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Color.FromArgb(240, 240, 240)
            };
            Controls.Add(panel);
            panel.BringToFront();

            _btnReset = new Button { Text = "Reset camera", Left = 8, Top = 8, Width = 110 };
            panel.Controls.Add(_btnReset);

            _lblExag = new Label { Text = "Vertical exag: 1.0x", Left = 128, Top = 12, Width = 140 };
            panel.Controls.Add(_lblExag);

            _tbVerticalExaggeration = new TrackBar { Left = 278, Top = 4, Width = 220, Minimum = 1, Maximum = 50, Value = 10 };
            panel.Controls.Add(_tbVerticalExaggeration);

            _cbCurvature = new CheckBox { Text = "Simulate curvature (geoid)", Left = 520, Top = 12, Width = 220 };
            panel.Controls.Add(_cbCurvature);

            _cbColorMap = new ComboBox { Left = 760, Top = 8, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            _cbColorMap.Items.AddRange(new object[] { "Topo (green->brown->white)", "Elevation (blue->green->brown)", "Grayscale" });
            _cbColorMap.SelectedIndex = 0;
            panel.Controls.Add(_cbColorMap);

            _btnAddTile = new Button { Text = "Add another tile", Left = 980, Top = 8, Width = 160 };
            panel.Controls.Add(_btnAddTile);

            // Create WPF control and host it
            _terrainControl = new TerrainWpfControl();
            _host.Child = _terrainControl;

            // event wiring
            _btnReset.Click += (s, e) => _terrainControl.ResetCamera();
            _tbVerticalExaggeration.Scroll += (s, e) =>
            {
                double exag = _tbVerticalExaggeration.Value / 10.0;
                _lblExag.Text = $"Vertical exag: {exag:0.0}x";
                _terrainControl.SetVerticalExaggeration(exag);
            };
            _cbCurvature.CheckedChanged += (s, e) => _terrainControl.SetCurvatureEnabled(_cbCurvature.Checked);
            _cbColorMap.SelectedIndexChanged += (s, e) => _terrainControl.SetColorMap(_cbColorMap.SelectedIndex);

            _btnAddTile.Click += (s, e) => AddAnotherTileSample();

            // Load sample data and render (as list of tiles)
            LoadSampleDataAndRender();
            LoadSampleRoute();
        }

        private void LoadSampleDataAndRender()
        {
            // build one tile initially
            int cols = 300, rows = 200;
            double[][] heights = SampleTerrainGenerator.Generate(cols, rows);

            GeoCoord start = new GeoCoord(55.7500, 37.5900);
            GeoCoord end = new GeoCoord(55.7500, 37.6100);
            double widthMeters = 2000.0;
            double heightMeters = 1500.0;


            var tile = new TerrainTile(heights, start, end, widthMeters, heightMeters);


            GeoCoord start2 = new GeoCoord(55.8500, 37.5900);
            GeoCoord end2 = new GeoCoord(55.8500, 37.6100);
            var tile2 = new TerrainTile(heights, start2, end2, widthMeters, heightMeters);

            // If you have multiple tiles, create multiple TerrainTile instances and pass as list.
            var tiles = new List<TerrainTile> { tile, tile2 };

            _terrainControl.SetTiles(tiles);

            _terrainControl.SetVerticalExaggeration(_tbVerticalExaggeration.Value / 10.0);
            _terrainControl.SetColorMap(_cbColorMap.SelectedIndex);
        }

        private int _extraTileIndex = 0;
        private void AddAnotherTileSample()
        {
            // Example: create another tile shifted east by 2000m (approx ~0.018° at 55.75 lat)
            int cols = 200, rows = 150;
            double[][] heights2 = SampleTerrainGenerator.Generate(cols, rows);

            // compute approximate degree offset for width (approx meters per degree lon at 55.75N)
            double lat = 55.75;
            double metersPerDegLon = 111319.0 * Math.Cos(lat * Math.PI / 180.0);
            double lonOffsetDeg = 2000.0 / metersPerDegLon;

            GeoCoord start2 = new GeoCoord(55.7500, 37.6100 + _extraTileIndex * lonOffsetDeg);
            GeoCoord end2 = new GeoCoord(55.7500, 37.6300 + _extraTileIndex * lonOffsetDeg);

            var tile2 = new TerrainTile(heights2, start2, end2, 2000.0, 1500.0);

            // Add tile to control (it will be drawn together with existing tiles)
            _terrainControl.AddTile(tile2);
            _extraTileIndex++;
        }

        private void LoadSampleRoute()
        {
            var route = new List<RoutePoint>()
            {
                new RoutePoint { Id=1, Latitude=55.7501, Longitude=37.5905, HeightAboveTerrain=10 },
                new RoutePoint { Id=2, Latitude=55.7503, Longitude=37.5950, HeightAboveTerrain=20 },
                new RoutePoint { Id=3, Latitude=55.7499, Longitude=37.6001, HeightAboveTerrain=15 },
                new RoutePoint { Id=4, Latitude=55.7498, Longitude=37.6058, HeightAboveTerrain=5 },
                // point outside tiles:
                new RoutePoint { Id=5, Latitude=56.0000, Longitude=38.0000, HeightAboveTerrain=50 }
            };

            _terrainControl.SetRoute(route);
        }
    }
}