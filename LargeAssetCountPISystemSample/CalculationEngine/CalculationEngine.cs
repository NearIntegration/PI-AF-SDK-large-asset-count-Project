using OSIsoft.AF;
using OSIsoft.AF.Asset;
using OSIsoft.AF.Data;
using OSIsoft.AF.EventFrame;
using OSIsoft.AF.PI;
using OSIsoft.AF.Time;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Utilities;

namespace CalculationEngine
{
    class CalculationEngine
    {
        private const int ChunkSize = 10000;
        private const int PIPageSize = 1000;
        private const int HoursToRollup = 336; // two weeks 
        private const int MaxParallel = 4;

        private const string FluctuationIndexReportFile = @"FluctuationIndexReport";
        private const string BranchOutlierReportFile = @"BranchOutlierReport";

        private Settings _settings;
        private string _leafTargetMode = "Prog-Auto";
        private ManualResetEventSlim _closeEvent = new ManualResetEventSlim(false);
        private List<Task> _calculationTasks = new List<Task>();
        private PIPagingConfiguration _pagingConfig;
        private DateTime _programStartTime = DateTime.Now;
        private List<AFElement> _leafElements;
        private List<Tuple<uint, string, List<AFElement>>> _nonLeafElements;

        public CalculationEngine(Settings settings)
        {
            _settings = settings;
            _pagingConfig = new PIPagingConfiguration(PIPageType.TagCount, PIPageSize, new TimeSpan(1, 0, 0));
        }

        /// <summary>
        /// Perform calculations including outlier identification, creating Event Frame on target mode, roll up values to higher levels and calculate the fluctuation index
        /// </summary>
        public void Run()
        {
            // Find and partially load elements
            _leafElements = LoadLeafElements();
            _nonLeafElements = LoadNonLeafElements();

            // branchElements is the list of all branch elements, which has a level of 1
            var branchElements = _nonLeafElements.Where(tuple => tuple.Item1 == 1).First().Item3;

            // Outlier identification 
            // Sign up for branch's value updates and check against a given threshold defined in a static sibling attribute
            var outlierReporter = new OutlierReporter(BranchOutlierReportFile + _programStartTime.ToString("_MMddyyyy_HHmm") + @".csv");
            _calculationTasks.Add(Task.Factory.StartNew(() =>
            {
                using (var branchLevelMonitor = new AFDataObserver(branchElements.Select(elm => elm.Attributes[Constants.ROLLUP_SUM_ATTRIBUTE]).ToList(), outlierReporter.ReportOutlier))
                {
                    branchLevelMonitor.Start();
                    _closeEvent.Wait();
                }
            }));

            // Sign up for leaf's status updates and build Event Frame based on the status change
            _calculationTasks.Add(Task.Factory.StartNew(() =>
            {
                using (var leafLevelMonitor = new AFDataObserver(_leafElements.Select(elm => elm.Attributes[Constants.LEAF_MODE]).ToList(), CreateEFOnTargetMode))
                {
                    leafLevelMonitor.Start();
                    _closeEvent.Wait();
                }
            }));

            // Run rollup and fluctuation index calculation
            _calculationTasks.Add(Task.Factory.StartNew(() =>
            {
                RunHistoricalCalculations();
                _closeEvent.Wait();
            }));
        }

        /// <summary>
        /// Stop the Calculation Engine
        /// </summary>
        public void Stop()
        {
            _closeEvent.Set();
            Task.WaitAll(_calculationTasks.ToArray());

            if (_settings != null && _settings.TargetDatabase != null)
            {
                _settings.TargetDatabase.PISystem.Disconnect();
                _settings.TargetDatabase.PISystem.Dispose();
            }
        }

        /// <summary>
        /// Find leaf elements and load relevant attributes
        /// </summary>
        private List<AFElement> LoadLeafElements()
        {
            Console.WriteLine("{0} | Started to search for leaf elements on {1}...", DateTime.Now, _settings.TargetDatabase.GetPath());

            var leafElements = FindElements(_settings.LeafElementTemplate, new List<string>() { Constants.LEAF_VALUE, Constants.LEAF_MODE });

            Console.WriteLine("{0} | Found {1} leaf elements\n", DateTime.Now, leafElements.Count());

            return leafElements;
        }

