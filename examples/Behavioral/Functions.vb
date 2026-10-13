Option Strict On
Option Explicit On
Namespace Behavioral
    Public Module VisualBasicFunctions
        Public Function Minimum(a As Integer,b As Integer) As Integer
            If a < b Then Return a
            Return b
        End Function
        Public Function SelectArithmetic(a As Integer,b As Integer) As Integer
            If a > b Then Return a + 2
            Return b - 1
        End Function
        Public Function CheckedAdd(a As Integer,b As Integer) As Integer
            Return a + b
        End Function
    End Module
End Namespace
