using System;
namespace OQNTools
{
    public struct Vector3
    {
        public readonly double X,Y,Z;
        public Vector3(double x,double y,double z){X=x;Y=y;Z=z;}
        public double Length=>Math.Sqrt(Dot(this));
        public double Dot(Vector3 b)=>X*b.X+Y*b.Y+Z*b.Z;
        public Vector3 Cross(Vector3 b)=>new Vector3(Y*b.Z-Z*b.Y,Z*b.X-X*b.Z,X*b.Y-Y*b.X);
        public Vector3 Unit(){if(Length<1e-12)throw new ArgumentException("O tubo precisa ter comprimento válido.");return this/Length;}
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
        public static Vector3 operator *(Vector3 a,double k)=>new Vector3(a.X*k,a.Y*k,a.Z*k);
        public static Vector3 operator /(Vector3 a,double k)=>a*(1/k);
    }
    public static class BranchAlignment
    {
        // Minimum translation between infinite axes, perpendicular to both directions.
        // No division by the XY determinant: near-parallel plan bearings must not
        // amplify a small separation into an enormous height correction.
        public static Vector3 Offset(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var u=(b-a).Unit();var v=(d-c).Unit();var gap=a-c;var n=u.Cross(v);
            if(n.Length<1e-12)return gap-v*gap.Dot(v);
            var normal=n.Unit();
            return normal*gap.Dot(normal);
        }
        // Retained exclusively for the Plus command; LT no longer prefers a height-only move.
        public static Vector3 OffsetWithHeightPreference(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            var u=(b-a).Unit();var v=(d-c).Unit();var gap=a-c;var n=u.Cross(v);
            if(n.Length<1e-8)return gap-u*gap.Dot(u); // parallel axes: make trajectories collinear
            double h1=Math.Sqrt(u.X*u.X+u.Y*u.Y),h2=Math.Sqrt(v.X*v.X+v.Y*v.Y);
            // A vertical branch is positioned in plan by perpendicular projection onto
            // the infinite main trajectory. Keep Z, length and vertical direction unchanged.
            if(h2<1e-10 && h1>1e-8)
            {
                double t=((c.X-a.X)*u.X+(c.Y-a.Y)*u.Y)/(h1*h1);
                return new Vector3(a.X+t*u.X-c.X,a.Y+t*u.Y-c.Y,0);
            }
            if(h1>1e-8&&h2>1e-8&&Math.Abs(n.Z)>1e-6*n.Length)
                return new Vector3(0,0,gap.Dot(n)/n.Z);
            return n*(gap.Dot(n)/n.Dot(n)); // vertical / skew / section-plane case
        }
        // Same slope magnitude, perpendicular XY bearing. Near main end determines drainage direction.
        public static Vector3 PlusDirection(Vector3 a,Vector3 b,Vector3 near,Vector3 far)
        {
            var main=b-a;var old=(far-near).Unit();double h=Math.Sqrt(main.X*main.X+main.Y*main.Y);
            if(h<1e-8*main.Length)throw new ArgumentException("Um principal vertical não tem declividade percentual para copiar. Use Alinhar ramal LT nesse caso.");
            var normal=new Vector3(-main.Y/h,main.X/h,0);
            double dot=normal.Dot(old);
            if(Math.Abs(dot)<1e-10)dot=normal.Dot(near-a);
            if(dot<0)normal=normal*(-1);
            // Orient main from its nearest end to the farther one, independent of endpoint numbering.
            double signedRise=(near-a).Length<=(near-b).Length?b.Z-a.Z:a.Z-b.Z;
            return new Vector3(normal.X,normal.Y,signedRise/h).Unit();
        }
        public static double VerticalOffset(double ax,double ay,double az,double bx,double by,double bz,double cx,double cy,double cz,double dx,double dy,double dz)
        {return OffsetWithHeightPreference(new Vector3(ax,ay,az),new Vector3(bx,by,bz),new Vector3(cx,cy,cz),new Vector3(dx,dy,dz)).Z;}
    }
}
