using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
namespace OQNTools
{
    internal sealed class BranchLiteState
    {
        sealed class Link { public int OwnPort,PeerPort;public ElementId Peer; }
        readonly ElementId id;
        readonly XYZ a,b;
        readonly double diameter;
        readonly List<Link> links=new List<Link>();
        static Vector3 V(XYZ p)=>new Vector3(p.X,p.Y,p.Z);
        public BranchLiteState(Pipe pipe)
        {
            id=pipe.Id;var line=(Line)((LocationCurve)pipe.Location).Curve;
            a=line.GetEndPoint(0);b=line.GetEndPoint(1);diameter=pipe.Diameter;
            foreach(var port in Mep.Connectors(pipe))
                foreach(var peer in ConnectedMove.Peers(port))links.Add(new Link{OwnPort=port.Id,Peer=peer.Owner.Id,PeerPort=peer.Id});
        }
        public void Check(Document doc,XYZ offset,bool allowLengthChange,string side)
        {
            var pipe=doc.GetElement(id) as Pipe;
            if(pipe==null||!((pipe.Location as LocationCurve)?.Curve is Line line))Fail("tubo removido ou eixo não linear",side);
            // Reacquire after regeneration/commit, never compare circular connector BasisX.
            var curve=(Line)((LocationCurve)pipe.Location).Curve;
            string reason=BranchLiteGeometry.Difference(V(a),V(b),V(curve.GetEndPoint(0)),V(curve.GetEndPoint(1)),V(offset),allowLengthChange);
            if(reason!=null)Fail(reason,side);
            if(Math.Abs(pipe.Diameter-diameter)>1e-7)Fail("diâmetro alterado",side);
            var ports=Mep.Connectors(pipe).ToList();
            foreach(var link in links)
            {
                var peerElement=doc.GetElement(link.Peer);
                var own=ports.SingleOrDefault(p=>p.Id==link.OwnPort);
                var peer=peerElement==null?null:Mep.Connectors(peerElement).SingleOrDefault(p=>p.Id==link.PeerPort);
                if(own==null||peer==null||!own.IsConnectedTo(peer))Fail("ligação existente desfeita",side);
            }
        }
        void Fail(string reason,string side)
        {
            throw new InvalidOperationException("Não foi possível alinhar o "+side+" (elemento "+id.OqnValue()+"): "+reason+". Operação revertida.");
        }
    }
}
