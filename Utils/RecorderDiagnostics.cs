using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SoulPlayer.Utils
{
    /// <summary>
    /// Development-only probe used to discover EFT's built-in recorder/tape implementation
    /// without extracting or redistributing any BSG assets. Trigger with Ctrl+Shift+F10.
    /// </summary>
    internal sealed class RecorderDiagnostics : MonoBehaviour
    {
        private static readonly string[] SearchTokens =
        {
            "recorder",
            "recording",
            "cassette",
            "audio tape",
            "audiotape",
            "audio_tape",
            "voevoda",
            "tape"
        };

        private float _nextAllowedProbe;

        private void Update()
        {
            if (Time.unscaledTime < _nextAllowedProbe)
            {
                return;
            }

            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!control || !shift || !Input.GetKeyDown(KeyCode.F10))
            {
                return;
            }

            _nextAllowedProbe = Time.unscaledTime + 2f;
            RunProbe();
        }

        private static void RunProbe()
        {
            List<string> lines = new List<string>();
            lines.Add("SoulPlayer EFT recorder discovery probe");
            lines.Add("Generated: " + DateTime.Now.ToString("O"));
            lines.Add("Unity version: " + Application.unityVersion);
            lines.Add("Game version: " + Application.version);
            lines.Add("Search tokens: " + string.Join(", ", SearchTokens));
            lines.Add(string.Empty);

            try
            {
                DumpManagedCandidates(lines);
            }
            catch (Exception ex)
            {
                lines.Add("[managed scan failed] " + ex);
            }

            try
            {
                DumpLoadedAssetBundles(lines);
            }
            catch (Exception ex)
            {
                lines.Add("[asset-bundle scan failed] " + ex);
            }

            try
            {
                DumpUnityCandidates(lines);
            }
            catch (Exception ex)
            {
                lines.Add("[Unity object scan failed] " + ex);
            }

            string pluginFolder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            if (string.IsNullOrWhiteSpace(pluginFolder))
            {
                pluginFolder = Environment.CurrentDirectory;
            }

            string outputPath = Path.Combine(
                pluginFolder,
                "SoulPlayer-RecorderProbe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");

            try
            {
                File.WriteAllLines(outputPath, lines, new UTF8Encoding(false));
                Plugin.Log.LogInfo("SoulPlayer recorder probe finished: " + outputPath);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("SoulPlayer recorder probe could not write its report: " + ex);
            }
        }

        private static void DumpManagedCandidates(List<string> lines)
        {
            lines.Add("=== MANAGED TYPE / MEMBER CANDIDATES ===");

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies()
                .OrderBy(assembly => assembly.GetName().Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            int assemblyCount = 0;
            int typeHitCount = 0;

            foreach (Assembly assembly in assemblies)
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (!IsGameAssembly(assemblyName))
                {
                    continue;
                }

                assemblyCount++;
                Type[] types = GetLoadableTypes(assembly);
                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    List<string> hits = new List<string>();
                    string fullName = type.FullName ?? type.Name;
                    if (Matches(fullName))
                    {
                        hits.Add("type=" + fullName);
                    }

                    try
                    {
                        MemberInfo[] members = type.GetMembers(
                            BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Instance |
                            BindingFlags.Static |
                            BindingFlags.DeclaredOnly);

                        foreach (MemberInfo member in members)
                        {
                            if (Matches(member.Name))
                            {
                                hits.Add(member.MemberType + "=" + member.Name);
                                if (hits.Count >= 16)
                                {
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Some generated/obfuscated types can throw during reflection.
                    }

                    try
                    {
                        foreach (FieldInfo field in type.GetFields(
                                     BindingFlags.Public |
                                     BindingFlags.NonPublic |
                                     BindingFlags.Static |
                                     BindingFlags.DeclaredOnly))
                        {
                            if (field.FieldType != typeof(string) || !field.IsLiteral)
                            {
                                continue;
                            }

                            string value = field.GetRawConstantValue() as string;
                            if (Matches(value))
                            {
                                hits.Add("const " + field.Name + "=\"" + TrimForReport(value, 180) + "\"");
                                if (hits.Count >= 16)
                                {
                                    break;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Constants are only a bonus signal; never fail the probe for them.
                    }

                    if (hits.Count == 0)
                    {
                        continue;
                    }

                    typeHitCount++;
                    lines.Add("[" + assemblyName + "] " + fullName);
                    foreach (string hit in hits.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        lines.Add("  " + hit);
                    }
                }
            }

            lines.Add("Scanned game assemblies: " + assemblyCount);
            lines.Add("Types with recorder/tape signals: " + typeHitCount);
            lines.Add(string.Empty);
        }

        private static void DumpLoadedAssetBundles(List<string> lines)
        {
            lines.Add("=== LOADED ASSET-BUNDLE CANDIDATES ===");
            int bundleCount = 0;
            int assetHitCount = 0;

            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (bundle == null)
                {
                    continue;
                }

                bundleCount++;
                string bundleName = bundle.name ?? string.Empty;
                if (Matches(bundleName))
                {
                    lines.Add("bundle: " + bundleName);
                }

                try
                {
                    foreach (string assetName in bundle.GetAllAssetNames())
                    {
                        if (!Matches(assetName))
                        {
                            continue;
                        }

                        assetHitCount++;
                        lines.Add("asset: [" + bundleName + "] " + assetName);
                    }
                }
                catch (Exception ex)
                {
                    lines.Add("asset-list error: [" + bundleName + "] " + ex.Message);
                }
            }

            lines.Add("Loaded bundles scanned: " + bundleCount);
            lines.Add("Matching asset paths: " + assetHitCount);
            lines.Add(string.Empty);
        }

        private static void DumpUnityCandidates(List<string> lines)
        {
            lines.Add("=== CURRENTLY LOADED UNITY OBJECT CANDIDATES ===");
            DumpObjects<GameObject>(lines, "GameObject");
            DumpObjects<AnimationClip>(lines, "AnimationClip");
            DumpObjects<RuntimeAnimatorController>(lines, "AnimatorController");
            DumpObjects<AudioClip>(lines, "AudioClip");
            DumpObjects<TextAsset>(lines, "TextAsset");
            DumpObjects<ScriptableObject>(lines, "ScriptableObject");
            DumpObjects<MonoBehaviour>(lines, "MonoBehaviour");
            lines.Add(string.Empty);
        }

        private static void DumpObjects<T>(List<string> lines, string label) where T : UnityEngine.Object
        {
            int scanned = 0;
            int hits = 0;
            T[] objects;

            try
            {
                objects = Resources.FindObjectsOfTypeAll<T>();
            }
            catch (Exception ex)
            {
                lines.Add(label + ": scan failed: " + ex.Message);
                return;
            }

            foreach (T value in objects)
            {
                if (value == null)
                {
                    continue;
                }

                scanned++;
                string objectName = value.name ?? string.Empty;
                string typeName = value.GetType().FullName ?? value.GetType().Name;
                if (!Matches(objectName) && !Matches(typeName))
                {
                    continue;
                }

                hits++;
                lines.Add(label + ": " + typeName + " :: " + objectName);
            }

            lines.Add(label + " scanned=" + scanned + ", hits=" + hits);
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null).ToArray();
            }
            catch
            {
                return new Type[0];
            }
        }

        private static bool IsGameAssembly(string assemblyName)
        {
            return assemblyName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.StartsWith("EFT", StringComparison.OrdinalIgnoreCase) ||
                   assemblyName.IndexOf("Tarkov", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   assemblyName.Equals("Comfort", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Matches(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (string token in SearchTokens)
            {
                if (value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string TrimForReport(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength) + "...";
        }
    }
}
