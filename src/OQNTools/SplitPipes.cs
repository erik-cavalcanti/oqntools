using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace OQNTools
{
    [Transaction(TransactionMode.Manual)]
    public sealed class SplitPipes : Command
    {
        public sealed class Row
        {
            public long Tubo {get;set;}
            public double Comprimento_m {get;set;}
            public int Cortes {get;set;}
            public string Trechos_m {get;set;}
            public string Resultado {get;set;}
            internal Pipe Pipe;internal List<double> Distances;
        }
        protected override void Run(Context c)
        {
            if(c.Doc.IsFamilyDocument)throw new ArgumentException("Abra um projeto Revit.");
            // Deliberately document-wide: no active view or selection collector.
            var pipes=new FilteredElementCollector(c.Doc).OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
            var types=pipes.Select(p=>p.GetTypeId()).Distinct().Select(id=>c.Doc.GetElement(id)).OfType<PipeType>().OrderBy(t=>t.Name).ToList();
            if(types.Count==0){c.Done("Não há tubos rígidos no projeto atual.");return;}
            var f=new Form(c.App,"Split Pipes · Cortar tubos","Escopo: TODO o projeto atual. Corta os tubos do tipo escolhido no comprimento informado, medido ao longo do eixo. A sobra permanece como último trecho. Vínculos não são alterados.")
                .Select("type","Tipo de tubo",types.Select(t=>new Choice(t.Name+" ("+pipes.Count(p=>p.GetTypeId()==t.Id)+" tubos)",t)))
                .Text("length","Comprimento dos trechos (metros)","3,0");
            double minimum=Math.Max(c.App.Application.ShortCurveTolerance,0.1/12.0)+1e-6;
            f.Validate=()=>{double v=f.Number("length");if(double.IsNaN(v)||double.IsInfinity(v)||v/0.3048<=minimum)throw new ArgumentException("Informe um comprimento maior que "+(minimum*304.8).ToString("F2")+" mm.");};
            if(!f.Run())return;
            var type=f.Pick<PipeType>("type");double step=f.Number("length")/0.3048;var rows=new List<Row>();
            foreach(var pipe in pipes.Where(p=>p.GetTypeId()==type.Id))
            {
                var row=new Row{Tubo=pipe.Id.OqnValue(),Pipe=pipe};rows.Add(row);
                try{
                    var line=(pipe.Location as LocationCurve)?.Curve as Line;
                    if(line==null)throw new ArgumentException("Apenas tubos rígidos retos são suportados.");
                    row.Comprimento_m=Math.Round(line.Length*0.3048,4);
                    if(pipe.Pinned||pipe.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("Tubo fixado ou em grupo.");
                    row.Distances=PipeSplitPlan.Cuts(line.Length,step,minimum);
                    row.Cortes=row.Distances.Count;row.Resultado=row.Cortes==0?"Já está no comprimento ou abaixo":"Pronto";
                }catch(Exception ex){row.Resultado="Ignorado: "+ex.Message;row.Distances=null;}
            }
            if(!Form.Preview(c.App,"Split Pipes · Prévia do projeto inteiro",rows,rows.Sum(r=>r.Cortes)+" corte(s) previsto(s). Será inserida uma luva por divisão, pelas preferências de roteamento do tipo. O comprimento informa a distância entre cortes; a geometria da luva pode ajustar as pontas dos trechos. Cada tubo com falha será preservado integralmente."))return;
            using(var group=new TransactionGroup(c.Doc,"OQN · Split Pipes")){
                group.Start();foreach(var row in rows.Where(r=>r.Distances!=null&&r.Cortes>0)){
                    try{c.Mutate("Cortar tubo "+row.Tubo,()=>row.Trechos_m=Split(c.Doc,row.Pipe,row.Distances,minimum));row.Resultado="Cortado";}
                    catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}
                    catch(Exception ex){row.Trechos_m="";row.Resultado="Preservado: "+Errors.Describe(ex);}
                }group.Assimilate();
            }
            Form.ShowIssues(c.App,"Split Pipes · Atenção",rows,r=>r.Resultado!="Cortado"&&r.Resultado!="Já está no comprimento ou abaixo");
        }
        static List<Connector> Ends(Pipe pipe)=>pipe.ConnectorManager.Connectors.Cast<Connector>().Where(x=>x.ConnectorType==ConnectorType.End).ToList();
        static List<Connector> Physical(Pipe pipe)=>pipe.ConnectorManager.Connectors.Cast<Connector>().Where(x=>x.ConnectorType==ConnectorType.End||x.ConnectorType==ConnectorType.Curve||x.ConnectorType==ConnectorType.Physical).ToList();
        static string Split(Document doc,Pipe original,List<double> cuts,double minimum)
        {
            var line=(Line)((LocationCurve)original.Location).Curve;var start=line.GetEndPoint(0);var direction=(line.GetEndPoint(1)-start).Normalize();
            double length=line.Length;var type=original.GetTypeId();double diameter=original.Diameter;
            // Record external connections before breaking; verify they survive on the resulting chain.
            var neighbors=Physical(original).SelectMany(x=>x.AllRefs.Cast<Connector>()).Where(x=>x.Owner.Id!=original.Id&&(x.ConnectorType==ConnectorType.End||x.ConnectorType==ConnectorType.Curve||x.ConnectorType==ConnectorType.Physical)).ToList();
            var ids=new List<ElementId>{original.Id};var cutPoints=new List<XYZ>();
            foreach(double distance in cuts.OrderByDescending(x=>x)){
                var point=start+direction*distance;
                var candidate=ids.Select(id=>doc.GetElement(id)).OfType<Pipe>().Single(p=>{
                    var curve=((LocationCurve)p.Location).Curve;var projection=curve.Project(point);
                    double a=(curve.GetEndPoint(0)-start).DotProduct(direction),b=(curve.GetEndPoint(1)-start).DotProduct(direction);
                    return distance>Math.Min(a,b)+minimum&&distance<Math.Max(a,b)-minimum&&projection!=null&&projection.XYZPoint.DistanceTo(point)<1e-6;
                });
                var id=PlumbingUtils.BreakCurve(doc,candidate.Id,point);
                if(id==ElementId.InvalidElementId)throw new InvalidOperationException("A API recusou o corte.");
                ids.Add(id);cutPoints.Add(point);doc.Regenerate();
            }
            var unions=new List<ElementId>();
            foreach(var point in cutPoints){
                var ends=ids.Select(id=>(Pipe)doc.GetElement(id)).SelectMany(Ends).Where(x=>x.Origin.DistanceTo(point)<1e-6).ToList();
                if(ends.Count!=2)throw new InvalidOperationException("Não foi possível localizar os dois conectores da divisão.");
                if(ends[0].IsConnectedTo(ends[1]))ends[0].DisconnectFrom(ends[1]);
                var union=doc.Create.NewUnionFitting(ends[0],ends[1]);
                if(union==null)throw new ArgumentException("Não foi criada a luva. Confira as preferências de roteamento do tipo e a faixa de diâmetros.");
                unions.Add(union.Id);doc.Regenerate();
                var joined=Mep.Connectors(union);
                if(!joined.Any(x=>x.IsConnectedTo(ends[0]))||!joined.Any(x=>x.IsConnectedTo(ends[1])))throw new ArgumentException("A luva não conectou os dois trechos; tubo original preservado.");
            }
            var result=ids.Select(id=>doc.GetElement(id)).OfType<Pipe>().ToList();
            var expected=cuts.Concat(new[]{length}).OrderBy(x=>x).ToList();
            var actual=result.OrderBy(p=>Math.Min(((LocationCurve)p.Location).Curve.GetEndPoint(0).Subtract(start).DotProduct(direction),((LocationCurve)p.Location).Curve.GetEndPoint(1).Subtract(start).DotProduct(direction))).ToList();
            if(actual.Count!=expected.Count)throw new InvalidOperationException("Quantidade de trechos inesperada.");
            for(int i=0;i<actual.Count;i++){
                var curve=((LocationCurve)actual[i].Location).Curve;
                if(actual[i].GetTypeId()!=type||Math.Abs(actual[i].Diameter-diameter)>1e-7||curve.Length<=minimum||curve.GetEndPoint(0).Subtract(start).CrossProduct(direction).GetLength()>1e-5||curve.GetEndPoint(1).Subtract(start).CrossProduct(direction).GetLength()>1e-5)throw new InvalidOperationException("Tipo, diâmetro ou comprimento alterado inesperadamente.");
            }
            var endpoints=result.SelectMany(p=>new[]{((LocationCurve)p.Location).Curve.GetEndPoint(0),((LocationCurve)p.Location).Curve.GetEndPoint(1)}).ToList();
            if(!endpoints.Any(p=>p.DistanceTo(start)<1e-5)||!endpoints.Any(p=>p.DistanceTo(start+direction*length)<1e-5))throw new ArgumentException("As extremidades originais seriam deslocadas; tubo preservado.");
            if(unions.Count!=cuts.Count)throw new ArgumentException("Quantidade de luvas inesperada.");
            foreach(var neighbor in neighbors)
                if(!result.SelectMany(Physical).Any(x=>x.IsConnectedTo(neighbor)))throw new InvalidOperationException("Uma conexão externa seria perdida.");
            return string.Join(" + ",actual.Select(p=>(((LocationCurve)p.Location).Curve.Length*.3048).ToString("F4")));
        }
    }
}