        /// <summary>
        /// Find non-leaf elements and load relevant attributes
        /// </summary>
        /// <returns>A list of 3-item tuples, where item1 is level, item2 is the name and item3 is the full list of element</returns>
        private List<Tuple<uint, string, List<AFElement>>> LoadNonLeafElements()
        {
            Console.WriteLine("{0} | Started to search for non-leaf elements on {1}...", DateTime.Now, _settings.TargetDatabase.GetPath());

            var nonLeafElements = new List<Tuple<uint, string, List<AFElement>>>();

            uint i = 1;
            foreach (var levelName in _settings.RollupLevels.Skip(1))
            {
                var elementTemplate = _settings.TargetDatabase.ElementTemplates[levelName];
                var elements = FindElements(elementTemplate, new List<string>() { Constants.THRESHOLD_ATTRIBUTE, Constants.ROLLUP_SUM_ATTRIBUTE });

                nonLeafElements.Add(Tuple.Create(i++, levelName, elements));
            }

            Console.WriteLine("{0} | Found {1} non-leaf elements\n", DateTime.Now, nonLeafElements.Sum(tuple => tuple.Item3.Count));
            return nonLeafElements;
        }

        /// <summary>
        /// Find elements implementing a given AF Element Template and load a list of attributes from these elements
        /// </summary>
        private List<AFElement> FindElements(AFElementTemplate elementTemplate, IEnumerable<string> attributesToLoad)
        {
            int totalCount;
            int startIndex = 0;
            var results = new List<AFElement>();

            do
            {
                var baseElements = elementTemplate.FindInstantiatedElements(
                                includeDerived: true,
                                sortField: AFSortField.Name,
                                sortOrder: AFSortOrder.Ascending,
                                startIndex: startIndex,
                                maxCount: ChunkSize,
                                totalCount: out totalCount);

                // if there is no new leaf elements, break the process
                if (baseElements.Count() == 0)
                    break;

                var elements = baseElements.Select(elm => (AFElement)elm).ToList();

                var elementGroupings = elements.GroupBy(elm => elm.Template);
                foreach (var item in elementGroupings)
                {
                    List<AFAttributeTemplate> attrTemplates = attributesToLoad.Select(atr => AFHelper.GetLastAttributeTemplateOverride(item.Key, atr)).ToList();
                    AFElement.LoadAttributes(item.ToList(), attrTemplates);
                }

                results.AddRange(elements);

                startIndex += baseElements.Count();
            } while (startIndex < totalCount);

            return results;
        }

        /// <summary>
        /// Perform calculations using historical values
        /// </summary>
        private void RunHistoricalCalculations()
        {
            var topElements = _nonLeafElements.Last();
            var leafValueAttributes = new List<AFAttribute>();
            var rootElements = new List<AFElement>();
            int index = 0;

            foreach (var topElement in topElements.Item3)
            {
                // Block the thread for a short time to check whether user has called Stop() and set the state of WaitHandle signaled
                // If true, stop calculations
                if (_closeEvent.Wait(100))
                {
                    leafValueAttributes = new List<AFAttribute>();
                    rootElements = new List<AFElement>();
                    break;
                }

                // Collect value attributes from all leaf elements, which will be used in both rollup and fluctuation index calculations
                AggregateLeafAttributesRecursively(topElements.Item1, topElement, Constants.LEAF_VALUE, leafValueAttributes);
                rootElements.Add(topElement);

                // Process a chunk of leaf value attributes at one time in order to keep the memory consumption low
                if (leafValueAttributes.Count >= ChunkSize * MaxParallel)
                {
                    Console.WriteLine("{0} | StartIndex = {1} | Started historical data analyses for {2} leaf elements", DateTime.Now, index, leafValueAttributes.Count);
                    RunHistoricalCalculations(topElements.Item1, rootElements, leafValueAttributes);
                    Console.WriteLine("{0} | StartIndex = {1} | Finished historical data analyses for {2} leaf elements\n", DateTime.Now, index, leafValueAttributes.Count);

                    index += leafValueAttributes.Count;
                    leafValueAttributes = new List<AFAttribute>();
                    rootElements = new List<AFElement>();
                }
            }

            Console.WriteLine("{0} | StartIndex = {1} | Started historical data analyses for {2} leaf elements", DateTime.Now, index, leafValueAttributes.Count);
            RunHistoricalCalculations(topElements.Item1, rootElements, leafValueAttributes);
            Console.WriteLine("{0} | StartIndex = {1} | Finished historical data analyses for {2} leaf elements\n", DateTime.Now, index, leafValueAttributes.Count);
        }

