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
        private delegate string ModifyPackagePathDelegate(
            string originalEvaluatedValue,
            string originalUnevaluatedValue,
            IEnumerable<IVsPackageMetadata> packages,
            string slnDir,
            string projectDir);

        private static string GetRelativePackagePath(string originalEvaluatedValue, string originalUnevaluatedValue,
            IEnumerable<IVsPackageMetadata> packages, string slnDir, string projectDir)
        {
            const string basePathVariable = "$(SolutionDir)";
            if (originalUnevaluatedValue.StartsWith(basePathVariable))
            {
                return originalUnevaluatedValue;
            }

            try
            {
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
            catch (ArgumentException e) when (e.Message == "Illegal characters in path.")
            {
                Logger.Error($"Can't determine full path because path contains illegal characters: {originalEvaluatedValue}");
                return null;
            }
        }

        private static string GetNewErrorText(string oldText, IEnumerable<IVsPackageMetadata> packages, string slnDir, string projectDirPath, ModifyPackagePathDelegate modifyPackagePath, MsBuild.Project project)
        {
            return Regex.Replace
                (oldText
                    , @"(?<=^\$\(\[System\.String\]::Format\('\$\(ErrorText\)', ').*(?='\)\)$)"
                    , m => modifyPackagePath(MsBuildHelper.Evaluate(m.Value, project), m.Value, packages, slnDir, projectDirPath) ?? m.Value
                );
        }

        private static string GetNewErrorCondition(string oldCondition, IEnumerable<IVsPackageMetadata> packages, string slnDir, string projectDirPath, ModifyPackagePathDelegate modifyPackagePath, MsBuild.Project project)
        {
            return Regex.Replace
                (oldCondition
                , @"(?<=^!Exists\(').*(?='\)$)"
                , m => modifyPackagePath(MsBuildHelper.Evaluate(m.Value, project), m.Value, packages, slnDir, projectDirPath) ?? m.Value
                );
        }

        private static string GetNewImportCondition(string oldCondition, string evaluatedOldPath, string unevaluatedOldPath, IEnumerable<IVsPackageMetadata> packages, string slnDir, string projectDirPath, ModifyPackagePathDelegate modifyPackagePath)
        {
            return Regex.Replace
                ( oldCondition
                , $@"(?<=^Exists\('){Regex.Escape(unevaluatedOldPath)}(?='\)$)"
                , m => modifyPackagePath(evaluatedOldPath, m.Value, packages, slnDir, projectDirPath) ?? m.Value
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

        private static int ModifyPackagePaths(MsBuild.Project project, IReadOnlyCollection<IVsPackageMetadata> packages, string slnDir, ModifyPackagePathDelegate modifyPackagePath)
        {
            const string pathAttributeName = "HintPath";
            var projectDirPath = project.DirectoryPath;
            var result = 0;
            
            foreach (var reference in project.GetItems("Reference").Where(r => r.HasMetadata(pathAttributeName)))
            {
                var metadata = reference.GetMetadata(pathAttributeName);
                var newPath = modifyPackagePath(metadata.EvaluatedValue, metadata.UnevaluatedValue, packages, slnDir, projectDirPath);
                result += SetIfNew("reference", x => metadata.UnevaluatedValue = x, newPath, metadata.UnevaluatedValue);
            }

            foreach (var import in project.Imports)
            {
                var unevaluatedOldPath = import.ImportingElement.Project;
                var evaluatedOldPath = import.ImportedProject.FullPath;

                var newPath = modifyPackagePath(evaluatedOldPath, unevaluatedOldPath, packages, slnDir, projectDirPath);
                result += SetIfNew("import path", x => import.ImportingElement.Project = x, newPath, unevaluatedOldPath);

                var oldCondition = import.ImportingElement.Condition;
                var newCondition = GetNewImportCondition(oldCondition, evaluatedOldPath, unevaluatedOldPath, packages, slnDir, projectDirPath, modifyPackagePath);
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
                var newCondition = GetNewErrorCondition(oldCondition, packages, slnDir, projectDirPath, modifyPackagePath, project);
                result += SetIfNew("error condition", x => error.Condition = x, newCondition, oldCondition);

                var oldText = error.GetParameter("Text");
                var newText = GetNewErrorText(oldText, packages, slnDir, projectDirPath, modifyPackagePath, project);
                result += SetIfNew("error text", x => error.SetParameter("Text", x), newText, oldText);
            }
            return result;
        }

        public static void FixPackagePathsAndSaveProject(EnvDTE.Project project, IReadOnlyCollection<IVsPackageMetadata> packages, string solutionDir)
        {
            var msBuildProject = project.AsMsBuildProject();
            var cntModifiedPaths = ModifyPackagePaths(msBuildProject, packages, solutionDir, GetRelativePackagePath);
            Logger.Info($"Fixed {cntModifiedPaths} paths.");
            if (cntModifiedPaths > 0)
            {
                project.Save();
            }
        }

        public static void FixPackagePathsAndSaveProject(EnvDTE.Project project, IVsPackageMetadata package, string solutionDir)
        {
            FixPackagePathsAndSaveProject(project, new[] { package }, solutionDir);
        }

        private static string GetOriginalPackagePath(string originalEvaluatedValue, string originalUnevaluatedValue,
            IEnumerable<IVsPackageMetadata> packages, string slnDir, string projectDir)
        {
            const string basePathVariable = "$(SolutionDir)";
            if (!originalUnevaluatedValue.StartsWith(basePathVariable))
            {
                return originalUnevaluatedValue;
            }

            try
            {
                var absoluteOldPath = Path.GetFullPath(Path.Combine(projectDir, originalEvaluatedValue));
                if (!absoluteOldPath.StartsWith(slnDir) || !packages.Any(p => absoluteOldPath.StartsWith(p.InstallPath)))
                {
                    return null;
                }

                return PathHelper.GetRelativePath(projectDir, absoluteOldPath);
            }
            catch (ArgumentException e) when (e.Message == "Illegal characters in path.")
            {
                Logger.Error($"Can't determine full path because path contains illegal characters: {originalEvaluatedValue}");
                return null;
            }
        }

        public static void RevertPackagePaths(EnvDTE.Project project, IReadOnlyCollection<IVsPackageMetadata> packages, string solutionDir)
        {
            var msBuildProject = project.AsMsBuildProject();
            var cntModifiedPaths = ModifyPackagePaths(msBuildProject, packages, solutionDir, GetOriginalPackagePath);
            Logger.Info($"Reverted {cntModifiedPaths} paths.");
        }

        public static void RevertPackagePaths(EnvDTE.Project project, IVsPackageMetadata package, string solutionDir)
        {
            RevertPackagePaths(project, new[] { package }, solutionDir);
        }
    }

    public static class MsBuildHelper
    {
        public static string Evaluate(string unevaluatedValue, MsBuild.Project project)
        {
            var props = project.AllEvaluatedProperties
                .GroupBy(prop => prop.Name)
                .ToDictionary(g => g.Key, g => g.Last().EvaluatedValue);
            return Evaluate(unevaluatedValue, props);
        }

        private static string Evaluate(string unevaluatedValue, IReadOnlyDictionary<string, string> properties)
        {
            return properties.Aggregate(unevaluatedValue, (value, prop) => value.Replace($"$({prop.Key})", prop.Value));
        }
    }
}