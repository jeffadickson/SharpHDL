using SharpHDL;

public static class FeedbackExamples
{
    [HardwareProcess]
    public static void RunningSum(HwStream<uint> input,HwStream<uint> output)
    {
        uint sum=7;
        while(true)
        {
            sum=unchecked(sum+input.Read());
            output.Write(sum);
        }
    }
    [HardwareProcess]
    public static void ConditionalChecksum(HwStream<uint> input,HwStream<uint> output)
    {
        uint checksum=0x1234,count=0;
        while(true)
        {
            uint word=input.Read();
            if(word==0){checksum=0;count=0;}
            else
            {
                checksum=unchecked((checksum*3)^word);
                count=unchecked(count+1);
            }
            output.Write(unchecked(checksum+count));
        }
    }
    [HardwareProcess]
    public static void PreviousValue(HwStream<uint> input,HwStream<uint> output)
    {
        uint previous=42;
        while(true)
        {
            uint word=input.Read();
            output.Write(previous);
            previous=word;
        }
    }
}
