using OSIsoft.AF;
using OSIsoft.AF.Asset;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using Utilities;

namespace HierarchyBuilder
{
    /// <summary>
    /// This program is to build an AF hierarchy from a flat structure
    /// </summary>
    class HierarchyBuilder : IDisposable
    {
        private const int ChunkSize = 10000;
        private const int RefreshIntervalInMilliseconds = 10000;

        private AFReferenceType _weakReferenceType;
        private List<ElementContainer> _elementContainers;
        private Timer _refreshTimer;
        private object _dbCookie;
        private object _lock = new object();
        private AFDatabase _targetDatabase;
        private string[] _hierarchyLevels;
        private string _leafElementTemplateName;
        private bool _disposed;

        public HierarchyBuilder(Settings settings)
        {
            _targetDatabase = settings.TargetDatabase;
            _hierarchyLevels = settings.HierarchyLevels;
            _leafElementTemplateName = settings.LeafElementTemplateName;
            _weakReferenceType = _targetDatabase.ReferenceTypes["Weak Reference"];

            // If the cookie is null, initialize it to start monitoring changes
            if (ReferenceEquals(_dbCookie, null))
                _targetDatabase.FindChangedItems(false, int.MaxValue, _dbCookie, out _dbCookie);
        }

        /// <summary>
        /// Build and update hierarchy
        /// </summary>
        public void Run()
        {
            BuildContainers();

            BuildHierarchy();

            UpdateHierarchyOnTheFly();
        }

        /// <summary>
        /// Build element containers
        /// </summary>
        public void BuildContainers()
        {
            // Add the leaf ElementConainer
            _elementContainers = new List<ElementContainer>();
            _elementContainers.Add(new ElementContainer(_targetDatabase.ElementTemplates[_leafElementTemplateName],
                _targetDatabase.Elements[String.Format(Constants.CONTAINERELEMENT_NAMEFORMAT, _leafElementTemplateName)],
                true));

            // Build container elements for non-leaf levels
            var topLevelIndex = _hierarchyLevels.Count() - 1;
            for (int i = 1; i < _hierarchyLevels.Count(); i++)
            {
                // Check the existence of other element templates
                if (!_targetDatabase.ElementTemplates.Contains(_hierarchyLevels[i]))
                {
                    throw new InvalidOperationException(
                        String.Format("Cannot find the expected element template, {0}, in AF Database, {1}.", _hierarchyLevels[i], this._targetDatabase.Name));
                }

                // The name of container element usually follows a defined format. However, if it is the top level, a special name, "HierarchyRoot", is used 
                // so that end users can easily identify the entry point of a complete hierarchy.
                String containerElementName = (i == topLevelIndex) ? Constants.HIERARCHY_ROOT : String.Format(Constants.CONTAINERELEMENT_NAMEFORMAT, _hierarchyLevels[i]);

                if (!_targetDatabase.Elements.Contains(containerElementName))
                    _targetDatabase.Elements.Add(containerElementName);

                _elementContainers.Add(new ElementContainer(_targetDatabase.ElementTemplates[_hierarchyLevels[i]],
                    _targetDatabase.Elements[containerElementName],
                    false));
            }

            // Check in all hierarchy container elements
            _targetDatabase.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisThread);
        }

        /// <summary>
        /// Build the entire hierarchy based on all leaf elements
        /// </summary>
        private void BuildHierarchy()
        {
            int index = 0;
            int total;

            do
            {
                var leafElements = _elementContainers[0]
                    .ElementTemplate
                    .FindInstantiatedElements(true, AFSortField.Name, AFSortOrder.Ascending, index, ChunkSize, out total);

                var elementCount = leafElements.Count;
                if (elementCount == 0)
                    break;

                Console.WriteLine(
                    "{0} | StartIndex = {1} | Found a chunk of {2} leaf elements using FindInstantiatedElements",
                    DateTime.Now,
                    index,
                    elementCount);

                // Convert a list of AFBaseElement to a list of AFElement
                IList<AFElement> elist = leafElements.Select(elm => (AFElement)elm).ToList();
                BuildorUpdateHierarchy(elist);
                Console.WriteLine(
                    "{0} | StartIndex = {1} | Finished hierarchy building for a chunk of {2} leaf elements",
                    DateTime.Now,
                    index,
                    elementCount);

                index += ChunkSize;
#if DEBUG
            } while (index < 50000);
# else
            } while (index < total);
# endif

