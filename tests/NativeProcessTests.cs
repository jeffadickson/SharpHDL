using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SharpHDL;
static class NativeProcessTests
{
    static IEnumerable<uint> WithCleanup(Action cleanup)
    {try{yield return 1;yield return 2;}finally{cleanup();}}
    static IEnumerable<uint> Words(int n){for(int i=0;i<n;i++)yield return (uint)i;}
    public static void Run()
    {
        var words=new uint[]{65535,1,0x80000000,0x80000005};var got=new List<uint>();
        using(var input=HwStream<uint>.FromEnumerable(words))
        using(var output=HwStream<uint>.ToAction(got.Add))NativeProcess.RunUntilInputEnds(()=>PacketBenchmark.Checksum(input,output));
        if(!got.SequenceEqual(new uint[]{65535,0,0x80000000,0x80000005}))throw new Exception("Native packet values");
        using(var input=HwStream<uint>.FromEnumerable(new uint[0]))
        using(var output=HwStream<uint>.ToAction(x=>throw new Exception("Empty input emitted")))NativeProcess.RunUntilInputEnds(()=>FeedbackExamples.RunningSum(input,output));
        bool propagated=false;
        try{using(var input=HwStream<uint>.FromEnumerable(new uint[]{1}))using(var output=HwStream<uint>.ToAction(x=>throw new IOException("consumer")))NativeProcess.RunUntilInputEnds(()=>FeedbackExamples.RunningSum(input,output));}
        catch(IOException){propagated=true;}if(!propagated)throw new Exception("Consumer exception swallowed");
        bool cleaned=false;
        using(var resource=HwStream<uint>.FromEnumerable(WithCleanup(()=>cleaned=true)))resource.Read();
        if(!cleaned)throw new Exception("Enumerator not disposed");
        var disposed=HwStream<uint>.FromEnumerable(words);disposed.Dispose();bool rejected=false;
        try{disposed.Read();}catch(ObjectDisposedException){rejected=true;}if(!rejected)throw new Exception("Disposed input");
        // Warm the same original C# method; report observed throughput, never gate on wall time.
        Measure(10000);double seconds=Measure(1000000);
        var cells=new uint[16];var memoryClock=Stopwatch.StartNew();
        for(uint i=0;i<1000000;i++)NativeMemory.Bump(cells,i&15,1);
        memoryClock.Stop();if(cells.Any(x=>x!=62500))throw new Exception("Native array loop");
        Console.WriteLine("PASS native array memory: 1000000 original-method calls in "+memoryClock.Elapsed.TotalSeconds.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+" s");
        Console.WriteLine("PASS native .NET streams: finite/empty inputs, packet boundaries, overflow, callback errors, disposal; 1000000 running-sum tokens in "+seconds.ToString("F6",System.Globalization.CultureInfo.InvariantCulture)+" s (no synthesis or graph simulation)");
    }
    static double Measure(int n)
    {
        uint last=0;int count=0;var clock=Stopwatch.StartNew();
        using(var input=HwStream<uint>.FromEnumerable(Words(n)))
        using(var output=HwStream<uint>.ToAction(x=>{last=x;count++;}))NativeProcess.RunUntilInputEnds(()=>FeedbackExamples.RunningSum(input,output));
        clock.Stop();if(count!=n||last!=unchecked((uint)(7+(ulong)n*(ulong)(n-1)/2)))throw new Exception("Native large recurrence");
        return clock.Elapsed.TotalSeconds;
    }
}
