using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI.Selection;

namespace OQNTools
{
    internal static class BranchMove
    {
        static Vector3 V(XYZ p)=>new Vector3(p.X,p.Y,p.Z);
        static XYZ P(Vector3 p)=>new XYZ(p.X,p.Y,p.Z);
        static double Distance(XYZ p,Line line)
        {var v=p-line.GetEndPoint(0);return (v-line.Direction*v.DotProduct(line.Direction)).GetLength();}
        static void RunLite(Context c,Pipe main,Pipe branch,Line stationary,Line moving)
        {
            if(branch.Pinned)throw new ArgumentException("O ramal está fixado. Desfixe-o para alinhar.");
            if(branch.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("O ramal pertence a um grupo. Edite o grupo para alinhar.");
            var offset=P(BranchAlignment.Offset(V(stationary.GetEndPoint(0)),V(stationary.GetEndPoint(1)),V(moving.GetEndPoint(0)),V(moving.GetEndPoint(1))));
            if(offset.GetLength()<1e-8)return;
            var before=new BranchLiteState(branch);
            var fixedMain=new BranchLiteState(main);
            using(var group=new TransactionGroup(c.Doc,"OQN · Alinhar ramal LT"))
            {
                group.Start();
                try
                {
                    c.Mutate("Alinhar ramal LT",()=>{
                        // Match the reference's movement scope. Revit manages dependent
                        // fittings/runs; never translate a recursively collected network here.
                        ElementTransformUtils.MoveElement(c.Doc,branch.Id,offset);
                        c.Doc.Regenerate();
                        before.Check(c.Doc,offset,true,"ramal");fixedMain.Check(c.Doc,XYZ.Zero,false,"principal");
                    });
                    before.Check(c.Doc,offset,true,"ramal");fixedMain.Check(c.Doc,XYZ.Zero,false,"principal");
                    group.Assimilate();
                }
                catch{if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
            }
        }
        public static void Run(Context c,bool plus)
        {
            var main=Mep.Pick(c,"Selecione o tubo principal (fica parado)") as Pipe;
            var reference=c.Ui.Selection.PickObject(ObjectType.Element,new MepSelection(),plus?"Selecione o ramal: ajustar direção e declividade":"Selecione o ramal: preservar direção e inclinação");
            var branch=c.Doc.GetElement(reference) as Pipe;
            if(main==null||branch==null||main.Id==branch.Id)throw new ArgumentException("Selecione dois tubos diferentes.");
            if(!((main.Location as LocationCurve)?.Curve is Line ml)||!((branch.Location as LocationCurve)?.Curve is Line bl))throw new ArgumentException("Use tubos retos.");
            if(!plus){RunLite(c,main,branch,ml,bl);return;}
            XYZ a=ml.GetEndPoint(0),b=ml.GetEndPoint(1),near=bl.GetEndPoint(0),far=bl.GetEndPoint(1);
            double d0=Distance(near,ml),d1=Distance(far,ml);
            if(d1<d0-1e-8 || (Math.Abs(d1-d0)<1e-8 && reference.GlobalPoint!=null && far.DistanceTo(reference.GlobalPoint)<near.DistanceTo(reference.GlobalPoint)))
            {var tmp=near;near=far;far=tmp;}
            var oldDirection=(far-near).Normalize();
            var direction=plus?P(BranchAlignment.PlusDirection(V(a),V(b),V(near),V(far))):oldDirection;
            var offset=P(BranchAlignment.OffsetWithHeightPreference(V(a),V(b),V(near),V(near+direction*bl.Length)));
            double angle=oldDirection.AngleTo(direction);
            if(angle<1e-8&&offset.GetLength()<1e-8)return;
            // Translate/rotate the complete physical component: moving fittings alone lets
            // Revit stretch attached pipes and can prevent a vertical branch from reaching its axis.
            var members=ConnectedMove.Component(c.Doc,branch).ToDictionary(e=>e.Id.OqnValue());
            if(members.ContainsKey(main.Id.OqnValue()))throw new ArgumentException("O principal e o ramal já pertencem à mesma rede física. Não é possível mover o conjunto inteiro mantendo o principal parado.");
            foreach(var e in members.Values)
            {
                if(e.Pinned)throw new ArgumentException("O elemento "+e.Id.OqnValue()+" está fixado. Desfixe-o para alinhar o ramal.");
                if(e.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("O elemento "+e.Id.OqnValue()+" pertence a um grupo. Edite o grupo para alinhar o ramal.");
            }
            var original=new ConnectedMove.Snapshot(members.Values);
            var fixedMain=new ConnectedMove.Snapshot(new[]{main});
            var ids=members.Values.Select(e=>e.Id).ToList();
            var axis=oldDirection.CrossProduct(direction);
            if(axis.GetLength()<1e-8)axis=oldDirection.CrossProduct(Math.Abs(oldDirection.Z)<.9?XYZ.BasisZ:XYZ.BasisX);
            axis=axis.Normalize();
            var rotation=angle<1e-8?Transform.Identity:Transform.CreateRotationAtPoint(axis,angle,near);
            var total=Transform.CreateTranslation(offset).Multiply(rotation);
            using(var group=new TransactionGroup(c.Doc,plus?"OQN · Alinhar ramal +":"OQN · Alinhar ramal LT"))
            {
                group.Start();
                try
                {
                    c.Mutate(plus?"Alinhar ramal +":"Alinhar ramal LT",()=>{
                        if(angle>=1e-8)ElementTransformUtils.RotateElements(c.Doc,ids,Line.CreateUnbound(near,axis),angle);
                        if(offset.GetLength()>1e-8)ElementTransformUtils.MoveElements(c.Doc,ids,offset);
                        c.Doc.Regenerate();original.Check(c.Doc,total,"ramal");fixedMain.Check(c.Doc,Transform.Identity,"principal");
                    });
                    original.Check(c.Doc,total,"ramal");fixedMain.Check(c.Doc,Transform.Identity,"principal");
                    group.Assimilate();
                }catch{if(group.GetStatus()==TransactionStatus.Started)group.RollBack();throw;}
            }
        }
    }
}
