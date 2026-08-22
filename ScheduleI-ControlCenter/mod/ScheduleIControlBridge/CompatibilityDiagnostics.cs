using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Handover;
using Il2CppScheduleOne.UI.Phone;
using Newtonsoft.Json.Linq;

namespace ScheduleIControlBridge
{
    internal sealed class CompatibilityDiagnosticsResult
    {
        private readonly JArray checks = new JArray();

        public bool Passed { get; private set; }
        public int PassedCount { get; private set; }
        public int FailedCount { get; private set; }

        public CompatibilityDiagnosticsResult()
        {
            Passed = true;
        }

        public void Check(string name, bool passed, string detail)
        {
            if (passed)
                PassedCount++;
            else
            {
                Failed = true;
                FailedCount++;
            }

            checks.Add(new JObject
            {
                ["name"] = name,
                ["passed"] = passed,
                ["detail"] = detail ?? string.Empty
            });
        }

        private bool Failed
        {
            set
            {
                if (value)
                    Passed = false;
            }
        }

        public JObject ToJson()
        {
            return new JObject
            {
                ["passed"] = Passed,
                ["passedCount"] = PassedCount,
                ["failedCount"] = FailedCount,
                ["checks"] = checks.DeepClone()
            };
        }

        public string Summary
        {
            get
            {
                return string.Format(
                    "{0} of {1} compatibility checks passed.",
                    PassedCount,
                    PassedCount + FailedCount);
            }
        }
    }

    internal static class CompatibilityDiagnostics
    {
        private const BindingFlags AllMethods = BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.Public
            | BindingFlags.NonPublic;

        public static CompatibilityDiagnosticsResult Run()
        {
            var result = new CompatibilityDiagnosticsResult();
            CheckMethod(result, "ProductManager.CalculateProductValue(ProductDefinition,float)",
                typeof(ProductManager), nameof(ProductManager.CalculateProductValue),
                typeof(ProductDefinition), typeof(float));
            CheckMethod(result, "CustomerData.GetAdjustedWeeklySpend(float)",
                typeof(CustomerData), nameof(CustomerData.GetAdjustedWeeklySpend),
                typeof(float));
            CheckMethod(result, "CounterofferInterface.Send()",
                typeof(CounterofferInterface), nameof(CounterofferInterface.Send));
            CheckMethod(result, "HandoverScreen.PriceChanged(float)",
                typeof(HandoverScreen), nameof(HandoverScreen.PriceChanged),
                typeof(float));
            CheckMethod(result, "HandoverScreen.DonePressed()",
                typeof(HandoverScreen), nameof(HandoverScreen.DonePressed));
            CheckMethod(result, "Business.MinsPass()",
                typeof(Business), nameof(Business.MinsPass), (Type[])null);
            CheckMethod(result, "Business.StartLaunderingOperation()",
                typeof(Business), nameof(Business.StartLaunderingOperation), (Type[])null);
            CheckMethod(result, "LaunderingInterface.Initialize()",
                typeof(LaunderingInterface), nameof(LaunderingInterface.Initialize), (Type[])null);
            CheckMethod(result, "LaunderingInterface.Open()",
                typeof(LaunderingInterface), nameof(LaunderingInterface.Open), (Type[])null);
            CheckMethod(result, "LaunderingInterface.RefreshLaunderButton()",
                typeof(LaunderingInterface), nameof(LaunderingInterface.RefreshLaunderButton), (Type[])null);
            CheckMember(result, "ProductManager.MIN_PRICE", typeof(ProductManager), "MIN_PRICE");
            CheckMember(result, "ProductManager.MAX_PRICE", typeof(ProductManager), "MAX_PRICE");
            return result;
        }

        private static void CheckMethod(
            CompatibilityDiagnosticsResult result,
            string name,
            Type type,
            string methodName,
            params Type[] parameterTypes)
        {
            try
            {
                bool found = false;
                foreach (MethodInfo method in type.GetMethods(AllMethods))
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                        continue;

                    if (parameterTypes == null)
                    {
                        found = true;
                        break;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != parameterTypes.Length)
                        continue;

                    bool parametersMatch = true;
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (parameters[i].ParameterType != parameterTypes[i])
                        {
                            parametersMatch = false;
                            break;
                        }
                    }

                    if (parametersMatch)
                    {
                        found = true;
                        break;
                    }
                }

                result.Check(name, found, found ? "method found" : "method signature was not found");
            }
            catch (Exception ex)
            {
                result.Check(name, false, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void CheckMember(
            CompatibilityDiagnosticsResult result,
            string name,
            Type type,
            string memberName)
        {
            try
            {
                bool found = type.GetField(memberName, AllMethods) != null
                    || type.GetProperty(memberName, AllMethods) != null;
                result.Check(name, found, found ? "field or property found" : "field or property was not found");
            }
            catch (Exception ex)
            {
                result.Check(name, false, ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
