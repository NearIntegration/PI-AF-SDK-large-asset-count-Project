using OSIsoft.AF;

using System;
using System.Configuration;
using System.Linq;

namespace CalculationEngine
{
    class Program
    {
        /// <summary>
        /// Print Usage 
        /// </summary>
        private static void Usage()
        {
            Console.WriteLine("\nUsage: CalculationEngine.exe\n"
                + "Two parameters need to be set in the appSettings section of the App.config file :\n"
                + "\t1. <AFDatabasePath> represents the path to the target AF Database that contains the flat structure and where the hierarchy will reside in, for example, '\\\\MyAFServer\\MyAFDatabase'.\n"
                + "\t2. <RollupPath> represents the hierarchy levels for rollup from bottom up divided by pipe signs, for example, 'Leaf|Level1|Level2|...|LevelN'. The level name should be the same as the respective AF element template name.\n\n"
                + "The program is designed to demonstrate typical calculations against a hierarchical AF structure with a high asset count, such as rollup, outlier identification, and Event Frame creation");
        }

        /// <summary>
        /// The main entry point for the application
        /// </summary>
        static void Main(string[] args)
        {
            try
            {
                Settings settings;
                if (!TryInitialize(out settings))
                {
                    Usage();
                    return;
                }

                var engine = new CalculationEngine(settings);
                engine.Run();

                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();

                Console.WriteLine("\n{0} | Exiting...", DateTime.Now);
                engine.Stop();
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

            // Check the required settings
            string path = ConfigurationManager.AppSettings["AFDatabasePath"];
            if (String.IsNullOrWhiteSpace(path))
            {
                Console.WriteLine("AFDatabasePath is missing in appSettings.");
                return false;
            }

            string rollupPath = ConfigurationManager.AppSettings["RollupPath"];
            if (String.IsNullOrWhiteSpace(rollupPath))
            {
                Console.WriteLine("RollupPath is missing in appSettings.");
                return false;
            }

            // Parse the RollupPath setting into an array of hierarchy level names.
            // If there is no more than one level, return false beacuse there is nothing to build
            settings.RollupLevels = rollupPath.Split('|');
            if (settings.RollupLevels.Count() <= 1)
            {
                Console.WriteLine("RollupPath, {0}, is not valid.", rollupPath);
                return false;
            }

            // Get the target AF Database and the leaf element template
            AFObject result = AFObject.FindObject(path);
            if (result is AFDatabase)
            {
                settings.TargetDatabase = (AFDatabase)result;
                settings.LeafElementTemplate = settings.TargetDatabase.ElementTemplates[settings.RollupLevels[0]];
            }
            else
            {
                Console.WriteLine("Cannot find the specified AF Database, {0}.", path);
                return false;
            }

            return true;
        }
    }
}
