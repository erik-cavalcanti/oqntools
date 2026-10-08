namespace OQNTools.Everse.Windows.MainWindow
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;
    using Autodesk.Revit.UI;
    using OQNTools.Everse.Utils;
    using OQNTools.Everse.ViewModel;
    using OQNTools.Everse.UI;

    public class LabelVersion
    {
        public static void Update(UnitsViewModel unitsViewModel)
        {
            string version = SettingsConfig.currentVersion;

            unitsViewModel.Version = version;
        }
    }
}
