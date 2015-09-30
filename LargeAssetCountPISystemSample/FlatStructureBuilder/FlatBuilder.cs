using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Diagnostics;

using OSIsoft.AF;
using OSIsoft.AF.Asset;

using Utilities;

namespace FlatStructureBuilder
{
    public class FlatBuilder
    {
        private Stopwatch _sw = new Stopwatch();
        private TimeSpan _elapsed = TimeSpan.FromSeconds(0);
        private FlatBuilderConf _conf;
        private AFContext _afContext;
        private object _updateLock = new object();

        /// <summary>
        /// Create the flat AF database structure
        /// </summary>
        /// <param name="args"></param>
        public void Run(string[] args)
        {
            GetConfiguration(args);

            CreateAFTemplates();

            RunOperation("Creating elements", () => CreateLeafElements());

            RunOperation("Creating PI Points", () => CreatePIPoints());

            Console.WriteLine("Total execution time: {0}\n", _elapsed.ToString());
            Console.WriteLine("Press any key to exit");
            Console.ReadKey();
        }

        /// <summary>
        /// Read configuration from app.config and store in FlatBuilderConf instance
        /// </summary>
        /// <param name="args"></param>
        private void GetConfiguration(string[] args)
        {
            _conf = new FlatBuilderConf();
            try
            {
                if (!_conf.Initialize())
                {
                    Environment.Exit(0);
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("Press any key to exit");
                Console.ReadKey();
            }
        }

        /// <summary>
        /// Create the AF Database, Element Templates, and Attribute Templates.
        /// </summary>
        private void CreateAFTemplates()
        {
            // Check if PI System exists.
            PISystem targetPISystem = new PISystems()[_conf.AFServerName];
            if (targetPISystem == null)
            {
                Console.WriteLine("AF server does not exist");
                Environment.Exit(0);
            }

            // Check if AF Database exists. If so, delete it and recreate it to start anew.
            _afContext = new AFContext();
            _afContext.Database = targetPISystem.Databases[_conf.AFDatabaseName];
            if (_afContext.Database != null)
            {
                Console.Write(string.Format(@"AF Database {0} already exists. " +
                                            @"Press Y to remove and recreate the database, " +
                                            @"N to quit the program and specify a different database name : ",
                                            _conf.AFDatabaseName));
                while (true)
                {
                    ConsoleKeyInfo result = Console.ReadKey();
                    Console.WriteLine("\n");
                    if ((result.KeyChar == 'Y') || (result.KeyChar == 'y'))
                    {
                        targetPISystem.Databases.Remove(_afContext.Database);
                        break;
                    }
                    else if ((result.KeyChar == 'N') || (result.KeyChar == 'n'))
                    {
                        Environment.Exit(0);
                    }

                    Console.Write("Invalid input, try again (Y/N) : ");
                }
            }

            _afContext.Database = targetPISystem.Databases.Add(_conf.AFDatabaseName);

            // Create the Enumeration Set.
            AFEnumerationSet modes = _afContext.Database.EnumerationSets.Add("Modes");
            modes.Add("Manual", 0);
            modes.Add("Auto", 1);
            modes.Add("Cascade", 2);
            modes.Add("Program", 3);
            modes.Add("Prog-Auto", 4);

            // Create element templates for SubTree and Branch elements.
            AFElementTemplate subTree_ElemTmp = _afContext.Database.ElementTemplates.Add(Constants.SUBTREE);
            AFElementTemplate branch_ElemTmp = _afContext.Database.ElementTemplates.Add(Constants.BRANCH);

            AFAttributeTemplate subtree_Rollup_AttrTmp = subTree_ElemTmp.AttributeTemplates.Add(Constants.ROLLUP_SUM_ATTRIBUTE);
            AFAttributeTemplate branch_Rollup_AttrTmp = branch_ElemTmp.AttributeTemplates.Add(Constants.ROLLUP_SUM_ATTRIBUTE);
            AFAttributeTemplate threshold_AttrTmp = branch_ElemTmp.AttributeTemplates.Add(Constants.THRESHOLD_ATTRIBUTE);

            subtree_Rollup_AttrTmp.Type = typeof(float);
            branch_Rollup_AttrTmp.Type = typeof(float);
            threshold_AttrTmp.Type = typeof(int);

            AFPlugIn _piPointDR = AFDataReference.GetPIPointDataReference(targetPISystem);
            subtree_Rollup_AttrTmp.DataReferencePlugIn = _piPointDR;
            branch_Rollup_AttrTmp.DataReferencePlugIn = _piPointDR;

            string serverPath = @"\\" + _conf.PIServerName + @"\";

            subtree_Rollup_AttrTmp.ConfigString = serverPath +
                @"HighAsset_%Element%_Total";

            branch_Rollup_AttrTmp.ConfigString = serverPath +
                @"HighAsset_%Element%_Total";

            threshold_AttrTmp.SetValue(_conf.ThresholdValue, null);

            // Create element templates for Leaf elements.
            _afContext.BaseLeafTemplate = _afContext.Database.ElementTemplates.Add(Constants.LEAF);
            _afContext.SinusoidLeafTemplate = _afContext.Database.ElementTemplates.Add(Constants.LEAF_SIN);
            _afContext.RandomLeafTemplate = _afContext.Database.ElementTemplates.Add(Constants.LEAF_RAND);

            _afContext.SinusoidLeafTemplate.BaseTemplate = _afContext.BaseLeafTemplate;
            _afContext.RandomLeafTemplate.BaseTemplate = _afContext.BaseLeafTemplate;

            // Add attribute templates for base leaf Element Templates.
            AFAttributeTemplate subtree_AttrTmp = _afContext.BaseLeafTemplate.AttributeTemplates.Add(Constants.SUBTREE);
            AFAttributeTemplate branch_AttrTmp = _afContext.BaseLeafTemplate.AttributeTemplates.Add(Constants.BRANCH);
            AFAttributeTemplate ID_AttrTmp = _afContext.BaseLeafTemplate.AttributeTemplates.Add(Constants.LEAF_ID);
            AFAttributeTemplate value_AttrTmp = _afContext.BaseLeafTemplate.AttributeTemplates.Add("Value");
            AFAttributeTemplate mode_AttrTmp = _afContext.BaseLeafTemplate.AttributeTemplates.Add("Mode");

            subtree_AttrTmp.Type = typeof(string);
            branch_AttrTmp.Type = typeof(string);
            ID_AttrTmp.Type = typeof(string);

            value_AttrTmp.Type = typeof(float);
            value_AttrTmp.DataReferencePlugIn = _piPointDR;

            mode_AttrTmp.DataReferencePlugIn = _piPointDR;
            mode_AttrTmp.TypeQualifier = modes;
            mode_AttrTmp.SetValue(modes["Manual"], null);
            mode_AttrTmp.ConfigString = serverPath +
                @"HighAsset_%Element%_Mode;
                    ptclassname=classic;
                    pointtype=digital;
                    digitalset=modes;
                    pointsource=R;
                    location1=0;
                    location4=3;
                    location5=1";

            // Add attribute templates for sinusoid leaf Element Templates.
            AFAttributeTemplate value_sinusoid_AttrTmp = _afContext.SinusoidLeafTemplate.AttributeTemplates.Add("Value");

            value_sinusoid_AttrTmp.Type = typeof(double);
            value_sinusoid_AttrTmp.DataReferencePlugIn = _piPointDR;
            value_sinusoid_AttrTmp.ConfigString = serverPath +
                @"HighAsset_%Element%_Sinusoid;
                    ptclassname=classic;
                    pointtype=float32;
                    pointsource=R;
                    location1=0;
                    location4=3;
                    location5=0";

            // Add attribute templates for random leaf Element Templates.
            AFAttributeTemplate value_random_AttrTmp = _afContext.RandomLeafTemplate.AttributeTemplates.Add("Value");

            value_random_AttrTmp.Type = typeof(double);
            value_random_AttrTmp.DataReferencePlugIn = _piPointDR;
            value_random_AttrTmp.ConfigString = serverPath +
                @"HighAsset_%Element%_Random;
                    ptclassname=classic;
                    pointtype=float32;
                    pointsource=R;
                    location1=0;
                    location4=3;
                    location5=1";

            // Create container element under which all leaf elements will be stored.
            _afContext.Database.Elements.Add("LeafElements");

            // Do a bulk checkin of all changes made so far.
            _afContext.Database.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisThread);
        }

