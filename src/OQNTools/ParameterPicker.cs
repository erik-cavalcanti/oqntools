using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ComboBox=System.Windows.Controls.ComboBox;
using TextBox=System.Windows.Controls.TextBox;
namespace OQNTools
{
    public sealed class ParameterPicker : StackPanel
    {
        readonly List<ParameterOption> options;readonly StackPanel rows=new StackPanel();
        readonly List<ComboBox> selections=new List<ComboBox>();
        public ParameterPicker(IEnumerable<ParameterOption> values){options=values.ToList();Children.Add(rows);var add=new Button{Content="+ Adicionar parâmetro",Margin=new Thickness(0,6,0,6)};add.Click+=(s,e)=>AddRow();Children.Add(add);AddRow();}
        void AddRow(){
            var row=new DockPanel{Margin=new Thickness(0,4,0,4)};var remove=new Button{Content="Remover"};var up=new Button{Content="↑",ToolTip="Subir"};var down=new Button{Content="↓",ToolTip="Descer"};
            var cb=new ComboBox{ItemsSource=options,DisplayMemberPath="Label",MinWidth=300,IsTextSearchEnabled=true,SelectedIndex=0};
            foreach(var b in new[]{remove,down,up}){DockPanel.SetDock(b,Dock.Right);row.Children.Add(b);}row.Children.Add(cb);rows.Children.Add(row);selections.Add(cb);
            remove.Click+=(s,e)=>{rows.Children.Remove(row);selections.Remove(cb);};
            Action<int> move=delta=>{int i=rows.Children.IndexOf(row),n=i+delta;if(n<0||n>=rows.Children.Count)return;rows.Children.RemoveAt(i);rows.Children.Insert(n,row);selections.RemoveAt(i);selections.Insert(n,cb);};
            up.Click+=(s,e)=>move(-1);down.Click+=(s,e)=>move(1);
        }
        public List<ParameterOption> Selected=>selections.Select(x=>x.SelectedItem as ParameterOption).Where(x=>x!=null).ToList();
    }
}
