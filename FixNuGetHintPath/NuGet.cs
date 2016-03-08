using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.VisualStudio;

namespace FixNuGetHintPath
{
    public static class NuGet
    {
        public static void FixReferences(Microsoft.Build.Evaluation.Project project, IReadOnlyCollection<IVsPackageMetadata> packages, string slnDir)
        {
            foreach (var reference in project.GetItems("Reference"))
            {
                var hintPath = reference.GetMetadataValue("HintPath");
                if (string.IsNullOrEmpty(hintPath))
                {
                    continue;
                }
                var absoluteHintPath = Path.GetFullPath(Path.Combine(project.DirectoryPath, hintPath));
                if (!packages.Any(p => absoluteHintPath.StartsWith(p.InstallPath)))
                {
                    continue;
                }
                var path = absoluteHintPath.Substring(slnDir.Length).TrimStart(Path.DirectorySeparatorChar);
                reference.SetMetadataValue("HintPath", "$(SolutionDir)" + path);
            }
        }

        public static void FixReferences(Microsoft.Build.Evaluation.Project project, IVsPackageMetadata package, string slnDir)
        {
            FixReferences(project, new[] { package }, slnDir);
        }
    }
}