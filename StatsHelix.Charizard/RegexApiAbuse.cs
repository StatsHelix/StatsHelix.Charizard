using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    public class RegexApiAbuse
    {
        public ModuleBuilder ModuleBuilder { get; }

        private readonly MethodInfo RegexParser_Parse;
        private readonly MethodInfo RegexWriter_Write;
        private readonly MethodInfo FactoryTypeFromCode;
        private readonly MethodInfo GenerateRegexType;

        private readonly object RtcObj;

        /// <summary>
        /// Initializes the (private) RegexCompiler from MS referencesource.
        /// </summary>
        /// <param name="name">The name of the assembly to compile to.</param>
        public RegexApiAbuse(ModuleBuilder moduleBuilder)
        {
            var ass = typeof(Regex).Assembly;
            var compiler = ass.GetType("System.Text.RegularExpressions.RegexCompiler");
            var rtcKlass = ass.GetType("System.Text.RegularExpressions.RegexTypeCompiler");
            var xy = rtcKlass.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);

            // Grab an /uninitialized/ object because we create our own AssemblyBuilder.
            // The RegexCompiler sets everything to SecurityTransparent which messes up ... everything.
            RtcObj = FormatterServices.GetSafeUninitializedObject(rtcKlass);

            FactoryTypeFromCode = rtcKlass.GetMethod("FactoryTypeFromCode", BindingFlags.Instance | BindingFlags.NonPublic);
            GenerateRegexType = rtcKlass.GetMethod("GenerateRegexType", BindingFlags.Instance | BindingFlags.NonPublic);
            var moduleBuilderField = rtcKlass.GetField("_module", BindingFlags.Instance | BindingFlags.NonPublic);

            RegexParser_Parse = ass.GetType("System.Text.RegularExpressions.RegexParser").GetMethod("Parse", BindingFlags.NonPublic | BindingFlags.Static);
            RegexWriter_Write = ass.GetType("System.Text.RegularExpressions.RegexWriter").GetMethod("Write", BindingFlags.NonPublic | BindingFlags.Static);

            ModuleBuilder = moduleBuilder;

            moduleBuilderField.SetValue(RtcObj, ModuleBuilder);
        }

        /// <summary>
        /// Compiles a given regex to the current assembly.
        /// </summary>
        /// <param name="pattern">The regex to compile.</param>
        /// <param name="options">Regex options to use.</param>
        /// <param name="fullName">Full name of the class to compile to.</param>
        /// <param name="mTimeout">Timeout for the regex matching.</param>
        /// <param name="isPublic">Whether the generated class should be public.</param>
        public void CompileRegex(string pattern, RegexOptions options, string fullName, TimeSpan mTimeout, bool isPublic)
        {
            var tree = RegexParser_Parse.Invoke(null, new object[] { pattern, options });
            var code = RegexWriter_Write.Invoke(null, new object[] { tree });
            var factory = FactoryTypeFromCode.Invoke(RtcObj, new object[] { code, options, fullName });
            GenerateRegexType.Invoke(RtcObj, new object[] { pattern, options, fullName, isPublic, code, tree, factory, mTimeout });
        }
    }
}
