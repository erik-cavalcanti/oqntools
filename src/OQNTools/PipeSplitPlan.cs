using System;
using System.Collections.Generic;
namespace OQNTools
{
    public static class PipeSplitPlan
    {
        // All inputs use the same unit (feet inside Revit). No geometry or document access.
        public static List<double> Cuts(double length,double step,double minimum)
        {
            if(double.IsNaN(length)||double.IsInfinity(length)||double.IsNaN(step)||double.IsInfinity(step)||length<=0||double.IsNaN(minimum)||double.IsInfinity(minimum)||minimum<=0||step<=minimum)throw new ArgumentException("Comprimento inválido.");
            var cuts=new List<double>();const double epsilon=1e-7;
            if(length<=step+epsilon)return cuts;
            double count=Math.Ceiling((length-epsilon)/step)-1;
            if(count>10000)throw new ArgumentException("Mais de 10.000 cortes no mesmo tubo; aumente o comprimento.");
            for(int i=1;i<=count;i++)cuts.Add(i*step);
            if(cuts.Count>0&&length-cuts[cuts.Count-1]<=minimum)throw new ArgumentException("A sobra seria menor que o mínimo permitido pelo Revit. Ajuste o comprimento.");
            return cuts;
        }
    }
}
