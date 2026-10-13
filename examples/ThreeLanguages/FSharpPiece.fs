namespace ThreeLanguages
open System
open SharpHDL
module FSharpPiece =
    let AddTwo (b: Expr) =
        let a = b + Expr.Const(8, 2I)
        a
    let HostAddTwo (b: int) = b + 2
    let Build () =
        let m = Module("AddTwo")
        let b = m.Input("b",8)
        let a = m.Output("a",8)
        m.Drive(a, AddTwo b)
        m
    let AddStage (m: Module) (input: Stream) =
        StreamBlocks.Map(m, input, "fsharp_add", 8, Func<Expr,Expr>(AddTwo))