        /// <summary>
        /// Execute the operation and time it
        /// </summary>
        /// <param name="name"></param>
        /// <param name="Operation"></param>
        private void RunOperation(string name, Action Operation)
        {
            Console.WriteLine(name);

            _sw.Start();

            Operation();

            _sw.Stop();

            _elapsed += _sw.Elapsed;
            Console.WriteLine("Elapsed time: {0}\n", _sw.Elapsed.ToString());
            _sw.Reset();
        }

        /// <summary>
        /// Create leaf elements. Multi-thread the preprocessing but use a lock during element additions and check ins.
        /// </summary>
        private void CreateLeafElements()
        {
            int counter = 0;
            Parallel.For(1, _conf.TotalLeafs + 1, new ParallelOptions { MaxDegreeOfParallelism = _conf.MaxParallel }, leafID =>
            {
                int currentSubTreeID = GetID(leafID, _conf.LeavesPerSubTree);
                int currentBranchID = GetID(leafID, _conf.LeavesPerBranch);

                ElementCreationInfo leafInfo = GetElementCreationInfo(leafID);

                AFElement leaf = null;
                // Lock the AF Database when writing to LeafElements collection and checking in.
                lock (_afContext.DbLock)
                {
                    leaf = _afContext.Database.Elements["LeafElements"].Elements.Add(
                        leafInfo.ElementName, leafInfo.ElementTemplate);

                    leaf.Attributes[Constants.SUBTREE].SetValue(currentSubTreeID, null);
                    leaf.Attributes[Constants.BRANCH].SetValue(currentBranchID, null);
                    leaf.Attributes[Constants.LEAF_ID].SetValue(leafID, null);

                    counter += 1;
                    TryCheckIn(counter);
                }

                // Update configstrings without checkout/checkin
                UpdateConfigStrings(leaf, leafID);

            });

            // Check in any remaining AF Element additions.
            _afContext.Database.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisSession);
        }

