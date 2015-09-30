
using OSIsoft.AF.Asset;
using System;
using System.IO;
using Utilities;

namespace CalculationEngine
{
    class OutlierReporter
    {
        private readonly string _fileName;
        private readonly Object _fileLock = new object();

        public OutlierReporter(string fileName)
        {
            _fileName = fileName;
        }

        /// <summary>
        /// Compare the value with the specified threshold and report outliers
        /// </summary>
        /// <param name="obj"></param>
        public void ReportOutlier(AFValue obj)
        {
            var value = Convert.ToSingle(obj.Value);
            var element = (AFElement)obj.Attribute.Element;
            var threshold = Convert.ToSingle(element.Attributes[Constants.THRESHOLD_ATTRIBUTE].GetValue().Value);

            if (value > threshold)
            {
                lock (_fileLock)
                {
                    using (StreamWriter writer = new StreamWriter(_fileName, true))
                    {
                        writer.WriteLine("Found outlier in Branch element {0} at {1}", element.Name, obj.Timestamp);
                    }
                }
            }
        }
    }
}
