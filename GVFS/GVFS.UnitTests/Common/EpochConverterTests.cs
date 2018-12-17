using GVFS.Common;
using GVFS.Tests.Should;
using NUnit.Framework;
using System;

namespace GVFS.UnitTests.Common
{
    [TestFixture]
    public class EpochConverterTests
    {
        [TestCase]
        public void DateToEpochToDate()
        {
            DateTime time = new DateTime(2018, 12, 18, 8, 12, 13, DateTimeKind.Utc);
            DateTime converted = EpochConverter.FromUnixEpochSeconds(EpochConverter.ToUnixEpochSeconds(time));

            time.ShouldEqual(converted);
        }

        [TestCase]
        public void EpochToDateToEpoch()
        {
            long time = 15237623489;
            long converted = EpochConverter.ToUnixEpochSeconds(EpochConverter.FromUnixEpochSeconds(time));

            time.ShouldEqual(converted);
        }

        [TestCase]
        public void FixedDates()
        {
            DateTime[] times = new DateTime[]
            {
                new DateTime(2018, 12, 13, 20, 53, 30, DateTimeKind.Utc),
                new DateTime(2035, 1, 3, 5, 0, 59, DateTimeKind.Utc),
                new DateTime(1989, 12, 31, 23, 59, 59, DateTimeKind.Utc)
            };
            long[] epochs = new long[]
            {
                1544734410,
                2051413259,
                631151999
            };

            for (int i = 0; i < times.Length; i++)
            {
                long epoch = EpochConverter.ToUnixEpochSeconds(times[i]);
                epoch.ShouldEqual(epochs[i]);

                DateTime time = EpochConverter.FromUnixEpochSeconds(epochs[i]);
                time.ShouldEqual(times[i]);
            }
        }
    }
}
