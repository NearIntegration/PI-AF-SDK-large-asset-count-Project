using OSIsoft.AF;
using OSIsoft.AF.Asset;
using OSIsoft.AF.PI;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Utilities
{
    public static class ElementCreator
    {
        private const int POINTCREATION_CHUNKSIZE = 1000;

        /// <summary>
        /// Create or update PI point data reference for one AF element
        /// </summary>
        /// <param name="elementToProcess"></param>
        public static void CreateorUpdatePIPointDataReference(AFElement elementToProcess)
        {
            IList<AFElement> listElem = new List<AFElement>();
            listElem.Add(elementToProcess);
            CreateorUpdatePIPointDataReference(listElem);
        }

        /// <summary>
        /// Create or update PI point data reference for a list of AF elements
        /// </summary>
        /// <param name="elementsToProcess"></param>
        /// <param name="updateProgress"></param>
        public static void CreateorUpdatePIPointDataReference(
            IList<AFElement> elementsToProcess,
            EventHandler<AFProgressEventArgs> updateProgress = null)
        {
            List<List<AFElement>> elementChunks = elementsToProcess.Select((x, i) => new { Value = x, Index = i })
                                                                .GroupBy(e => e.Index / POINTCREATION_CHUNKSIZE)
                                                                .Select(g => g.Select(e => e.Value).ToList())
                                                                .ToList();

            foreach (var chunk in elementChunks)
            {
                var elmByTemplate = chunk.GroupBy(elm => elm.Template).ToDictionary(grp => grp.Key, grp => grp.ToList());

                foreach (var kvp in elmByTemplate)
                {
                    var currentTemplate = kvp.Key;
                    var processedAttributes = new HashSet<String>();
                    while (currentTemplate != null)
                    {
                        foreach (var at in currentTemplate.AttributeTemplates)
                        {
                            if (processedAttributes.Contains(at.Name))
                                continue;

                            try
                            {
                                ProcessAttributeTemplate(at, kvp.Value, updateProgress);
                                processedAttributes.Add(at.Name);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine("Exception reported at CreatePointsNConfig : {0}", ex);
                            }
                        }

                        currentTemplate = currentTemplate.BaseTemplate;
                    }
                }
            }
        }

        /// <summary>
        /// Create or update PI point data reference for an AF attribute in a list of AF elements
        /// </summary>
        /// <param name="at"></param>
        /// <param name="elist"></param>
        /// <param name="updateProgress"></param>
        private static void ProcessAttributeTemplate(AFAttributeTemplate at, IList<AFElement> elist, EventHandler<AFProgressEventArgs> updateProgress = null)
        {
            if (at.DataReference == null || !at.DataReference.Name.Equals("PI Point", StringComparison.OrdinalIgnoreCase))
                return;

            // Load attributes
            IList<AFAttributeTemplate> ats = new List<AFAttributeTemplate>();
            ats.Add(at);
            AFElement.LoadAttributes(elist, ats);

            ProcessAttributes(elist.Select(elm => elm.Attributes[at.Name]).ToList(), updateProgress);
        }

        /// <summary>
        /// Process a list of AF Attributes
        /// </summary>
        /// <param name="attributes"></param>
        /// <param name="updateProgress"></param>
        private static void ProcessAttributes(IList<AFAttribute> attributes, EventHandler<AFProgressEventArgs> updateProgress = null)
        {
            PIServer targetPI = null;
            IDictionary<string, IDictionary<string, object>> pointDefinitions = new Dictionary<string, IDictionary<string, object>>();

            var PIPointInfoList = new List<PIPointInfo>();
            foreach (AFAttribute attr in attributes)
            {
                PIPointInfoList.Add(ParseConfigString(attr));
            }

            var attributesByPIDataArchive = PIPointInfoList.GroupBy(info => info.PIDataArchiveName);
            foreach (var group in attributesByPIDataArchive)
            {
                targetPI = FindTargetPIServer(group.Key);

                foreach (var info in group)
                {
                    pointDefinitions.Add(info.PointName, info.PointAttributes);
                }

                //Create PI Points          
                CreatePIPoints(targetPI, pointDefinitions);
                SetConfigStrings(attributes);

                if (updateProgress != null)
                {
                    AFProgressEventArgs eventArgs = new AFProgressEventArgs(AFProgressStatus.InProgress,
                        AFIdentity.AttributeList,
                        new Guid(),
                        "Succeeded");

                    eventArgs.OperationsCompleted = pointDefinitions.Count;
                    updateProgress(null, eventArgs);
                }

                pointDefinitions.Clear();
            }
        }

        /// <summary>
        /// Compose a PIPointInfo object based on an AF Attribute
        /// </summary>
        /// <param name="attribute"></param>
        /// <returns></returns>
        private static PIPointInfo ParseConfigString(AFAttribute attribute)
        {
            string configStringInput = attribute.ConfigString;

            var configStringSplits = configStringInput.Split(new Char[] { '\\', ';' }, StringSplitOptions.RemoveEmptyEntries);
            string PIDataArchiveName = configStringSplits[0];
            PIDataArchiveName = PIDataArchiveName.Split(new Char[] { '?' }, StringSplitOptions.RemoveEmptyEntries)[0];
            string pointNameFormat = configStringSplits[1];

            string pointName = GetSubstitutedPIPointName(pointNameFormat, attribute.Element, attribute.Name);

            int ci = configStringInput.IndexOf(';');
            IDictionary<string, object> pointAttributes = ci != -1 ? GetPointAttributes(configStringInput.Substring(ci + 1)) : null;

            return new PIPointInfo { PointName = pointName, PIDataArchiveName = PIDataArchiveName, PointAttributes = pointAttributes };
        }

        /// <summary>
        /// Substitute placeholders in a name template with actual values
        /// </summary>
        /// <param name="pointNameTemplate"></param>
        /// <param name="element"></param>
        /// <param name="attrName"></param>
        /// <returns></returns>
        private static string GetSubstitutedPIPointName(string pointNameTemplate, AFBaseElement element, string attrName)
        {
            // Find the substitutions in the point name template string.
            Regex regex = new Regex("%[^%]+%");
            MatchCollection substitutions = regex.Matches(pointNameTemplate, 0);

            var pointName = pointNameTemplate;
            foreach (var substitution in substitutions)
            {
                var sub = substitution.ToString();
                switch (sub)
                {
                    case "%Element%":
                        pointName = pointName.Replace(sub, element.Name);
                        break;
                    case "%Attribute%":
                        pointName = pointName.Replace(sub, attrName);
                        break;
                    default:
                        if (sub.StartsWith(@"%@"))
                        {
                            var siblingAttribute = sub.Substring(2, sub.Length - 3);
                            pointName = pointName.Replace(sub, element.Attributes[siblingAttribute].GetValue().ToString());
                        }
                        break;
                }
            }

            return pointName;
        }

        /// <summary>
        /// Parse a configuration string to get a collection of PI point attributes
        /// </summary>
        /// <param name="ptConfigString"></param>
        /// <returns></returns>
        private static IDictionary<string, object> GetPointAttributes(string ptConfigString)
        {
            Dictionary<string, object> pointAttributes = new Dictionary<string, object>();
            string[] split = ptConfigString.Split(';');
            foreach (string p in split)
            {
                int ci = p.IndexOf('=');
                if (ci > 0)
                {
                    string attr = p.Substring(0, ci);
                    string value = p.Substring(ci + 1);
                    pointAttributes.Add(attr, value);
                }
            }

            return pointAttributes;
        }

        /// <summary>
        /// Find the target PI Data Archive
        /// </summary>
        /// <param name="PIDataArchiveName"></param>
        /// <returns></returns>
        private static PIServer FindTargetPIServer(string PIDataArchiveName)
        {
            PIServer targetPI = null;

            if (PIDataArchiveName.Equals(@"%Server%", StringComparison.OrdinalIgnoreCase))
                targetPI = new PIServers().DefaultPIServer;
            else
                targetPI = new PIServers()[PIDataArchiveName];

            return targetPI;
        }

        /// <summary>
        /// Create any non-existing PI points based on a list of point definitions on a given PI Data Archive
        /// </summary>
        /// <param name="targetPI"></param>
        /// <param name="pointDefinitions"></param>
        private static void CreatePIPoints(PIServer targetPI, IDictionary<string, IDictionary<string, object>> pointDefinitions)
        {
            IEnumerable<string> pointNames = pointDefinitions.Keys;

            // See what points exist
            var resolvedPoints = new HashSet<string>(PIPoint.FindPIPoints(targetPI, pointNames)
                .Select(pt => pt.Name));

            // Filter out existing points
            var pointsToCreate = pointDefinitions
                .Where(p => !resolvedPoints.Contains(p.Key))
                .ToDictionary(p => p.Key, p => p.Value);

            // Create any points with default PI point attributes
            IEnumerable<string> pointsWithDefaultAttributes = pointsToCreate
                .Where(p => p.Value == null)
                .Select(p => p.Key)
                .ToList();
            var results = targetPI.CreatePIPoints(pointsWithDefaultAttributes);
            if (results.Errors.Count > 0)
                throw new AggregateException(results.Errors.Values);

            // Create other PI points
            foreach (var pt in pointsWithDefaultAttributes)
            {
                pointsToCreate.Remove(pt);
            }
            results = targetPI.CreatePIPoints(pointsToCreate);
            if (results.Errors.Count > 0)
                throw new AggregateException(results.Errors.Values);
        }

        /// <summary>
        /// Set AF Attribute's ConfigString based on existing PI points
        /// </summary>
        /// <param name="attributes"></param>
        private static void SetConfigStrings(IList<AFAttribute> attributes)
        {
            AFAttributeList attrList = new AFAttributeList(attributes);
            var points = attrList.GetPIPoint();

            IList<string> configStrings = new List<string>(attrList.Count);

            foreach (var attrPoint in points.Results)
            {
                AFAttribute attribute = attrPoint.Key;
                string configString = attrPoint.Value.GetPath(AFEncodeType.NameThenID).Replace(";{", "?").Replace("}", "").Replace(";?", "?");
                configStrings.Add(configString);
            }

            AFAttribute.SetConfigStrings(attrList, configStrings);
        }
    }

    /// <summary>
    /// A wrapper class for creating PI point
    /// </summary>
    class PIPointInfo
    {
        public string PointName { get; set; }
        public string PIDataArchiveName { get; set; }
        public IDictionary<string, object> PointAttributes { get; set; }
    }
}
