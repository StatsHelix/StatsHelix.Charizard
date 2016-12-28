using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    internal class DynamicMsilManager : MarshalByRefObject
    {
        private const string MagicIdClass = "MagicId";
        private const string MagicIdMember = "Value";

        private string Filename { get { return $"{AssemblyName}.dll"; } }
        private string MagicIdFullName { get { return $"{Namespace}.{MagicIdClass}"; } }
        private string AssemblyName { get; }
        private string Namespace { get; }
        private string Path { get; }

        /// <summary>
        /// Creates a new DynamicMsilManager.
        /// </summary>
        /// <param name="nameSpace">The namespace to define our Id class in.</param>
        /// <param name="assemblyName">The assembly name (also used as filename for the result DLL).</param>
        /// <param name="path">The folder where the DLL should be saved. May be null to save it in the current directory.</param>
        public DynamicMsilManager(string nameSpace, string assemblyName, string path = null)
        {
            Namespace = nameSpace;
            AssemblyName = assemblyName;
            Path = path;
        }

        private class Invoker : MarshalByRefObject
        {
            public DynamicMsilManager Parent { get; set; }

            /// <summary>
            /// Loads the assembly as reflection-only and reads the magic constant.
            /// </summary>
            public string ReadMagic()
            {
                try
                {
                    var cta = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(Parent.Filename));
                    return cta?.GetType(Parent.MagicIdFullName)?.GetField(MagicIdMember)?.GetValue(null) as string;
                }
                catch (Exception)
                {
                }
                return null;
            }

            /// <summary>
            /// Generates a new cache assembly.
            /// </summary>
            /// <param name="expectedId">The magic constant to write into the generated assembly.</param>
            /// <param name="generator">
            /// The generator that populates the assembly with code.
            /// Note that you MUST be careful as this has to be a "simply" delegate that's just a static function without any context.
            /// In particular, it CAN'T be a delegate to a function on a MarshalByRefObject as that will transfer right back into the
            /// original AppDomain!
            /// </param>
            /// <param name="param">Parameter object for the generator.</param>
            public void WriteMagic(string expectedId, Action<object, ModuleBuilder> generator, object param)
            {
                // If it doesn't match, we have to regenerate it.
                var assBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(Parent.AssemblyName), AssemblyBuilderAccess.Save);
                var modBuilder = assBuilder.DefineDynamicModule(Parent.AssemblyName + ".dll");

                var type = modBuilder.DefineType(Parent.MagicIdFullName, TypeAttributes.Class | TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract);
                var testField = type.DefineField(MagicIdMember, typeof(string), FieldAttributes.Literal | FieldAttributes.Public | FieldAttributes.Static);
                testField.SetConstant(expectedId);
                type.CreateType();

                generator(param, modBuilder);

                assBuilder.Save(Parent.Filename);
            }
        }

        /// <summary>
        /// Checks whether the current cache assembly is still usable and regenerates it if necessary.
        /// </summary>
        /// <param name="expectedId">The expected Id string inside the cache assembly.</param>
        /// <param name="generator">
        /// The generator that populates the assembly with code.
        /// Note that you MUST be careful as this has to be a "simply" delegate that's just a static function without any context.
        /// In particular, it CAN'T be a delegate to a function on a MarshalByRefObject as that will transfer right back into the
        /// original AppDomain!
        /// </param>
        /// <param name="param">Parameter object for the generator.</param>
        /// <returns>The assembly (always reloaded from disk).</returns>
        public Assembly Generate(string expectedId, Action<object, ModuleBuilder> generator, object param)
        {
            // Run this stuff in its own AppDomain so we can unload the assembly.
            var readDomain = AppDomain.CreateDomain("DynamicMsilManager.MagicalJourney");
            try
            {
                var invo = (Invoker)readDomain.CreateInstanceAndUnwrap(typeof(Invoker).Assembly.FullName, typeof(Invoker).FullName);
                invo.Parent = this;
                var magicValue = invo.ReadMagic();
                if (magicValue != expectedId)
                {
                    invo.WriteMagic(expectedId, generator, param);
                }

                return Assembly.LoadFrom(Filename);
            }
            finally
            {
                AppDomain.Unload(readDomain);
            }
        }
    }
}
