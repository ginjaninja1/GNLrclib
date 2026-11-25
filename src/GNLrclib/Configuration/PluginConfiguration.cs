using System.Collections.Generic;
using MediaBrowser.Model.Plugins;
using GNLrclib.UI.Config;

namespace GNLrclib.Configuration
{
    /// <summary>
    /// The plugin's persisted settings - and the ONLY class involved in
    /// persistence. This uses Emby's standard BasePlugin&lt;T&gt; mechanism:
    /// Plugin.Instance.Configuration / SaveConfiguration() / UpdateConfiguration(),
    /// which serializes to XML in the plugin configurations folder
    /// automatically. No custom store, no hand-rolled JSON round-trip.
    ///
    /// This class has no UI/visual members, by construction - it isn't
    /// rendered by GenericEdit and is never assigned as ContentData, so
    /// there's nothing for it to accidentally leak. The config page instead
    /// builds a separate view-model, ConfigUI, fresh from this class every
    /// time it's shown - see GNLrclib.UI.Config.ConfigViewBuilder.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool EnablePlugin { get; set; } = true;

        /// <summary>
        /// The real, persisted library/path filter data. Only music tracks
        /// under an enabled path are ever looked up; nothing is enabled by default.
        /// </summary>
        public List<LibraryPathFilterItem> LibraryPaths { get; set; } =
            new List<LibraryPathFilterItem>();

        /// <summary>
        /// Public LRCLIB client-side application id, sent as the Lrclib-App-Id
        /// header. Empty means requests are sent without an application id.
        /// </summary>
        public string LrcLibAppId { get; set; } = string.Empty;

        /// <summary>
        /// When LRCLIB only has unsynchronised lyrics, save them as a .txt
        /// sidecar (and keep looking for a synced version later).
        /// </summary>
        public bool WritePlainTextWhenNoSyncedLyrics { get; set; } = true;

        /// <summary>
        /// When synced lyrics are found for a track that only has a .txt
        /// sidecar, move that .txt to the recycle bin.
        /// </summary>
        public bool ReplacePlainTextSidecarWhenSyncedFound { get; set; } = true;

        /// <summary>Shortest wait before a track that was checked without success is checked again.</summary>
        public int MinRecheckIntervalDays { get; set; } = 3;

        /// <summary>Longest wait before such a track is checked again.</summary>
        public int MaxRecheckIntervalDays { get; set; } = 90;
    }
}
