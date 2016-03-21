using System;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.VisualStudio;

namespace FixNuGetPackagePaths
{
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // Info on this package for Help/About
    [Guid(PackageGuidString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class VsPackage : Package
    {
        private IDisposable _InstallSubscription;
        private IDisposable _UninstallSubscription;
        private const string PackageGuidString = "41ae4a56-653c-439f-b1ac-7dec17faa923";

        #region Package Members

        protected override void Initialize()
        {
            base.Initialize();
            FixNuGetReferencesCommand.Initialize(this);

            var componentModel = (IComponentModel)GetService(typeof(SComponentModel));

            var installerEvents = componentModel.GetService<IVsPackageInstallerEvents>();

            var dte = (DTE)ServiceProvider.GlobalProvider.GetService(typeof(DTE));

            // Order of events when installing a package:
            // 1. PackageInstalling
            // 2. PackageInstalled
            // 3. PackageReferenceAdded
            // Order of events when uninstalling a package:
            // 1. PackageUninstalling
            // 2. PackageReferenceRemoved
            // 3. PackageUninstalled

            var installFinished = Observable.FromEvent<VsPackageEventHandler, IVsPackageMetadata>(
                h => new VsPackageEventHandler(h),
                h => installerEvents.PackageReferenceAdded += h,
                h => installerEvents.PackageReferenceAdded -= h);

            _InstallSubscription = installFinished
                .Where(x => x.InstallPath.StartsWith(dte.GetSolutionDir()))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(package =>
                {
                    foreach (var project in dte.Solution.GetAllProjects())
                    {
                        Logger.Info($"===== Fix paths for NuGet package {package.Id} in project {project.Name} ======");
                        try
                        {
                            NuGet.FixPackagePathsAndSaveProject(project, package, dte.GetSolutionDir());
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Unexpected error: {ex}");
                        }
                    }
                });

            var uninstallStarted = Observable.FromEvent<VsPackageEventHandler, IVsPackageMetadata>(
                h => new VsPackageEventHandler(h),
                h => installerEvents.PackageUninstalling += h,
                h => installerEvents.PackageUninstalling -= h);

            _UninstallSubscription = uninstallStarted
                .Where(x => x.InstallPath.StartsWith(dte.GetSolutionDir()))
                .ObserveOn(SynchronizationContext.Current)
                .Subscribe(package =>
                {
                    foreach (var project in dte.Solution.GetAllProjects())
                    {
                        Logger.Info($"===== Restore paths for NuGet package {package.Id} in project {project.Name} ======");
                        try
                        {
                            NuGet.RevertPackagePaths(project, package, dte.GetSolutionDir());
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Unexpected error: {ex}");
                        }
                    }
                });
        }

        protected override void Dispose(bool disposing)
        {
            _InstallSubscription.Dispose();
            _UninstallSubscription.Dispose();

            base.Dispose(disposing);
        }

        #endregion
    }
}
