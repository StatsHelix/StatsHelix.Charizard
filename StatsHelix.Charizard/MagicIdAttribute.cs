using System;

namespace StatsHelix.Charizard
{
    /// <summary>
    /// This attribute is applied to the StatsHelix.Charizard.Dynamic assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = false)]
    public sealed class MagicIdAttribute : Attribute
    {
        /// <summary>
        /// This hash is used to determine whether the dynamic code needs to be regenerated.
        /// </summary>
        public string Hash { get; }

        public MagicIdAttribute(string hash)
        {
            Hash = hash;
        }
    }
}

