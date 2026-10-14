using System;
namespace SharpHDL
{
    // Only native input exhaustion terminates the runner. User exceptions propagate.
    internal sealed class NativeInputEndedException : Exception { }
    public static class NativeProcess
    {
        /// <summary>Run the original method through the .NET JIT until a finite input ends.
        /// Functional token execution, not cycle simulation. No implicit threads or queues.
        /// If a process consumes multiple inputs per iteration, exhaustion may stop a partial iteration.</summary>
        public static void RunUntilInputEnds(Action process)
        {
            if(process==null)throw new ArgumentNullException(nameof(process));
            try{process();}catch(NativeInputEndedException){}
        }
    }
}
