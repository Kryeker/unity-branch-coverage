using UnityEditor;

namespace Kryeker.CodeCoverage
{
    internal static class BranchEnricherMenu
    {
        [MenuItem("Tools/Code Coverage/Enrich Branch Points In OpenCover XMLs...")]
        private static void EnrichFromMenu()
        {
            string folder = EditorUtility.OpenFolderPanel("Select OpenCover results folder", "", "");
            if (string.IsNullOrEmpty(folder))
                return;

            string[] files = System.IO.Directory.GetFiles(folder, "*.xml", System.IO.SearchOption.AllDirectories);
            int total = 0;
            int enriched = 0;
            foreach (string file in files)
            {
                if (file.EndsWith("_fullEmpty.xml", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                total++;
                try
                {
                    enriched += BranchCoverageEnricher.EnrichFile(file);
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogWarning("[BranchEnricher] Failed to enrich '" + file + "': " + ex.Message);
                }
            }

            EditorUtility.DisplayDialog("Branch Enricher",
                "Processed " + total + " file(s), added " + enriched + " synthetic branch point(s).\nCounts are heuristic (sequence-derived), not instrumented.",
                "OK");
        }
    }
}
