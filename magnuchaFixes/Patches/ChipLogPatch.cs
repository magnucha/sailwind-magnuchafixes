using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace magnuchaFixes.Patches
{
    internal static class ChipLogPatchState
    {
        private static readonly Dictionary<int, float> LastSuppressionLogTimes = new Dictionary<int, float>();
        private static bool _runtimeErrorLogged;

        internal static ManualLogSource Logger { get; private set; }
        internal static ConfigEntry<bool> Enabled { get; private set; }
        internal static ConfigEntry<bool> DiagnosticLogging { get; private set; }
        internal static bool IsReady { get; private set; }

        internal static MethodBase ChangeLineLengthMethod { get; private set; }
        internal static FieldInfo ThrownField { get; private set; }
        internal static FieldInfo MinLengthField { get; private set; }
        internal static FieldInfo CurrentTargetLengthField { get; private set; }
        internal static FieldInfo LastRotField { get; private set; }
        internal static FieldInfo LastBobberPosField { get; private set; }
        internal static FieldInfo BobberJointField { get; private set; }
        internal static PropertyInfo JointLinearLimitProperty { get; private set; }

        internal static bool ShouldModify => Enabled.Value && IsReady;

        internal static void Configure(ConfigEntry<bool> enabled, ConfigEntry<bool> diagnosticLogging, ManualLogSource logger)
        {
            Enabled = enabled;
            DiagnosticLogging = diagnosticLogging;
            Logger = logger;
            IsReady = false;
            _runtimeErrorLogged = false;
            LastSuppressionLogTimes.Clear();

            ChangeLineLengthMethod = AccessTools.Method(
                typeof(ShipItemChipLog),
                "ChangeLineLength",
                new[] { typeof(float) });
            ThrownField = AccessTools.Field(typeof(ShipItemChipLog), "thrown");
            MinLengthField = AccessTools.Field(typeof(ShipItemChipLog), "minLength");
            CurrentTargetLengthField = AccessTools.Field(typeof(ShipItemChipLog), "currentTargetLength");
            LastRotField = AccessTools.Field(typeof(ShipItemChipLog), "lastRot");
            LastBobberPosField = AccessTools.Field(typeof(ShipItemChipLog), "lastBobberPos");
            BobberJointField = AccessTools.Field(typeof(ShipItemChipLog), "bobberJoint");
            JointLinearLimitProperty = BobberJointField == null
                ? null
                : AccessTools.Property(BobberJointField.FieldType, "linearLimit");

            if (ChangeLineLengthMethod == null ||
                ThrownField == null ||
                MinLengthField == null ||
                CurrentTargetLengthField == null ||
                LastRotField == null ||
                LastBobberPosField == null ||
                BobberJointField == null ||
                JointLinearLimitProperty == null)
            {
                Logger.LogError(
                    "ChipLog fix disabled: the loaded ShipItemChipLog layout does not match the supported game version.");
                return;
            }

            IsReady = true;
            Logger.LogInfo("ChipLog spontaneous-retraction patch is ready.");
        }

        internal static bool TryGetThrown(ShipItemChipLog instance, out bool thrown)
        {
            thrown = false;
            try
            {
                thrown = (bool)ThrownField.GetValue(instance);
                return true;
            }
            catch (Exception exception)
            {
                LogRuntimeError("reading ShipItemChipLog.thrown", exception);
                return false;
            }
        }

        internal static void InitializeOnLoad(ShipItemChipLog instance)
        {
            try
            {
                float minLength = (float)MinLengthField.GetValue(instance);
                CurrentTargetLengthField.SetValue(instance, minLength);
                LastRotField.SetValue(instance, instance.transform.rotation);

                Component bobberJoint = BobberJointField.GetValue(instance) as Component;
                if (bobberJoint != null)
                    LastBobberPosField.SetValue(instance, bobberJoint.transform.localPosition);
            }
            catch (Exception exception)
            {
                LogRuntimeError("initializing ChipLog transient state", exception);
            }
        }

        internal static void InitializeBeforeCast(ShipItemChipLog instance)
        {
            try
            {
                LastRotField.SetValue(instance, instance.transform.rotation);

                Component bobberJoint = BobberJointField.GetValue(instance) as Component;
                if (bobberJoint != null)
                    LastBobberPosField.SetValue(instance, bobberJoint.transform.localPosition);
            }
            catch (Exception exception)
            {
                LogRuntimeError("initializing ChipLog cast state", exception);
            }
        }

        internal static void LogSuppressedRetract(ShipItemChipLog instance)
        {
            if (!DiagnosticLogging.Value || Logger == null)
                return;

            try
            {
                int instanceId = instance.GetInstanceID();
                float now = Time.unscaledTime;
                float lastLogTime;
                if (LastSuppressionLogTimes.TryGetValue(instanceId, out lastLogTime) && now - lastLogTime < 1f)
                    return;

                LastSuppressionLogTimes[instanceId] = now;
                float currentTargetLength = (float)CurrentTargetLengthField.GetValue(instance);
                Component bobberJoint = BobberJointField.GetValue(instance) as Component;
                float currentJointLength = GetJointLimit(bobberJoint);
                Logger.LogInfo(
                    string.Format(
                        "Suppressed ChipLog auto-retract: instance={0}, targetLength={1:F2}, jointLength={2:F2}.",
                        instanceId,
                        currentTargetLength,
                        currentJointLength));
            }
            catch (Exception exception)
            {
                LogRuntimeError("logging ChipLog diagnostic state", exception);
            }
        }

        private static float GetJointLimit(Component bobberJoint)
        {
            if (!bobberJoint)
                return -1f;

            var linearLimit = JointLinearLimitProperty.GetValue(bobberJoint, null);
            if (linearLimit == null)
                return -1f;

            var limitProperty = AccessTools.Property(linearLimit.GetType(), "limit");
            return limitProperty == null ? -1f : (float)limitProperty.GetValue(linearLimit, null);
        }

        private static void LogRuntimeError(string operation, Exception exception)
        {
            if (_runtimeErrorLogged || Logger == null)
                return;

            _runtimeErrorLogged = true;
            Logger.LogError(string.Format("ChipLog fix stopped modifying behavior while {0}: {1}", operation, exception));
        }
    }

    [HarmonyPatch]
    internal static class ChipLogChangeLineLengthPatch
    {
        private static MethodBase TargetMethod() => ChipLogPatchState.ChangeLineLengthMethod;

        private static bool Prepare() => ChipLogPatchState.IsReady;

        private static bool Prefix(ShipItemChipLog __instance, float value)
        {
            if (!ChipLogPatchState.ShouldModify || value >= 0f)
                return true;

            bool thrown;
            if (!ChipLogPatchState.TryGetThrown(__instance, out thrown) || !thrown)
                return true;

            ChipLogPatchState.LogSuppressedRetract(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(ShipItemChipLog), "OnLoad")]
    internal static class ChipLogOnLoadPatch
    {
        private static bool Prepare() => ChipLogPatchState.IsReady;

        private static void Postfix(ShipItemChipLog __instance)
        {
            if (ChipLogPatchState.ShouldModify)
                ChipLogPatchState.InitializeOnLoad(__instance);
        }
    }

    [HarmonyPatch(typeof(ShipItemChipLog), "OnAltActivate")]
    internal static class ChipLogActivationPatch
    {
        private static bool Prepare() => ChipLogPatchState.IsReady;

        private static void Prefix(ShipItemChipLog __instance)
        {
            if (!ChipLogPatchState.ShouldModify || !__instance.sold)
                return;

            bool thrown;
            if (ChipLogPatchState.TryGetThrown(__instance, out thrown) && !thrown)
                ChipLogPatchState.InitializeBeforeCast(__instance);
        }
    }
}



