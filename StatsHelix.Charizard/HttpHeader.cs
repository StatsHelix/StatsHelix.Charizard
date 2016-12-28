using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    [DebuggerDisplay("{Name,nq}: {Value,nq}")]
    public struct HttpHeader
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
