# FixNuGetPackagePath
Visual Studio extension (.vsix) to make sure that paths to NuGet packages are relative to the directory of the current solution.

When installing a NuGet package the paths to the package files (i.e. *.dll, *.targets, ...) are set relative to the solution that is open at the time the package is installed. This extension ensures that the paths are always relative to the solution that is currently open. That only really makes a difference if you have a project that is referenced from different solutions and those solutions live in different directories (e.g. when using git submodules).

For some discussion on this see https://github.com/NuGet/Home/issues/738

Usage:

Fix NuGet paths in existing projects:

* Open all solutions in Visual Studio
* Select Tools -> Fix NuGet hint paths

When installing a new NuGet package the paths are fixed automatically.
