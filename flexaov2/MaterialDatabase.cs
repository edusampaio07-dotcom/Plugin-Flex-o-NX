using System;

namespace NXDeflectionPlugin
{
    public static class MaterialDatabase
    {
        public static double GetKc(string materialType)
        {
            switch (materialType.ToLower())
            {
                case "alumínio aeronáutico": return 700.0;
                case "aço 1045": return 1500.0;
                case "titânio ti-6al-4v": return 2200.0;
                case "pa-cf (nylon com carbono)": return 150.0;
                default: return 1000.0;
            }
        }
    }
}