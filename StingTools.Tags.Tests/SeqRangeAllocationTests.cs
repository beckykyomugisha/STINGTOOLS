using System;
using System.Collections.Generic;
using StingTools.Core;
using Xunit;

namespace StingTools.Tags.Tests
{
    /// <summary>
    /// DSCH-39 — SEQ allocation honours SEQ_RANGE_ALLOCATION. The range is keyed by
    /// DISC and applies to every counter of that DISC (counters are keyed DISC/SYS/LVL
    /// [/ZONE/LOC]); a new counter starts at the range minimum, and the number after
    /// the maximum is refused (RangeExhausted) — never wrapped, never written. With no
    /// range, allocation is unchanged (1 up to the pad capacity).
    /// </summary>
    public class SeqRangeAllocationTests
    {
        private const string Body = "E-BLD1-Z01-L01-LV-PWR-DB-";
        private const string Key = "E_LV_L01";
        private static readonly (int Min, int Max)? ERange = (10000, 19999);

        private static SeqResult Next(Dictionary<string, int> c, (int Min, int Max)? range,
                                      HashSet<string> existing = null, int pad = 5, SeqBlockReservation res = null)
            => SeqAssigner.AssignNext(Key, c, Body, "", SeqScheme.Numeric, pad, "", 100, existing, res, range);

        [Fact]
        public void NewCounter_StartsAtTheRangeMinimum()
        {
            var c = new Dictionary<string, int>();
            var r = Next(c, ERange);
            Assert.True(r.Success);
            Assert.Equal("10000", r.Seq);
            Assert.Equal(10000, c[Key]);
            Assert.Equal("10001", Next(c, ERange).Seq);
        }

        [Fact]
        public void CounterBelowTheMinimum_JumpsToTheMinimum()
        {
            // Legacy numbers 1..50 were held before the range was configured.
            var c = new Dictionary<string, int> { [Key] = 50 };
            Assert.Equal("10000", Next(c, ERange).Seq);
        }

        [Fact]
        public void CounterInsideTheRange_Continues()
        {
            var c = new Dictionary<string, int> { [Key] = 12345 };
            Assert.Equal("12346", Next(c, ERange).Seq);
        }

        [Fact]
        public void NextAfterTheMaximum_IsRefused_AndTheCounterIsRestored()
        {
            var c = new Dictionary<string, int> { [Key] = 19999 };
            var r = Next(c, ERange);
            Assert.False(r.Success);
            Assert.Equal(SeqFailureReason.RangeExhausted, r.Failure);
            Assert.Null(r.Seq);
            Assert.Equal(19999, c[Key]);
        }

        [Fact]
        public void RefusedNewCounter_IsRestoredToItsPreAllocationValue()
        {
            // Min == Max and the only number is taken: refused, counter back to 0.
            var c = new Dictionary<string, int>();
            var existing = new HashSet<string>(StringComparer.Ordinal) { Body + "10000" };
            var r = Next(c, (10000, 10000), existing);
            Assert.False(r.Success);
            Assert.Equal(SeqFailureReason.RangeExhausted, r.Failure);
            Assert.Equal(0, c[Key]);
        }

        [Fact]
        public void CollisionPastTheMaximum_IsRefused_NotWrapped()
        {
            var c = new Dictionary<string, int> { [Key] = 19997 };
            var existing = new HashSet<string>(StringComparer.Ordinal) { Body + "19998", Body + "19999" };
            var r = Next(c, ERange, existing);
            Assert.False(r.Success);
            Assert.Equal(SeqFailureReason.RangeExhausted, r.Failure);
            Assert.Equal(19997, c[Key]);
        }

        [Fact]
        public void RangeAbovePadCapacity_StillReportsPadOverflow()
        {
            // The pad (4 digits) binds before the range maximum: the old overflow reason.
            var c = new Dictionary<string, int> { [Key] = 9999 };
            var r = Next(c, (1, 99999), pad: 4);
            Assert.False(r.Success);
            Assert.Equal(SeqFailureReason.InitialOverflow, r.Failure);
        }

        [Fact]
        public void ReservedNumberOutsideTheRange_IsRefused()
        {
            var res = new SeqBlockReservation();
            res.Add(Key, 41, 43);
            var c = new Dictionary<string, int>();
            var r = Next(c, ERange, res: res);
            Assert.False(r.Success);
            Assert.Equal(SeqFailureReason.ReservationOutsideRange, r.Failure);
            Assert.Equal(0, c[Key]);
        }

        [Fact]
        public void ReservedNumberInsideTheRange_IsTaken()
        {
            var res = new SeqBlockReservation();
            res.Add(Key, 10500, 10502);
            var c = new Dictionary<string, int>();
            Assert.Equal("10500", Next(c, ERange, res: res).Seq);
        }

        [Fact]
        public void NoRange_BehaviourIsUnchanged()
        {
            var c = new Dictionary<string, int>();
            Assert.Equal("00001", Next(c, null).Seq);
            var full = new Dictionary<string, int> { [Key] = 99999 };
            var r = Next(full, null);
            Assert.Equal(SeqFailureReason.InitialOverflow, r.Failure);
        }

        [Theory]
        [InlineData(5, false)]
        [InlineData(10000, true)]
        [InlineData(19999, true)]
        [InlineData(9999, false)]
        [InlineData(20000, false)]
        public void InRange(int n, bool expectedWithRange)
        {
            Assert.Equal(expectedWithRange, SeqAssigner.InRange(n, ERange));
            Assert.True(SeqAssigner.InRange(n, null));
        }

        [Theory]
        [InlineData(0, 9999)]
        [InlineData(9999, 9999)]
        [InlineData(10000, 10000)]
        [InlineData(15000, 15000)]
        public void FloorForRange(int counter, int expected)
        {
            Assert.Equal(expected, SeqAssigner.FloorForRange(counter, ERange));
            Assert.Equal(counter, SeqAssigner.FloorForRange(counter, null));
        }
    }
}
