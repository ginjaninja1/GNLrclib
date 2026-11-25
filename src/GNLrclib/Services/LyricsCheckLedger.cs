using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GNLrclib.Services
{
    /// <summary>How the last check of a track ended. Drives whether and when the track is looked up again.</summary>
    public enum LyricsCheckOutcome
    {
        NotFound,

        Instrumental,

        PlainTextOnly,

        /// <summary>Terminal: synced lyrics were written. Never looked up again, so a sidecar the user deletes stays deleted.</summary>
        Synced,

        /// <summary>LRCLIB had lyrics but they could not be written to disk. Retried on the normal back-off schedule.</summary>
        WriteFailed
    }

    public class LyricsCheckEntry
    {
        public DateTime LastCheckedUtc { get; set; }

        public LyricsCheckOutcome Outcome { get; set; }
    }

    /// <summary>
    /// Persistent record of the last check per track (the negative-result cache). Without it every scan would
    /// re-query LRCLIB for every track that has no lyrics there. Keyed by Emby internal item id; stored as JSON
    /// and written back a few seconds after the last change.
    /// </summary>
    public sealed class LyricsCheckLedger : IDisposable
    {
        private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

        private readonly object sync = new object();
        private readonly Dictionary<string, LyricsCheckEntry> entries;
        private readonly string filePath;
        private readonly IJsonSerializer jsonSerializer;
        private readonly ILogger logger;
        private readonly Timer saveTimer;

        private bool isDirty;

        public LyricsCheckLedger(string filePath, IJsonSerializer jsonSerializer, ILogger logger)
        {
            this.filePath = filePath;
            this.jsonSerializer = jsonSerializer;
            this.logger = logger;
            this.entries = this.Load();
            this.saveTimer = new Timer(_ => this.Save(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public bool TryGet(long itemId, out LyricsCheckEntry entry)
        {
            lock (this.sync)
            {
                return this.entries.TryGetValue(itemId.ToString(), out entry);
            }
        }

        public void Record(long itemId, LyricsCheckOutcome outcome, DateTime checkedAtUtc)
        {
            lock (this.sync)
            {
                this.entries[itemId.ToString()] = new LyricsCheckEntry
                {
                    LastCheckedUtc = checkedAtUtc,
                    Outcome = outcome
                };

                this.isDirty = true;
            }

            this.saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }

        public void Dispose()
        {
            this.saveTimer.Dispose();
            this.Save();
        }

        private Dictionary<string, LyricsCheckEntry> Load()
        {
            try
            {
                if (File.Exists(this.filePath))
                {
                    var loaded = this.jsonSerializer.DeserializeFromFile<Dictionary<string, LyricsCheckEntry>>(this.filePath);

                    if (loaded != null)
                    {
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.ErrorException("Could not read the lyrics check ledger {0}; starting with an empty ledger", ex, this.filePath);
            }

            return new Dictionary<string, LyricsCheckEntry>();
        }

        private void Save()
        {
            Dictionary<string, LyricsCheckEntry> snapshot;

            lock (this.sync)
            {
                if (!this.isDirty)
                {
                    return;
                }

                snapshot = new Dictionary<string, LyricsCheckEntry>(this.entries);
                this.isDirty = false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.filePath));

                var temporaryPath = this.filePath + ".tmp";
                this.jsonSerializer.SerializeToFile(snapshot, temporaryPath);

                if (File.Exists(this.filePath))
                {
                    File.Replace(temporaryPath, this.filePath, null);
                }
                else
                {
                    File.Move(temporaryPath, this.filePath);
                }
            }
            catch (Exception ex)
            {
                lock (this.sync)
                {
                    this.isDirty = true;
                }

                this.logger.ErrorException("Could not save the lyrics check ledger {0}", ex, this.filePath);
            }
        }
    }
}
