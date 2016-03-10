using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace FixNuGetHintPath
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

        public static void Log(string message)
        {
            OutputWindow.Value.OutputString(message + Environment.NewLine);
        }
    }
}