        /// <summary>
        /// Given the Leaf ID, return the name of the element to be created and template to create from.
        /// </summary>
        /// <param name="leafID"></param>
        /// <returns></returns>
        private ElementCreationInfo GetElementCreationInfo(int leafID)
        {
            int j = leafID - 1;

            // Example: AddLeadingZeros(15) converts 15 to 00000015
            string paddedNumber = AddLeadingZeros(leafID);

            // Assign sinusoid Leaf elements to first branch.
            // Assign random Leaf elements to second branch.
            // Assign sinusoid Leaf elements to third branch.
            // Assign random Leaf elements to fourth branch, etc.
            if ((j / _conf.LeavesPerBranch) % 2 == 0)
            {
                return new ElementCreationInfo { ElementName = Constants.LEAF_SIN + paddedNumber, ElementTemplate = _afContext.SinusoidLeafTemplate };
            }
            else
            {
                return new ElementCreationInfo { ElementName = Constants.LEAF_RAND + paddedNumber, ElementTemplate = _afContext.RandomLeafTemplate };
            }
        }

        /// <summary>
        /// Update the config strings by appending random values for location2.
        /// Do this without requiring a checkout.
        /// </summary>
        /// <param name="leaf"></param>
        /// <param name="leafID"></param>
        private void UpdateConfigStrings(AFElement leaf, int leafID)
        {
            // Add Location2 point attributes to the ConfigString.
            // This is used later during PI Point creation to set the point attributes.
            // We choose semi-random numbers for Location2 so there is variability in data
            // generated by the Random Interface.

            Random rnd = new Random(leafID);

            int loc2 = rnd.Next(1, 100);
            string modeString = leaf.Attributes[Constants.LEAF_MODE].ConfigString + string.Format(";location2={0}", loc2);

            string valueString = null;
            if (leaf.Template == _afContext.RandomLeafTemplate)
            {
                loc2 = rnd.Next(2, 10);
                valueString = leaf.Attributes[Constants.LEAF_VALUE].ConfigString + string.Format(";location2={0}", loc2);
            }

            if (leaf.Template == _afContext.SinusoidLeafTemplate)
            {
                loc2 = rnd.Next(0, 100);
                valueString = leaf.Attributes[Constants.LEAF_VALUE].ConfigString + string.Format(";location2={0}", loc2);
            }

            IList<AFAttribute> listAttr = new List<AFAttribute> {
                leaf.Attributes[Constants.LEAF_MODE],
                leaf.Attributes[Constants.LEAF_VALUE]
            };

            IList<string> listConfigStrings = new List<string>
            {
                modeString,
                valueString
            };

            AFAttribute.SetConfigStrings(listAttr, listConfigStrings);
        }

