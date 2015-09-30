using OSIsoft.AF;
using OSIsoft.AF.Asset;

namespace CalculationEngine
{
    class Settings
        {
            public AFDatabase TargetDatabase { get; set; }
            public AFElementTemplate LeafElementTemplate { get; set; }
            public string[] RollupLevels { get; set; }
        }
}
