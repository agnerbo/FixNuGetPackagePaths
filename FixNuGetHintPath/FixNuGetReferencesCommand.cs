//------------------------------------------------------------------------------
// <copyright file="FixNuGetReferencesCommand.cs" company="Hewlett-Packard Company">
//     Copyright (c) Hewlett-Packard Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.ComponentModel.Design;
using System.Linq;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using NuGet.VisualStudio;

namespace FixNuGetHintPath
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class FixNuGetReferencesCommand
    {
        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 0x0100;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("df73de9a-1805-4ec7-86d3-28877781f144");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly Package package;

        /// <summary>
        /// Initializes a new instance of the <see cref="FixNuGetReferencesCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
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

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static FixNuGetReferencesCommand Instance
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private IServiceProvider ServiceProvider => this.package;

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static void Initialize(Package package)
        {
            Instance = new FixNuGetReferencesCommand(package);
        }

        /// <summary>
        /// This function is the callback used to execute the command when the menu item is clicked.
        /// See the constructor to see how the menu item is associated with this function using
        /// OleMenuCommandService service and MenuCommand class.
        /// </summary>
        /// <param name="sender">Event sender.</param>
        /// <param name="e">Event args.</param>
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
