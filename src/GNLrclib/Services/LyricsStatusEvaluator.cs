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

    public enum LyricsDecisionReason
    {
        FetchMissing,
        UpgradePlainText,
        EmbeddedLyricsPresent,
        SyncedSidecarPresent,
        PriorSyncedResult,
        RecheckNotDue
    }

    public sealed class LyricsDecision
    {
        public LyricsWork Work { get; set; }

        public LyricsDecisionReason Reason { get; set; }

        public DateTime? NextCheckUtc { get; set; }

        public LyricsCheckOutcome? LastOutcome { get; set; }
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

        public LyricsDecision Evaluate(Audio item, LyricsInventory inventory, RecheckPolicy recheckPolicy, DateTime nowUtc)
        {
            if (inventory.HasEmbeddedLyrics)
            {
                return new LyricsDecision { Work = LyricsWork.None, Reason = LyricsDecisionReason.EmbeddedLyricsPresent };
            }

            if (inventory.HasSyncedSidecar)
            {
                return new LyricsDecision { Work = LyricsWork.None, Reason = LyricsDecisionReason.SyncedSidecarPresent };
            }

            var work = inventory.PlainTextSidecarPaths.Count > 0
                ? LyricsWork.UpgradePlainText
                : LyricsWork.FetchMissing;
            var workReason = work == LyricsWork.UpgradePlainText
                ? LyricsDecisionReason.UpgradePlainText
                : LyricsDecisionReason.FetchMissing;

            if (!this.ledger.TryGet(item.InternalId, out LyricsCheckEntry lastCheck))
            {
                return new LyricsDecision { Work = work, Reason = workReason };
            }

            if (lastCheck.Outcome == LyricsCheckOutcome.Synced)
            {
                return new LyricsDecision
                {
                    Work = LyricsWork.None,
                    Reason = LyricsDecisionReason.PriorSyncedResult,
                    LastOutcome = lastCheck.Outcome
                };
            }

            if (recheckPolicy.IsDue(item.DateCreated.UtcDateTime, lastCheck.LastCheckedUtc, nowUtc))
            {
                return new LyricsDecision { Work = work, Reason = workReason, LastOutcome = lastCheck.Outcome };
            }

            return new LyricsDecision
            {
                Work = LyricsWork.None,
                Reason = LyricsDecisionReason.RecheckNotDue,
                LastOutcome = lastCheck.Outcome,
                NextCheckUtc = recheckPolicy.GetNextCheckUtc(item.DateCreated.UtcDateTime, lastCheck.LastCheckedUtc)
            };
        }
    }
}
