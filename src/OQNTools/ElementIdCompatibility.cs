using Autodesk.Revit.DB;
namespace Autodesk.Revit.DB
{
    internal static class OqnElementIdCompatibility
    {
        public static long OqnValue(this ElementId id)
        {
#if REVIT2022 || REVIT2023
            return id.IntegerValue;
#else
            return id.Value;
#endif
        }
        public static ElementId Create(long value)
        {
#if REVIT2022 || REVIT2023
            return new ElementId(checked((int)value));
#else
            return new ElementId(value);
#endif
        }
    }
}
