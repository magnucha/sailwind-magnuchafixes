using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using magnuchaFixes.Patches;

namespace magnuchaFixes
{
    [BepInPlugin(ModId, ModName, ModVersion)]
    internal class SailwindMod : BaseUnityPlugin
    {
        private const string ModId = "com.magnucha.magnuchafixes";
        private const string ModName = "Magnucha Fixes";
        private const string ModVersion = "0.0.1";

        private Harmony _harmony;
        
        internal static ConfigEntry<bool> enableChipLogRetractionFix;
        internal static ConfigEntry<bool> enableDiagnosticLogging;

        private void Awake()
        {
            enableChipLogRetractionFix = Config.Bind(
                "General",
                "EnableChipLogFix",
                true,
                "Prevents an actively deployed ChipLog from retracting because its bobber briefly reports being out of water.");
            enableDiagnosticLogging = Config.Bind(
                "Diagnostics",
                "EnableDiagnosticLogging",
                false,
                "Logs rate-limited ChipLog automatic-retraction suppressions.");

            ChipLogPatchState.Configure(enableChipLogRetractionFix, enableDiagnosticLogging, Logger);
            _harmony = new Harmony(ModId);  
            try
            {
                _harmony.PatchAll();
            }
            catch (Exception exception)
            {
                Logger.LogError("Unable to apply the ChipLog patch: " + exception);
                _harmony.UnpatchSelf();
                _harmony = null;
            }

            Logger.LogInfo("magnuchaFixes is loaded.");
        }

        private void OnDestroy()
        {
            if (_harmony != null)
                _harmony.UnpatchSelf();
        }
    }
}