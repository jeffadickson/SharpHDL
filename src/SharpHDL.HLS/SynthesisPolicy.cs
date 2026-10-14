namespace SharpHDL.HLS;

/// <summary>Hardware scheduling intent shared by future CIL and structural lowering.</summary>
public sealed record SynthesisPolicy(int LogicLevelsPerStage = 1, bool EnablePipelineScheduling = true)
{
    public SynthesisPolicy Validate()
    {
        if (LogicLevelsPerStage < 1) throw new System.ArgumentOutOfRangeException(nameof(LogicLevelsPerStage));
        return this;
    }
}
