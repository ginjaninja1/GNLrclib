using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GNLrclib.Services
{
    /// <summary>
    /// Writes and removes lyrics sidecar files next to a track. Every file operation is bracketed with
    /// ILibraryMonitor so the library watcher does not react to the plugin's own changes. A new sidecar is
    /// picked up by Emby's normal probe on a following scan, because ffprobe's change monitor compares the
    /// sidecar files on disk with the ones stored for the item.
    /// </summary>
    public sealed class LyricsSidecarStore
    {
        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        private readonly IFileSystem fileSystem;
        private readonly ILibraryMonitor libraryMonitor;
        private readonly ILogger logger;

        public LyricsSidecarStore(IFileSystem fileSystem, ILibraryMonitor libraryMonitor, ILogger logger)
        {
            this.fileSystem = fileSystem;
            this.libraryMonitor = libraryMonitor;
            this.logger = logger;
        }

        /// <summary>
        /// Writes "{track file name}{extension}" beside the track, falling back to the item's internal metadata
        /// folder when the media folder is not writable (Emby searches both). An existing file is never overwritten.
        /// Returns the path written, or null when no location could be written.
        /// </summary>
        public string Write(Audio item, string extension, string content)
        {
            var fileName = Path.GetFileNameWithoutExtension(item.Path) + extension;

            var candidatePaths = new[]
            {
                Path.Combine(Path.GetDirectoryName(item.Path), fileName),
                Path.Combine(item.GetInternalMetadataPath(), fileName)
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    this.logger.Debug("Lyrics sidecar already exists, not overwriting: {0}", path);
                    return path;
                }

                this.libraryMonitor.ReportFileSystemChangeBeginning(path);

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, content, Utf8WithoutBom);
                    return path;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    this.logger.Warn("Could not write lyrics sidecar {0}: {1}", path, ex.Message);
                    this.TryDeletePartialFile(path);
                }
                finally
                {
                    this.libraryMonitor.ReportFileSystemChangeComplete(path, false);
                }
            }

            return null;
        }

        /// <summary>Moves sidecar files to the recycle bin where the platform supports it.</summary>
        public void Delete(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                this.libraryMonitor.ReportFileSystemChangeBeginning(path);

                try
                {
                    this.fileSystem.DeleteFile(path, true);
                    this.logger.Info("Removed plain-text lyrics sidecar replaced by synced lyrics: {0}", path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    this.logger.Warn("Could not remove lyrics sidecar {0}: {1}", path, ex.Message);
                }
                finally
                {
                    this.libraryMonitor.ReportFileSystemChangeComplete(path, false);
                }
            }
        }

        private void TryDeletePartialFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                this.logger.Debug("Could not clean up partial lyrics file {0}: {1}", path, ex.Message);
            }
        }
    }
}
