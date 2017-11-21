nuget restore fixnugetpackagepaths.sln
MSBuild.exe .\FixNuGetPackagePaths.sln /t:FixNuGetPackagePaths /p:Configuration=Release
