using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;

namespace OQNTools
{
    [Transaction(TransactionMode.Manual)] public sealed class Bloom : Command
    {
        protected override void Run(Context c)
        {
            var elements=c.Scope("Seleção").ToList();c.NeedSelection(elements);
            var connectors=elements.SelectMany(Mep.Connectors).Where(x=>x.ConnectorType==ConnectorType.End&&!x.IsConnected&&x.Domain==Domain.DomainPiping&&x.Shape==ConnectorProfileType.Round).ToList();
            if(connectors.Count==0)throw new ArgumentException("Não há conectores livres de tubulação nos elementos selecionados.");
            var f=new Form(c.App,"Prolongar conectores","Cada conector fornece seu próprio diâmetro, sistema e direção. Selecione os elementos no modelo antes de abrir a ferramenta.")
                .Select("type","Tipo de tubo",c.All<PipeType>().OrderBy(t=>t.Name).Select(t=>new Choice(t.Name,t)))
                .Text("length","Comprimento (mm)","150")
                .Select("mode","Conectores",new[]{new Choice("Todos os conectores livres",false),new Choice("Escolher conectores",true)}).ActionLabel("Prolongar");
            f.Validate=()=>{if(f.Number("length")/304.8<=Math.Max(c.App.Application.ShortCurveTolerance,.1/12))throw new ArgumentException("Comprimento abaixo do mínimo do Revit.");};if(!f.Run())return;
            if(f.Pick<bool>("mode")){
                var pick=new Form(c.App,"Escolher conectores","Marque os conectores livres a prolongar.").CheckList("items","Conectores",connectors.Select(x=>new Choice(x.Owner.Name+" ["+x.Owner.Id.OqnValue()+"] · conector "+x.Id+" · Ø "+(x.Radius*609.6).ToString("F1")+" mm",x))).ActionLabel("Prolongar");
                if(!pick.Run())return;connectors=pick.Picks<Connector>("items");
            }
            var levels=c.All<Level>();var type=f.Pick<PipeType>("type");double length=f.Number("length")/304.8;var rows=new List<Change>();
            foreach(var connector in connectors){
                var row=new Change{Id=connector.Owner.Id.OqnValue(),Elemento=connector.Owner.Name+" · conector "+connector.Id,Depois=f.Get("length")+" mm",Estado="Pronto"};rows.Add(row);
                row.Apply=()=>{
                    if(connector.IsConnected)throw new ArgumentException("Conector já conectado; ignorado.");
                    var level=c.Doc.GetElement(connector.Owner.LevelId) as Level??levels.OrderBy(l=>Math.Abs(l.Elevation-connector.Origin.Z)).FirstOrDefault();
                    if(level==null)throw new ArgumentException("Nenhum nível disponível no projeto.");
                    var start=connector.Origin;double diameter=connector.Radius*2;var end=start+connector.CoordinateSystem.BasisZ.Normalize()*length;
                    var pipe=Pipe.Create(c.Doc,type.Id,level.Id,connector,end);c.Doc.Regenerate();
                    if(Math.Abs(pipe.Diameter-diameter)>1e-6)throw new ArgumentException("O tipo escolhido não aceitou o diâmetro deste conector.");
                    if(Math.Abs(((LocationCurve)pipe.Location).Curve.Length-length)>1e-5)throw new ArgumentException("Não foi possível manter o comprimento pedido neste conector.");
                };
            }
            Batch.Run(c,"Prolongar conectores",rows);Form.ShowChangeIssues(c.App,"Prolongamentos",rows);
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class SectionGlance : Command
    {
        protected override void Run(Context c)
        {var selected=c.Scope("Seleção").ToList();c.NeedSelection(selected);FilterElements.Box(c,selected,.5);}
    }
}
