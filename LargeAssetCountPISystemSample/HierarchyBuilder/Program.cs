using OSIsoft.AF;

using System;
using System.Configuration;
using System.Linq;

using Utilities;

namespace HierarchyBuilder
{
    /// <summary>
    /// This program is to build an AF hierarchy from a flat structure
    /// </summary>
    class Program
    {
        /// <summary>
        /// Print Usage 
        /// </summary>
        private static void Usage()
        {
            Console.WriteLine("\nUsage: HierarchyBuilder.exe\n"
                + "Two parameters need to be set in the appSettings section of the App.config file :\n"
                + "\t1. <AFDatabasePath> represents the path to the target AF Database that contains the flat structure and where the hierarchy will reside in, for example, '\\\\MyAFServer\\MyAFDatabase'.\n"
                + "\t2. <HierarchyLevels> represents the hierarchy levels from bottom to top divided by pipe signs, for example, 'Leaf|Level1|Level2|...|LevelN'.\n\n"

                + "The program is designed to build a hierarchical AF structure based on an existing flat AF structure. "
                + "The flat structure should contain a collection of leaf elements, which define the higher-level parent elements in the values of the different attributes, such as Level1, Level2, ..., LevelN.\n\n"
                + "The program will try to resolve the parent-child relationship between different levels, create any non-leaf elements and link child elements to their immediate parent elements with weak reference. "
                + "From the bottom to top, the final hierarchy path would be : Leaf -> Level1 -> Level2 -> ... -> LevelN -> HierarchyRoot.\n\n"

                + "Assumptions :\n"
                + "\t1. All non-leaf level elements have pre-existing AF element templates, the template name is the same as the corresponding AF Attribute template name in the leaf element.\n"
                + "\t2. All elements at a given level are under a container element at the database root. The names of container elements follow the pattern of 'element template name' + 'Elements', e.g., 'Leaf' => 'LeafElements'.\n"
                + "\t3. An element at any given level should only have one parent element at the immediate higher level.");
        }

        /// <summary>
        /// The main entry point for the application
        /// </summary>
        private static void Main(string[] args)
        {
            try
            {
                Settings settings;
                if (!TryInitialize(out settings))
                {
                    Usage();
                    return;
                }

                using (var builder = new HierarchyBuilder(settings))
                {
                    builder.Run();

                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                Usage();
            }
        }

        /// <summary>
        /// Verify and process configuration
        /// </summary>
        private static bool TryInitialize(out Settings settings)
        {
            settings = new Settings();

            AFDatabase targetDatabase;
            string[] hierarchyLevels;

            // Check the required settings
            string path = ConfigurationManager.AppSettings["AFDatabasePath"];
            if (String.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine("AFDatabasePath is missing in appSettings.");
                return false;
            }

            string hierarchyLevelPath = ConfigurationManager.AppSettings["HierarchyLevels"];
            if (String.IsNullOrWhiteSpace(hierarchyLevelPath))
            {
                Console.WriteLine("HierarchyLevels is missing in appSettings.");
                return false;
            }

            // Get the target AF Database
            AFObject result = AFObject.FindObject(path);
            if (result is AFDatabase)
            {
                targetDatabase = (AFDatabase)result;
            }
            else
            {
                Console.WriteLine("Cannot find the specified AF Database, {0}.", path);
                return false;
            }

            // Parse the HierarchyLevels setting into an array of hierarchy level names.
            // If there is no more than one level, return false beacuse there is nothing to build
            hierarchyLevels = hierarchyLevelPath.Split('|');
            if (hierarchyLevels.Count() <= 1)
            {
                Console.WriteLine("HierarchyLevels, {0}, is not a valid hierarchy path.", hierarchyLevelPath);
                return false;
            }

            // Check the existence of leaf container element and leaf element template
            var leafElementTemplateName = hierarchyLevels[0];
            if (!(targetDatabase.Elements.Contains(String.Format(Constants.CONTAINERELEMENT_NAMEFORMAT, leafElementTemplateName))
                && targetDatabase.ElementTemplates.Contains(leafElementTemplateName)))
            {
                Console.WriteLine("Cannot find the specified leaf elements or element template in AF Database, {0}.", targetDatabase.Name);
                return false;
            }

            settings.TargetDatabase = targetDatabase;
            settings.HierarchyLevels = hierarchyLevels;
            settings.LeafElementTemplateName = leafElementTemplateName;

            return true;
        }
    }
}