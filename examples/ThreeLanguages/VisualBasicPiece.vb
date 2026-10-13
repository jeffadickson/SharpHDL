Option Infer On
Option Strict On
Option Explicit On
Imports SharpHDL
Namespace ThreeLanguages
    Public Module VisualBasicPiece
        Public Function AddTwo(b As Expr) As Expr
            Dim a = b + Expr.Const(8, 2)
            Return a
        End Function
        Public Function HostAddTwo(b As Integer) As Integer
            Return b + 2
        End Function
        Public Function Build() As SharpHDL.Module
            Dim m As New SharpHDL.Module("AddTwo")
            Dim b = m.Input("b", 8)
            Dim a = m.Output("a", 8)
            m.Drive(a, AddTwo(b))
            Return m
        End Function
        Public Function MultiplyStage(m As SharpHDL.Module, input As Stream) As Stream
            Return StreamBlocks.Map(m, input, "vb_multiply", 8,
                Function(a As Expr) a * Expr.Const(8, 3))
        End Function
    End Module
End Namespace
