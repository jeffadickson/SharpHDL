using SharpHDL;
public static class NativeMemory
{
    // The exact method runs directly on a PC with uint[] or is lowered by
    // BuildSequential to owned synchronous RAM plus a controller/read wait.
    public static uint Bump(uint[] cells,uint index,uint delta)
    {
        uint value=unchecked(cells[index]+delta);
        cells[index]=value;
        return value;
    }
}
