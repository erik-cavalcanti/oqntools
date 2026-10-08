using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using TextBox=System.Windows.Controls.TextBox;
using ComboBox=System.Windows.Controls.ComboBox;
namespace OQNTools
{
    // This control contains strings only. No Revit API access or native object binding.
    public sealed class RuleEditor : StackPanel
    {
        readonly StackPanel rows=new StackPanel();
        readonly Dictionary<Rule,Action> sync=new Dictionary<Rule,Action>();
        readonly Dictionary<Rule,ComboBox> joins=new Dictionary<Rule,ComboBox>();
        public readonly List<Rule> Rules=new List<Rule>();
        static readonly string[] Operators={"=","!=","contém","não contém","começa","termina","vazio",">",">=","<","<="};
        public RuleEditor(IEnumerable<Rule> initial)
        {
            Children.Add(new TextBlock{Text="Digite o nome do parâmetro. E exige as condições juntas; OU inicia uma alternativa. Sem regras, busca todos os elementos das categorias escolhidas.",TextWrapping=TextWrapping.Wrap});
            Children.Add(rows);var add=new Button{Content="+ Adicionar regra"};add.Click+=(a,b)=>Add(new Rule{Grupo=Rules.Count==0?1:Rules.Last().Grupo});Children.Add(add);
            foreach(var r in (initial??Enumerable.Empty<Rule>()).OrderBy(x=>x.Grupo))Add(r);
        }
        void Add(Rule r)
        {
            bool alternate=Rules.Count>0&&Rules.Last().Grupo!=r.Grupo;Rules.Add(r);var panel=new StackPanel{Margin=new Thickness(0,7,0,7)};
            var first=new DockPanel();var remove=new Button{Content="Remover"};DockPanel.SetDock(remove,Dock.Right);first.Children.Add(remove);
            var join=new ComboBox{ItemsSource=new[]{"E — atender também","OU — alternativa"},SelectedIndex=alternate?1:0,MinHeight=28,Margin=new Thickness(0,0,0,5),Visibility=Rules.Count==1?Visibility.Collapsed:Visibility.Visible};joins[r]=join;panel.Children.Add(join);
            panel.Children.Add(new TextBlock{Text="Nome do parâmetro",Margin=new Thickness(0,2,0,3)});
            var param=new TextBox{Text=r.Parametro,ToolTip="Nome do parâmetro ou @GUID",MinHeight=28};first.Children.Add(param);panel.Children.Add(first);
            var second=new DockPanel();var op=new ComboBox{Width=140,ItemsSource=Operators,SelectedItem=Operators.Contains(r.Operador)?r.Operador:"="};DockPanel.SetDock(op,Dock.Left);second.Children.Add(op);var value=new TextBox{Text=r.Valor};second.Children.Add(value);panel.Children.Add(second);rows.Children.Add(panel);
            sync[r]=()=>{r.Parametro=param.Text.Trim();r.Operador=(string)op.SelectedItem;r.Valor=value.Text;};
            remove.Click+=(a,b)=>{Rules.Remove(r);sync.Remove(r);joins.Remove(r);rows.Children.Remove(panel);if(Rules.Count>0)joins[Rules[0]].Visibility=Visibility.Collapsed;};
        }
        public void Validate(){foreach(var update in sync.Values)update();int group=1;for(int i=0;i<Rules.Count;i++){if(i>0&&joins[Rules[i]].SelectedIndex==1)group++;Rules[i].Grupo=group;}if(Rules.Any(r=>string.IsNullOrWhiteSpace(r.Parametro)))throw new ArgumentException("Informe o nome do parâmetro em cada regra.");}
        public void Save(string path){Validate();new XDocument(new XElement("OQNRules",new XAttribute("version","3"),Rules.Select(r=>new XElement("Rule",new XAttribute("group",r.Grupo),new XAttribute("parameter",r.Parametro),new XAttribute("scope",r.Scope),new XAttribute("operator",r.Operador),new XAttribute("value",r.Valor??""))))).Save(path);}
        public void Load(string path)
        {
            var xml=XDocument.Load(path);if(xml.Root?.Name!="OQNRules" && xml.Root?.Name!="ErarikRules")throw new ArgumentException("Conjunto XML inválido.");var loaded=new List<Rule>();
            foreach(var node in xml.Root.Elements("Rule")){
                string name=(string)node.Attribute("parameter")??"",scope=(string)node.Attribute("scope")??"auto",key=(string)node.Attribute("key");
                if(name.StartsWith("I:")||name.StartsWith("T:")){scope=name.StartsWith("I:")?"instance":"type";name=name.Substring(2);}
                else if(key!=null){var oldScope=key.Split('|')[0];if(new[]{"auto","instance","type"}.Contains(oldScope))scope=oldScope;}
                string op=(string)node.Attribute("operator")??"=";int group=(int?)node.Attribute("group")??1;
                if(string.IsNullOrWhiteSpace(name)||!Operators.Contains(op)||!new[]{"auto","instance","type"}.Contains(scope)||group<1)throw new ArgumentException("Regra XML inválida.");
                loaded.Add(new Rule{Parametro=name.Trim(),Scope=scope,Grupo=group,Operador=op,Valor=(string)node.Attribute("value")??""});
            }
            Rules.Clear();sync.Clear();joins.Clear();rows.Children.Clear();foreach(var r in loaded.OrderBy(x=>x.Grupo))Add(r);
        }
    }
    public sealed class ElementResult
    { public long Id;public string Label; }
    public sealed class ElementResults : StackPanel
    {
        readonly StackPanel rows=new StackPanel();readonly TextBlock count=new TextBlock();readonly List<CheckBox> boxes=new List<CheckBox>();
        public ElementResults(){Children.Add(count);Children.Add(new ScrollViewer{Content=rows,MaxHeight=180,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});}
        public void Set(IEnumerable<ElementResult> elements,bool searched=false){rows.Children.Clear();boxes.Clear();foreach(var e in elements){var cb=new CheckBox{Tag=e.Id,IsChecked=true,Content=e.Label,Margin=new Thickness(2)};rows.Children.Add(cb);boxes.Add(cb);}count.Text=boxes.Count+" elementos encontrados";Visibility=searched||boxes.Count>0?Visibility.Visible:Visibility.Collapsed;}
        public List<long> Selected=>boxes.Where(x=>x.IsChecked==true).Select(x=>(long)x.Tag).ToList();
    }
}
