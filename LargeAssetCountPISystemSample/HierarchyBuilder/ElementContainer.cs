using OSIsoft.AF.Asset;
using System;
using System.Collections.Generic;

namespace HierarchyBuilder
{
    /// <summary>
    /// This class holds the container element and its child elements for a single hierarchy level
    /// </summary>
    class ElementContainer
    {
        #region Private Members
        
        // Dictionary used to hold all child elements
        private Dictionary<String, AFElement> _elements = new Dictionary<string, AFElement>();
        // Function to create a non-leaf element name, it depends on customer's preference
        private Func<String, String, String> GetNonLeafElementName = (templateName, id) => (templateName + Convert.ToInt32(id).ToString("D8"));
        private Boolean _isLeaf = false;
        private object _lock = new object();

        #endregion

        #region Public Properties
        
        public AFElementTemplate ElementTemplate { get; private set; }
        public AFElement ContainerElement { get; private set; } 

        #endregion

        #region Constructors
        
        public ElementContainer(AFElementTemplate elementTemplate, AFElement containerElement, Boolean isLeaf)
        {
            ElementTemplate = elementTemplate;
            ContainerElement = containerElement;
            _isLeaf = isLeaf;

            if (!isLeaf)
            {
                foreach (var item in ContainerElement.Elements)
                {
                    _elements.Add(item.Name, item);
                }
            }
        } 

        #endregion

        #region Public Methods
        
        public AFElement GetorCreateNonLeafElementWithoutCheckIn(string id)
        {
            if (_isLeaf)
            {
                throw new InvalidOperationException("Method is not designed to get or create a leaf element.");
            }

            var elementName = GetNonLeafElementName(ElementTemplate.Name, id);
            
            AFElement elementToReturn;
            lock (_lock)
            {
                if (!_elements.ContainsKey(elementName))
                {
                    elementToReturn = ContainerElement.Elements.Add(elementName, ElementTemplate);
                    _elements.Add(elementName, elementToReturn);
                }
                else
                    elementToReturn = _elements[elementName];
            }

            return elementToReturn;
        } 

        #endregion
    }
}
