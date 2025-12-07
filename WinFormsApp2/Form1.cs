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
                Height = 48,
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

            // Load sample data and render
            LoadSampleDataAndRender();
            LoadSampleRoute();
        }

        private void LoadSampleDataAndRender()
        {
            int cols = 300, rows = 200; // reasonable default
            double[][] heights = SampleTerrainGenerator.Generate(cols, rows);

            // Example geo coords for left and right midpoints (replace with your real coords)
            GeoCoord start = new GeoCoord(55.7500, 37.5900); // left midpoint
            GeoCoord end = new GeoCoord(55.7500, 37.6100);   // right midpoint

            // Horizontal extent in meters (example: 2000m x 1500m)
            double widthMeters = 2000.0;
            double heightMeters = 1500.0;

            _terrainControl.SetData(heights, start, end, widthMeters, heightMeters);
            _terrainControl.SetVerticalExaggeration(_tbVerticalExaggeration.Value / 10.0);
            _terrainControl.SetColorMap(_cbColorMap.SelectedIndex);
        }

        private void LoadSampleRoute()
        {
            var route = new List<RoutePoint>()
    {
        new RoutePoint { Id=1, Latitude=55.7501, Longitude=37.5905, HeightAboveTerrain=10 },
        new RoutePoint { Id=2, Latitude=55.7503, Longitude=37.5950, HeightAboveTerrain=20 },
        new RoutePoint { Id=3, Latitude=55.7499, Longitude=37.6001, HeightAboveTerrain=15 },
        new RoutePoint { Id=4, Latitude=55.7498, Longitude=37.6058, HeightAboveTerrain=5 },
    };

            _terrainControl.SetRoute(route);
        }
    }
}