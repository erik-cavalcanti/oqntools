using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace OQNTools
{
    internal static class ConnectedMove
    {
        static bool Physical(Connector connector)=>connector.ConnectorType==ConnectorType.End||connector.ConnectorType==ConnectorType.Curve||connector.ConnectorType==ConnectorType.Physical;
        internal static IEnumerable<Connector> Peers(Connector connector)
        {
            foreach(Connector peer in connector.AllRefs)
                if(peer.Owner.Id!=connector.Owner.Id && Physical(peer) && connector.IsConnectedTo(peer))yield return peer;
        }
        internal static List<Element> Component(Document doc,Element seed)
        {
            var ids=ConnectedGraph.Collect(seed.Id.OqnValue(),id=>Mep.Connectors(doc.GetElement(OqnElementIdCompatibility.Create(id))).SelectMany(Peers).Select(p=>p.Owner.Id.OqnValue()));
            return ids.Select(id=>doc.GetElement(OqnElementIdCompatibility.Create(id))).ToList();
        }
        static Connector Port(Document doc,long owner,int id)
        {
            var element=doc.GetElement(OqnElementIdCompatibility.Create(owner));
            if(element==null)throw new InvalidOperationException("Um elemento da rede deixou de existir. Operação revertida.");
            return Mep.Connectors(element).SingleOrDefault(k=>k.Id==id)??throw new InvalidOperationException("Um conector da rede deixou de existir. Operação revertida.");
        }
        sealed class PortState
        {
            public long Owner;public int Id;public XYZ Origin,X,Z;
        }
        sealed class LinkState
        {
            public long A,B;public int PortA,PortB;
        }
        sealed class ElementState
        {
            public long Id;public XYZ[] Points;
        }
        internal sealed class Snapshot
        {
            readonly List<PortState> ports=new List<PortState>();
            readonly List<LinkState> links=new List<LinkState>();
            readonly List<ElementState> shapes=new List<ElementState>();
            static XYZ[] Points(Element e)
            {
                if(e.Location is LocationCurve lc)return new[]{lc.Curve.Evaluate(0,true),lc.Curve.Evaluate(.5,true),lc.Curve.Evaluate(1,true)};
                if(e.Location is LocationPoint lp)return new[]{lp.Point};
                return new XYZ[0];
            }
            public Snapshot(IEnumerable<Element> elements)
            {
                foreach(var element in elements)
                {
                    shapes.Add(new ElementState{Id=element.Id.OqnValue(),Points=Points(element)});
                    foreach(var port in Mep.Connectors(element))
                    {
                        ports.Add(new PortState{Owner=element.Id.OqnValue(),Id=port.Id,Origin=port.Origin,X=port.CoordinateSystem.BasisX,Z=port.CoordinateSystem.BasisZ});
                        foreach(var peer in Peers(port))
                            links.Add(new LinkState{A=element.Id.OqnValue(),PortA=port.Id,B=peer.Owner.Id.OqnValue(),PortB=peer.Id});
                    }
                }
            }
            public void Check(Document doc,Transform expected,string side)
            {
                const double tolerance=1e-5; // feet, approximately 0.003 mm
                foreach(var old in ports)
                {
                    var current=Port(doc,old.Owner,old.Id);
                    if(current.Origin.DistanceTo(expected.OfPoint(old.Origin))>tolerance ||
                       current.CoordinateSystem.BasisZ.DistanceTo(expected.OfVector(old.Z))>tolerance ||
                       current.CoordinateSystem.BasisX.DistanceTo(expected.OfVector(old.X))>tolerance)
                        throw new InvalidOperationException("O Revit alterou a geometria do "+side+" (elemento "+old.Owner+"). Operação revertida para preservar a rede.");
                }
                foreach(var old in shapes)
                {
                    var e=doc.GetElement(OqnElementIdCompatibility.Create(old.Id));if(e==null)throw new InvalidOperationException("Elemento removido durante o movimento. Operação revertida.");
                    var points=Points(e);
                    if(points.Length!=old.Points.Length || points.Where((p,i)=>p.DistanceTo(expected.OfPoint(old.Points[i]))>tolerance).Any())
                        throw new InvalidOperationException("O Revit deformou o "+side+" (elemento "+old.Id+"). Operação revertida.");
                }
                foreach(var link in links)
                    if(!Port(doc,link.A,link.PortA).IsConnectedTo(Port(doc,link.B,link.PortB)))
                        throw new InvalidOperationException("Uma ligação existente foi desfeita pelo Revit. Operação revertida.");
            }
        }
        public static void Run(Context c,Element destination,Element moving,bool align,ICollection<ElementId> preselection=null)
        {
            var pair=Mep.Pair(destination,moving,true);
            int destinationPort=pair.Item1.Id,movingPort=pair.Item2.Id;
            var existingFittings=new HashSet<long>(new FilteredElementCollector(c.Doc).OfClass(typeof(FamilyInstance)).ToElementIds().Select(id=>id.OqnValue()));
            existingFittings.UnionWith(new FilteredElementCollector(c.Doc).OfClass(typeof(FabricationPart)).ToElementIds().Select(id=>id.OqnValue()));
            Func<Connector> getDestination=()=>Port(c.Doc,destination.Id.OqnValue(),destinationPort);
            Func<Connector> getMoving=()=>Port(c.Doc,moving.Id.OqnValue(),movingPort);
            using(var group=new TransactionGroup(c.Doc,align?"OQN · Mover, conectar e alinhar":"OQN · Mover e conectar"))
            {
                group.Start();
                try
                {
                    c.Mutate(align?"Mover, conectar e alinhar":"Mover e conectar",()=>
                    {
                        var delta=getDestination().Origin-getMoving().Origin;
                        using(var move=new SubTransaction(c.Doc))
                        {
                            move.Start();
                            // Reference workflow: move the picked element (or an explicitly
                            // preselected set). Revit solves its attached elements naturally.
                            // Never require the recursively connected network to move rigidly.
                            if(!align&&preselection!=null&&preselection.Contains(moving.Id)&&!preselection.Contains(destination.Id))
                                ElementTransformUtils.MoveElements(c.Doc,preselection,delta);
                            else ElementTransformUtils.MoveElement(c.Doc,moving.Id,delta);
                            if(move.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("O Revit não confirmou o movimento.");
                        }
                        c.Doc.Regenerate();
                        if(align)
                        {
                            var a=getDestination();var b=getMoving();
                            Mep.RotateTo(c,moving,a.Origin,b.CoordinateSystem.BasisZ,a.CoordinateSystem.BasisZ.Negate());
                        }
                        // Same direction as the reference: moving connector -> destination.
                        // No dimension equality check and no forced transition creation.
                        if(!Mep.Joined(getMoving(),getDestination(),existingFittings))Mep.Connect(c,getMoving(),getDestination());
                    });
                    if(!Mep.Joined(getMoving(),getDestination(),existingFittings))
                        throw new InvalidOperationException("O Revit não manteve a conexão entre os conectores escolhidos. Operação revertida.");
                    group.Assimilate();
                }
                catch{if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
            }
        }
    }
}
