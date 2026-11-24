
using MediaBrowser.Common;
using MediaBrowser.Model.Logging;
using GNLrclib.UI.Config;
using GNLrclib.UIBaseClasses.Store;

namespace GNLrclib.Storage
{
    public class BasicsOptionsStore : SimpleFileStore<ConfigUI>
    {
        public BasicsOptionsStore(IApplicationHost applicationHost, ILogger logger, string pluginFullName)
        : base(applicationHost, logger, pluginFullName)
        {
        }
    }
}
