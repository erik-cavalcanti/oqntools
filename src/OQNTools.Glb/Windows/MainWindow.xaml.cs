using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OQNTools.Everse;
using OQNTools.Everse.Core;
using OQNTools.Everse.Utils;
using OQNTools.Everse.ViewModel;
using OQNTools.Everse.Windows.MainWindow;
using Theme = OQNTools.Everse.Utils.Theme;
using View = Autodesk.Revit.DB.View;
using OQNTools.Everse.Materials;

namespace OQNTools.Everse.UI
{
    /// <summary>
    /// Interaction logic for Settings.xaml.
    /// </summary>
    /// 
    public partial class MainWindow : Window
    {
        private Document doc;
        public static List<string> TexturePaths { get; set; }

        public MainWindow(View view)
        {
            this.UnitsViewModel = new UnitsViewModel();
            this.DataContext = this.UnitsViewModel;
            MainView = this;
            doc = ExternalApplication.RevitCollectorService.GetDocument();

            this.InitializeComponent();

            ComboUnits.Set(doc);
            this.View = view;

            UpdateForm.Run(this.MainWindow_Border);
            LabelVersion.Update(this.UnitsViewModel);

            Theme.ApplyDarkLightMode(this.Resources.MergedDictionaries[0]);

            TexturePaths = TextureLocation.GetPaths();

            ExportLog.StartLog();
            ExportLog.Write("Open Window");
        }

        public static MainWindow MainView { get; set; }

        private View View { get; set; }

        private UnitsViewModel UnitsViewModel { get; set; }

        private void OnExportView(object sender, RoutedEventArgs e)
        {
            try { ExportView(); }
            catch (Exception ex) {
                if(ProgressBarWindow.MainView!=null)ProgressBarWindow.MainView.Close();
                ExportLog.WriteException(ex);
                MessageWindow.Show("Falha na exportação", ex.GetType().Name+": "+ex.Message);
            }
        }
        private void ExportView()
        {
            GLTFExportContext.cancelation=false;
            string format = string.Concat(".", SettingsConfig.GetValue("format"));
            LogConfiguration.SaveConfig();
            View3D exportView = this.View as View3D;
            
            string fileName = SettingsConfig.GetValue("fileName");
            bool dialogResult = FilesHelper.AskToSave(ref fileName, "Modelo 3D|*"+format, format);
            if (dialogResult != true)
            {
                return;
            }

            string directory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fileName), System.IO.Path.GetFileNameWithoutExtension(fileName));
            string nameOnly = System.IO.Path.GetFileNameWithoutExtension(fileName);

            string stage=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(fileName),".oqn-glb-"+Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(stage);
            try {
            SettingsConfig.SetValue("path", System.IO.Path.Combine(stage,nameOnly));
            SettingsConfig.SetValue("fileName", nameOnly);

            List<Element> elementsInView = Collectors.AllVisibleElementsByView(doc, doc.ActiveView);

            if (!doc.IsFamilyDocument && !elementsInView.Any())
            {
                MessageWindow.Show("No Valid Elements", "There are no valid elements to export in this view");
                ExportLog.Write("There are no valid elements to export in this view");
                return;
            }

            int numberRuns = int.Parse(SettingsConfig.GetValue("runs"));
            int incrementRun = numberRuns + 1;
            SettingsConfig.SetValue("runs", incrementRun.ToString());

            int elemInView = elementsInView.Count;
            ExportLog.Write($"{elemInView} elements will be exported");      
            ProgressBarWindow progressBar =
                ProgressBarWindow.Create(elemInView + 1, 0, "Convertendo elementos...", this);

            // Use our custom implementation of IExportContext as the exporter context.
            GLTFExportContext ctx = new GLTFExportContext(doc);

            // Create a new custom exporter with the context.
            CustomExporter exporter = new CustomExporter(doc, ctx);
            exporter.ShouldStopOnError = true;

            #if REVIT2019
            exporter.Export(exportView);
            #else
           exporter.Export(exportView as View);
            #endif


            if(!GLTFExportContext.cancelation){
                var generated=System.IO.Directory.GetFiles(stage);
                var destination=System.IO.Path.GetDirectoryName(fileName);
                if(generated.Any(path=>System.IO.File.Exists(System.IO.Path.Combine(destination,System.IO.Path.GetFileName(path))))
                    &&System.Windows.MessageBox.Show(this,"Já existem arquivos com os nomes da exportação. Substituir?","Exportar GLB",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)
                    GLTFExportContext.cancelation=true;
                else OQNTools.Everse.Export.OutputCommit.Run(stage,destination);
            }
            ProgressBarWindow.ViewModel.ProgressBarValue = elemInView + 1;
            ProgressBarWindow.ViewModel.Message = GLTFExportContext.cancelation ? "Exportação cancelada." : "Exportação concluída!";
            ExportLog.EndLog();
            ProgressBarWindow.ViewModel.Action = "Accept";
            }finally{
                SettingsConfig.SetValue("path",directory);
                // Leave recovery files available if committing/restoring files failed.
                if(System.IO.Directory.Exists(stage)&&!System.IO.Directory.GetFiles(stage,"*.backup").Any())System.IO.Directory.Delete(stage,true);
            }
        }

        private void Border_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Title_Link(object sender, RoutedEventArgs e)
        {
            Hyperlink.Run(Links.everseWebsite);
        }

        private void Leia_Link(object sender, RoutedEventArgs e)
        {
            Hyperlink.Run(Links.leiaWebsite);
        }

        private void TrueFalseToggles(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.Primitives.ToggleButton button = sender as System.Windows.Controls.Primitives.ToggleButton;
            SettingsConfig.SetValue(button.Name, button.IsChecked.ToString());
        }

        private void RadioButtonClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.RadioButton button = sender as System.Windows.Controls.RadioButton;
            string value = button.Name;
            string key = "compression";
            SettingsConfig.SetValue(key, value);
        }

        private void RadioButtonMaterialsClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.RadioButton button = sender as System.Windows.Controls.RadioButton;
            string value = button.Name;
            string key = "materials";
            SettingsConfig.SetValue(key, value);
        }
        
        private void RadioButtonFormatClick(object sender, RoutedEventArgs e)
        {
            System.Windows.Controls.RadioButton button = sender as System.Windows.Controls.RadioButton;
            string value = button.Name;
            string key = "format";
            SettingsConfig.SetValue(key, value);
        }

        private void DigitsSliderValueChanged(object sender, RoutedEventArgs e)
        {
            Slider slider = sender as Slider;
            int value = Convert.ToInt32(slider.Value.ToString());
            string key = "digits";
            SettingsConfig.SetValue(key, value.ToString());
        }
    }
}