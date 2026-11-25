using System;
using System.Linq;
using MediaBrowser.Controller.Entities.Audio;

namespace GNLrclib.Models
{
    /// <summary>
    /// The fields LRCLIB needs to identify a track: title, artist, album and duration.
    /// Built from tag data that is only reliable once ffprobe has populated the item.
    /// </summary>
    public sealed class TrackSignature
    {
        /// <summary>LRCLIB accepts durations of 1-3600 seconds.</summary>
        private const int MaxDurationSeconds = 3600;

        private TrackSignature(string trackName, string artistName, string albumName, int durationSeconds)
        {
            this.TrackName = trackName;
            this.ArtistName = artistName;
            this.AlbumName = albumName;
            this.DurationSeconds = durationSeconds;
        }

        public string TrackName { get; }

        public string ArtistName { get; }

        /// <summary>Empty when the track has no album tag.</summary>
        public string AlbumName { get; }

        public int DurationSeconds { get; }

        /// <summary>
        /// Returns false when the track lacks a title, an artist or a usable duration.
        /// The track artist is preferred; the album artist is the fallback.
        /// </summary>
        public static bool TryCreate(Audio item, out TrackSignature signature)
        {
            signature = null;

            string trackName = item.Name;
            string artistName = FirstNonBlank(item.Artists) ?? FirstNonBlank(item.AlbumArtists);

            if (string.IsNullOrWhiteSpace(trackName) || artistName == null || !item.RunTimeTicks.HasValue)
            {
                return false;
            }

            int durationSeconds = (int)Math.Round(TimeSpan.FromTicks(item.RunTimeTicks.Value).TotalSeconds);

            if (durationSeconds < 1 || durationSeconds > MaxDurationSeconds)
            {
                return false;
            }

            signature = new TrackSignature(
                trackName.Trim(),
                artistName.Trim(),
                item.Album == null ? string.Empty : item.Album.Trim(),
                durationSeconds);

            return true;
        }

        private static string FirstNonBlank(string[] values)
        {
            return values == null ? null : values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }
    }
}
