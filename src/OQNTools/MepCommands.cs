using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI.Selection;

namespace OQNTools
{
    internal static class Mep
    {
        public static List<Connector> Connectors(Element e)
        {
            ConnectorManager cm=(e as MEPCurve)?.ConnectorManager??(e as FamilyInstance)?.MEPModel?.ConnectorManager??(e as FabricationPart)?.ConnectorManager;
            return cm==null?new List<Connector>():cm.Connectors.Cast<Connector>().Where(c=>c.ConnectorType==ConnectorType.End||c.ConnectorType==ConnectorType.Curve||c.ConnectorType==ConnectorType.Physical).ToList();
        }
        public static Element Pick(Context c,string prompt)=>c.Doc.GetElement(c.Ui.Selection.PickObject(ObjectType.Element,new MepSelection(),prompt));
        public static Connector Nearest(Element e,XYZ point,bool open=true)=>Connectors(e).Where(x=>!open||!x.IsConnected).OrderBy(x=>x.Origin.DistanceTo(point)).FirstOrDefault()??throw new ArgumentException("Nenhum conector "+(open?"livre ":"")+"no elemento.");
        public static Tuple<Connector,Connector> Pair(Element a,Element b,bool requireOpen=true)
        {
            var pairs=from x in Connectors(a) where (!requireOpen||!x.IsConnected) from y in Connectors(b) where (!requireOpen||!y.IsConnected)&&x.Domain==y.Domain select Tuple.Create(x,y);
            return pairs.OrderBy(p=>p.Item1.Origin.DistanceTo(p.Item2.Origin)).FirstOrDefault()??throw new ArgumentException("Nenhum par de conectores livres do mesmo domínio.");
        }
        public static void RotateTo(Context c,Element element,XYZ origin,XYZ from,XYZ to)
        {
            double angle=from.AngleTo(to);if(angle<1e-8)return;var axis=from.CrossProduct(to);if(axis.GetLength()<1e-8)axis=from.CrossProduct(Math.Abs(from.Z)<.9?XYZ.BasisZ:XYZ.BasisX);ElementTransformUtils.RotateElement(c.Doc,element.Id,Line.CreateUnbound(origin,axis.Normalize()),angle);c.Doc.Regenerate();
        }
        public static void Connect(Context c,Connector a,Connector b)
        {
            if(a.Owner is FabricationPart&&b.Owner is FabricationPart)
            {
                var method=typeof(FabricationPart).GetMethod("ConnectAndCouple",new[]{typeof(Document),typeof(Connector),typeof(Connector)});
                if(method==null)throw new NotSupportedException("Esta API não expõe FabricationPart.ConnectAndCouple com a assinatura esperada.");
                object result=method.Invoke(null,new object[]{c.Doc,a,b});if(result is bool ok&&!ok)throw new InvalidOperationException("O Revit não conseguiu criar o acoplamento.");c.Doc.Regenerate();if(!a.IsConnected||!b.IsConnected)throw new InvalidOperationException("Acoplamento não conectado; operação revertida.");return;
            }
            if(a.Owner is FabricationPart||b.Owner is FabricationPart)throw new ArgumentException("Selecione duas peças de fabricação ou dois elementos de projeto; não misture os dois tipos.");
            a.ConnectTo(b);c.Doc.Regenerate();
        }
        internal static bool Joined(Connector a,Connector b,HashSet<long> existingFittings)
        {
            // Only the chosen ports and newly created fittings may establish this path.
            // Existing system membership or an unrelated connection is not proof of success.
            var ports=new Dictionary<Tuple<long,int>,Connector>();
            Func<Connector,Tuple<long,int>> key=c=>{var k=Tuple.Create(c.Owner.Id.OqnValue(),c.Id);ports[k]=c;return k;};
            var start=key(a);var end=key(b);
            Func<Tuple<long,int>,bool> bridge=k=>!existingFittings.Contains(k.Item1)&&
                (ports[k].Owner is FabricationPart || (ports[k].Owner is FamilyInstance f && f.MEPModel is MechanicalFitting));
            return ConnectedGraph.ReachesThrough(start,end,k=>
            {
                var port=ports[k];var next=ConnectedMove.Peers(port).Select(key).ToList();
                if(bridge(k))next.AddRange(Connectors(port.Owner).Select(key));
                return next;
            },bridge);
        }
        public static MEPCurve Segment(Context c,MEPCurve source,XYZ start,XYZ end)
        {
            var level=source.ReferenceLevel;if(level==null)throw new ArgumentException("Trecho sem nível de referência.");MEPCurve result;
            if(source is Pipe p){var sys=p.MEPSystem?.GetTypeId()??p.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId();if(sys==null||sys==ElementId.InvalidElementId)throw new ArgumentException("Tubo sem tipo de sistema.");result=Pipe.Create(c.Doc,sys,source.GetTypeId(),level.Id,start,end);}
            else if(source is Duct d){var sys=d.MEPSystem?.GetTypeId()??d.get_Parameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM)?.AsElementId();if(sys==null||sys==ElementId.InvalidElementId)throw new ArgumentException("Duto sem tipo de sistema.");result=Duct.Create(c.Doc,sys,source.GetTypeId(),level.Id,start,end);}
            else if(source is Conduit)result=Conduit.Create(c.Doc,source.GetTypeId(),start,end,level.Id);
            else throw new ArgumentException("Use tubo, duto ou conduíte de projeto.");
            foreach(var bip in new[]{BuiltInParameter.RBS_PIPE_DIAMETER_PARAM,BuiltInParameter.RBS_CURVE_DIAMETER_PARAM,BuiltInParameter.RBS_CURVE_WIDTH_PARAM,BuiltInParameter.RBS_CURVE_HEIGHT_PARAM,BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM})
            {var a=source.get_Parameter(bip);var b=result.get_Parameter(bip);if(a!=null&&b!=null&&!b.IsReadOnly&&a.StorageType==StorageType.Double)b.Set(a.AsDouble());}
            c.Doc.Regenerate();return result;
        }
    }
    sealed class MepSelection:ISelectionFilter
    {public bool AllowElement(Element e)=>Mep.Connectors(e).Count>0;public bool AllowReference(Reference r,XYZ p)=>false;}

    [Transaction(TransactionMode.Manual)] public sealed class MoveConnect:Command
    {
        protected override void Run(Context c)
        {
            var preselection=c.Ui.Selection.GetElementIds().ToList();
            var destination=Mep.Pick(c,"Selecione o destino (fica parado)");
            var moving=Mep.Pick(c,"Selecione a peça a mover e conectar; o Revit ajusta as peças ligadas");
            if(destination.Id==moving.Id)throw new ArgumentException("Escolha dois elementos diferentes.");
            ConnectedMove.Run(c,destination,moving,false,preselection);
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class MoveConnectAlign:Command
    {
        protected override void Run(Context c)
        {
            var destination=Mep.Pick(c,"Selecione o destino (fica parado; define a orientação)");
            var moving=Mep.Pick(c,"Selecione a peça a mover, alinhar e conectar; o Revit ajusta as peças ligadas");
            if(destination.Id==moving.Id)throw new ArgumentException("Escolha dois elementos diferentes.");
            ConnectedMove.Run(c,destination,moving,true);
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class Disconnect:Command
    {
        protected override void Run(Context c)
        {
            var a=Mep.Pick(c,"Selecione o primeiro elemento conectado");var b=Mep.Pick(c,"Selecione o segundo elemento conectado");var pairs=(from x in Mep.Connectors(a) from y in Mep.Connectors(b) where x.IsConnectedTo(y) select Tuple.Create(x,y)).ToList();if(pairs.Count==0)throw new ArgumentException("Não existe conexão direta entre os elementos.");c.Mutate("Desconectar",()=>{foreach(var p in pairs)p.Item1.DisconnectFrom(p.Item2);});
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class DeleteSystem:Command
    {
        protected override void Run(Context c)
        {
            var elements=c.Scope("Seleção").ToList();c.NeedSelection(elements);var rows=new List<Change>();
            foreach(var element in elements){var e=element;var row=new Change{Id=e.Id.OqnValue(),Elemento=e.Name,Estado="Pronto",Depois="Desconectar dos vizinhos"};rows.Add(row);
                row.Apply=()=>{
                    var connectors=Mep.Connectors(e);int count=0;
                    foreach(var connector in connectors)foreach(var other in connector.AllRefs.Cast<Connector>().Where(x=>x.Owner.Id!=e.Id&&x.ConnectorType!=ConnectorType.Logical).ToList())
                        if(connector.IsConnectedTo(other)){connector.DisconnectFrom(other);count++;}
                    if(count==0)throw new ArgumentException("Sem conexões externas para desfazer.");
                };
            }
            if(!Form.Preview(c.App,"Desconectar do sistema",rows,"Rompe a conectividade dos elementos selecionados, mantendo sua posição. Não exclui sistemas nem elementos.","Desconectar"))return;
            Batch.Run(c,"Desconectar do sistema",rows);Form.ShowChangeIssues(c.App,"Desconexão",rows);
        }
    }
    public abstract class ElbowBase:Command
    {
        protected abstract string Direction {get;}
        protected override void Run(Context c)
        {
            var f=new Form(c.App,"Curva · "+Direction,"Clique perto da extremidade livre de um tubo, duto ou conduíte reto. A conexão usa as preferências de roteamento do tipo.").Text("length","Comprimento do novo trecho (m)","0,5");
            f.Validate=()=>{if(f.Number("length")<=.01)throw new ArgumentException("Informe comprimento maior que 0,01 m.");};if(!f.Run())return;
            var reference=c.Ui.Selection.PickObject(ObjectType.Element,new MepSelection(),"Clique perto da extremidade livre");var element=c.Doc.GetElement(reference);if(!(element is MEPCurve curve)||!((curve.Location as LocationCurve)?.Curve is Line))throw new ArgumentException("Use um trecho reto de tubo, duto ou conduíte.");
            var connector=Mep.Nearest(element,reference.GlobalPoint);var outward=connector.CoordinateSystem.BasisZ.Normalize();XYZ direction;
            if(Direction=="Cima")direction=XYZ.BasisZ;else if(Direction=="Baixo")direction=XYZ.BasisZ.Negate();
            else if(Direction=="Baixo 45°"){var horizontal=new XYZ(outward.X,outward.Y,0);if(horizontal.GetLength()<1e-8)throw new ArgumentException("Baixo 45° requer trecho com direção horizontal.");direction=(horizontal.Normalize()+XYZ.BasisZ.Negate()).Normalize();}
            else {direction=c.View.ViewDirection.CrossProduct(outward);if(direction.GetLength()<1e-8)throw new ArgumentException("O eixo é perpendicular à vista; use outra vista.");direction=direction.Normalize().Multiply(Direction=="Esquerda"?1:-1);}
            if(Math.Abs(outward.DotProduct(direction))>.999)throw new ArgumentException("A direção escolhida é colinear com o trecho; não forma um joelho.");
            var start=connector.Origin;var end=start+direction.Multiply(UnitUtils.ConvertToInternalUnits(f.Number("length"),UnitTypeId.Meters));
            c.Mutate("Criar curva "+Direction,()=>{var segment=Mep.Segment(c,curve,start,end);var other=Mep.Nearest(segment,start);c.Doc.Create.NewElbowFitting(connector,other);});
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class ElbowUp:ElbowBase{protected override string Direction=>"Cima";}
    [Transaction(TransactionMode.Manual)] public sealed class ElbowDown:ElbowBase{protected override string Direction=>"Baixo";}
    [Transaction(TransactionMode.Manual)] public sealed class ElbowDown45:ElbowBase{protected override string Direction=>"Baixo 45°";}
    [Transaction(TransactionMode.Manual)] public sealed class ElbowLeft:ElbowBase{protected override string Direction=>"Esquerda";}
    [Transaction(TransactionMode.Manual)] public sealed class ElbowRight:ElbowBase{protected override string Direction=>"Direita";}

    public abstract class RotateBase:Command
    {
        protected virtual bool HalfTurn=>false;
        protected override void Run(Context c)
        {
            var selected=c.Scope("Seleção").ToList();Element element=selected.Count==1?selected[0]:null;XYZ clickedPoint=null;
            if(element==null||RotationSelection.Axes(element).Count==0){var pick=c.Ui.Selection.PickObject(ObjectType.Element,new RotationSelection(),"Selecione um elemento com conectores perto do eixo de rotação");element=c.Doc.GetElement(pick);clickedPoint=pick.GlobalPoint;}
            var connectors=RotationSelection.Axes(element);if(connectors.Count==0)throw new ArgumentException("O elemento não possui conector com eixo geométrico disponível.");
            if(clickedPoint!=null)connectors=connectors.OrderBy(k=>k.Origin.DistanceTo(clickedPoint)).ToList();
            var form=new Form(c.App,HalfTurn?"Girar conexão 180°":"Rotate Fittings · Girar conexão","Escolha o conector que define o eixo. Os ângulos são incrementos em relação à posição atual, no sentido do eixo do conector.")
                .Select("axis","Eixo de rotação",connectors.Select(k=>new Choice("Conector "+k.Id+" · direção ("+k.Direction.X.ToString("0.##")+", "+k.Direction.Y.ToString("0.##")+", "+k.Direction.Z.ToString("0.##")+")",k.Id))).ActionLabel("Girar");
            double angle=180;bool preset=false;
            if(!HalfTurn){form.Text("angle","Ângulo personalizado (graus)","90");var buttons=new System.Windows.Controls.WrapPanel();foreach(double degrees in new[]{-180d,-90,-45,-30,-22.5,-15,15,22.5,30,45,90,180}){
                double chosen=degrees;var button=new System.Windows.Controls.Button{Content=degrees.ToString("0.##")+"°",MinWidth=68,Margin=new System.Windows.Thickness(4),Padding=new System.Windows.Thickness(8)};
                button.Click+=(o,e)=>{angle=chosen;preset=true;form.DialogResult=true;};buttons.Children.Add(button);
            }form.Custom("angles","Ângulos rápidos — clique para girar",buttons);}
            form.Validate=()=>{if(!HalfTurn&&Math.Abs(form.Number("angle"))<1e-9)throw new ArgumentException("Informe um ângulo diferente de zero.");};
            if(!form.Run())return;if(!HalfTurn&&!preset)angle=form.Number("angle");
            var connector=connectors.Single(k=>k.Id==form.Pick<int>("axis"));var axis=Line.CreateUnbound(connector.Origin,connector.Direction);
            // Rotate connected fittings directly. Native Revit failure handling must
            // remain available (e.g. resolving a connection affected by the rotation).
            // Do not use Context.Mutate, whose preprocessor rolls errors back immediately.
            using(var transaction=new Transaction(c.Doc,"OQN · Girar conexão"))
            {
                transaction.Start();
                var options=transaction.GetFailureHandlingOptions();
                options.SetForcedModalHandling(true);
                transaction.SetFailureHandlingOptions(options);
                ElementTransformUtils.RotateElement(c.Doc,element.Id,axis,angle*Math.PI/180);
                if(transaction.Commit()!=TransactionStatus.Committed)throw new Cancelled();
            }

        }
    }
    internal sealed class RotationSelection:ISelectionFilter
    {
        internal sealed class Axis
        {
            internal int Id;
            internal XYZ Origin,Direction;
        }
        // Accept connector-bearing elements by capability, not category or domain.
        // Logical ports have no geometric coordinate system and cannot define an axis.
        internal static List<Axis> Axes(Element element)
        {
            var manager=(element as FamilyInstance)?.MEPModel?.ConnectorManager
                ??(element as MEPCurve)?.ConnectorManager
                ??(element as FabricationPart)?.ConnectorManager
                ??(element as Wire)?.ConnectorManager;
            var axes=new List<Axis>();
            if(manager==null)return axes;
            foreach(Connector connector in manager.Connectors)
            {
                if(connector.ConnectorType==ConnectorType.Logical)continue;
                try
                {
                    var origin=connector.Origin;
                    var direction=connector.CoordinateSystem.BasisZ;
                    if(direction.GetLength()>1e-9)axes.Add(new Axis{Id=connector.Id,Origin=origin,Direction=direction.Normalize()});
                }
                catch(Autodesk.Revit.Exceptions.InvalidOperationException) { /* Port has no geometric axis. */ }
            }
            return axes.OrderBy(axis=>axis.Id).ToList();
        }
        public bool AllowElement(Element element)=>Axes(element).Count>0;
        public bool AllowReference(Reference reference,XYZ point)=>false;
    }
    [Transaction(TransactionMode.Manual)] public sealed class RotateFitting:RotateBase{}
    [Transaction(TransactionMode.Manual)] public sealed class Rotate180:RotateBase{protected override bool HalfTurn=>true;}
    [Transaction(TransactionMode.Manual)] public sealed class FlipWorkPlane:Command
    {
        protected override void Run(Context c)
        {
            var rows=new List<Change>();foreach(var e in c.Scope("Seleção").OfType<FamilyInstance>()){bool allowed=e.CanFlipWorkPlane&&!e.Pinned;rows.Add(new Change{Id=e.Id.OqnValue(),Elemento=e.Name,Depois="Inverter plano",Estado=allowed?"Pronto":"Ignorado: não permite inverter ou fixado",Apply=allowed?(Action)(()=>e.IsWorkPlaneFlipped=!e.IsWorkPlaneFlipped):null});}Changes.Apply(c,"Inverter plano de trabalho",rows);
        }
    }
    public abstract class AlignBranchBase:Command
    {
        protected abstract bool AdjustSlope {get;}
        protected override void Run(Context c){BranchMove.Run(c,AdjustSlope);}
    }
    [Transaction(TransactionMode.Manual)] public sealed class AlignBranchLite:AlignBranchBase{protected override bool AdjustSlope=>false;}
    [Transaction(TransactionMode.Manual)] public sealed class AlignBranch:AlignBranchBase{protected override bool AdjustSlope=>true;}
    [Transaction(TransactionMode.Manual)] public sealed class SectionByMep:Command
    {
        protected override void Run(Context c)
        {
            var e=Mep.Pick(c,"Selecione o trecho para criar o corte");if(!((e.Location as LocationCurve)?.Curve is Line line))throw new ArgumentException("Selecione um trecho reto.");
            var f=new Form(c.App,"Corte paralelo ao trecho","Cria um corte alinhado ao eixo e abre a nova vista.").Text("height","Altura do corte (m)","3").Text("depth","Profundidade (m)","1").Text("margin","Margem nas extremidades (m)","0,5");
            f.Validate=()=>{if(f.Number("height")<=0||f.Number("depth")<=0||f.Number("margin")<0)throw new ArgumentException("Confira as dimensões do corte.");};if(!f.Run())return;
            var right=line.Direction;var normal=right.CrossProduct(XYZ.BasisZ);if(normal.GetLength()<1e-8)normal=XYZ.BasisY;normal=normal.Normalize();var up=normal.CrossProduct(right).Normalize();
            var tr=Transform.Identity;tr.Origin=line.Evaluate(.5,true);tr.BasisX=right;tr.BasisY=up;tr.BasisZ=normal;
            double h=UnitUtils.ConvertToInternalUnits(f.Number("height"),UnitTypeId.Meters)/2,d=UnitUtils.ConvertToInternalUnits(f.Number("depth"),UnitTypeId.Meters),m=UnitUtils.ConvertToInternalUnits(f.Number("margin"),UnitTypeId.Meters);ViewSection section=null;
            c.Mutate("Corte por trecho",()=>{var type=c.All<ViewFamilyType>().First(t=>t.ViewFamily==ViewFamily.Section);section=ViewSection.CreateSection(c.Doc,type.Id,new BoundingBoxXYZ{Transform=tr,Min=new XYZ(-line.Length/2-m,-h,-d/2),Max=new XYZ(line.Length/2+m,h,d/2)});section.Name=DuplicateViews.UniqueName(c,"OQN Corte "+e.Id.OqnValue());});c.Ui.ActiveView=section;
        }
    }
}
