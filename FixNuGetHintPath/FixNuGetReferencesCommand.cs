using System;
using System.ComponentModel.Design;
using System.Linq;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using NuGet.VisualStudio;

namespace FixNuGetHintPath
{
    internal sealed class FixNuGetReferencesCommand
    {
        public const int CommandId = 0x0100;

        public static readonly Guid CommandSet = new Guid("df73de9a-1805-4ec7-86d3-28877781f144");

        private readonly Package package;

        private FixNuGetReferencesCommand(Package package)
        {
            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            this.package = package;

            var commandService = this.ServiceProvider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService != null)
            {
                var menuCommandID = new CommandID(CommandSet, CommandId);
                var menuItem = new MenuCommand(this.MenuItemCallback, menuCommandID);
                commandService.AddCommand(menuItem);
            }
        }

        public static FixNuGetReferencesCommand Instance
        {
            get;
            private set;
        }

        private IServiceProvider ServiceProvider => this.package;

        public static void Initialize(Package package)
        {
            Instance = new FixNuGetReferencesCommand(package);
        }

        private void MenuItemCallback(object sender, EventArgs e)
        {
            var componentModel = (IComponentModel)ServiceProvider.GetService(typeof(SComponentModel));
            var installerServices = componentModel.GetService<IVsPackageInstallerServices>();

            var allPackages = installerServices.GetInstalledPackages().ToList();
            var packages = allPackages.Where(p => !string.IsNullOrEmpty(p.InstallPath)).ToList();
            foreach (var p in allPackages.Except(packages))
            {
                Logger.Error($"Can't fix paths for {p.Id} because the install path is empty.");
            }

            var dte = (EnvDTE.DTE)ServiceProvider.GetService(typeof(EnvDTE.DTE));

            foreach (var project in dte.Solution.GetAllProjects())
            {
                Logger.Info($"===== Fix paths for all NuGet packages in project {project.Name} ======");
                try
                {
                    NuGet.FixPackagePathsAndSaveProject(project, packages, dte.GetSolutionDir());
                }
                catch (Exception ex)
                {
                    Logger.Error($"Unexpected error: {ex}");
                }
            }
        }
    }
}
