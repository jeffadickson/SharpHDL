namespace SharpHDL.FSharpExamples

open System
open SharpHDL

/// These functions RUN in .NET to construct a circuit. Lists and folds are
/// elaboration-time tools; they do not become processors executing F# code.
module Designs =
    let bits (width: int) (value: int) = Expr.Const(width, bigint value)
    let signed (width: int) (value: int) = Expr.SignedConst(width, bigint value)
    let expr (signal: Signal) = signal :> Expr

    /// A parallel FIR. Each list item elaborates a multiplier; fold builds
    /// the adder chain. Defaults to gain-eight binomial smoothing [1;3;3;1].
    let fir (coefficients: int list) =
        if List.isEmpty coefficients then invalidArg "coefficients" "At least one tap is required"
        let m = Module("FsFir")
        let input = Stream.Input(m, "s_axis", 16, 8)
        let output = Stream.Output(m, "m_axis", 32, 8)
        let delays =
            [ for i in 1 .. coefficients.Length - 1 -> m.Register(sprintf "delay_%d" i, 16, 0I) ]
        let previous = (input.Data :: delays) |> List.take delays.Length
        List.zip delays previous
        |> List.iter (fun (r, source) -> m.NextWhen(r, input.Fire, source))
        let compute (sample: Expr) =
            List.zip coefficients (sample :: List.map expr delays)
            |> List.map (fun (c, x) -> x.SignExtend(32) * signed 32 c)
            |> List.fold (fun sum product -> sum + product) (bits 32 0)
        let filtered = StreamBlocks.Map(m, input, "fir", 32, Func<Expr, Expr>(compute))
        Stream.Connect(m, filtered, output)
        m, input, output

    /// A sample-indexed oscillator: phase advances only as the CORDIC accepts
    /// a sample. Stalling pauses sample generation; this is not a wall-clock DDS.
    let oscillator () =
        let m = Module("FsOscillator")
        let step = m.Input("phase_step", 24)
        let output = Stream.Output(m, "m_axis", 64, 8)
        let phase = m.Register("phase", 24, 0I)
        let tag = m.Register("sample_tag", 8, 0I)
        let angles = Stream.Internal(m, "angles", 24, 8)
        m.Drive(angles.Data, phase)
        m.Drive(angles.Keep, bits 3 7)
        m.Drive(angles.Valid, bits 1 1)
        m.Drive(angles.Last, bits 1 0)
        m.Drive(angles.User, tag)
        m.NextWhen(phase, angles.Fire, expr phase + expr step)
        m.NextWhen(tag, angles.Fire, expr tag + bits 8 1)
        Stream.Connect(m, HwMath.SinCos(m, angles, "osc"), output)
        m, step, output

    /// Eight-bit Galois LFSR, one update per accepted byte. Applying two
    /// independently reset copies unscrambles the data. Not encryption.
    let private scramble (m: Module) (input: Stream) name =
        let state = m.Register(name + "_lfsr", 8, 165I)
        let s = expr state
        let feedback = Expr.Mux(s.Slice(0, 0), bits 8 0xB8, bits 8 0)
        m.NextWhen(state, input.Fire, (s >>> 1) ^^^ feedback)
        StreamBlocks.Map(m, input, name, 8, Func<Expr, Expr>(fun data -> data ^^^ s))

    /// F# partial application and |> connect two hardware stages. Each stage
    /// advances its own state only when that stage accepts a byte.
    let scramblerRoundTrip () =
        let m = Module("FsScramblerRoundTrip")
        let input = Stream.Input(m, "s_axis", 8, 8)
        let output = Stream.Output(m, "m_axis", 8, 8)
        let scrambled = input |> fun stream -> scramble m stream "encode"
        let decoded = scrambled |> fun stream -> scramble m stream "decode"
        Stream.Connect(m, decoded, output)
        m, input, output

    type FilterSpec = { Name: string; Coefficients: int list; QueueDepth: int }

    let monitorFilters =
        [ { Name="smooth"; Coefficients=[1;3;3;1]; QueueDepth=4 }
          { Name="slope"; Coefficients=[1;-1]; QueueDepth=2 }
          { Name="curvature"; Coefficients=[1;-2;1]; QueueDepth=3 }
          { Name="raw"; Coefficients=[1]; QueueDepth=1 } ]

    let private firStage (m: Module) (spec: FilterSpec) (input: Stream) =
        if spec.Coefficients.IsEmpty then invalidArg "spec" "Empty coefficient list"
        let delays =
            [ for i in 1 .. spec.Coefficients.Length-1 ->
                m.Register(sprintf "%s_delay%d" spec.Name i,16,0I) ]
        List.zip delays ((input.Data::delays) |> List.take delays.Length)
        |> List.iter (fun (r, previous) -> m.NextWhen(r,input.Fire,previous))
        let compute (sample: Expr) =
            List.zip spec.Coefficients (sample::List.map expr delays)
            |> List.map (fun (c,x) -> x.SignExtend(32) * signed 32 c)
            |> List.fold (fun total product -> total+product) (bits 32 0)
        StreamBlocks.Map(m,input,spec.Name+"_fir",32,Func<Expr,Expr>(compute))

    let private tagChannel (m: Module) channel name (input: Stream) =
        let output=Stream.Internal(m,name,32,16)
        m.Drive(input.Ready,output.Ready)
        m.Drive(output.Valid,input.Valid)
        m.Drive(output.Data,input.Data)
        m.Drive(output.Keep,input.Keep)
        // Each result is a one-beat message, so arbitration can switch channels.
        m.Drive(output.Last,bits 1 1)
        m.Drive(output.User,Expr.Concat(bits 8 channel,input.User))
        output

    let private mergeTree (m: Module) streams =
        let rec merge level current =
            match current with
            | [single] -> single
            | [] -> invalidArg "streams" "At least one stream required"
            | _ ->
                current
                |> List.chunkBySize 2
                |> List.mapi (fun i group ->
                    match group with
                    | [a;b] -> StreamBlocks.Arbitrate(m,a,b,sprintf "merge_%d_%d" level i)
                    | [a] -> a
                    | _ -> failwith "Invalid merge group")
                |> merge (level+1)
        merge 0 streams

    /// The configuration list determines the hardware graph. Different branch
    /// rates are safe: Broadcast remembers delivery separately for each branch.
    let filterBank (filters: FilterSpec list) =
        if filters.IsEmpty || filters.Length>16 then invalidArg "filters" "Choose 1..16 filters"
        let m=Module(sprintf "FsFilterBank%d" filters.Length)
        let input=Stream.Input(m,"s_axis",16,8)
        let output=Stream.Output(m,"m_axis",32,16)
        let branches=StreamBlocks.Broadcast(m,input,"fanout",filters.Length)
        let results =
            filters
            |> List.mapi (fun channel spec ->
                branches[channel]
                |> firStage m spec
                |> fun s -> StreamBlocks.Fifo(m,s,spec.Name+"_queue",spec.QueueDepth)
                |> tagChannel m channel (spec.Name+"_tag"))
        results |> mergeTree m |> fun merged -> Stream.Connect(m,merged,output)
        m,input,output