        /// <summary>
        /// Given Leaf ID, assign it to the corresponding SubTree or Branch ID.
        /// </summary>
        /// <param name="leafID"></param>
        /// <param name="numSubTreeOrBranch"></param>
        /// <returns></returns>
        private int GetID(int leafID, int numSubTreeOrBranch)
        {

            return (leafID - 1) / numSubTreeOrBranch + 1;
        }

        /// <summary>
        /// Check if elements created should be checked in. 
        /// If so, perform a check in of all elements added during this session.
        /// </summary>
        /// <param name="elementsToCheckin"></param>
        private void TryCheckIn(int elementsToCheckin)
        {
            // TIP: Check in elements in chunks of __numPerCheckIn
            if (elementsToCheckin % _conf.ElementsPerCheckin == 0)
            {
                // Note that we are using AFCheckedOutMode.ObjectsCheckedOutThisSession.
                // The multi-threading pattern we use allows one thread to check in objects
                // on behalf of other threads.
                _afContext.Database.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisSession);
                ReportTiming(elementsToCheckin, _conf.TotalLeafs, "Element");
            }
        }

        /// <summary>
        /// This is a custom implementation of AF SDK's CreateConfig() that does not require a 1-by-1 element check out/in.
        /// Create PI Points according to configstring values.
        /// Modify the configstring afterward by stripping off the trailing key-value pairs.
        /// </summary>
        private void CreatePIPoints()
        {
            EventHandler<AFProgressEventArgs> PIPointCreationEventHandler = new EventHandler<AFProgressEventArgs>(NotifyPIPointCreationToConsole);

            AFElements leafElements = _afContext.Database.Elements["LeafElements"].Elements;
            OrderablePartitioner<Tuple<int, int>> rangePartition = Partitioner.Create(0, _conf.TotalLeafs, _conf.PointsChunkSize / 2);

            Parallel.ForEach(rangePartition, new ParallelOptions { MaxDegreeOfParallelism = _conf.MaxParallel }, range =>
            {
                List<AFElement> chunk = new List<AFElement>();
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    chunk.Add(leafElements[i]);
                }

                ElementCreator.CreateorUpdatePIPointDataReference(chunk, PIPointCreationEventHandler);
            });
        }

        /// <summary>
        /// Event handler to check when PI Point creation process should be written to console.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void NotifyPIPointCreationToConsole(object sender, AFProgressEventArgs eventArgs)
        {
            if (eventArgs.ToString().Contains("Succeeded"))
            {
                lock (_updateLock)
                {
                    _conf.PointsCreated += eventArgs.OperationsCompleted;
                    ReportTiming(_conf.PointsCreated, _conf.TotalPoints, "PI Point");
                }
            }
        }

        /// <summary>
        /// Report the progress of element and PI Point creation to console.
        /// </summary>
        /// <param name="completedOps"></param>
        /// <param name="totalOps"></param>
        /// <param name="item"></param>
        private void ReportTiming(int completedOps, int totalOps, string item)
        {
            Console.WriteLine("{0}s created: {1} ({2}%)", item, completedOps, (double)completedOps / totalOps * 100);

            double timePerElement = _sw.Elapsed.TotalMilliseconds / Convert.ToDouble(completedOps);
            Console.WriteLine("Time (ms) per {0}: {1}\n", item, timePerElement);
        }

        private string AddLeadingZeros(int i)
        {
            return i.ToString("D8");
        }
    }

    /// <summary>
    /// A helper class to store information needed to create an AF Element.
    /// </summary>
    public class ElementCreationInfo
    {
        public string ElementName { get; set; }
        public AFElementTemplate ElementTemplate { get; set; }
    }
}
