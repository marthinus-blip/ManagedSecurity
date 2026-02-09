using System;
using System.Collections.Generic;

namespace ManagedSecurity.Sentinel;

public enum NalUnitType : byte
{
    Unspecified = 0,
    CodedSliceNonIdr = 1,
    CodedSliceDataPartitionA = 2,
    CodedSliceDataPartitionB = 3,
    CodedSliceDataPartitionC = 4,
    Idr = 5, // I-Frame
    Sei = 6,
    Sps = 7,
    Pps = 8,
    AccessUnitDelimiter = 9,
    EndOfSequence = 10,
    EndOfStream = 11,
    FillerData = 12
}

public static class NalUnitScanner
{
    /// <summary>
    /// Searches for H.264 Start Codes and identifies I-Frames (IDR).
    /// </summary>
    public static List<(int Offset, NalUnitType Type)> Scan(ReadOnlySpan<byte> data)
    {
        var results = new List<(int Offset, NalUnitType Type)>();
        for (int i = 0; i < data.Length - 4; i++)
        {
            int startCodeLen = 0;
            
            // Check for 0x00000001
            if (data[i] == 0x00 && data[i+1] == 0x00 && data[i+2] == 0x00 && data[i+3] == 0x01)
            {
                startCodeLen = 4;
            }
            // Check for 0x000001
            else if (data[i] == 0x00 && data[i+1] == 0x00 && data[i+2] == 0x01)
            {
                startCodeLen = 3;
            }

            if (startCodeLen > 0 && (i + startCodeLen) < data.Length)
            {
                // The byte after the start code contains the NAL type
                byte nalHeader = data[i + startCodeLen];
                int type = nalHeader & 0x1F;
                
                results.Add((i, (NalUnitType)type));
                
                // Skip the start code to avoid double detection
                i += startCodeLen;
            }
        }
        return results;
    }


    public static bool IsSyncPoint(NalUnitType type)
    {
        // IDR units are the primary sync points. 
        // SPS/PPS are often prefixed to IDR units in streams and are also critical for decoders.
        return type == NalUnitType.Idr || type == NalUnitType.Sps || type == NalUnitType.Pps;
    }
}
