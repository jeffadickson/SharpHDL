using SharpHDL;
public static class CaptureInstrument
{
    // CONTROL bit0=enable, bit1=clear (level). Hold clear during reconfiguration,
    // then write CONTROL=1 to capture from index zero. RAM contents survive clear.
    public static Module Build(MemoryOptions memoryOptions=null) {
        var m=new Module("CaptureInstrument",memoryOptions);
        var axi=AxiLitePort.Slave(m,"s_axi",8);
        var bank=new AxiLiteRegisterBank(m,axi,"control");
        var control=bank.AddRegister("command",0);
        var hostRow=bank.AddRegister("host_row",4);
        var input=Stream.Input(m,"s_axis",16,8);
        var capture=StreamCapture.Build(m,input,"capture",2,16,control.Value.Slice(0,0),control.Value.Slice(1,1),
            new Expr[]{hostRow.Value.Resize(4),hostRow.Value.Resize(4)},Expr.Const(1,1));
        bank.AddStatus("count",8,capture.Count.Resize(32));
        bank.AddStatus("status",12,Expr.Concat(capture.Full,Expr.Concat(capture.Done,capture.Busy)).Resize(32));
        bank.AddStatus("bank0_low",16,capture.Memory.ReadData[0].Resize(32));
        bank.AddStatus("bank1_low",20,capture.Memory.ReadData[1].Resize(32));
        bank.Build();return m;
    }
}
