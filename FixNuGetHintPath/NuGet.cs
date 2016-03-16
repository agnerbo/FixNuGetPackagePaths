using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Build.Construction;
using MsBuild = Microsoft.Build.Evaluation;
using NuGet.VisualStudio;

namespace FixNuGetHintPath
{
    public static class NuGet
    {
        private static string GetRelativePackagePath(string originalEvaluatedValue, string originalUnevaluatedValue,
            IEnumerable<IVsPackageMetadata> packages, string slnDir, string projectDir)
        {
            const string basePathVariable = "$(SolutionDir)";
            if (originalUnevaluatedValue.StartsWith(basePathVariable))
            {
                return originalUnevaluatedValue;
            }

            var absoluteOldPath = Path.GetFullPath(Path.Combine(projectDir, originalEvaluatedValue));
            if (!absoluteOldPath.StartsWith(slnDir) || !packages.Any(p => absoluteOldPath.StartsWith(p.InstallPath)))
            {
                return null;
            }

            var path = absoluteOldPath
                .Substring(slnDir.Length)
                .TrimStart(Path.DirectorySeparatorChar);
            return basePathVariable + path;
        }

        private static string GetRelativePackagePath(MsBuild.ProjectMetadata metadata, IEnumerable<IVsPackageMetadata> packages, string slnDir, string projectDir)
        {
            return GetRelativePackagePath(metadata.UnevaluatedValue, metadata.EvaluatedValue, packages, slnDir, projectDir);
        }

        private static string GetNewErrorText(string oldText, IEnumerable<IVsPackageMetadata> packages, string slnDir, string projectDirPath)
        {
            return Regex.Replace
                (oldText
                    , @"(?<=^\$\(\[System\.String\]::Format\('\$\(ErrorText\)', ').*(?='\)\)$)"
                    // TODO hope that `m.Value` doesn't contain MSBuild variables
                    , m => GetRelativePackagePath(m.Value, m.Value, packages, slnDir, projectDirPath) ?? m.Value
                );
        }

        private static string GetNewErrorCondition(string oldCondition, IEnumerable<IVsPackageMetadata> packages, string slnDir, string projectDirPath)
        {
            return Regex.Replace
                (oldCondition
                , @"(?<=^!Exists\(').*(?='\)$)"
                // TODO hope that `m.Value` doesn't contain MSBuild variables
                , m => GetRelativePackagePath(m.Value, m.Value, packages, slnDir, projectDirPath) ?? m.Value
                );
        }

        private static string GetNewImportCondition(string oldCondition, string evaluatedOldPath, string unevaluatedOldPath, IEnumerable<IVsPackageMetadata> packages, string slnDir, string projectDirPath)
        {
            return Regex.Replace
                ( oldCondition
                , $@"(?<=^Exists\('){Regex.Escape(unevaluatedOldPath)}(?='\)$)"
                , m => GetRelativePackagePath(evaluatedOldPath, m.Value, packages, slnDir, projectDirPath) ?? m.Value
                );
        }

        private static int SetIfNew(string name, Action<string> set, string newValue, string oldValue)
        {
            if (newValue != null && newValue != oldValue)
            {
                Logger.Info($"Updating {name}: {oldValue} --> {newValue}");
                set(newValue);
                return 1;
            }
            return 0;
        }

        public static int FixPackagePaths(MsBuild.Project project, IReadOnlyCollection<IVsPackageMetadata> packages, string slnDir)
        {
            const string pathAttributeName = "HintPath";
            var projectDirPath = project.DirectoryPath;
            var result = 0;
            
            foreach (var reference in project.GetItems("Reference").Where(r => r.HasMetadata(pathAttributeName)))
            {
                var metadata = reference.GetMetadata(pathAttributeName);
                var newPath = GetRelativePackagePath(metadata, packages, slnDir, projectDirPath);
                result += SetIfNew("reference", x => metadata.UnevaluatedValue = x, newPath, metadata.UnevaluatedValue);
            }

            foreach (var import in project.Imports)
            {
                var unevaluatedOldPath = import.ImportingElement.Project;
                var evaluatedOldPath = import.ImportedProject.FullPath;

                var newPath = GetRelativePackagePath(evaluatedOldPath, unevaluatedOldPath, packages, slnDir, projectDirPath);
                result += SetIfNew("import path", x => import.ImportingElement.Project = x, newPath, unevaluatedOldPath);

                var oldCondition = import.ImportingElement.Condition;
                var newCondition = GetNewImportCondition(oldCondition, evaluatedOldPath, unevaluatedOldPath, packages, slnDir, projectDirPath);
                result += SetIfNew("import condition", x => import.ImportingElement.Condition = x, newCondition, oldCondition);
            }

            var errors = project.Xml.Targets
                .Where(x => x.Name == "EnsureNuGetPackageBuildImports")
                .SelectMany(t => t.Children)
                .OfType<ProjectTaskElement>()
                .Where(x => x.Name == "Error");
            foreach (var error in errors)
            {
                var oldCondition = error.Condition;
                var newCondition = GetNewErrorCondition(oldCondition, packages, slnDir, projectDirPath);
                result += SetIfNew("error condition", x => error.Condition = x, newCondition, oldCondition);

                var oldText = error.GetParameter("Text");
                var newText = GetNewErrorText(oldText, packages, slnDir, projectDirPath);
                result += SetIfNew("error text", x => error.SetParameter("Text", x), newText, oldText);
            }
            return result;
        }

        public static void FixPackagePathsAndSaveProject(EnvDTE.Project project, IReadOnlyCollection<IVsPackageMetadata> packages, string solutionDir)
        {
            var msBuildProject = project.AsMsBuildProject();
            var fixedPaths = FixPackagePaths(msBuildProject, packages, solutionDir);
            Logger.Info($"Fixed {fixedPaths} paths.");
            if (fixedPaths > 0)
            {
                project.Save();
            }
        }

        public static void FixPackagePathsAndSaveProject(EnvDTE.Project project, IVsPackageMetadata package, string solutionDir)
        {
            FixPackagePathsAndSaveProject(project, new[] { package }, solutionDir);
        }
    }
}