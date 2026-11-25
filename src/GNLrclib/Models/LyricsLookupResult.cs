namespace GNLrclib.Models
{
    public enum LyricsLookupOutcome
    {
        /// <summary>Rate limited, rejected, offline or server error. Transient: nothing is recorded and the track is retried on a later scan.</summary>
        Unavailable,

        NotFound,

        Instrumental,

        PlainTextOnly,

        Synced
    }

    public sealed class LyricsLookupResult
    {
        private LyricsLookupResult(LyricsLookupOutcome outcome, string syncedLyrics = null, string plainLyrics = null)
        {
            this.Outcome = outcome;
            this.SyncedLyrics = syncedLyrics;
            this.PlainLyrics = plainLyrics;
        }

        public static LyricsLookupResult Unavailable { get; } = new LyricsLookupResult(LyricsLookupOutcome.Unavailable);

        public static LyricsLookupResult NotFound { get; } = new LyricsLookupResult(LyricsLookupOutcome.NotFound);

        public static LyricsLookupResult Instrumental { get; } = new LyricsLookupResult(LyricsLookupOutcome.Instrumental);

        public LyricsLookupOutcome Outcome { get; }

        public string SyncedLyrics { get; }

        public string PlainLyrics { get; }

        public static LyricsLookupResult FromPlainText(string plainLyrics)
        {
            return new LyricsLookupResult(LyricsLookupOutcome.PlainTextOnly, plainLyrics: plainLyrics);
        }

        public static LyricsLookupResult FromSynced(string syncedLyrics, string plainLyrics)
        {
            return new LyricsLookupResult(LyricsLookupOutcome.Synced, syncedLyrics, plainLyrics);
        }
    }
}
