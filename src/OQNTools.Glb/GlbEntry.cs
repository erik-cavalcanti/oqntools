using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OQNTools.Everse.UI;
using OQNTools.Everse.Service;
using OQNTools.Everse.Utils;
namespace OQNTools.Everse
{
    public static class GlbEntry
    {
        public static void Open(UIApplication app)
        {
            if(!(app.ActiveUIDocument.ActiveView is View3D view)||view.IsTemplate)
                throw new ArgumentException("Abra a vista 3D que deseja exportar. A visibilidade e a caixa de corte definem os elementos exportados.");
            ExternalApplication.RevitCollectorService=new RevitCollectorService(app);
            SettingsConfig.SetValue("release",app.Application.VersionNumber);
            SettingsConfig.SetValue("isRFA",view.Document.IsFamilyDocument.ToString());
            var window=new MainWindow(view);
            new System.Windows.Interop.WindowInteropHelper(window).Owner=app.MainWindowHandle;
            window.ShowDialog();
        }
    }
}
