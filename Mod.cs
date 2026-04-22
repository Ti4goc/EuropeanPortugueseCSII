using Colossal;
using Colossal.Localization;
using Colossal.Logging;

using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Settings;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace EuropeanPortugueseLocale
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(EuropeanPortugueseLocale)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info("=== European Portuguese Locale Mod Loading ===");

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                TryAddVanillaSource(Path.GetDirectoryName(asset.path));
            }

            GameManager.instance.localizationManager.LoadAvailableLocales();

            typeof(InterfaceSettings).GetMethod("RegisterInOptionsUI", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new Type[] { typeof(string), typeof(bool) }, null)?
                .Invoke(GameManager.instance.settings.userInterface, new object[] { "Interface", false });

            log.Info("Mod loaded successfully");
        }

        private void TryAddVanillaSource(string modDir)
        {
            try
            {
                var vanillaJson = Path.Combine(modDir, "Localization", "pt-PT", "Vanilla", "pt-PT.json");
                if (!File.Exists(vanillaJson))
                {
                    log.Warn($"Vanilla JSON nao encontrado em: {vanillaJson}");
                    return;
                }

                var allEntries = JsonConvert.DeserializeObject<Dictionary<string, string>>(
                    File.ReadAllText(vanillaJson));

                if (allEntries == null)
                {
                    log.Warn("Falha ao desserializar Vanilla/pt-PT.json");
                    return;
                }

                var indexCounts = new Dictionary<string, int>();
                foreach (var kv in allEntries)
                {
                    var colonIdx = kv.Key.LastIndexOf(':');
                    if (colonIdx > 0 && int.TryParse(kv.Key.Substring(colonIdx + 1), out int idx))
                    {
                        var baseKey = kv.Key.Substring(0, colonIdx);
                        if (!indexCounts.ContainsKey(baseKey) || indexCounts[baseKey] <= idx)
                            indexCounts[baseKey] = idx + 1;
                    }
                }

                GameManager.instance.localizationManager.AddSource("pt-PT",
                    new VanillaLocaleSource(allEntries, indexCounts));

                log.Info($"Adicionadas {allEntries.Count} entradas, {indexCounts.Count} categorias indexadas");

                // UserInterface.ctor caches the hint list before our mod loads — fix it via reflection.
                TryRefreshLoadingHints();
            }
            catch (Exception e)
            {
                log.Warn($"Nao foi possivel adicionar fonte vanilla: {e.Message}");
            }
        }

        private void TryRefreshLoadingHints()
        {
            try
            {
                // Get UserInterface from GameManager
                var uiProp = typeof(GameManager).GetProperty("userInterface",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var ui = uiProp?.GetValue(GameManager.instance);
                if (ui == null) return;

                // m_HintMessages lives inside OverlayBindings (one level below UserInterface)
                object hintOwner = null;
                FieldInfo hintField = null;

                foreach (var topField in ui.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    var topVal = topField.GetValue(ui);
                    if (topVal == null) continue;

                    foreach (var sub in topVal.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        if (sub.Name == "m_HintMessages")
                        {
                            hintOwner = topVal;
                            hintField = sub;
                            break;
                        }
                    }
                    if (hintField != null) break;
                }

                if (hintField == null) { log.Warn("m_HintMessages nao encontrado"); return; }

                // Get hint IDs from the active localization dictionary
                var locMgr = GameManager.instance.localizationManager;
                var dictProp = locMgr.GetType().GetProperty("activeDictionary",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var dict = dictProp?.GetValue(locMgr);
                if (dict == null) return;

                var getIdsMethod = dict.GetType().GetMethod("GetIndexedLocaleIDs",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getIdsMethod == null) return;

                var hintIdsList = getIdsMethod.Invoke(dict, new object[] { "Loading.HINTMESSAGE" })
                    as IList<string>;
                if (hintIdsList == null) return;

                var hintIdsArray = new string[hintIdsList.Count];
                hintIdsList.CopyTo(hintIdsArray, 0);

                // m_HintMessages is a ValueBinding<string[]> — call Update(string[])
                var bindingObj = hintField.GetValue(hintOwner);
                var updateMethod = bindingObj?.GetType().GetMethod("Update",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(string[]) }, null);

                updateMethod?.Invoke(bindingObj, new object[] { hintIdsArray });
                log.Info($"Loading hints actualizados: {hintIdsArray.Length} entradas");
            }
            catch (Exception e)
            {
                log.Warn($"TryRefreshLoadingHints falhou: {e.Message}");
            }
        }

        public void OnDispose() { }

        private class VanillaLocaleSource : IDictionarySource
        {
            private readonly Dictionary<string, string> _entries;
            private readonly Dictionary<string, int> _indexCounts;

            public VanillaLocaleSource(Dictionary<string, string> entries, Dictionary<string, int> indexCounts)
            {
                _entries = entries;
                _indexCounts = indexCounts;
            }

            public IEnumerable<KeyValuePair<string, string>> ReadEntries(
                IList<IDictionaryEntryError> errors,
                Dictionary<string, int> indexCounts)
            {
                foreach (var kv in _indexCounts)
                    indexCounts[kv.Key] = kv.Value;
                return _entries;
            }

            public void Unload() { }
        }
    }
}
