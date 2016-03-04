//------------------------------------------------------------------------------
// <copyright file="VSPackage.cs" company="Hewlett-Packard Company">
//     Copyright (c) Hewlett-Packard Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using VSLangProj;
using Project = EnvDTE.Project;

namespace FixNuGetHintPath
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
    [Guid(VSPackage.PackageGuidString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    public sealed class VSPackage : Package
    {
        private SolutionEvents _SolutionEvents;
        private List<Tuple<Project, ReferencesEvents>> _ReferencesAddedEvents;
        public const string PackageGuidString = "41ae4a56-653c-439f-b1ac-7dec17faa923";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            var dte = (DTE)ServiceProvider.GlobalProvider.GetService(typeof(DTE));

            _SolutionEvents = dte.Events.SolutionEvents;
            _SolutionEvents.Opened += () =>
            {
                _ReferencesAddedEvents = dte.Solution.Projects.Cast<Project>()
                    .Select(p =>
                    {
                        var vsProject = (VSProject) p.Object;
                        return Tuple.Create(p, vsProject.Events.ReferencesEvents);
                    })
                    .ToList();
                _ReferencesAddedEvents.Select(x =>
                {
                    try
                    {
                        return Observable.FromEvent<_dispReferencesEvents_ReferenceAddedEventHandler, Reference>
                            (h => new _dispReferencesEvents_ReferenceAddedEventHandler(h)
                                , h => x.Item2.ReferenceAdded += h
                                , h => x.Item2.ReferenceAdded -= h
                            )
                            .Select(reference => new {Project = x.Item1, Reference = reference});
                    }
                    catch (Exception e)
                    {
                        return Observable.Empty(new {Project = (Project) null, Reference = (Reference) null});
                    }
                })
                .Merge()
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(x =>
                {
                    var project = AsMsBuildProject(x.Project);
                    FixReferences(project, Path.GetDirectoryName(dte.Solution.FullName));
                    x.Project.Save();
                });
            };

            base.Initialize();
        }

        private static void FixReferences(Microsoft.Build.Evaluation.Project project, string slnDir)
        {
            foreach (var reference in project.GetItems("Reference"))
            {
                var hintPath = reference.GetMetadataValue("HintPath");
                if (string.IsNullOrEmpty(hintPath))
                {
                    continue;
                }
                var absoluteHintPath = Path.GetFullPath(Path.Combine(project.DirectoryPath, hintPath));
                if (!absoluteHintPath.StartsWith(slnDir))
                {
                    continue;
                }
                var path = absoluteHintPath.Substring(slnDir.Length).TrimStart(Path.DirectorySeparatorChar);
                reference.SetMetadataValue("HintPath", @"$(SolutionDir)\" + path);
            }
        }

        private static Microsoft.Build.Evaluation.Project AsMsBuildProject(Project project)
        {
            return ProjectCollection.GlobalProjectCollection.GetLoadedProjects(project.FullName).FirstOrDefault() ??
                   ProjectCollection.GlobalProjectCollection.LoadProject(project.FullName);
        }

        #endregion
    }
}
