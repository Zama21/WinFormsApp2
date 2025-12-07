using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinFormsApp2
{
    public class RoutePoint
    {
        public int Id { get; set; }
        public double Latitude { get; set; }      // в градусах
        public double Longitude { get; set; }     // в градусах
        public double HeightAboveTerrain { get; set; } // метры
    }
}
