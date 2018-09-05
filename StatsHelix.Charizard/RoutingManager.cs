using Newtonsoft.Json;
using StatsHelix.Charizard.Backend;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StatsHelix.Charizard
{
    // aka google maps
    public class RoutingManager
    {
        private const string CharizardDynamic = "StatsHelix.Charizard.Dynamic";
        private const string RequestDispatcher = CharizardDynamic + ".RequestDispatcher";
        private static readonly Type[] QuerystringPrimitives = new[] { typeof(string), typeof(string[]), typeof(int), typeof(long), typeof(bool), typeof(double) };

        [Serializable]
        private class CodegenInfo
        {
            public ControllerDefinition[] Controllers { get; set; }
            public MethodInfo[] AppMiddleware { get; set; }
        }

        [Serializable]
        private class ControllerDefinition
        {
            public ControllerAttribute Info { get; }
            public Type Type { get; }
            public ActionDefinition[] Actions { get; set; }

            public MethodInfo[] StaticMiddleware { get; set; }
            public MethodInfo[] InstanceMiddleware { get; set; }

            public ControllerDefinition(ControllerAttribute info, Type controllerClass)
            {
                Info = info;
                Type = controllerClass;
                if (info.Prefix == null)
                {
                    const string ControllerSuffix = "Controller";
                    var controllerName = controllerClass.Name;
                    if (controllerName.EndsWith(ControllerSuffix))
                        controllerName = controllerName.Substring(0, controllerName.Length - ControllerSuffix.Length);
                    info.Prefix = controllerName + "/";
                }

                var statics = new List<MethodInfo>();
                var instance = new List<MethodInfo>();
                var middlewares = from method in controllerClass.GetMethods()
                                  from attr in method.GetCustomAttributes<MiddlewareAttribute>()
                                  orderby attr.Order
                                  select method;
                foreach (var middleware in middlewares)
                {
                    if (middleware.ReturnType != typeof(Task<HttpResponse>))
                        throw new InvalidOperationException($"Middleware {middleware} has invalid return type!");
                    if (!middleware.GetParameters().Select(x => x.ParameterType).SequenceEqual(new[] { typeof(HttpRequest) }))
                        throw new InvalidOperationException($"Middleware {middleware} has invalid parameters!");

                    (middleware.IsStatic ? statics : instance).Add(middleware);
                }
                StaticMiddleware = statics.ToArray();
                InstanceMiddleware = instance.ToArray();
            }

            public override string ToString()
            {
                return $"{Info.Prefix}|{Info.PathParamsPattern}`{String.Join("|", Actions.Select(y => y.Signature))}`{String.Join("|", StaticMiddleware.Concat(InstanceMiddleware).Select(HashSignature))}`";
            }
        }

        [Serializable]
        private class ActionDefinition
        {
            public ControllerDefinition Controller { get; }
            public MethodInfo Method { get; }
            public Type ReturnType { get { return Method.ReturnType; } }
            public bool IsSynchronous { get { return ReturnType == typeof(HttpResponse); } }
            public string Signature { get; }

            public ActionDefinition(ControllerDefinition controller, MethodInfo method)
            {
                Controller = controller;
                Method = method;
                Signature = HashSignature(Method);
            }

            public string Name { get { return Method.Name; } }
        }

        private readonly HttpServer Server;
        private readonly IRequestDispatcher Dispatcher;

        public RoutingManager(HttpServer server, Assembly[] assemblies)
        {
            Server = server;

            var qAppMiddleware = from assembly in assemblies
                                 from type in assembly.ExportedTypes
                                 from attr in type.GetCustomAttributes<MiddlewareAttribute>()
                                 orderby attr.Order
                                 select type;
            var appMiddleware = qAppMiddleware.SelectMany(ParseMiddlewareClass).ToArray();

            var qCntr = from assembly in assemblies
                        from type in assembly.ExportedTypes
                        from attr in type.GetCustomAttributes<ControllerAttribute>()
                        let controller = new ControllerDefinition(attr, type)
                        from method in type.GetMethods()
                        where method.GetCustomAttribute<MiddlewareAttribute>() == null
                        let actionDef = new ActionDefinition(controller, method)
                        where actionDef.IsSynchronous || (actionDef.ReturnType == typeof(Task<HttpResponse>))
                        orderby actionDef.Signature
                        group actionDef by controller into grouped
                        orderby grouped.Key.Info.Prefix
                        select grouped;
            var byController = qCntr.ToArray();

            foreach (var controller in byController)
                controller.Key.Actions = controller.ToArray();

            // We compose using '|', '`', '\n' because these are invalid in URLs.
            // Of course you could still screw things up here but frankly, why would you (other than to shoot your own foot)?
            // The data here comes straight from attributes and class names - there is literally no way to hijack this.
            // Even then, all you get is a simple and obvious DoS.
            var fullHash = String.Join("\n", byController.Select(x => x.Key))
                + "\n\nMiddleware:\n" + String.Join("\n", appMiddleware.Select(x => $"{x.DeclaringType.FullName}.{x.Name}"));

            var manager = new DynamicMsilManager(CharizardDynamic, CharizardDynamic);
            var compileParams = new CodegenInfo
            {
                Controllers = byController.Select(x => x.Key).ToArray(),
                AppMiddleware = appMiddleware,
            };
            var ass = manager.Generate(fullHash, CompileDynamicCode, compileParams);
            Dispatcher = (IRequestDispatcher)Activator.CreateInstance(ass.GetType(RequestDispatcher));
        }

        private static IEnumerable<MethodInfo> ParseMiddlewareClass(Type type)
        {
            if (!type.IsAbstract || !type.IsSealed)
                throw new InvalidOperationException($"Application middleware type {type} must be static!");

            foreach (var method in type.GetMethods())
            {
                if (method.ReturnType != typeof(Task<HttpResponse>))
                    continue;
                if (!method.GetParameters().Select(x => x.ParameterType).SequenceEqual(new[] { typeof(HttpRequest) }))
                    continue;

                yield return method;
            }
        }

        private static void CompileDynamicCode(object param, ModuleBuilder modBuilder)
        {
            var cp = (CodegenInfo)param;
            var byController = cp.Controllers;

            for (int i = 0; i < byController.Length; i++)
            {
                var cntr = byController[i];
                var pattern = cntr.Info.PathParamsPattern;
                if (pattern != null)
                {
                    var klass = $"{CharizardDynamic}.Regex{i}__{cntr.Type.Name}";
                    throw new InvalidOperationException("regexes not supported in this build");
                }
            }

            const string dispatchInternal = "DispatchInternal";

            var tBuilder = modBuilder.DefineType(RequestDispatcher,
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
                typeof(object), new[] { typeof(IRequestDispatcher) });

            var mBuilder = tBuilder.DefineMethod(dispatchInternal,
                MethodAttributes.HideBySig | MethodAttributes.Public | MethodAttributes.Static,
                typeof(Task<HttpResponse>), new[] { typeof(HttpRequest) });

            // now that we got the formalities out of the way, we can start building the dispatcher

            // we have nested jumptables that match the prefix char by char
            var request = Expression.Parameter(typeof(HttpRequest));
            var path = Expression.Variable(typeof(StringSegment));
            var ssIndexer = GetIndexer(path.Type, typeof(int), typeof(char));
            Expression dispatcherBody = Expression.Block(new[] { path },
                Expression.Assign(path, Expression.PropertyOrField(request, nameof(HttpRequest.Path))),
                GenerateSwitches(request, path, ssIndexer, 0, byController, x => x.Info.Prefix, FindAndInvokeActions));

            foreach (var am in cp.AppMiddleware.Reverse())
                dispatcherBody = Expression.Coalesce(Expression.Call(am, request), dispatcherBody);

            var expr = Expression.Lambda<Func<HttpRequest, Task<HttpResponse>>>(dispatcherBody, request);
            expr.CompileToMethod(mBuilder);

            var mInstanceBuilder = tBuilder.DefineMethod(nameof(IRequestDispatcher.Dispatch),
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
                typeof(Task<HttpResponse>), new[] { typeof(HttpRequest) });
            var ilgen = mInstanceBuilder.GetILGenerator();
            ilgen.Emit(OpCodes.Ldarg_1);
            ilgen.Emit(OpCodes.Tailcall);
            ilgen.Emit(OpCodes.Call, mBuilder);
            ilgen.Emit(OpCodes.Ret);

            tBuilder.CreateType();
        }

        // This employs recursion to build a tree of switches that match a request path char by char.
        // The easiest way to find out what this does is pretty much to just look at the decompiled results.
        private static Expression GenerateSwitches<TPayload>(Expression request, Expression pathStr, PropertyInfo pathIndexer, int i, IEnumerable<TPayload> candidates,
            Func<TPayload, string> getKey, Func<Expression, Expression, PropertyInfo, int, TPayload, Expression> descend)
        {
            // End-of-string handling is difficult here, see #149. tl;dr Distinguishing /Heatmap/GetHeatmap and /Heatmap/GetHeatmapTypes is difficult.
            // The solution turns out to be surprisingly easy, as StringSegment returns '\0' for out-of-range indices.
            // This means, that we can just use Skip+FirstOrDefault() below which will return '\0' after the shorter one, ensuring a correct match.
            // We still have to do some special handling though as the suffix check would be harmless, but crashes because we're trying to get an OOB
            // substring. While there are less-invasive alternatives, we go for the cleanest solution which is to just not generate the check at all.
            var currentChar = Expression.MakeIndex(pathStr, pathIndexer, new[] { Expression.Constant(i) });
            var cases = candidates.GroupBy(x => getKey(x).Skip(i).FirstOrDefault()).OrderBy(x => x.Key).Select(c =>
            {
                Expression caseBody;
                if (c.Count() == 1)
                {
                    if (c.Key == default(char))
                    {
                        caseBody = descend(request, pathStr, pathIndexer, i, c.Single());
                    }
                    else
                    {
                        // generate a final check to make sure it's this one
                        var methodInfo = typeof(StringSegment).GetMethod(nameof(StringSegment.Substring), new[] { typeof(int), typeof(int) });
                        var expectedSubstring = getKey(c.Single()).Substring(i + 1);
                        var expectedSubstringExpr = Expression.Constant(expectedSubstring);
                        var reqSubstring = Expression.Call(pathStr, methodInfo, Expression.Constant(i + 1), Expression.Constant(expectedSubstring.Length));
                        var callAction = descend(request, pathStr, pathIndexer, i + expectedSubstring.Length + 1, c.Single());
                        caseBody = Expression.Condition(Expression.Equal(reqSubstring, expectedSubstringExpr), callAction, ResponseNotFound);
                    }
                }
                else
                {
                    caseBody = GenerateSwitches(request, pathStr, pathIndexer, i + 1, c, getKey, descend);
                }
                return Expression.SwitchCase(caseBody, Expression.Constant(c.Key));
            });
            return Expression.Switch(currentChar, ResponseNotFound, cases.ToArray());
        }

        private static readonly Expression ResponseNotFound = Expression.Constant(null, typeof(Task<HttpResponse>));

        private static Expression FindAndInvokeActions(Expression request, Expression pathStr, PropertyInfo pathIndexer, int i, ControllerDefinition controller)
        {
            var filler = new String(new char[i]);

            var controllerVar = Expression.Variable(controller.Type);

            var runAction = GenerateSwitches(request, pathStr, pathIndexer, i, controller.Actions, x => filler + x.Name, (request2, pathStrI, pathIndexerI, lastI, d) =>
            {
                return Expression.Condition(Expression.Equal(Expression.PropertyOrField(pathStrI, nameof(StringSegment.Length)),
                    Expression.Constant(lastI)), BuildActionInvocation(d, request, controllerVar), ResponseNotFound);
            });

            foreach (var mw in controller.InstanceMiddleware.Reverse())
                runAction = Expression.Coalesce(Expression.Call(controllerVar, mw, request), runAction);

            Expression ret = Expression.Block(
                new[] { controllerVar },
                Expression.Assign(controllerVar, Expression.New(controller.Type)),
                // TODO: match regex
                runAction);

            foreach (var mw in controller.StaticMiddleware.Reverse())
                ret = Expression.Coalesce(Expression.Call(mw, request), ret);

            return ret;
        }

        /// <summary>
        /// Builds a string that represents every aspect of an action that's relevant to us.
        /// AFAICS that's name, return type, parameter types and names as well as default values.
        /// Any change to the action that doesn't cause this string to change as well must not
        /// be relevant for our dynamically generated code.
        /// </summary>
        /// <param name="method">The action's MethodInfo.</param>
        /// <returns>The "hash" string.</returns>
        private static string HashSignature(MethodInfo method)
        {
            var kind = method.IsStatic ? "static" : "instance";
            var parameters = String.Join(", ", method.GetParameters().Select(x => $"{x.ParameterType} {x.Name}" + (x.HasDefaultValue ? $" = {x.DefaultValue}" : "")));
            return $"{method.ReturnType} {kind} {method.Name} ( {parameters} )";
        }

        private static Expression BuildActionInvocation(ActionDefinition actionDef, Expression request, Expression controller)
        {
            // Indentation preserved to avoid needlessly huge diffs.
            // Welp, doesn't help much.
            {
                {
                    {
                        var method = actionDef.Method;

                        Expression[] args = null;
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1)
                        {
                            if (parameters[0].ParameterType == typeof(HttpRequest))
                            {
                                args = new[] { request };
                            }
                            else if (!QuerystringPrimitives.Contains(parameters[0].ParameterType))
                            {
                                // JsonConvert.DeserializeObject<ParameterType>(request.StringBody)
                                args = new[]
                                {
                                    Expression.Call(typeof(JsonConvert).GetMethods().Single(x =>
                                    {
                                        var mParams = x.GetParameters();
                                        return x.Name == nameof(JsonConvert.DeserializeObject) && x.IsGenericMethod
                                        && mParams.Length == 1 && mParams[0].ParameterType == typeof(string);
                                    }).MakeGenericMethod(parameters[0].ParameterType),
                                    Expression.PropertyOrField(request, nameof(HttpRequest.StringBody)))
                                };
                            }
                        }

                        var earlyReturn = Expression.Label(typeof(Task<HttpResponse>));

                        if (args == null)
                        {
                            // last resort: querystring parsing

                            // System.Web.HttpUtility.ParseQueryString(request.QueryString)
                            var querystring = Expression.Call(typeof(System.Web.HttpUtility).GetMethod(nameof(System.Web.HttpUtility.ParseQueryString),
                                new[] { typeof(string) }),
                                Expression.Call(
                                    Expression.PropertyOrField(request, nameof(HttpRequest.Querystring)), typeof(object).GetMethod(nameof(ToString))));

                            var indexer = GetIndexer(querystring.Type, typeof(string), typeof(string));

                            // now for every parameter:
                            // * retrieve the string value from the parsed query string
                            // * try to convert it to the required type
                            // * if conversion failed (or if there was no param to begin with):
                            //     * fall back to the default value
                            //     * if there is no default value, return an error message
                            args = new Expression[parameters.Length];
                            for (int i = 0; i < args.Length; i++)
                            {
                                var parameterName = parameters[i].Name;
                                var stringVal = Expression.MakeIndex(querystring, indexer, new[] { Expression.Constant(parameterName) });
                                var stringVar = Expression.Variable(typeof(string));

                                var pt = parameters[i].ParameterType;

                                // unwrap nullable
                                var originalPt = pt;
                                if (pt.IsGenericType && pt.GetGenericTypeDefinition() == typeof(Nullable<>))
                                    pt = pt.GetGenericArguments()[0];

                                var thingVar = Expression.Variable(pt);
                                Expression success = Expression.ReferenceNotEqual(stringVar, Expression.Constant(null, typeof(string)));

                                if (pt == typeof(string))
                                {
                                    success = Expression.Block(Expression.Assign(thingVar, stringVar), success);
                                }
                                else if (pt == typeof(string[]))
                                {
                                    success = Expression.Block(Expression.Assign(thingVar, Expression.Call(querystring,
                                        nameof(System.Collections.Specialized.NameValueCollection.GetValues),
                                        Type.EmptyTypes, Expression.Constant(parameterName))), success);
                                }
                                else if (QuerystringPrimitives.Contains(pt))
                                {
                                    success = Expression.AndAlso(success, Expression.Call(pt.GetMethod("TryParse", new[] { typeof(string), pt.MakeByRefType() }), stringVar, thingVar));
                                }
                                else
                                    throw new NotImplementedException();


                                // wrap nullable
                                Expression thingVarResult = thingVar;
                                if (pt != originalPt)
                                    thingVarResult = Expression.Convert(thingVar, originalPt);

                                Expression result;
                                if (parameters[i].HasDefaultValue)
                                    result = Expression.Condition(success, thingVarResult, Expression.Constant(parameters[i].DefaultValue, originalPt));
                                else
                                {
                                    result = Expression.Block(Expression.IfThenElse(success, Expression.Constant(null), Expression.Return(earlyReturn,
                                        Expression.Call(typeof(RoutingManager).GetMethod(nameof(InvalidOrMissingParameterHelper)), Expression.Constant(parameterName)))), thingVarResult);
                                }

                                args[i] = Expression.Block(new[] { thingVar, stringVar }, Expression.Assign(stringVar, stringVal), result);
                            }
                        }

                        // TODO: regex support
                        var path = $"/{actionDef.Controller.Info.Prefix}{method.Name}";

                        var actionResult = Expression.Call(method.IsStatic ? null : controller, method, args);
                        if (actionDef.IsSynchronous)
                            actionResult = Expression.Call(typeof(Task).GetMethod(nameof(Task.FromResult)).MakeGenericMethod(typeof(HttpResponse)), actionResult);
                        var lambda = Expression.Label(earlyReturn, actionResult);

                        return Expression.Block(lambda);
                    }
                }
            }
        }

        public static Task<HttpResponse> InvalidOrMissingParameterHelper(string parameterName)
        {
            return Task.FromResult(HttpResponse.String("Invalid or missing parameter: " + parameterName, HttpStatus.BadRequest));
        }

        private static PropertyInfo GetIndexer(Type type, Type indexType, Type returnType)
        {
            return type.GetDefaultMembers().OfType<PropertyInfo>()
                .Single(x => x.PropertyType == returnType && x.GetIndexParameters()
                .Select(a => a.ParameterType).SequenceEqual(new[] { indexType }));
        }

        public async Task<HttpResponse> DispatchRequest(HttpRequest request)
        {
            try
            {
                var task = Dispatcher.Dispatch(request);
                return task == null ? HttpResponse.NotFound() : await task;
            }
            catch (Exception e)
            {
                return Server.ActionExceptionHandler(e);
            }
        }
    }
}
