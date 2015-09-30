using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using OSIsoft.AF;

namespace Utilities
{
    internal class PISystemConnection
    {
        public PISystem AFServer { get; set; }
        public AFDatabase Database { get; set; }

        public string sPIServer { get; set; }

    }
}
