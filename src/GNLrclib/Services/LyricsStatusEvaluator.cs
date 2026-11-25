using GNLrclib.Models;
using MediaBrowser.Controller.Entities.Audio;
using System;

namespace GNLrclib.Services
{
    public enum LyricsWork
    {
        None,

        /// <summary>The track has no lyrics at all.</summary>
        FetchMissing,

        /// <summary>The track only has a plain-text (.txt) sidecar; look for synced lyrics to replace it.</summary>
        UpgradePlainText
    }

    /// <summary>
    /// The single place that decides whether a track needs a LRCLIB lookup. Used both by the change monitor
    /// (cheap, before the refresh) and by the refresh itself, so the two can never disagree.
    /// </summary>
    public sealed class LyricsStatusEvaluator
    {
        private readonly LyricsCheckLedger ledger;

        public LyricsStatusEvaluator(LyricsCheckLedger ledger)
        {
            this.ledger = ledger;
        }

        public LyricsWork Evaluate(Audio item, LyricsInventory inventory, RecheckPolicy recheckPolicy, DateTime nowUtc)
        {
            if (inventory.HasEmbeddedLyrics || inventory.HasSyncedSidecar)
            {
                return LyricsWork.None;
            }

            var work = inventory.PlainTextSidecarPaths.Count > 0
                ? LyricsWork.UpgradePlainText
                : LyricsWork.FetchMissing;

            if (!this.ledger.TryGet(item.InternalId, out LyricsCheckEntry lastCheck))
            {
                return work;
            }

            if (lastCheck.Outcome == LyricsCheckOutcome.Synced)
            {
                return LyricsWork.None;
            }

            return recheckPolicy.IsDue(item.DateCreated.UtcDateTime, lastCheck.LastCheckedUtc, nowUtc)
                ? work
                : LyricsWork.None;
        }
    }
}
