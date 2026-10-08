using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using OQNTools.Everse.Windows.MainWindow;
using OQNTools.Everse.UI;

namespace OQNTools.Everse.Transform
{
    public static class ModelScale
    {
        public static List<double> Get(Preferences preferences)
        {
            double scale = Util.ConvertFeetToUnitTypeId(preferences);
            return new List<double> { scale, scale, scale };
        }
    }
}
