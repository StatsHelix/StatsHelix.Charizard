using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    static class Helpers
    {
        public static V GetValueOrDefault<K, V>(this Dictionary<K, V> dic, K key, V defaultValue)
        {
            if (dic.TryGetValue(key, out V val))
                return val;

            return defaultValue;
        }
    }
}
