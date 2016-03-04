//------------------------------------------------------------------------------
// <copyright file="VSPackage.cs" company="Hewlett-Packard Company">
//     Copyright (c) Hewlett-Packard Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using NuGet.ProjectManagement;
using NuGet.VisualStudio;

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
            var installerService = componentModel.GetService<IVsPackageInstallerServices>();

            var installing = Observable.FromEvent<VsPackageEventHandler, IVsPackageMetadata>
                (h => new VsPackageEventHandler(h)
                , h => installerEvents.PackageInstalling += h
                , h => installerEvents.PackageInstalling -= h
                )
                .Select(_ => GetProjectPackages(installerService));

            var installed = Observable.FromEvent<VsPackageEventHandler, IVsPackageMetadata>
                (h => new VsPackageEventHandler(h)
                , h => installerEvents.PackageInstalled += h
                , h => installerEvents.PackageInstalled -= h
                )
                .Do(x => { });

            installing
                .Zip(installed, (oldProjectPackages, newPackage) =>
                {
                    return GetProjectPackages(installerService)
                        .Where(x => x.Value.Any(m => m.Id == newPackage.Id))
                        .Select(x => x.Key)
                        .Single(x => oldProjectPackages[x].All(m => m.Id != newPackage.Id));
                })
                .Subscribe((Project project) =>
                {
                    try
                    {
                        var dte = (DTE)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }
                });
            base.Initialize();
        }

        private IDictionary<Project, IReadOnlyCollection<IVsPackageMetadata>> GetProjectPackages(IVsPackageInstallerServices installerService)
        {
            var dte = (DTE)ServiceProvider.GlobalProvider.GetService(typeof(DTE));
            return dte.Solution.Projects
                .Cast<Project>()
                .ToDictionary(x => x, x => (IReadOnlyCollection<IVsPackageMetadata>)installerService.GetInstalledPackages(x).ToList());
        }

        #endregion
    }
}
