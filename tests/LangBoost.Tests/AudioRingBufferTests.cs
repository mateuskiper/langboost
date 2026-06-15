using LangBoost;
using Xunit;

namespace LangBoost.Tests;

/// <summary>
/// Unit tests for the thread-safe circular buffer that keeps the last N bytes of audio.
/// Pure logic — no audio hardware involved.
/// </summary>
public class AudioRingBufferTests
{
    private static byte[] Seq(int start, int count)
    {
        var data = new byte[count];
        for (int i = 0; i < count; i++) data[i] = (byte)((start + i) & 0xFF);
        return data;
    }

    [Fact]
    public void Snapshot_EmptyBuffer_ReturnsEmpty()
    {
        var ring = new AudioRingBuffer(10);
        Assert.Empty(ring.Snapshot());
    }

    [Fact]
    public void Snapshot_BeforeFilled_ReturnsOnlyWrittenBytesInOrder()
    {
        var ring = new AudioRingBuffer(10);
        var data = Seq(1, 4); // 1,2,3,4
        ring.Write(data, 0, data.Length);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ring.Snapshot());
    }

    [Fact]
    public void Write_ExactCapacity_SnapshotEqualsInput()
    {
        var ring = new AudioRingBuffer(5);
        var data = Seq(10, 5); // 10..14
        ring.Write(data, 0, data.Length);

        Assert.Equal(data, ring.Snapshot());
    }

    [Fact]
    public void Write_BlockLargerThanCapacity_KeepsOnlyLastBytes()
    {
        var ring = new AudioRingBuffer(4);
        var data = Seq(0, 10); // 0..9 -> last 4 are 6,7,8,9
        ring.Write(data, 0, data.Length);

        Assert.Equal(new byte[] { 6, 7, 8, 9 }, ring.Snapshot());
    }

    [Fact]
    public void Write_Wraparound_SnapshotReturnsChronologicalOrder()
    {
        var ring = new AudioRingBuffer(5);
        ring.Write(Seq(1, 5), 0, 5); // fills: 1,2,3,4,5
        ring.Write(Seq(6, 3), 0, 3); // wraps: oldest 4,5 then 6,7,8

        Assert.Equal(new byte[] { 4, 5, 6, 7, 8 }, ring.Snapshot());
    }

    [Fact]
    public void Write_ManySmallWrites_AccumulateThenWrap()
    {
        var ring = new AudioRingBuffer(4);
        for (byte b = 1; b <= 6; b++)
            ring.Write(new[] { b }, 0, 1); // 1,2,3,4,5,6 -> keep last 4

        Assert.Equal(new byte[] { 3, 4, 5, 6 }, ring.Snapshot());
    }

    [Fact]
    public void Write_RespectsOffsetAndCount()
    {
        var ring = new AudioRingBuffer(10);
        var data = new byte[] { 99, 99, 1, 2, 3, 99 };
        ring.Write(data, 2, 3); // only 1,2,3

        Assert.Equal(new byte[] { 1, 2, 3 }, ring.Snapshot());
    }

    [Fact]
    public void Write_FillExactlyToBoundary_ThenContinue()
    {
        var ring = new AudioRingBuffer(4);
        ring.Write(Seq(1, 4), 0, 4); // exactly full: writePos wraps to 0, filled=true
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, ring.Snapshot());

        ring.Write(Seq(5, 2), 0, 2); // 5,6 -> keep 3,4,5,6
        Assert.Equal(new byte[] { 3, 4, 5, 6 }, ring.Snapshot());
    }
}