            Console.WriteLine(
                 "{0} | Finished hierarchy building for a total of {1} leaf elements",
                 DateTime.Now,
                 total);
        }

        /// <summary>
        /// Sign up for database changes and update the hierarchy if necessary
        /// </summary>
        private void UpdateHierarchyOnTheFly()
        {
            // Sign up for the AF Database's Changed event
            EventHandler<AFChangedEventArgs> changedEH = new EventHandler<AFChangedEventArgs>(OnChanged);
            _targetDatabase.Changed += changedEH;

            // Create a timer to refresh the database periodically
            _refreshTimer = new Timer(RefreshIntervalInMilliseconds);
            ElapsedEventHandler elapsedEH = new ElapsedEventHandler(OnElapsed);
            _refreshTimer.Elapsed += elapsedEH;

            // Set AutoReset to false so that we can delay the next trigger if the program is still processing previous changes
            _refreshTimer.AutoReset = false;
            _refreshTimer.Start();
        }

        /// <summary>
        /// Handles the Elapsed event of a Timer
        /// </summary>
        private void OnElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock (_lock)
            {
                Console.WriteLine("Refreshing AF Database for changes");
                _targetDatabase.Refresh();
                _refreshTimer.Start();
            }
        }

        /// <summary>
        /// Handles the Changed event of an AF Database
        /// </summary>
        private void OnChanged(object sender, AFChangedEventArgs e)
        {
            lock (_lock)
            {
                // Find changes made by other users.
                List<AFChangeInfo> list = new List<AFChangeInfo>();
                list.AddRange(_targetDatabase.FindChangedItems(false, int.MaxValue, _dbCookie, out _dbCookie));

                if (list.Count == 0)
                    return;

                // Refresh objects that have been changed.
                AFChangeInfo.Refresh(_targetDatabase.PISystem, list);

                // Find leaf elements that have been changed.
                List<AFElement> changedLeafElements = new List<AFElement>();
                foreach (AFChangeInfo info in list)
                {
                    AFObject obj = info.FindObject(_targetDatabase.PISystem);
                    if (obj is AFElement)
                    {
                        AFElement elm = (AFElement)obj;
                        string baseTemplateName = AFHelper.GetBaseTemplateName(elm.Template);
                        if (String.Equals(baseTemplateName, _hierarchyLevels[0], StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("{0} | Change in leaf element {1} detected", DateTime.Now, elm.Name);
                            changedLeafElements.Add(elm);
                        }
                    }
                }

                // Update hierarchy with the changed elements
                BuildorUpdateHierarchy(changedLeafElements);
            }
        }

        /// <summary>
        /// Build or update partial hierarchy based on a set of leaf elements
        /// </summary>
        private void BuildorUpdateHierarchy(IList<AFElement> leafElements)
        {
            if (leafElements == null || leafElements.Count == 0)
                return;

            Console.WriteLine("{0} | Starting to build hierarchy for {1} leaf elements", DateTime.Now, leafElements.Count);

            // Load attributes representing higher levels from leaf elements, assuming the higher-level element template name is the same as the attribute template name in leaf elements.
            IList<AFAttributeTemplate> ats = new List<AFAttributeTemplate>();
            foreach (var currentContainer in _elementContainers.Skip(1))
            {
                // The first element container in the list holds the leaf elements
                ats.Add(_elementContainers[0].ElementTemplate.AttributeTemplates[currentContainer.ElementTemplate.Name]);
            }
            AFElement.LoadAttributes(leafElements, ats);

            // Iterate through each hierarchical level above the leaf from bottom to top
            foreach (var container in _elementContainers.Skip(1))
            {
                int currentLevel = _elementContainers.IndexOf(container);
                var lowerLevelContainer = _elementContainers[currentLevel - 1];
                            
                // Build a mapping dictionary between the current level and the leaf level
                // Key is the element at the current level, Value is a list of leaf elements under the Key element
                Dictionary<string, List<AFElement>> mappings = leafElements
                    .GroupBy(elm => elm.Attributes[container.ElementTemplate.Name].GetValue().Value.ToString())
                    .Where(grp => !String.IsNullOrWhiteSpace(grp.Key))
                    .ToDictionary(grp => grp.Key, grp => grp.ToList());

                // Create the key element (parent) for each group and add children to their parent with the weak reference
                List<AFElement> elementsToProcess = new List<AFElement>();
                foreach (var kvp in mappings)
                {
                    AFElement parentElement = container.GetorCreateNonLeafElementWithoutCheckIn(kvp.Key);

                    if (AFHelper.IsAnyAttributeDataReferenceDefinedByTemplate(parentElement))
                        elementsToProcess.Add(parentElement);

                    // Search for child elements or lower level elements
                    List<AFElement> childElements;
                    if (currentLevel == 1)
                    {
                        childElements = kvp.Value;
                    }
                    else
                    {
                        var distinctChildElementNames = kvp.Value.Select(leafElm => leafElm.Attributes[lowerLevelContainer.ElementTemplate.Name].GetValue().Value.ToString()).Distinct();
                        childElements = distinctChildElementNames.Select(name => lowerLevelContainer.GetorCreateNonLeafElementWithoutCheckIn(name)).ToList();
                    }

                    // Update hierarchy weak reference
                    childElements.ForEach(childElm =>
                    {
                        var parents = childElm.GetParents(_weakReferenceType, AFSortField.Name, AFSortOrder.Ascending, int.MaxValue);

                        // Build the weak reference between parent and child, depending on the number of existing weak-reference parents.
                        switch (parents.Count)
                        {
                            case 0:
                                parentElement.Elements.Add(childElm, _weakReferenceType);
                                break;
                            case 1:
                                if (parentElement != parents[0])
                                {
                                    parents[0].Elements.Remove(childElm);
                                    parentElement.Elements.Add(childElm, _weakReferenceType);
                                }
                                break;
                            default:
                                Console.WriteLine(
                                    "{0} | Warning | The leaf element, {1}, had more than one weak-reference parents. Any invalid parents will be removed in the hierarchy",
                                    DateTime.Now,
                                    childElm.Name);

                                foreach (var invalidParent in parents.Where(elm => elm != parentElement))
                                {
                                    invalidParent.Elements.Remove(childElm);
                                }

                                if (!parents.Contains(parentElement))
                                    parentElement.Elements.Add(childElm, _weakReferenceType);
                                break;
                        }
                    });
                }

                // Create and resolve PI Point paths in new elements if necessary
                ElementCreator.CreateorUpdatePIPointDataReference(elementsToProcess);

                _targetDatabase.CheckIn(AFCheckedOutMode.ObjectsCheckedOutThisThread);

                Console.WriteLine("{0} | Finished building hierarchy at {1} level", DateTime.Now, container.ElementTemplate);
            }

            Console.WriteLine("{0} | Finished building hierarchy for {1} leaf elements", DateTime.Now, leafElements.Count);
        }
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if(disposing)
            {
                lock (_lock)
                {
                    if (_refreshTimer != null)
                    {
                        _refreshTimer.Dispose();
                    }

                    if (_targetDatabase != null)
                    {
                        _targetDatabase.PISystem.Dispose();
                    }
                }
            }

            _disposed = true;
        }
    }
}