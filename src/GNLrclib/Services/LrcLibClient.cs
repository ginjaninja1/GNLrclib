using GNLrclib.Configuration;
using GNLrclib.Models;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Serialization;
using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace GNLrclib.Services
{
    /// <summary>
    /// Minimal LRCLIB client for the exact-signature lookup (GET /api/get).
    /// Requests are strictly sequential with a minimum gap, and 429 / 401 responses open a circuit breaker
    /// so that a throttled or misconfigured client stops sending requests.
    /// </summary>
    public sealed class LrcLibClient
    {
        private const string LookupUrl = "https://lrclib.net/api/get";

        // Fixed LRCLIB client id. Keep this here so it is easy to update.
        private const string LrcLibAppId = "60ZiUAIPottC2C1q8J0_Ea-1DMP7LnZsC_SiSmTa4_g";

        /// <summary>LRCLIB recommends 200-500 ms between requests.</summary>
        private static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromMilliseconds(400);

        private static readonly TimeSpan DefaultRateLimitPause = TimeSpan.FromSeconds(60);

        private static readonly TimeSpan RejectedAppIdPause = TimeSpan.FromHours(1);

        private readonly IHttpClient httpClient;
        private readonly IJsonSerializer jsonSerializer;
        private readonly ILogger logger;

        private readonly SemaphoreSlim requestGate = new SemaphoreSlim(1, 1);

        // Only used while holding requestGate, so Random's lack of thread safety does not matter.
        private readonly Random jitterSource = new Random();

        private DateTime nextRequestAllowedUtc = DateTime.MinValue;
        private DateTime blockedUntilUtc = DateTime.MinValue;

        public LrcLibClient(IHttpClient httpClient, IJsonSerializer jsonSerializer, ILogger logger)
        {
            this.httpClient = httpClient;
            this.jsonSerializer = jsonSerializer;
            this.logger = logger;
        }

        public async Task<LyricsLookupResult> FindAsync(
            TrackSignature signature,
            PluginConfiguration settings,
            CancellationToken cancellationToken)
        {
            await this.requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var now = DateTime.UtcNow;

                if (now < this.blockedUntilUtc)
                {
                    this.logger.Debug("LRCLIB lookup not sent: circuit breaker is open for another {0:0} seconds", (this.blockedUntilUtc - now).TotalSeconds);
                    return LyricsLookupResult.Unavailable;
                }

                if (now < this.nextRequestAllowedUtc)
                {
                    this.logger.Debug("Throttling LRCLIB lookup by {0:0} ms", (this.nextRequestAllowedUtc - now).TotalMilliseconds);
                    await Task.Delay(this.nextRequestAllowedUtc - now, cancellationToken).ConfigureAwait(false);
                }

                this.nextRequestAllowedUtc = DateTime.UtcNow + MinimumRequestInterval;

                return await this.SendAsync(signature, settings, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                this.requestGate.Release();
            }
        }

        private static string BuildUrl(TrackSignature signature)
        {
            var url = LookupUrl
                + "?track_name=" + Uri.EscapeDataString(signature.TrackName)
                + "&artist_name=" + Uri.EscapeDataString(signature.ArtistName)
                + "&duration=" + signature.DurationSeconds.ToString(CultureInfo.InvariantCulture);

            if (signature.AlbumName.Length > 0)
            {
                url += "&album_name=" + Uri.EscapeDataString(signature.AlbumName);
            }

            return url;
        }

        private static string BuildUserAgent()
        {
            var version = typeof(LrcLibClient).Assembly.GetName().Version;
            var versionString = version?.ToString() ?? "0.0.0.1";
            return $"GinjaninjaEmbyLrclib v{versionString} (https://github.com/ginjaninja1/GNLrclib)";
        }

        private async Task<LyricsLookupResult> SendAsync(
            TrackSignature signature,
            PluginConfiguration settings,
            CancellationToken cancellationToken)
        {
            var options = new HttpRequestOptions
            {
                Url = BuildUrl(signature),
                CancellationToken = cancellationToken,
                UserAgent = BuildUserAgent(),
                AcceptHeader = "application/json",
                ThrowOnErrorResponse = false,
                LogRequestAsDebug = true,
            };

            this.logger.Debug("Sending LRCLIB exact lookup: track='{0}', artist='{1}', album='{2}', duration={3}s", signature.TrackName, signature.ArtistName, signature.AlbumName, signature.DurationSeconds);

            options.RequestHeaders["Lrclib-App-Id"] = LrcLibAppId;

            try
            {
                using (var response = await this.httpClient.GetResponse(options).ConfigureAwait(false))
                {
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var parsed = this.Parse(response.Content);
                        this.logger.Debug("LRCLIB HTTP 200 parsed as {0}", parsed.Outcome);
                        return parsed;
                    }

                    this.logger.Debug("LRCLIB returned HTTP {0}", (int)response.StatusCode);

                    string retryAfter = null;

                    if (response.Headers != null)
                    {
                        response.Headers.TryGetValue("Retry-After", out retryAfter);
                    }

                    return this.ClassifyFailure(response.StatusCode, retryAfter);
                }
            }
            catch (HttpException ex)
            {
                if (ex.StatusCode.HasValue)
                {
                    this.logger.Debug("LRCLIB request raised HTTP {0}", (int)ex.StatusCode.Value);
                    return this.ClassifyFailure(ex.StatusCode.Value, null);
                }

                this.logger.Warn("LRCLIB request failed ({0}): {1}", ex.IsTimedOut ? "timeout" : "network", ex.Message);
                return LyricsLookupResult.Unavailable;
            }
        }

        private LyricsLookupResult Parse(System.IO.Stream content)
        {
            var track = this.jsonSerializer.DeserializeFromStream<LrcLibTrackResponse>(content);

            if (track == null)
            {
                return LyricsLookupResult.NotFound;
            }

            if (!string.IsNullOrWhiteSpace(track.SyncedLyrics))
            {
                return LyricsLookupResult.FromSynced(track.SyncedLyrics, track.PlainLyrics);
            }

            if (track.Instrumental)
            {
                return LyricsLookupResult.Instrumental;
            }

            return string.IsNullOrWhiteSpace(track.PlainLyrics)
                ? LyricsLookupResult.NotFound
                : LyricsLookupResult.FromPlainText(track.PlainLyrics);
        }

        private LyricsLookupResult ClassifyFailure(HttpStatusCode statusCode, string retryAfterHeader)
        {
            switch ((int)statusCode)
            {
                case 404:
                    return LyricsLookupResult.NotFound;

                case 429:
                    this.Block(this.ParseRetryAfter(retryAfterHeader), "rate limited (HTTP 429)");
                    return LyricsLookupResult.Unavailable;

                case 401:
                    this.Block(RejectedAppIdPause, "rejected the Lrclib-App-Id header (HTTP 401); check the configured LRCLIB App ID");
                    return LyricsLookupResult.Unavailable;

                default:
                    this.logger.Warn("LRCLIB returned unexpected HTTP {0}", (int)statusCode);
                    return LyricsLookupResult.Unavailable;
            }
        }

        private void Block(TimeSpan pause, string reason)
        {
            this.blockedUntilUtc = DateTime.UtcNow + pause;
            this.logger.Warn("LRCLIB {0}. Pausing lookups for {1:0} seconds.", reason, pause.TotalSeconds);
        }

        /// <summary>Honours Retry-After and adds up to 30% jitter, as LRCLIB requires.</summary>
        private TimeSpan ParseRetryAfter(string retryAfterHeader)
        {
            var pause = DefaultRateLimitPause;

            if (int.TryParse(retryAfterHeader, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds > 0)
            {
                pause = TimeSpan.FromSeconds(seconds);
            }

            return pause + TimeSpan.FromSeconds(pause.TotalSeconds * 0.3 * this.jitterSource.NextDouble());
        }
    }
}
