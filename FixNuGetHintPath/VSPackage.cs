//------------------------------------------------------------------------------
// <copyright file="VSPackage.cs" company="Hewlett-Packard Company">
//     Copyright (c) Hewlett-Packard Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private List<Tuple<Project, ReferencesEvents>> _ReferencesAddedEvents;
        private IDisposable _Subscription;
        public const string PackageGuidString = "41ae4a56-653c-439f-b1ac-7dec17faa923";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            var componentModel = (IComponentModel)GetService(typeof(SComponentModel));

            var installerEvents = componentModel.GetService<IVsPackageInstallerEvents>();

            var dte = (DTE)ServiceProvider.GlobalProvider.GetService(typeof(DTE));

            // The events used here occur in the following order when a NuGet package is installed:
            // 1. IVsPackageInstallerEvents.PackageInstalled
            // 2. Project.Events.ReferencesEvents.ReferenceAdded
            // 3. IVsPackageInstallerEvents.PackageReferenceAdded

            var packageInstalled = Observable.FromEvent<VsPackageEventHandler, IVsPackageMetadata>
                (h => new VsPackageEventHandler(h)
                    , h => installerEvents.PackageInstalled += h
                    , h => installerEvents.PackageInstalled -= h
                );

            var installFinished = Observable.FromEvent<VsPackageEventHandler, IVsPackageMetadata>
                (h => new VsPackageEventHandler(h)
                    , h => installerEvents.PackageReferenceAdded += h
                    , h => installerEvents.PackageReferenceAdded -= h
                );

            _Subscription = packageInstalled
                .Where(x => x.InstallPath.StartsWith(dte.GetSolutionDir()))
                .Do(_ =>
                {
                    _ReferencesAddedEvents = dte.Solution.GetAllProjects()
                        .Select(p =>
                        {
                            var vsProject = (VSProject)p.Object;
                            return Tuple.Create(p, vsProject.Events.ReferencesEvents);
                        })
                        .ToList();
                })
                .Select(package =>
                    _ReferencesAddedEvents.Select(x =>
                        Observable.FromEvent<_dispReferencesEvents_ReferenceAddedEventHandler, Reference>
                            (h => new _dispReferencesEvents_ReferenceAddedEventHandler(h)
                                , h => x.Item2.ReferenceAdded += h
                                , h => x.Item2.ReferenceAdded -= h
                            )
                            .Select(reference => new { Project = x.Item1, Reference = reference, Package = package })
                        )
                        .Merge()
                        .TakeUntil(installFinished)
                        .LastAsync()
                )
                .Switch()
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(x =>
                {
                    var project = x.Project.AsMsBuildProject();
                    NuGet.FixReferences(project, x.Package, dte.GetSolutionDir());
                    x.Project.Save();
                });

            base.Initialize();
            FixNuGetReferencesCommand.Initialize(this);
        }

        protected override void Dispose(bool disposing)
        {
            _Subscription.Dispose();

            base.Dispose(disposing);
        }

        #endregion
    }
}
