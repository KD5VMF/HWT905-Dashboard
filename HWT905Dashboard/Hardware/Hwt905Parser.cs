using System;
using System.Collections.Generic;

namespace HWT905Dashboard.Hardware;

public sealed class Hwt905Parser
{
    private const int FrameLength = 11;
    private const int MaxBufferBytes = 8192;
    private const int KeepAfterOverflowBytes = 1024;

    private readonly List<byte> _buffer = new(4096);

    public event Action<Hwt905Frame> FrameReceived;
    public event Action<byte[]> BadFrameReceived;

    public long FramesOk { get; private set; }
    public long FramesBad { get; private set; }
    public long BytesDiscarded { get; private set; }

    public void Reset()
    {
        _buffer.Clear();
        FramesOk = 0;
        FramesBad = 0;
        BytesDiscarded = 0;
    }

    public void Push(byte[] data, int count)
    {
        if (data == null || count <= 0) return;

        count = Math.Min(count, data.Length);
        for (int i = 0; i < count; i++)
            _buffer.Add(data[i]);

        // Keep memory bounded if the cable is noisy or the wrong baud is selected.
        if (_buffer.Count > MaxBufferBytes)
        {
            int remove = _buffer.Count - KeepAfterOverflowBytes;
            _buffer.RemoveRange(0, remove);
            BytesDiscarded += remove;
        }

        ParseAvailable();
    }

    private void ParseAvailable()
    {
        // REV7: parse with a moving offset and compact once at the end.
        // This avoids thousands of RemoveAt/RemoveRange front-shifts per second.
        int pos = 0;
        int count = _buffer.Count;

        while (count - pos >= FrameLength)
        {
            int sync = IndexOfSync(pos, count);
            if (sync < 0)
            {
                BytesDiscarded += count - pos;
                pos = count;
                break;
            }

            if (sync > pos)
            {
                BytesDiscarded += sync - pos;
                pos = sync;
            }

            if (count - pos < FrameLength)
                break;

            byte type = _buffer[pos + 1];
            if (!IsLikelyType(type))
            {
                // False sync byte. Drop one byte only and search again.
                pos++;
                BytesDiscarded++;
                FramesBad++;
                continue;
            }

            byte sum = 0;
            for (int i = 0; i < 10; i++)
                sum += _buffer[pos + i];

            byte expected = _buffer[pos + 10];
            var frameBytes = new byte[FrameLength];
            _buffer.CopyTo(pos, frameBytes, 0, FrameLength);

            if (sum == expected)
            {
                pos += FrameLength;
                FramesOk++;
                FrameReceived?.Invoke(new Hwt905Frame(frameBytes));
            }
            else
            {
                // Error recovery: only drop the leading sync byte.
                pos++;
                FramesBad++;
                BadFrameReceived?.Invoke(frameBytes);
            }
        }

        if (pos > 0)
        {
            if (pos >= _buffer.Count)
                _buffer.Clear();
            else
                _buffer.RemoveRange(0, pos);
        }
    }

    private int IndexOfSync(int start, int count)
    {
        for (int i = start; i < count; i++)
        {
            if (_buffer[i] == 0x55) return i;
        }
        return -1;
    }

    private static bool IsLikelyType(byte type)
    {
        // WIT standard protocol: time, accel, gyro, angle, mag, port, pressure/height,
        // GPS, quaternion, and register response. Accept adjacent WIT extensions too.
        return (type >= 0x50 && type <= 0x5F) || type == 0x60 || type == 0x61;
    }
}
