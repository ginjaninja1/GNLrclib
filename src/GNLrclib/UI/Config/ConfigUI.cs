using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Model.Attributes;
using System.Collections.Generic;
using System.ComponentModel;

namespace GNLrclib.UI.Config
{
    public class ConfigUI : EditableOptionsBase
    {
        public override string EditorTitle => "LRCLIB Lyrics - Configuration";

        public override string EditorDescription =>
            "Downloads missing synchronised lyrics (.lrc) for music tracks from LRCLIB while Emby scans and refreshes your library.";

        public CaptionItem GeneralHeading { get; set; } = new CaptionItem("General");

        [DisplayName("Enable Plugin")]
        [Description("When disabled, no lyrics are looked up or written.")]
        [AutoPostBack("updateconfig", nameof(EnablePlugin))]
        public bool EnablePlugin { get; set; } = true;

        public CaptionItem LyricsHeading { get; set; } = new CaptionItem("Lyrics Files");

        [DisplayName("Save plain-text lyrics when no synced lyrics exist")]
        [Description("Writes an unsynchronised .txt lyrics file for tracks LRCLIB only has plain lyrics for. The track is checked again later for a synced version.")]
        [AutoPostBack("updateconfig", nameof(WritePlainTextWhenNoSyncedLyrics))]
        public bool WritePlainTextWhenNoSyncedLyrics { get; set; } = true;

        [DisplayName("Replace plain-text lyrics when synced lyrics are found")]
        [Description("Moves an existing .txt lyrics file to the recycle bin once a synced .lrc file has been saved for the same track.")]
        [AutoPostBack("updateconfig", nameof(ReplacePlainTextSidecarWhenSyncedFound))]
        public bool ReplacePlainTextSidecarWhenSyncedFound { get; set; } = true;

        public CaptionItem RecheckHeading { get; set; } = new CaptionItem("Re-checking Tracks Without Lyrics");

        [DisplayName("Minimum days between checks")]
        [Description("A track checked without success is checked again after half its age in your library, but never sooner than this.")]
        [MinValue(1)]
        [MaxValue(365)]
        [AutoPostBack("updateconfig", nameof(MinRecheckIntervalDays))]
        public int MinRecheckIntervalDays { get; set; } = 3;

        [DisplayName("Maximum days between checks")]
        [Description("...and never later than this, however old the track is.")]
        [MinValue(1)]
        [MaxValue(730)]
        [AutoPostBack("updateconfig", nameof(MaxRecheckIntervalDays))]
        public int MaxRecheckIntervalDays { get; set; } = 90;

        public CaptionItem LibraryFilterHeading { get; set; } =
            new CaptionItem("Library / Path Filter");


        /// <summary>
        /// Persistent configuration data.
        /// </summary>
        [Browsable(false)]
        public List<LibraryPathFilterItem> LibraryPaths { get; set; } =
            new List<LibraryPathFilterItem>();


        /// <summary>
        /// GenericUI representation of LibraryPaths.
        /// </summary>
        public GenericItemList LibraryList { get; set; } =
            new GenericItemList();

        public GenericItemList GithubLink { get; set; } = new GenericItemList
        {
            new GenericListItem
            {
                PrimaryText = "Github repository",
                SecondaryText = "",
                Icon = IconNames.link,
                Status = ItemStatus.Succeeded,
                HyperLink = "https://github.com/ginjaninja1/GNLrclib",
                HyperLinkTargetExternal = true
            }
        };
    }
}
