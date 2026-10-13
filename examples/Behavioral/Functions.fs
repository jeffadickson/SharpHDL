namespace Behavioral
module FSharpFunctions =
    let Minimum (a:int) (b:int) = if a < b then a else b
    let SelectArithmetic (a:int) (b:int) = if a > b then a + 2 else b - 1
    let Loop (a:int) (b:int) =
        let mutable result = a
        for i in 0..3 do result <- (result <<< 1) + b
        result
