using OSIsoft.AF;

namespace HierarchyBuilder
{
    class Settings
        {
            public AFDatabase TargetDatabase { get; set; }
            public string LeafElementTemplateName { get; set; }
            public string[] HierarchyLevels { get; set; }
        }
}