        /// <summary>
        /// Perform historical calculations under a list of elements
        /// </summary>
        private void RunHistoricalCalculations(uint level, List<AFElement> rootElements, List<AFAttribute> leafValueAttributes)
        {
            if (leafValueAttributes.Count == 0)
                return;

            ResolvePIPoints(leafValueAttributes);

            // Rollup from bottom to top recursively
            PerformRollupOnce(level, rootElements, leafValueAttributes);

            // Calculate the fluctuation index for each leaf element
            CalculateFluctuationIndex(leafValueAttributes);
        }

        /// <summary>
        /// Recursively traverse the hierarchy and collect all leaf attribute to roll up
        /// </summary>
        /// <param name="level">the level of element in the hierarchy (leaf level = 0)</param>
        /// <param name="element">the element to process</param>
        /// <param name="leafAttributes">the collection to hold all leaf attributes</param>
        private void AggregateLeafAttributesRecursively(uint level, AFElement element, string attributeName, List<AFAttribute> leafAttributes)
        {
            try
            {
                if (level > 0) // Recursively add from the lower level elements
                {
                    foreach (var child in element.Elements)
                    {
                        AggregateLeafAttributesRecursively(level - 1, child, attributeName, leafAttributes);
                    }
                }
                else // element is a leaf
                {
                    var attribute = element.Attributes[attributeName];
                    if (attribute != null && !attribute.IsDataReferenceDefinedByTemplate)
                        leafAttributes.Add(attribute);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception reported at AggregateLeafAttributesRecursively : {0}", ex);
            }
        }

        /// <summary>
        /// Resolve the PI Point path for a list of AF attributes in parallel chunks
        /// </summary>
        /// <param name="attributes"></param>
        private void ResolvePIPoints(List<AFAttribute> attributes)
        {
            AFAttributeList[] attributeLists = ChunkifyAFAttributes(attributes, ChunkSize);

            Parallel.ForEach(attributeLists,
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallel },
                attributeList => attributeList.GetPIPoint());
        }

        /// <summary>
        /// For each element in the hierarchy following the rollup path, calculate the hourly total value from immediate children and save the results back to PI 
        /// </summary>
        /// <remarks>To address the potential data latency, this method will do rollup analysis based on the values in the past 2 weeks.
        /// New total values will replace the current values with the same timestamp assuming the new values are always more accurate.
        /// One may use a timer to run this method once a week.</remarks>
        private void PerformRollupOnce(uint level, IList<AFElement> rootElements, List<AFAttribute> leafValueAttributes)
        {
            List<AFTime> times = new List<AFTime>();

            // Convert the timestamp to the exact hour
            var rollupEndTime = _programStartTime.Date.AddHours(_programStartTime.Hour);
            for (int i = HoursToRollup; i > 0; i--)
            {
                times.Add(new AFTime(rollupEndTime.AddHours(-1 * i + 1)));
            }

            // Retrieve hourly total values of leaf attributes in the past 2 weeks
            AFTimeRange timeRange = new AFTimeRange(times.First().UtcTime.AddHours(-1), times.Last().UtcTime);
            var results = SummarizeAttributes(leafValueAttributes, attributeList =>
            {
                try
                {
                    return attributeList.Data.Summaries(
                          timeRange: timeRange,
                          summaryDuration: new AFTimeSpan(0, 0, 0, 1.0, 0, 0),
                          summaryTypes: AFSummaryTypes.Total,
                          calculationBasis: AFCalculationBasis.EventWeighted,
                          timeType: AFTimestampCalculation.Auto,
                          pagingConfig: _pagingConfig).ToList();
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Exception reported at PerformRollupOnce : {0}", _pagingConfig.Error);
                    throw _pagingConfig.Error;
                }
            });

            // Create an element to total AFValues mapping and use it in rollup calculation
            var elementToTotals = results.Select(d => d[AFSummaryTypes.Total]).ToDictionary(v => v.Attribute.Element);
            foreach (var root in rootElements)
            {
                CalculateRollupRecursively(level, root, times, elementToTotals);
            }
        }

