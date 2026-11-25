using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Model.Entities;

namespace GNLrclib.Models
{
    /// <summary>
    /// What lyrics a track already has, derived from its media streams. Emby exposes embedded lyrics as a
    /// non-external subtitle stream and sidecar lyrics files as external subtitle streams.
    /// </summary>
    public sealed class LyricsInventory
    {
        private static readonly string[] NoPaths = new string[0];

        public bool HasEmbeddedLyrics { get; private set; }

        public bool HasSyncedSidecar { get; private set; }

        public IReadOnlyList<string> PlainTextSidecarPaths { get; private set; } = NoPaths;

        public string Description => string.Format("embedded={0}, syncedSidecar={1}, plainTextSidecars={2}", this.HasEmbeddedLyrics, this.HasSyncedSidecar, this.PlainTextSidecarPaths.Count);

        public static LyricsInventory FromStreams(IEnumerable<MediaStream> streams)
        {
            var inventory = new LyricsInventory();

            if (streams == null)
            {
                return inventory;
            }

            List<string> plainTextPaths = null;

            foreach (var stream in streams)
            {
                if (stream.Type != MediaStreamType.Subtitle)
                {
                    continue;
                }

                if (!stream.IsExternal)
                {
                    inventory.HasEmbeddedLyrics = true;
                    continue;
                }

                var extension = Path.GetExtension(stream.Path);

                if (string.Equals(extension, ".lrc", StringComparison.OrdinalIgnoreCase))
                {
                    inventory.HasSyncedSidecar = true;
                }
                else if (string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase))
                {
                    if (plainTextPaths == null)
                    {
                        plainTextPaths = new List<string>();
                    }

                    plainTextPaths.Add(stream.Path);
                }
            }

            if (plainTextPaths != null)
            {
                inventory.PlainTextSidecarPaths = plainTextPaths;
            }

            return inventory;
        }
    }
}
