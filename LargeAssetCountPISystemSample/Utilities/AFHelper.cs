using OSIsoft.AF.Asset;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utilities
{
    public static class AFHelper
    {
        public static bool IsAnyAttributeDataReferenceDefinedByTemplate(AFElement elm)
        {
            foreach (var item in elm.Attributes)
            {
                if (item.IsDataReferenceDefinedByTemplate)
                    return true;
            }

            return false;
        }

        public static string GetBaseTemplateName(AFElementTemplate elmTemplate)
        {
            if (elmTemplate == null)
                return String.Empty;

            AFElementTemplate currElementTemplate = elmTemplate;
            while (currElementTemplate.BaseTemplate != null)
            {
                currElementTemplate = currElementTemplate.BaseTemplate;
            }

            return currElementTemplate.Name;
        }

        public static AFAttributeTemplate GetLastAttributeTemplateOverride(AFElementTemplate elmTemplate, string attributeName)
        {
            if (elmTemplate == null)
                throw new ArgumentNullException("elmTemplate");

            AFElementTemplate currElementTemplate = elmTemplate;
            do
            {
                var attrTemplate = currElementTemplate.AttributeTemplates[attributeName];

                if (attrTemplate != null)
                    return attrTemplate;
                else
                    currElementTemplate = currElementTemplate.BaseTemplate;
            } while (currElementTemplate != null);

            return null;
        }
    }
}
