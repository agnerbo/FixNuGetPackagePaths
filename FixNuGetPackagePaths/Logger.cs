using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace FixNuGetPackagePaths
{
    public static class Logger
    {
        private static readonly Lazy<IVsOutputWindowPane> OutputWindow = new Lazy<IVsOutputWindowPane>(GetOrCreateOutputWindow);

        private static IVsOutputWindowPane GetOrCreateOutputWindow()
        {
            var outWindow = (IVsOutputWindow) Package.GetGlobalService(typeof (SVsOutputWindow));

            var paneId = new Guid("0EC386AC-83D0-4733-A8C1-A084F098E58F");
            const string paneTitle = "Fix NuGet hint path";
            var error = outWindow.CreatePane(ref paneId, paneTitle, 1, 1);

            IVsOutputWindowPane pane;
            outWindow.GetPane(ref paneId, out pane);

            return pane;
        }

        public static void Info(string message) => Output("Info", message);
        public static void Error(string message) => Output("Error", message);

        private static void Output(string type, string message)
        {
            OutputWindow.Value.OutputString($"[{type}] {message}{Environment.NewLine}");
        }
    }
}
