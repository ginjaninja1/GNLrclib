namespace GNLrclib.Models
{
    /// <summary>
    /// The parts of an LRCLIB track record the plugin uses. LRCLIB returns camelCase
    /// property names; Emby's JSON serializer matches them case-insensitively.
    /// </summary>
    public class LrcLibTrackResponse
    {
        public bool Instrumental { get; set; }

        public string PlainLyrics { get; set; }

        public string SyncedLyrics { get; set; }
    }
}
