namespace SharpHDL.FSharpExamples

open System
open System.Collections.Generic
open System.Numerics
open System.Text
open SharpHDL

module Program =
    let mutable checks = 0
    let check condition message =
        checks <- checks + 1
        if not condition then failwith message

    type Beat = { Data: bigint; Keep: bigint; Last: bool; User: bigint }

    /// Record the actual F#-constructed design's simulation and generate a
    /// self-checking Verilog testbench for exactly the same input/clock trace.
    type Trace(m: Module, directory: string) =
        let sim = m.Simulate()
        let tb = StringBuilder()
        let mutable cycle = 0
        let line (text: string) = tb.AppendLine(text) |> ignore
        do
            sim.Tick(true)
            System.IO.Directory.CreateDirectory(directory) |> ignore
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, m.Name + ".v"), m.Verilog())
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, m.Name + ".graph.txt"), m.DumpGraph())
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, m.Name + "_axi.v"), Axi4Stream.Wrapper(m))
            line "`timescale 1ns/1ps"
            line "module tb; reg clk=0; reg rst=1;"
            for p in m.Ports do
                line (sprintf "%s [%d:0] %s%s;" (if p.IsInput then "reg" else "wire") (p.Width-1) p.Name (if p.IsInput then "=0" else ""))
            tb.Append(sprintf "h_%s dut(.clk(clk),.rst(rst)" m.Name) |> ignore
            for p in m.Ports do tb.Append(sprintf ",.s_%s(%s)" p.Name p.Name) |> ignore
            line "); initial begin #1; clk=1; #1; clk=0; rst=0;"
        member _.Sim = sim
        member _.Set(s: Signal, value: bigint) =
            sim.Set(s, value)
            line (sprintf "%s=%d'd%s;" s.Name s.Width (string value))
        member _.On(e: Expr) = sim.Read(e) <> 0I
        member this.Source(s: Stream, beat: Beat option) =
            this.Set(s.Valid, if beat.IsSome then 1I else 0I)
            match beat with
            | None -> ()
            | Some b ->
                this.Set(s.Data, b.Data); this.Set(s.Keep, b.Keep)
                this.Set(s.Last, if b.Last then 1I else 0I); this.Set(s.User, b.User)
        member this.ReadBeat(s: Stream) =
            { Data=sim.Read(s.Data); Keep=sim.Read(s.Keep); Last=this.On(s.Last); User=sim.Read(s.User) }
        member this.Check(s: Stream, expected: Beat) = check (this.ReadBeat(s) = expected) (sprintf "%s cycle %d payload/sidebands" m.Name cycle)
        member _.Step() =
            line "#1;"
            for p in m.Ports |> Seq.filter (fun p -> p.IsOutput) do
                line (sprintf "if (%s !== %d'd%s) $fatal(1, \"%s cycle %d port %s\");" p.Name p.Width (string (sim.Read(p))) m.Name cycle p.Name)
            line "clk=1; #1; clk=0;"
            sim.Tick()
            cycle <- cycle + 1
        member _.Save() =
            line (sprintf "$display(\"PASS HDL %s\"); $finish; end endmodule" m.Name)
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, m.Name + "_tb.v"), tb.ToString())

    let stallCheck (t: Trace) (output: Stream) previous =
        match previous with
        | Some beat -> check (t.On(output.Valid)) "Valid dropped during stall"; t.Check(output, beat)
        | None -> ()
        if t.On(output.Valid) && not (t.On(output.Ready)) then Some(t.ReadBeat(output)) else None

    let testFir directory =
        let coefficients = [1; 3; 3; 1]
        let m, input, output = Designs.fir coefficients
        let t = Trace(m, directory)
        let rng = Random(271)
        let samples = [|32767; -32768; 0; 0; 0; 0; yield! [for _ in 1..250 -> rng.Next(-32768,32768)]|]
        let expected = Queue<Beat>()
        let history = Array.zeroCreate<int> 3
        let mutable offered: Beat option = None
        let mutable stalled = None
        let mutable sent, received, cycle = 0, 0, 0
        while received < samples.Length && cycle < 5000 do
            if offered.IsNone && sent < samples.Length && rng.Next(4) <> 0 then
                offered <- Some { Data=bigint samples[sent] &&& 65535I; Keep=3I; Last=sent%9=0; User=bigint(sent%256) }
            t.Source(input, offered)
            t.Set(output.Ready, if cycle < 12 || rng.Next(3)=0 then 0I else 1I)
            stalled <- stallCheck t output stalled
            if t.On(output.Fire) then
                check (expected.Count>0) "Unexpected FIR output"
                t.Check(output, expected.Dequeue()); received <- received+1
            if t.On(input.Fire) then
                let beat = offered.Value
                let sample = samples[sent]
                let result = sample + 3*history[0] + 3*history[1] + history[2]
                expected.Enqueue { beat with Data=bigint result &&& Expr.Mask(32); Keep=15I }
                history[2] <- history[1]; history[1] <- history[0]; history[0] <- sample
                offered <- None; sent <- sent+1
            t.Step(); cycle <- cycle+1
        check (received=samples.Length) "FIR failed to drain"
        t.Save()

    let testOscillator directory =
        let m, step, output = Designs.oscillator()
        let t = Trace(m, directory)
        let rng = Random(811)
        let mutable stalled = None
        let mutable received, cycle = 0, 0
        let mutable error = 0.0
        t.Set(step, 262144I) // 1/64 turn per accepted input sample.
        while received < 160 && cycle < 3000 do
            t.Set(output.Ready, if cycle < 40 || rng.Next(3)=0 then 0I else 1I)
            stalled <- stallCheck t output stalled
            if t.On(output.Fire) then
                let beat = t.ReadBeat(output)
                let angle = float received * 2.0*Math.PI/64.0
                let cosine = HwMath.DecodeSigned(beat.Data &&& Expr.Mask(32),32,24)
                let sine = HwMath.DecodeSigned(beat.Data >>> 32,32,24)
                let e = max (abs(cosine-Math.Cos angle)) (abs(sine-Math.Sin angle))
                error <- max error e
                check (e < 0.000005) "Oscillator phase/numeric mismatch"
                check (beat.User=bigint(received%256) && beat.Keep=255I && not beat.Last) "Oscillator metadata"
                received <- received+1
            t.Step(); cycle <- cycle+1
        check (received=160) "Oscillator failed to produce samples"
        t.Save()
        printfn "F# oscillator maximum observed error: %.9g" error

    let testScrambler directory =
        let m, input, output = Designs.scramblerRoundTrip()
        let t = Trace(m, directory)
        let rng = Random(1297)
        let expected = Queue<Beat>()
        let mutable offered: Beat option = None
        let mutable stalled = None
        let mutable sent, received, cycle = 0, 0, 0
        while received < 300 && cycle < 5000 do
            if offered.IsNone && sent < 300 && rng.Next(3) <> 0 then
                offered <- Some { Data=bigint(rng.Next(256)); Keep=1I; Last=sent%13=0; User=bigint(sent%256) }
            t.Source(input, offered)
            t.Set(output.Ready, if cycle<15 || rng.Next(3)=0 then 0I else 1I)
            stalled <- stallCheck t output stalled
            if t.On(output.Fire) then
                check (expected.Count>0) "Unexpected scrambler output"
                t.Check(output, expected.Dequeue()); received <- received+1
            if t.On(input.Fire) then
                expected.Enqueue offered.Value; offered <- None; sent <- sent+1
            t.Step(); cycle <- cycle+1
        check (received=300) "Scrambler failed to drain"
        t.Save()

    let testFilterBank directory count =
        let filters = Designs.monitorFilters |> List.take count
        let m,input,output = Designs.filterBank filters
        let t=Trace(m,directory)
        let rng=Random(3001+count)
        let samples=Array.init 96 (fun i -> if i=0 then -32768 elif i=1 then 32767 else rng.Next(-1000,1001))
        let positions=Array.zeroCreate<int> count
        let mutable offered: Beat option=None
        let mutable stalled=None
        let mutable sent,received,cycle=0,0,0
        while received<samples.Length*count && cycle<12000 do
            if offered.IsNone && sent<samples.Length && rng.Next(5)<>0 then
                offered<-Some { Data=bigint samples[sent] &&& 65535I;Keep=3I;Last=sent%7=0;User=bigint sent }
            t.Source(input,offered)
            t.Set(output.Ready,if cycle<60 || (cycle>220 && cycle<280) || rng.Next(4)=0 then 0I else 1I)
            stalled<-stallCheck t output stalled
            if t.On(output.Fire) then
                let beat=t.ReadBeat(output)
                let channel=int(beat.User >>> 8)
                check (channel>=0 && channel<count) "Bad filter-bank channel tag"
                let index=positions[channel]
                check (index<samples.Length) "Duplicate filter-bank output"
                check (index<=sent && (index<sent || offered.IsSome)) "Output from a sample not offered"
                let expected =
                    filters[channel].Coefficients
                    |> List.mapi (fun tap coefficient -> if index>=tap then coefficient*samples[index-tap] else 0)
                    |> List.sum
                check (beat.Data=(bigint expected &&& Expr.Mask(32))) "Filter-bank arithmetic/history"
                check ((beat.User &&& 255I)=bigint index && beat.Keep=15I && beat.Last) "Filter-bank tag/order"
                positions[channel]<-index+1
                received<-received+1
            if t.On(input.Fire) then offered<-None;sent<-sent+1
            t.Step();cycle<-cycle+1
        check (sent=samples.Length && received=samples.Length*count) "Filter bank deadlocked or lost samples"
        check (positions |> Array.forall ((=) samples.Length)) "Unequal branch delivery"
        t.Save()
        printfn "Filter bank %d channels: %d input samples, %d output results, %d cycles (includes stalls)" count sent received cycle

    let testNumeric directory power =
        let m,input,output = if power then NumericDesigns.complexPower () else NumericDesigns.quantizedGain ()
        let t = Trace(m,directory)
        let expected = System.Collections.Generic.Queue<Beat>()
        let f = FloatFormat.Compact24
        let rng = Random(299)
        let mutable offered: Beat option = None
        let mutable stalled = None
        let mutable sent,received,cycle = 0,0,0
        while received<96 && cycle<2000 do
            if offered.IsNone && sent<96 && rng.Next(4)<>0 then
                let data =
                    if power then f.Encode(float(sent-48)/8.0) ||| (f.Encode(float(2*sent-50)/16.0) <<< 24)
                    else bigint(rng.Next(1 <<< 24))
                offered <- Some { Data=data;Keep=(if power then 63I else 7I);Last=sent%7=0;User=bigint sent }
            t.Source(input,offered)
            t.Set(output.Ready,if cycle<20 || rng.Next(3)=0 then 0I else 1I)
            stalled <- stallCheck t output stalled
            if t.On(output.Fire) then
                check (expected.Count>0) "Unexpected numeric output"
                t.Check(output,expected.Dequeue())
                received <- received+1
            if t.On(input.Fire) then
                let beat = offered.Value
                let result =
                    if power then
                        let r,j = f.Decode(beat.Data &&& Expr.Mask(24)),f.Decode(beat.Data >>> 24)
                        f.Encode(r*r+j*j)
                    else bigint(Math.Clamp(Math.Round(float(Expr.SignedValue(beat.Data,24))*1.5),-8388608.0,8388607.0)) &&& Expr.Mask(24)
                expected.Enqueue({beat with Data=result;Keep=7I})
                offered <- None
                sent <- sent+1
            t.Step()
            cycle <- cycle+1
        check (received=96 && expected.Count=0) "Numeric F# pipeline did not drain"
        t.Save()

    [<EntryPoint>]
    let main args =
        let directory = if args.Length>0 then args[0] else "generated/fsharp"
        testNumeric directory false
        testNumeric directory true
        testFir directory
        testOscillator directory
        testScrambler directory
        testFilterBank directory 2
        testFilterBank directory 3
        testFilterBank directory 4
        printfn "PASS: %d F# assertions; eight actual F# hardware designs emitted." checks
        0
