using System;

namespace SharpHDL.Verification;

/// <summary>Independent protocol/array oracle, shared by unit tests and RTL vector generation.</summary>
internal static class ScheduledTableCampaign
{
    // Contract: accept, synchronous read, conditional commit, then response until accepted.
    // This model uses host integers/arrays and never reads DUT registers or RAM.
    internal static int Run(int addressWidth, Action<string, int> drive,
        Func<string, int> read, Action<bool> clock)
    {
        int depth = 1 << addressWidth;
        var table = new int[depth];
        var rng = new Random(261026 + addressWidth);
        int phase = 0, key = 0, delta = 0, result = 0;
        bool update = false, pending = false;
        int offeredKey = 0, offeredDelta = 0, offeredUpdate = 0;
        int accepted = 0, completed = 0, aborted = 0;
        clock(true);
        for (int cycle = 0; cycle < 3200; cycle++)
        {
            bool reset = cycle % 151 == 150 && cycle < 3000;
            if (!pending && cycle < 3000 && rng.Next(4) != 0)
            {
                pending = true;
                // Repeated keys and both address boundaries are frequent, not accidental.
                offeredKey = cycle % 3 == 0 ? depth - 1 : cycle % 3 == 1 ? 0 : rng.Next(depth);
                offeredDelta = rng.Next(65536);
                offeredUpdate = rng.Next(2);
            }
            bool ready = cycle >= 3000 || rng.Next(4) == 0;
            drive("request_valid", pending ? 1 : 0);
            drive("request_address", offeredKey);
            drive("request_delta", offeredDelta);
            drive("request_update", offeredUpdate);
            drive("response_ready", ready ? 1 : 0);
            if (read("request_ready") != (phase == 0 ? 1 : 0) ||
                read("response_valid") != (phase == 3 ? 1 : 0))
                throw new InvalidOperationException($"Protocol timing mismatch at cycle {cycle}.");
            if (phase == 3 && read("response_data") != result)
                throw new InvalidOperationException($"Table oracle mismatch at cycle {cycle}: expected {result}.");
            if (reset)
            {
                if (phase != 0) aborted++;
                phase = 0;
                pending = false;
            }
            else if (phase == 0 && pending)
            {
                key = offeredKey; delta = offeredDelta; update = offeredUpdate != 0;
                accepted++; pending = false; phase = 1;
            }
            else if (phase == 1) phase = 2;
            else if (phase == 2)
            {
                result = update ? (table[key] + delta) & 65535 : table[key];
                if (update) table[key] = result;
                phase = 3;
            }
            else if (phase == 3 && ready) { completed++; phase = 0; }
            clock(reset);
        }
        if (phase != 0 || pending || accepted != completed + aborted || completed < 100)
            throw new InvalidOperationException($"Transaction conservation failed: {accepted}/{completed}/{aborted}.");
        return completed;
    }
}
