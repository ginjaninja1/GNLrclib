using GNLrclib.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GNLrclib.Tasks
{
    /// <summary>Removes negative-cache entries for items that no longer exist in the Emby library.</summary>
    public sealed class PruneLyricsCheckLedgerTask : IScheduledTask
    {
        private readonly ILibraryManager libraryManager;
        private readonly IApplicationPaths applicationPaths;
        private readonly IJsonSerializer jsonSerializer;
        private readonly ILogger logger;

        public PruneLyricsCheckLedgerTask(
            ILibraryManager libraryManager,
            IApplicationPaths applicationPaths,
            IJsonSerializer jsonSerializer,
            ILogManager logManager)
        {
            this.libraryManager = libraryManager;
            this.applicationPaths = applicationPaths;
            this.jsonSerializer = jsonSerializer;
            this.logger = logManager.GetLogger("GNLrclib");
        }

        public string Name => "Prune LRCLIB lyrics check ledger";

        public string Key => "GNLrclibPruneLyricsCheckLedger";

        public string Description => "Removes lyrics check records for items no longer in the Emby library.";

        public string Category => "Library";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = "DailyTrigger",
                    TimeOfDayTicks = new TimeSpan(0, 5, 0).Ticks
                }
            };
        }

        public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            this.logger.Info("Starting LRCLIB lyrics check ledger pruning");

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ledgerPath = Path.Combine(this.applicationPaths.DataPath, "lrclib-lyrics", "lyrics-check-ledger.json");
                var ledger = new LyricsCheckLedger(ledgerPath, this.jsonSerializer, this.logger);
                try
                {
                    var activeIds = new HashSet<string>(
                        this.libraryManager.GetUserRootFolder()
                            .GetItemIdList(new InternalItemsQuery
                            {
                                Recursive = true,
                                IsFolder = false,
                                EnableTotalRecordCount = false
                            }, cancellationToken)
                            .Select(id => id.ToString()),
                        StringComparer.Ordinal);

                    var removed = ledger.PruneExcept(activeIds);
                    foreach (var itemId in removed)
                    {
                        this.logger.Info("Pruned lyrics check ledger record for item {0}", itemId);
                    }

                    progress?.Report(100);
                    this.logger.Info("Stopped LRCLIB lyrics check ledger pruning; pruned {0} record(s)", removed.Count);
                }
                finally
                {
                    ledger.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                this.logger.Info("Stopped LRCLIB lyrics check ledger pruning because it was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("LRCLIB lyrics check ledger pruning failed", ex);
                throw;
            }

            return Task.CompletedTask;
        }
    }
}
