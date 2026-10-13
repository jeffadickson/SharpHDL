namespace SharpHDL.FSharpExamples

open System
open SharpHDL

module NumericDesigns =
    /// F# sees the same generic width and fraction markers as C#.
    let quantizedGain () =
        let m = Module("FsQuantizedGain24")
        let input = Stream.Input(m, "input", 24, 8)
        let output = Stream.Output(m, "output", 24, 8)
        let applyGain (bits: Expr) =
            let sample = SFixed<W24,F16>(bits)
            let product = sample * SFixed<W24,F16>.Constant(1.5)
            product.Convert<W24,F16>(Rounding.NearestEven, Overflow.Saturate).Bits
        input
        |> fun s -> StreamBlocks.SkidBuffer(m, s, "input_cut")
        |> fun s -> StreamBlocks.Map(m, s, "gain", 24, Func<Expr,Expr>(applyGain))
        |> fun s -> StreamBlocks.SkidBuffer(m, s, "output_cut")
        |> fun s -> Stream.Connect(m, s, output)
        m, input, output

    /// Packed atomic {imaginary,real} beat -> real^2 + imaginary^2.
    let complexPower () =
        let m = Module("FsComplexPower24")
        let input = Stream.Input(m, "input", 48, 8)
        let output = Stream.Output(m, "output", 24, 8)
        let magnitude (real: FloatValue) (imaginary: FloatValue) = real*real + imaginary*imaginary
        FloatMath.Binary(m, input, "power", FloatFormat.Compact24,
                         Func<FloatValue,FloatValue,FloatValue>(magnitude))
        |> fun s -> StreamBlocks.SkidBuffer(m, s, "output_cut")
        |> fun s -> Stream.Connect(m, s, output)
        m, input, output
