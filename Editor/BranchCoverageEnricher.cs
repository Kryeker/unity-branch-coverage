using System;
using System.IO;
using System.Xml.Serialization;
using OpenCover.Framework.Model;
using UnityEditor;
using UnityEngine;
using SysAssembly = System.Reflection.Assembly;
using SysMethodBase = System.Reflection.MethodBase;
using SysReflectionModule = System.Reflection.Module;
using SysType = System.Type;
using IOFile = System.IO.File;

namespace Kryeker.CodeCoverage
{
    /// <summary>
    /// Subscribes to the Code Coverage session-finished event and backfills
    /// synthetic <see cref="BranchPoint"/>s into the OpenCover XML before
    /// ReportGenerator (Html/Cobertura/SonarQube) runs.
    /// </summary>
    [InitializeOnLoad]
    internal static class BranchCoverageEnricher
    {
        static BranchCoverageEnricher()
        {
            UnityEditor.TestTools.CodeCoverage.Events.onCoverageSessionFinished += OnSessionFinished;
        }

        private static void OnSessionFinished(UnityEditor.TestTools.CodeCoverage.SessionEventInfo info)
        {
            if (info == null || info.SessionResultPaths == null)
                return;

            foreach (string path in info.SessionResultPaths.ToArray())
            {
                try
                {
                    if (string.IsNullOrEmpty(path) || !IOFile.Exists(path))
                        continue;
                    if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;
                    // Skip the empty baseline report - it has no visits, branches would be noise.
                    if (path.EndsWith("_fullEmpty.xml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    EnrichFile(path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[BranchEnricher] Failed to enrich '" + path + "': " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Enriches a single OpenCover XML file in place. Public for menu/CI use.
        /// Returns the number of branch points added.
        /// </summary>
        public static int EnrichFile(string xmlPath)
        {
            CoverageSession session;
            var serializer = new XmlSerializer(typeof(CoverageSession));
            using (var reader = new StreamReader(xmlPath))
            {
                session = (CoverageSession)serializer.Deserialize(reader);
            }

            if (session == null || session.Modules == null)
                return 0;

            uint nextUspid = FindMaxUspid(session) + 1;
            int added = 0;

            foreach (Module module in session.Modules)
            {
                if (module == null || module.Classes == null)
                    continue;
                SysAssembly assembly = FindAssembly(module.ModuleName);
                foreach (Class klass in module.Classes)
                {
                    if (klass == null || klass.Methods == null)
                        continue;
                    SysType type = assembly != null ? FindType(assembly, klass.FullName) : null;
                    foreach (Method method in klass.Methods)
                    {
                        if (method == null)
                            continue;
                        if (method.BranchPoints != null && method.BranchPoints.Length > 0)
                            continue; // already enriched or natively present
                        if (method.SequencePoints == null || method.SequencePoints.Length == 0)
                            continue;

                        SysMethodBase methodBase = type != null ? ResolveMethod(type, method.MetadataToken) : null;

                        BranchPoint[] branches;
                        if (methodBase != null)
                        {
                            branches = BranchSynthesizer.Synthesize(methodBase, method.SequencePoints, ref nextUspid);
                        }
                        else
                        {
                            // Fallback when the assembly is not loaded (batch/CI context):
                            // one branch path per sequence point, visited iff the point was hit.
                            branches = BranchSynthesizer.SynthesizeFromSequencePoints(method.SequencePoints, ref nextUspid);
                        }
                        if (branches.Length == 0)
                            continue;

                        method.BranchPoints = branches;
                        added += branches.Length;
                        UpdateMethodBranchSummary(method);
                    }
                    UpdateClassBranchSummary(klass);
                }
                UpdateModuleBranchSummary(module);
            }

            UpdateSessionBranchSummary(session);

            using (var writer = new StreamWriter(xmlPath, false))
            {
                serializer.Serialize(writer, session);
            }

            Debug.Log("[BranchEnricher] Added " + added + " synthetic branch points to '" + xmlPath + "'. Counts are heuristic (sequence-derived), not instrumented.");
            return added;
        }

        private static uint FindMaxUspid(CoverageSession session)
        {
            uint max = 0;
            foreach (Module module in session.Modules)
            {
                if (module == null || module.Classes == null)
                    continue;
                foreach (Class klass in module.Classes)
                {
                    if (klass == null || klass.Methods == null)
                        continue;
                    foreach (Method method in klass.Methods)
                    {
                        if (method == null)
                            continue;
                        if (method.SequencePoints != null)
                        {
                            foreach (SequencePoint sp in method.SequencePoints)
                            {
                                if (sp != null && sp.UniqueSequencePoint > max)
                                    max = sp.UniqueSequencePoint;
                            }
                        }
                        if (method.BranchPoints != null)
                        {
                            foreach (BranchPoint bp in method.BranchPoints)
                            {
                                if (bp != null && bp.UniqueSequencePoint > max)
                                    max = bp.UniqueSequencePoint;
                            }
                        }
                    }
                }
            }
            return max;
        }

        private static SysAssembly FindAssembly(string moduleName)
        {
            if (string.IsNullOrEmpty(moduleName))
                return null;
            SysAssembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (SysAssembly assembly in assemblies)
            {
                try
                {
                    if (string.Equals(assembly.GetName().Name, moduleName, StringComparison.OrdinalIgnoreCase))
                        return assembly;
                }
                catch (Exception)
                {
                    // ignore dynamic assemblies without names
                }
            }
            return null;
        }

        private static SysType FindType(SysAssembly assembly, string fullName)
        {
            if (assembly == null || string.IsNullOrEmpty(fullName))
                return null;
            // Generic display names (with '[' or '`') do not round-trip; skip them.
            if (fullName.IndexOf('[') >= 0 || fullName.IndexOf('`') >= 0)
                return null;
            SysType[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            if (types == null)
                return null;
            foreach (SysType type in types)
            {
                if (type == null || type.FullName == null)
                    continue;
                string normalized = type.FullName.Replace('+', '/');
                if (string.Equals(normalized, fullName, StringComparison.Ordinal))
                    return type;
            }
            return null;
        }

        private static SysMethodBase ResolveMethod(SysType type, int metadataToken)
        {
            if (type == null || metadataToken == 0)
                return null;
            try
            {
                SysReflectionModule[] modules = type.Assembly.GetModules();
                foreach (SysReflectionModule module in modules)
                {
                    try
                    {
                        SysMethodBase resolved = module.ResolveMethod(metadataToken);
                        if (resolved != null && resolved.DeclaringType != null)
                        {
                            string normalized = resolved.DeclaringType.FullName != null
                                ? resolved.DeclaringType.FullName.Replace('+', '/')
                                : null;
                            if (string.Equals(normalized, type.FullName.Replace('+', '/'), StringComparison.Ordinal))
                                return resolved;
                        }
                    }
                    catch (Exception)
                    {
                        // token belongs to another module; keep looking
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }
            return null;
        }

        private static void UpdateMethodBranchSummary(Method method)
        {
            BranchPoint[] branches = method.BranchPoints ?? new BranchPoint[0];
            int total = branches.Length;
            int visited = 0;
            foreach (BranchPoint bp in branches)
            {
                if (bp != null && bp.VisitCount > 0)
                    visited++;
            }
            method.Summary.NumBranchPoints = total;
            method.Summary.VisitedBranchPoints = visited;
            method.Summary.BranchCoverage = total > 0
                ? decimal.Round(100.0m * (visited / (decimal)total), 2)
                : 0.0m;
            method.BranchCoverage = method.Summary.BranchCoverage;
        }

        private static void UpdateClassBranchSummary(Class klass)
        {
            int num = 0;
            int visited = 0;
            foreach (Method method in klass.Methods)
            {
                if (method == null || method.Summary == null)
                    continue;
                num += method.Summary.NumBranchPoints;
                visited += method.Summary.VisitedBranchPoints;
            }
            klass.Summary.NumBranchPoints = num;
            klass.Summary.VisitedBranchPoints = visited;
            klass.Summary.BranchCoverage = num > 0
                ? decimal.Round(100.0m * (visited / (decimal)num), 2)
                : 0.0m;
        }

        private static void UpdateModuleBranchSummary(Module module)
        {
            int num = 0;
            int visited = 0;
            foreach (Class klass in module.Classes)
            {
                if (klass == null || klass.Summary == null)
                    continue;
                num += klass.Summary.NumBranchPoints;
                visited += klass.Summary.VisitedBranchPoints;
            }
            module.Summary.NumBranchPoints = num;
            module.Summary.VisitedBranchPoints = visited;
            module.Summary.BranchCoverage = num > 0
                ? decimal.Round(100.0m * (visited / (decimal)num), 2)
                : 0.0m;
        }

        private static void UpdateSessionBranchSummary(CoverageSession session)
        {
            int num = 0;
            int visited = 0;
            foreach (Module module in session.Modules)
            {
                if (module == null || module.Summary == null)
                    continue;
                num += module.Summary.NumBranchPoints;
                visited += module.Summary.VisitedBranchPoints;
            }
            session.Summary.NumBranchPoints = num;
            session.Summary.VisitedBranchPoints = visited;
            session.Summary.BranchCoverage = num > 0
                ? decimal.Round(100.0m * (visited / (decimal)num), 2)
                : 0.0m;
        }
    }
}
