using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    class TypeScriptCodeGen
    {
        const int WhitespaceSize = 2;

        internal static string GenerateApi(RoutingManager.CodegenInfo info)
        {
            var controllerString = string.Join(", ", info.Controllers.Select(GetController));

            StringBuilder res = new StringBuilder();

            res.AppendLine("export const api = {");
            res.AppendLine(Indent(controllerString));
            res.AppendLine("}");

            return res.ToString();
        }

        static string GetController(RoutingManager.ControllerDefinition controller)
        {
            var methodsString = string.Join(", ", controller.Actions.Select(GetAction));


            var res = new StringBuilder();
            res.AppendLine($"\"{controller.Type.Name}\": {{");


            res.Append("}");

            return res.ToString();
        }

        static string GetAction(RoutingManager.ActionDefinition action)
        {
            return "";
        }

        static string Indent(string data, int depth = 1)
        {
            var res = data.Split('\n');
            for (var i = 0; i < res.Length; i++)
                res[i] = "".PadLeft(WhitespaceSize * depth, ' ') + res[i].TrimEnd('\r');

            return string.Join(Environment.NewLine, res);
        }
    }
}
