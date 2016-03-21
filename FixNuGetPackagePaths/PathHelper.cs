using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FixNuGetPackagePaths
{
    public static class PathHelper
    {
        public static string GetRelativePath(string fromPath, string toPath)
        {
            var fromAttr = GetPathAttribute(fromPath);
            var toAttr = GetPathAttribute(toPath);

            const int maxPath = 260;
            var path = new StringBuilder(maxPath);
            if (!PathRelativePathTo(path, fromPath, fromAttr, toPath, toAttr))
            {
                throw new ArgumentException("Paths must have a common prefix");
            }
            return path.ToString();
        }

        private static int GetPathAttribute(string path)
        {
            if (Directory.Exists(path)) return FileAttributeDirectory;
            if (File.Exists(path)) return FileAttributeNormal;

            throw new FileNotFoundException();
        }

        private const int FileAttributeDirectory = 0x10;
        private const int FileAttributeNormal = 0x80;

        [DllImport("shlwapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PathRelativePathTo(StringBuilder pszPath,
            string pszFrom, int dwAttrFrom, string pszTo, int dwAttrTo);
    }
}
