//------------------------------------------------------------------------------
// <copyright file="VSPackage.cs" company="Hewlett-Packard Company">
//     Copyright (c) Hewlett-Packard Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.VisualStudio;
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
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class VSPackage : Package
    {
        private IDisposable _Subscription;
        public const string PackageGuidString = "41ae4a56-653c-439f-b1ac-7dec17faa923";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            var componentModel = (IComponentModel)GetService(typeof(SComponentModel));

            var installerEvents = componentModel.GetService<IVsPackageInstallerEvents>();

            var dte = (DTE)ServiceProvider.GlobalProvider.GetService(typeof(DTE));

            var installFinished = Observable.FromEvent<VsPackageEventHandler, IVsPackageMetadata>
                (h => new VsPackageEventHandler(h)
                    , h => installerEvents.PackageReferenceAdded += h
                    , h => installerEvents.PackageReferenceAdded -= h
                )
                .Do(_ => { });

            var events = (ReferencesEvents)dte.Events.GetObject("CSharpReferencesEvents");
            events.ReferenceAdded += r => { };

            var events2 = (ProjectItemsEvents) dte.Events.GetObject("CSharpProjectItemsEvents");
            events2.ItemAdded += r => { };


            _Subscription = installFinished
                .Where(x => x.InstallPath.StartsWith(dte.GetSolutionDir()))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(package =>
                {
                    foreach (var project in dte.Solution.GetAllProjects())
                    {
                        Logger.Log($"===== Fix paths for NuGet package {package.Id} in project {project.Name} ======");
                        var msBuildProject = project.AsMsBuildProject();
                        var fixedPaths = NuGet.FixPackagePaths(msBuildProject, package, dte.GetSolutionDir());
                        Logger.Log($"Fixed {fixedPaths} paths.");
                        if (fixedPaths > 0)
                        {
                            project.Save();
                        }
                    }
                });

            FixNuGetReferencesCommand.Initialize(this);
        }

        protected override void Dispose(bool disposing)
        {
            _Subscription.Dispose();

            base.Dispose(disposing);
        }

        #endregion
    }

    public class ProjectEvents
    {
        public Project Project { get; }
        public ReferencesEvents ReferencesEvents { get;}
        public ImportsEvents ImportsEvent { get;}

        public ProjectEvents(Project project, ReferencesEvents referencesEvents, ImportsEvents importsEvent)
        {
            Project = project;
            ReferencesEvents = referencesEvents;
            ImportsEvent = importsEvent;
        }
    }
}
