using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace StatsHelix.Charizard
{
    public static class CaptureIter
    {
        public static IEnumerable<string> ToEnumerable(this CaptureCollection c) => Enumerable.Range(0, c.Count).Select(i => c[i].Value);
    }
}
