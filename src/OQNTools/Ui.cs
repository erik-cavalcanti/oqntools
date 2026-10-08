using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.UI;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace OQNTools
{
    public sealed class Choice
    {
        public string Label { get; set; }
        public object Value { get; set; }
        public override string ToString() => Label;
        public Choice(string label, object value) { Label = label; Value = value; }
    }

    // All dialogs are modal: every Revit API call remains on the command's UI thread.
    public sealed class Form : Window
    {
        readonly StackPanel fields = new StackPanel();
        readonly Dictionary<string, FrameworkElement> controls = new Dictionary<string, FrameworkElement>();
        readonly Button apply = new Button { Content = "Continuar", MinWidth = 110, Padding = new Thickness(16,8,16,8), IsDefault = true };
        public Form(UIApplication app, string title, string description)
        {
            Title = "OQN Tools · " + title; Width = 720; Height = 700;
            MinWidth = 520; MinHeight = 380; WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White; Foreground = new SolidColorBrush(Color.FromRgb(30,41,59));
            FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
            new WindowInteropHelper(this).Owner = app.MainWindowHandle;
            var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
            var header = new StackPanel { Margin = new Thickness(0,0,0,18) };
            header.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,8,0,0) });
            DockPanel.SetDock(header,Dock.Top); root.Children.Add(header);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0,16,0,0) };
            var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 100, Padding = new Thickness(16,8,16,8), Margin = new Thickness(0,0,12,0) };
            footer.Children.Add(cancel); footer.Children.Add(apply);
            DockPanel.SetDock(footer,Dock.Bottom); root.Children.Add(footer);
            root.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            apply.Click += (s,e) => { try { foreach(var grid in controls.Values.OfType<DataGrid>()){if(!grid.CommitEdit(DataGridEditingUnit.Cell,true)||!grid.CommitEdit(DataGridEditingUnit.Row,true))throw new ArgumentException("Conclua a edição da linha da tabela.");} Validate?.Invoke(); DialogResult = true; } catch(Exception ex) { MessageBox.Show(this,Errors.Describe(ex),"Confira os dados"); } };
        }
        public Form ActionLabel(string label){apply.Content=label;return this;}
        public Form Custom(string key,string label,FrameworkElement element){Add(key,label,element);return this;}
        public Form ActionButton(string label,Action action){var button=new Button{Content=label,Margin=new Thickness(0,6,8,6),Padding=new Thickness(10,5,10,5)};button.Click+=(a,b)=>{try{action();}catch(Exception ex){MessageBox.Show(this,Errors.Describe(ex),"Confira os dados");}};fields.Children.Add(button);return this;}
        public Action Validate { get; set; }
        void Add(string key, string label, FrameworkElement control)
        {
            if(!string.IsNullOrEmpty(label)) fields.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,10,0,5), TextWrapping = TextWrapping.Wrap });
            controls.Add(key,control); fields.Children.Add(control);
        }
        public Form Text(string key,string label,string value="",bool multiline=false)
        { Add(key,label,new WpfTextBox { Text=value, Padding=new Thickness(8), AcceptsReturn=multiline, MinHeight=multiline?90:32, TextWrapping=TextWrapping.Wrap }); return this; }
        public Form Check(string key,string label,bool value=false)
        { Add(key,"",new CheckBox { Content=label,IsChecked=value,Margin=new Thickness(0,12,0,6) });return this; }
        public Form Select(string key,string label,IEnumerable<Choice> values)
        { var cb=new WpfComboBox { ItemsSource=values.ToList(),MinHeight=32,Padding=new Thickness(6) };cb.SelectedIndex=0;Add(key,label,cb);return this; }
        public Form List(string key,string label,IEnumerable<Choice> values)
        {
            var items=values.ToList(); var panel=new StackPanel(); var search=new WpfTextBox { Padding=new Thickness(6),ToolTip="Digite para localizar" };
            var box=new ListBox { ItemsSource=items,SelectionMode=SelectionMode.Extended,Height=210 };
            var selected=new HashSet<Choice>(); bool filtering=false;
            box.SelectionChanged+=(s,e)=>{ if(filtering)return; foreach(Choice x in e.RemovedItems)selected.Remove(x); foreach(Choice x in e.AddedItems)selected.Add(x); };
            search.TextChanged+=(s,e)=>{filtering=true;box.ItemsSource=items.Where(x=>x.Label.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0).ToList();foreach(Choice x in box.Items)if(selected.Contains(x))box.SelectedItems.Add(x);filtering=false;};
            var all=new Button { Content="Selecionar resultados",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,4,0,4) };
            all.Click+=(s,e)=>box.SelectAll(); panel.Children.Add(search);panel.Children.Add(all);panel.Children.Add(box);panel.Tag=selected;Add(key,label,panel);return this;
        }
        public Form CheckList(string key,string label,IEnumerable<Choice> values,IEnumerable<object> checkedValues=null)
        {
            var items=values.ToList();var marked=new HashSet<object>(checkedValues??Enumerable.Empty<object>());var selected=new HashSet<Choice>(items.Where(x=>marked.Contains(x.Value)));var panel=new StackPanel();
            var search=new WpfTextBox{Padding=new Thickness(6),ToolTip="Pesquisar"};
            var buttons=new StackPanel{Orientation=Orientation.Horizontal};
            var all=new Button{Content="Marcar resultados",Margin=new Thickness(0,4,8,4)};
            var none=new Button{Content="Desmarcar todos",Margin=new Thickness(0,4,0,4)};
            buttons.Children.Add(all);buttons.Children.Add(none);
            var rows=new StackPanel();Action refresh=()=>{
                rows.Children.Clear();foreach(var item in items.Where(x=>x.Label.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0)){
                    var choice=item;var cb=new CheckBox{Content=choice.Label,IsChecked=selected.Contains(choice),Margin=new Thickness(4)};
                    cb.Checked+=(a,b)=>selected.Add(choice);cb.Unchecked+=(a,b)=>selected.Remove(choice);rows.Children.Add(cb);
                }
            };
            search.TextChanged+=(a,b)=>refresh();
            all.Click+=(a,b)=>{foreach(var item in items.Where(x=>x.Label.IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0))selected.Add(item);refresh();};
            none.Click+=(a,b)=>{selected.Clear();refresh();};
            panel.Children.Add(search);panel.Children.Add(buttons);panel.Children.Add(new ScrollViewer{Content=rows,Height=185,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            panel.Tag=selected;Add(key,label,panel);refresh();return this;
        }
        public Form Grid<T>(string key,string label,ObservableCollection<T> rows,bool editable=true)
        { Add(key,label,new DataGrid { ItemsSource=rows,AutoGenerateColumns=true,IsReadOnly=!editable,CanUserAddRows=editable,CanUserDeleteRows=editable,Height=240 }); return this; }
        public string Get(string key) => ((WpfTextBox)controls[key]).Text.Trim();
        public bool Yes(string key) => ((CheckBox)controls[key]).IsChecked==true;
        public T Pick<T>(string key)
        {var choice=((WpfComboBox)controls[key]).SelectedItem as Choice;if(choice==null)throw new ArgumentException("Selecione uma opção válida no campo: "+key);return (T)choice.Value;}
        public List<T> Picks<T>(string key) => ((HashSet<Choice>)controls[key].Tag).Select(c=>(T)c.Value).ToList();
        public double Number(string key) => Logic.Number(Get(key));
        public bool Run() => ShowDialog()==true;
        public static void ShowIssues<T>(UIApplication app,string title,IEnumerable<T> rows,Func<T,bool> isIssue){var issues=rows.Where(isIssue).ToList();if(issues.Count>0)ShowRows(app,title,issues,"Confira os itens que precisam de atenção.");}
        public static void ShowChangeIssues(UIApplication app,string title,IEnumerable<Change> rows)=>ShowIssues(app,title,rows,r=>r.Estado!="Concluído"&&r.Estado!="Mesmo destino já incluído");
        public static void ShowRows<T>(UIApplication app,string title,IEnumerable<T> rows,string text="",bool confirm=false)
        { var f=new Form(app,title,text);f.ActionLabel(confirm?"Continuar":"Fechar");f.Grid("result","",new ObservableCollection<T>(rows),false);if(!f.Run()&&confirm)throw new Cancelled(); }
        public static bool Preview<T>(UIApplication app,string title,IEnumerable<T> rows,string text,string actionLabel="Aplicar")
        { var f=new Form(app,title,text); f.apply.Content=actionLabel;f.Grid("result","",new ObservableCollection<T>(rows),false);return f.Run(); }
    }
    public sealed class Cancelled : Exception { }
}
