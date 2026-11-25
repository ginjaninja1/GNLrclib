using GNLrclib.Configuration;
using System;

namespace GNLrclib.Services
{
    /// <summary>
    /// Decides when a track that was already checked without getting synced lyrics is worth checking again.
    /// The wait is half the track's age in the library, clamped to the configured minimum and maximum:
    /// recently added tracks are likely to gain lyrics on LRCLIB soon, long-standing ones rarely do.
    /// </summary>
    public sealed class RecheckPolicy
    {
        private const double AgeFraction = 0.5;

        private readonly TimeSpan minimumInterval;
        private readonly TimeSpan maximumInterval;

        public RecheckPolicy(TimeSpan minimumInterval, TimeSpan maximumInterval)
        {
            this.minimumInterval = minimumInterval;
            this.maximumInterval = maximumInterval < minimumInterval ? minimumInterval : maximumInterval;
        }

        public static RecheckPolicy FromSettings(PluginConfiguration settings)
        {
            return new RecheckPolicy(
                TimeSpan.FromDays(Math.Max(0, settings.MinRecheckIntervalDays)),
                TimeSpan.FromDays(Math.Max(0, settings.MaxRecheckIntervalDays)));
        }

        public bool IsDue(DateTime itemDateCreatedUtc, DateTime lastCheckedUtc, DateTime nowUtc)
        {
            var itemAge = nowUtc - itemDateCreatedUtc;

            if (itemAge < TimeSpan.Zero)
            {
                itemAge = TimeSpan.Zero;
            }

            var interval = TimeSpan.FromTicks((long)(itemAge.Ticks * AgeFraction));

            if (interval < this.minimumInterval)
            {
                interval = this.minimumInterval;
            }
            else if (interval > this.maximumInterval)
            {
                interval = this.maximumInterval;
            }

            return nowUtc - lastCheckedUtc >= interval;
        }
    }
}