        /// <summary>
        /// Recursively sum up children's values and store the results at the parent element's output attribute
        /// </summary>
        /// <param name="level">the level of element in the hierarchy (leaf level = 0)</param>
        /// <param name="element">the element to process</param>
        /// <param name="times">the list of timestamps to calculate</param>
        /// <param name="elementToTotals">the dictionary holding total AFValues</param>
        /// <returns>the rollup results at times</returns>
        private AFValues CalculateRollupRecursively(uint level, AFElement element, IList<AFTime> times, Dictionary<AFBaseElement, AFValues> elementToTotals)
        {
            try
            {
                if (level > 0) // Recursively add from the lower level elements
                {
                    List<AFValues> valuesToSum = new List<AFValues>();
                    foreach (var child in element.Elements)
                    {
                        var values = CalculateRollupRecursively(level - 1, child, times, elementToTotals);
                        valuesToSum.Add(values);
                    }

                    return UpdateTotalValues(element, times, valuesToSum);
                }
                else // child is a leaf
                {
                    return elementToTotals[element];
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception reported at CalculateRollupRecursively : {0}", ex);
                throw;
            }
        }

        /// <summary>
        /// Roll up children's values to the parent element at specified timestamps
        /// </summary>
        private AFValues UpdateTotalValues(AFElement parent, IList<AFTime> times, IList<AFValues> childValues)
        {
            AFValues rollupValuesToReturn = new AFValues();
            AFValues rollupValuesToSet = new AFValues();

            // Filter out incomplete data sets
            childValues = childValues.Where(vals => vals.Count == times.Count).ToList();

            for (int i = 0; i < times.Count; i++)
            {
                var childValuesAtAGivenTime = childValues.Select(vals => vals[i]).Where(val => val.IsGood);

                var sumAFValue = new AFValue();
                sumAFValue.Timestamp = times[i];

                if (!childValuesAtAGivenTime.Any())
                {
                    sumAFValue.IsGood = false;
                }
                else
                {
                    sumAFValue.Value = childValuesAtAGivenTime.Sum(val => Convert.ToDouble(val.Value));
                    rollupValuesToSet.Add(sumAFValue);
                }

                rollupValuesToReturn.Add(sumAFValue);
            }

            AFAttribute rollupOutputAttr = parent.Attributes[Constants.ROLLUP_SUM_ATTRIBUTE];
            if (rollupValuesToSet.Count > 0 && rollupOutputAttr != null)
            {
                var results = rollupOutputAttr.Data.UpdateValues(rollupValuesToSet, AFUpdateOption.Replace);
                if (results != null)
                {
                    foreach (var ex in results.Errors)
                    {
                        Console.WriteLine("Exception reported at UpdateTotalValues : {0}", ex);
                    }
                }
            }

            return rollupValuesToReturn;
        }

        /// <summary>
        /// Calculate Fluctuation Index, which is defined as (Vmax-Vmin)/7, Vmax and Vmin are the maximum and minimum values in the past 7 days
        /// </summary>
        private void CalculateFluctuationIndex(List<AFAttribute> leafValueAttributes)
        {
            var timeRange = new AFTimeRange(_programStartTime.AddDays(-7), _programStartTime);
            var results = SummarizeAttributes(leafValueAttributes, attributeList =>
            {
                try
                {
                    return attributeList.Data.Summary(
                    timeRange: timeRange,
                    summaryTypes: AFSummaryTypes.Range,
                    calculationBasis: AFCalculationBasis.EventWeighted,
                    timeType: AFTimestampCalculation.Auto,
                    pagingConfig: _pagingConfig).ToList();
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Exception reported at CalculateFluctuationIndex : {0}", _pagingConfig.Error);
                    throw _pagingConfig.Error;
                }
            });

            var orderedResults = results
                .Select(d => d[AFSummaryTypes.Range])
                .Where(v => v.IsGood)
                .Select(v => Tuple.Create(v.Attribute.Element.Name, Convert.ToSingle(v.Value) / 7))
                .OrderBy(t => t.Item1);

            using (StreamWriter writer = new StreamWriter(FluctuationIndexReportFile + _programStartTime.ToString("_MMddyyyy_HHmm") + @".csv", true))
            {
                writer.WriteLine(@"Name, Fluctuation Index");

                foreach (var elementNameAndValue in orderedResults)
                {
                    writer.WriteLine(String.Format("{0}, {1}", elementNameAndValue.Item1, elementNameAndValue.Item2.ToString()));
                }
            }
        }

        /// <summary>
        /// Operate on a list of AF attributes in bulk and parallel
        /// </summary>
        /// <typeparam name="T">the type of expected results</typeparam>
        /// <param name="attributes">the list of AF attributes to process</param>
        /// <param name="listOperator">the operation definition</param>
        /// <returns>Operation results</returns>
        private IEnumerable<T> SummarizeAttributes<T>(IEnumerable<AFAttribute> attributes, Func<AFAttributeList, IList<T>> listOperator)
        {
            ConcurrentBag<IEnumerable<T>> results = new ConcurrentBag<IEnumerable<T>>();

            AFAttributeList[] attributeLists = ChunkifyAFAttributes(attributes, PIPageSize);

            Parallel.ForEach(attributeLists, new ParallelOptions { MaxDegreeOfParallelism = MaxParallel }, attributeList =>
            {
                if (attributeList.Count == 0)
                    return;

                var result = listOperator(attributeList);
                results.Add(result);
            });

            return results.SelectMany(r => r);
        }

        /// <summary>
        /// Divide a list of AFAttribute into multiple pages
        /// </summary>
        private AFAttributeList[] ChunkifyAFAttributes(IEnumerable<AFAttribute> attributes, int pageSize)
        {
            int listCount = attributes.Count() / pageSize + 1;
            var attributeLists = Enumerable.Range(0, listCount).Select(i => new AFAttributeList()).ToArray();
            int num = 0;
            foreach (var attribute in attributes)
            {
                attributeLists[num % listCount].Add(attribute);
                ++num;
            }

            return attributeLists;
        }

        /// <summary>
        /// Create an Event Frame when the leaf element shows the target mode
        /// </summary>
        /// <param name="obj"></param>
        private void CreateEFOnTargetMode(AFValue obj)
        {
            var newMode = ((AFEnumerationValue)obj.Value).Name;

            if (String.Equals(newMode, _leafTargetMode, StringComparison.OrdinalIgnoreCase))
            {
                var element = (AFElement)obj.Attribute.Element;

                var time = obj.Timestamp;
                AFEventFrame ef = new AFEventFrame(element.Database, String.Format("{0}_{1}_{2}", element.Name, time.ToString("yyyy_MM_dd_HH_mm"), _leafTargetMode));
                ef.PrimaryReferencedElement = element;
                ef.SetStartTime(time);
                ef.SetEndTime(AFTime.Now);
                ef.CheckIn();
            }
        }

    }
}
