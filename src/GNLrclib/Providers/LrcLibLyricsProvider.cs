using GNLrclib.Configuration;
using GNLrclib.Models;
using GNLrclib.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GNLrclib.Providers
{
    /// <summary>
    /// Downloads missing lyrics from LRCLIB while Emby refreshes a music track.
    ///
    /// Not an IPreRefreshProvider on purpose: custom providers without that marker run after the probe, so the
    /// item already carries its title, artists, album and duration read from the file tags.
    /// IHasItemChangeMonitor is what makes Emby run this provider on ordinary scans (it is skipped otherwise);
    /// IForcedProvider keeps it running for locked items.
    /// </summary>
    public sealed class LrcLibLyricsProvider :
        ICustomMetadataProvider<Audio>,
        IHasItemChangeMonitor,
        IForcedProvider,
        IDisposable
    {
        private const string SyncedExtension = ".lrc";
        private const string PlainTextExtension = ".txt";

        private readonly ILogger logger;
        private readonly LrcLibClient client;
        private readonly LyricsSidecarStore sidecars;
        private readonly LyricsCheckLedger ledger;
        private readonly LyricsStatusEvaluator evaluator;

        public LrcLibLyricsProvider(
            ILogManager logManager,
            IHttpClient httpClient,
            IJsonSerializer jsonSerializer,
            IFileSystem fileSystem,
            ILibraryMonitor libraryMonitor,
            IApplicationPaths applicationPaths)
        {
            this.logger = logManager.GetLogger("GNLrclib");
            this.client = new LrcLibClient(httpClient, jsonSerializer, this.logger);
            this.sidecars = new LyricsSidecarStore(fileSystem, libraryMonitor, this.logger);

            var ledgerPath = Path.Combine(applicationPaths.DataPath, "lrclib-lyrics", "lyrics-check-ledger.json");
            this.ledger = new LyricsCheckLedger(ledgerPath, jsonSerializer, this.logger);
            this.evaluator = new LyricsStatusEvaluator(this.ledger);
        }

        public string Name => "LRCLIB lyrics";

        /// <summary>
        /// Runs before the refresh, against stored item data. Returns true only when a lookup is worth making,
        /// so tracks that already have lyrics, or were checked recently, cost nothing on a scan.
        /// </summary>
        public bool HasChanged(BaseItem item, LibraryOptions libraryOptions, IDirectoryService directoryService)
        {
            var settings = Plugin.Instance == null ? null : Plugin.Instance.Configuration;
            var audio = item as Audio;

            if (audio == null)
            {
                return false;
            }

            if (!IsEnabledFor(audio, settings))
            {
                this.logger.Debug("Lyrics change check skipped for {0}: plugin disabled, item not saved/local, or path is outside enabled library paths", audio.Path);
                return false;
            }

            if (!TrackSignature.TryCreate(audio, out var signature))
            {
                this.logger.Debug("Lyrics change check skipped for {0}: title, artist, or usable duration is missing", audio.Path);
                return false;
            }

            var inventory = LyricsInventory.FromStreams(audio.GetMediaStreams());
            var decision = this.evaluator.Evaluate(audio, inventory, RecheckPolicy.FromSettings(settings), DateTime.UtcNow);
            this.logger.Debug("Lyrics change check for {0}: work={1}, reason={2}, lastOutcome={3}, nextCheckUtc={4}, signature={5}, lyrics={6}",
                audio.Path,
                decision.Work,
                decision.Reason,
                decision.LastOutcome.HasValue ? decision.LastOutcome.Value.ToString() : "none",
                decision.NextCheckUtc.HasValue ? decision.NextCheckUtc.Value.ToString("o") : "none",
                FormatSignature(signature),
                inventory.Description);
            return decision.Work != LyricsWork.None;
        }

        public async Task<ItemUpdateType> FetchAsync(
            MetadataResult<Audio> itemResult,
            MetadataRefreshOptions options,
            LibraryOptions libraryOptions,
            CancellationToken cancellationToken)
        {
            var item = itemResult.Item;
            var settings = Plugin.Instance == null ? null : Plugin.Instance.Configuration;

            if (!IsEnabledFor(item, settings))
            {
                this.logger.Debug("Lyrics refresh skipped for {0}: plugin disabled, item not saved/local, or path is outside enabled library paths", item.Path);
                return ItemUpdateType.None;
            }

            if (!TrackSignature.TryCreate(item, out var signature))
            {
                this.logger.Debug("Skipping lyrics lookup, track lacks title, artist or duration: {0}", item.Path);
                return ItemUpdateType.None;
            }

            // The probe has just run, so its streams are the freshest; fall back to stored streams if it did not run.
            IEnumerable<MediaStream> streams = itemResult.MediaStreams;

            if (streams == null)
            {
                streams = item.GetMediaStreams();
            }

            var inventory = LyricsInventory.FromStreams(streams);
            var now = DateTime.UtcNow;
            var decision = this.evaluator.Evaluate(item, inventory, RecheckPolicy.FromSettings(settings), now);

            if (decision.Work == LyricsWork.None)
            {
                this.logger.Debug("Lyrics refresh skipped for {0}: reason={1}, lastOutcome={2}, nextCheckUtc={3}, lyrics={4}",
                    item.Path,
                    decision.Reason,
                    decision.LastOutcome.HasValue ? decision.LastOutcome.Value.ToString() : "none",
                    decision.NextCheckUtc.HasValue ? decision.NextCheckUtc.Value.ToString("o") : "none",
                    inventory.Description);
                return ItemUpdateType.None;
            }

            try
            {
                this.logger.Debug("Starting LRCLIB lookup for {0}: work={1}, reason={2}, lastOutcome={3}, signature={4}, lyrics={5}",
                    item.Path,
                    decision.Work,
                    decision.Reason,
                    decision.LastOutcome.HasValue ? decision.LastOutcome.Value.ToString() : "none",
                    FormatSignature(signature),
                    inventory.Description);
                var result = await this.client.FindAsync(signature, settings, cancellationToken).ConfigureAwait(false);
                this.logger.Debug("LRCLIB lookup finished for {0}: outcome={1}", item.Path, result.Outcome);
                this.Apply(item, decision.Work, inventory, result, settings, now);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("Lyrics lookup failed for {0}", ex, item.Path);
            }

            // Nothing on the item itself changes; the new sidecar is ingested by the probe on a later scan.
            return ItemUpdateType.None;
        }

        private static string FormatSignature(TrackSignature signature)
        {
            return string.Format("track='{0}', artist='{1}', album='{2}', duration={3}s", signature.TrackName, signature.ArtistName, signature.AlbumName, signature.DurationSeconds);
        }

        public void Dispose()
        {
            this.ledger.Dispose();
        }

        /// <summary>
        /// Local, saved music tracks inside an opted-in library path only. An internal id of 0 means the item
        /// has not been saved yet (first refresh of a new track); the change monitor picks it up on the next scan.
        /// </summary>
        private static bool IsEnabledFor(Audio item, PluginConfiguration settings)
        {
            return settings != null
                && settings.EnablePlugin
                && item.InternalId > 0
                && item.IsFileProtocol
                && !item.IsShortcut
                && LibraryPathScope.IsInScope(item.Path, settings.LibraryPaths);
        }

        private void Apply(
            Audio item,
            LyricsWork work,
            LyricsInventory inventory,
            LyricsLookupResult result,
            PluginConfiguration settings,
            DateTime now)
        {
            switch (result.Outcome)
            {
                case LyricsLookupOutcome.Unavailable:
                    // Transient. Record nothing so the track is retried on a later scan.
                    return;

                case LyricsLookupOutcome.Synced:
                    this.SaveSynced(item, inventory, result, settings, now);
                    return;

                case LyricsLookupOutcome.PlainTextOnly:
                    this.SavePlainText(item, work, result, settings, now);
                    return;

                case LyricsLookupOutcome.Instrumental:
                    this.ledger.Record(item.InternalId, LyricsCheckOutcome.Instrumental, now);
                    return;

                default:
                    this.logger.Debug("No lyrics on LRCLIB for {0}", item.Path);
                    this.ledger.Record(item.InternalId, LyricsCheckOutcome.NotFound, now);
                    return;
            }
        }

        private void SaveSynced(
            Audio item,
            LyricsInventory inventory,
            LyricsLookupResult result,
            PluginConfiguration settings,
            DateTime now)
        {
            var writtenPath = this.sidecars.Write(item, SyncedExtension, result.SyncedLyrics);

            if (writtenPath == null)
            {
                this.ledger.Record(item.InternalId, LyricsCheckOutcome.WriteFailed, now);
                return;
            }

            this.logger.Info("Saved synced lyrics: {0}", writtenPath);

            if (settings.ReplacePlainTextSidecarWhenSyncedFound)
            {
                this.sidecars.Delete(inventory.PlainTextSidecarPaths);
            }

            this.ledger.Record(item.InternalId, LyricsCheckOutcome.Synced, now);
        }

        private void SavePlainText(
            Audio item,
            LyricsWork work,
            LyricsLookupResult result,
            PluginConfiguration settings,
            DateTime now)
        {
            // A track that already has a plain-text sidecar gains nothing from another copy; only the check date moves.
            if (work != LyricsWork.FetchMissing || !settings.WritePlainTextWhenNoSyncedLyrics)
            {
                this.ledger.Record(item.InternalId, LyricsCheckOutcome.PlainTextOnly, now);
                return;
            }

            var writtenPath = this.sidecars.Write(item, PlainTextExtension, result.PlainLyrics);

            if (writtenPath == null)
            {
                this.ledger.Record(item.InternalId, LyricsCheckOutcome.WriteFailed, now);
                return;
            }

            this.logger.Info("Saved plain-text lyrics (no synced version on LRCLIB yet): {0}", writtenPath);
            this.ledger.Record(item.InternalId, LyricsCheckOutcome.PlainTextOnly, now);
        }
    }
}
