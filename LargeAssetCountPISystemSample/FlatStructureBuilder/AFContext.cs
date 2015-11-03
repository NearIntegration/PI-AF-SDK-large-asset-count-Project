using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OSIsoft.AF;
using OSIsoft.AF.Asset;

namespace FlatStructureBuilder
{
    public class AFContext
    {

        public AFDatabase Database { get; set; }
        public AFElementTemplate BaseLeafTemplate { get; set; }
        public AFElementTemplate SinusoidLeafTemplate { get; set; }
        public AFElementTemplate RandomLeafTemplate { get; set; }

        public object DbLock { get; private set; }

        public AFContext()
        {
            DbLock = new object();
        }
        

    }
}
