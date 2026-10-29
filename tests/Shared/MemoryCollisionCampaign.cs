using System;

namespace SharpHDL.Verification;

/// <summary>Independent integer-array oracle, shared between unit tests and external HDL replay generation.</summary>
public static class MemoryCollisionCampaign
{
    /// <summary>Read/write collisions, out-of-range addresses, disabled writes and reset-time write suppression.</summary>
    public static int Run(Action<string,int> drive,Action<bool> clock,Func<string,int> read)
    {
        int[] left={1,2,3},right={7,8,9};int edges=0;
        // Fixed enumeration is reproducible and covers both RAMs with unequal words.
        for(int step=0;step<96;step++)
        {
            int address=step%4,a=(step*7+3)&15,b=(step*11+5)&15,enabled=step%3==0?0:1;
            bool reset=step%13==0;drive("address",address);drive("left",a);drive("right",b);drive("write",enabled);
            int oldLeft=reset||address==3?0:left[address],oldRight=reset||address==3?0:right[address];
            clock(reset);edges++;
            if(read("out_left")!=oldLeft||read("out_right")!=oldRight)throw new InvalidOperationException("Independent RAM collision/reset oracle mismatch at edge "+step);
            if(!reset&&enabled==1&&address<3){left[address]=a;right[address]=b;}
        }
        // Probe all cells after the last reset/write sequence, exposing retained writes.
        drive("write",0);
        for(int address=0;address<3;address++)
        {
            drive("address",address);clock(false);edges++;
            if(read("out_left")!=left[address]||read("out_right")!=right[address])throw new InvalidOperationException("RAM reset retention mismatch.");
        }
        return edges;
    }
}
