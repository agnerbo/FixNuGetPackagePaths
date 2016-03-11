using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.VisualStudio;

namespace FixNuGetHintPath
{
    public static class NuGet
    {
        public static int FixReferences(Microsoft.Build.Evaluation.Project project, IReadOnlyCollection<IVsPackageMetadata> packages, string slnDir)
        {
            const string pathAttributeName = "HintPath";
            var result = 0;
            foreach (var reference in project.GetItems("Reference"))
            {
                var oldPath = reference.GetMetadataValue(pathAttributeName);
                if (string.IsNullOrEmpty(oldPath))
                {
                    continue;
                }
                var absoluteOldPath = Path.GetFullPath(Path.Combine(project.DirectoryPath, oldPath));
                if (!absoluteOldPath.StartsWith(slnDir) || !packages.Any(p => absoluteOldPath.StartsWith(p.InstallPath)))
                {
                    continue;
                }
                var path = absoluteOldPath.Substring(slnDir.Length).TrimStart(Path.DirectorySeparatorChar);
                var newPath = "$(SolutionDir)" + path;
                Logger.Log($"{oldPath} --> {newPath}");
                reference.SetMetadataValue(pathAttributeName, newPath);
                result++;
            }
            return result;
        }

        public static int FixReferences(Microsoft.Build.Evaluation.Project project, IVsPackageMetadata package, string slnDir)
        {
            return FixReferences(project, new[] { package }, slnDir);
        }
    }
}