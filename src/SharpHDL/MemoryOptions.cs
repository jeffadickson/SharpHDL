using System;

namespace SharpHDL
{
    public enum MemoryTarget { Generic, Xilinx7Series, IntelCycloneV, IntelArria10 }
    public enum MemoryStorage { Auto, Block, Distributed, Registers }

    /// <summary>Immutable inference policy. Does not change the memory's timing or collision contract.</summary>
    public sealed class MemoryOptions
    {
        public MemoryTarget Target { get; }
        public MemoryStorage Storage { get; }
        public MemoryOptions(MemoryTarget target=MemoryTarget.Generic,MemoryStorage storage=MemoryStorage.Auto)
        {
            if(!Enum.IsDefined(typeof(MemoryTarget),target)||!Enum.IsDefined(typeof(MemoryStorage),storage))
                throw new ArgumentException("Unknown memory target or storage policy.");
            if(target==MemoryTarget.Generic&&storage!=MemoryStorage.Auto)
                throw new ArgumentException("Choose an explicit target for a storage inference policy.");
            if((target==MemoryTarget.IntelCycloneV||target==MemoryTarget.IntelArria10)&&storage==MemoryStorage.Distributed)
                throw new NotSupportedException("MLAB lowering with the required old-data collision contract is not implemented. Use Auto, Block or Registers.");
            Target=target;Storage=storage;
        }
        internal string Attribute
        {
            get
            {
                if(Storage==MemoryStorage.Auto)return null;
                if(Target==MemoryTarget.Xilinx7Series)
                    return "(* ram_style = \""+(Storage==MemoryStorage.Block?"block":Storage==MemoryStorage.Distributed?"distributed":"registers")+"\" *)";
                return "(* ramstyle = \""+(Storage==MemoryStorage.Registers?"logic":Target==MemoryTarget.IntelCycloneV?"M10K":"M20K")+"\" *)";
            }
        }
        public override string ToString() { return Target+" / "+Storage+" (inference policy, not a mapped-resource guarantee)"; }
    }
}
