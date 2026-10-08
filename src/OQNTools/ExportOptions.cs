using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Grid=System.Windows.Controls.Grid;
using ComboBox=System.Windows.Controls.ComboBox;
using TextBox=System.Windows.Controls.TextBox;
using Color=System.Windows.Media.Color;
using Autodesk.Revit.DB;
using Microsoft.Win32;
namespace OQNTools
{
    public sealed class ExportOptions
    {
        public List<ViewSchedule> Schedules;public string Path;public bool Open,Combined;public XlsxOptions Style;
        public static ExportOptions Show(Context c,List<ViewSchedule> schedules)
        {
            var tabs=new TabControl();var main=new StackPanel();var settings=new StackPanel();
            tabs.Items.Add(new TabItem{Header="Tabelas",Content=main});tabs.Items.Add(new TabItem{Header="Definições",Content=settings});
            var f=new Form(c.App,"Exportar tabelas XLSX","Selecione tabelas e destino. Em Definições, ajuste a aparência. Números são preservados quando o campo e o texto exibido permitem uma conversão inequívoca.").Custom("tabs","",tabs).ActionLabel("Exportar");
            var search=new TextBox{Padding=new Thickness(6)};main.Children.Add(new TextBlock{Text="Pesquisar tabelas"});main.Children.Add(search);
            var list=new StackPanel();var selected=new HashSet<ViewSchedule>();
            Action refresh=()=>{list.Children.Clear();foreach(var schedule in schedules.Where(x=>x.Name.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0)){var v=schedule;var cb=new CheckBox{Content=v.Name,IsChecked=selected.Contains(v),Margin=new Thickness(2)};cb.Checked+=(a,b)=>selected.Add(v);cb.Unchecked+=(a,b)=>selected.Remove(v);list.Children.Add(cb);}};
            search.TextChanged+=(a,b)=>refresh();var buttons=new StackPanel{Orientation=Orientation.Horizontal};
            foreach(bool check in new[]{true,false}){var b=new Button{Content=check?"Marcar resultados":"Desmarcar todos",Margin=new Thickness(0,4,8,4)};b.Click+=(a,e)=>{if(check)foreach(var v in schedules.Where(x=>x.Name.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0))selected.Add(v);else selected.Clear();refresh();};buttons.Children.Add(b);}main.Children.Add(buttons);
            main.Children.Add(new ScrollViewer{Content=list,Height=210,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});refresh();
            var mode=new ComboBox{ItemsSource=new[]{"Uma aba por tabela","Todas em uma única aba"},SelectedIndex=0};main.Children.Add(new TextBlock{Text="Modo de exportação",Margin=new Thickness(0,12,0,4)});main.Children.Add(mode);
            main.Children.Add(new TextBlock{Text="Arquivo",Margin=new Thickness(0,12,0,4)});var fileRow=new DockPanel();var browse=new Button{Content="Procurar..."};DockPanel.SetDock(browse,Dock.Right);fileRow.Children.Add(browse);var path=new TextBox();fileRow.Children.Add(path);main.Children.Add(fileRow);
            browse.Click+=(a,b)=>{var dlg=new SaveFileDialog{Filter="Excel (*.xlsx)|*.xlsx",FileName="Quantitativos.xlsx",DefaultExt=".xlsx"};if(dlg.ShowDialog()==true)path.Text=dlg.FileName;};
            var open=new CheckBox{Content="Abrir arquivo após exportação",IsChecked=true,Margin=new Thickness(0,12,0,4)};main.Children.Add(open);
            var styles=new[]{new Choice("Nenhuma","none"),new Choice("Thin","thin"),new Choice("Dashed","dashed"),new Choice("Dash Dot","dashDot"),new Choice("Medium","medium"),new Choice("Medium Dashed","mediumDashed"),new Choice("Medium Dash Dot","mediumDashDot"),new Choice("Thick","thick"),new Choice("Double","double")};
            settings.Children.Add(new TextBlock{Text="Bordas"});var border=new ComboBox{ItemsSource=styles,SelectedIndex=1};settings.Children.Add(border);
            var fill=new CheckBox{Content="Usar preenchimento no cabeçalho",IsChecked=true,Margin=new Thickness(0,12,0,4)};settings.Children.Add(fill);
            Color color=Color.FromRgb(207,229,237);var colorButton=new Button{Content="Escolher cor do cabeçalho",Background=new SolidColorBrush(color),Margin=new Thickness(0,4,0,4)};settings.Children.Add(colorButton);
            var bold=new CheckBox{Content="Cabeçalho em negrito",IsChecked=true,Margin=new Thickness(0,8,0,4)};settings.Children.Add(bold);
            var width=new CheckBox{Content="Ajustar automaticamente largura das colunas",IsChecked=true,Margin=new Thickness(0,8,0,4)};settings.Children.Add(width);
            var numbers=new CheckBox{Content="Manter formatos numéricos",IsChecked=true,Margin=new Thickness(0,8,0,4)};settings.Children.Add(numbers);
            var preview=new StackPanel{Margin=new Thickness(0,20,0,0)};settings.Children.Add(preview);
            Action draw=()=>{
                preview.Children.Clear();preview.Children.Add(new TextBlock{Text="Prévia ilustrativa",Margin=new Thickness(0,0,0,8)});preview.Children.Add(new TextBlock{Text="QUANTITATIVO DE TUBOS",FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center});
                string style=(string)((Choice)border.SelectedItem).Value;
                foreach(var row in new[]{new[]{"Família","DN","Comp."},new[]{"Tubo PVC","50","12,5 m"}}){bool heading=row[0]=="Família";var grid=new Grid();foreach(var v in row)grid.ColumnDefinitions.Add(new ColumnDefinition());
                    for(int i=0;i<row.Length;i++){var cell=new Grid{MinHeight=35,Background=heading&&fill.IsChecked==true?new SolidColorBrush(color):Brushes.White};var text=new TextBlock{Text=row[i],Margin=new Thickness(8),FontWeight=heading&&bold.IsChecked==true?FontWeights.Bold:FontWeights.Normal};cell.Children.Add(text);
                        if(style!="none"){var rect=new System.Windows.Shapes.Rectangle{Stroke=Brushes.SlateGray,StrokeThickness=style=="thick"?3:style.StartsWith("medium")||style=="double"?2:1};if(style.ToLowerInvariant().Contains("dash"))rect.StrokeDashArray=new DoubleCollection(style.ToLowerInvariant().Contains("dot")?new double[]{4,2,1,2}:new double[]{4,2});cell.Children.Add(rect);if(style=="double")cell.Children.Add(new System.Windows.Shapes.Rectangle{Stroke=Brushes.SlateGray,StrokeThickness=1,Margin=new Thickness(3)});}
                        Grid.SetColumn(cell,i);grid.Children.Add(cell);
                    }preview.Children.Add(grid);
                }
            };
            border.SelectionChanged+=(a,b)=>draw();fill.Checked+=(a,b)=>draw();fill.Unchecked+=(a,b)=>draw();bold.Checked+=(a,b)=>draw();bold.Unchecked+=(a,b)=>draw();
            colorButton.Click+=(a,b)=>{using(var dlg=new System.Windows.Forms.ColorDialog()){dlg.Color=System.Drawing.Color.FromArgb(color.R,color.G,color.B);if(dlg.ShowDialog()==System.Windows.Forms.DialogResult.OK){color=Color.FromRgb(dlg.Color.R,dlg.Color.G,dlg.Color.B);colorButton.Background=new SolidColorBrush(color);draw();}}};draw();
            f.Validate=()=>{if(selected.Count==0)throw new ArgumentException("Marque ao menos uma tabela.");if(!System.IO.Path.IsPathRooted(path.Text)||!path.Text.EndsWith(".xlsx",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Escolha um caminho completo para o arquivo .xlsx.");};
            if(!f.Run())return null;
            if(System.IO.File.Exists(path.Text)&&MessageBox.Show("Substituir o arquivo existente?","Exportar",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return null;
            return new ExportOptions{Schedules=schedules.Where(selected.Contains).ToList(),Path=path.Text,Open=open.IsChecked==true,Combined=mode.SelectedIndex==1,Style=new XlsxOptions{Border=(string)((Choice)border.SelectedItem).Value,HeaderFill=fill.IsChecked==true,HeaderColor="FF"+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2"),HeaderBold=bold.IsChecked==true,AutoWidth=width.IsChecked==true,Numbers=numbers.IsChecked==true}};
        }
    }
}
