using System;
namespace OQNTools
{
    // LT aligns infinite trajectories. Revit may trim/extend the connected branch
    // along that trajectory; connector roll on a circular pipe is not its slope.
    public static class BranchLiteGeometry
    {
        public static string Difference(Vector3 oldA,Vector3 oldB,Vector3 currentA,Vector3 currentB,Vector3 offset,bool allowLengthChange,double tolerance=1e-5)
        {
            var original=oldB-oldA;var current=currentB-currentA;
            if(original.Length<1e-12||current.Length<1e-12)return "comprimento inválido";
            if(original.Unit().Cross(current.Unit()).Length>1e-7)return "direção ou inclinação alterada";
            var a=oldA+offset;var b=oldB+offset;
            if(allowLengthChange)
            {
                var u=original.Unit();
                if((currentA-a).Cross(u).Length>tolerance||(currentB-a).Cross(u).Length>tolerance)return "eixo fora da trajetória calculada";
            }
            else
            {
                bool same=(currentA-a).Length<=tolerance&&(currentB-b).Length<=tolerance;
                bool swapped=(currentB-a).Length<=tolerance&&(currentA-b).Length<=tolerance;
                if(!same&&!swapped)return "posição ou comprimento do principal alterado";
            }
            return null;
        }
    }
}
