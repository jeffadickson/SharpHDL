#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace SharpHDL;

/// <summary>Structured CIL rejection details attached to a compatible NotSupportedException.</summary>
public sealed class CilDiagnostic
{
    private const string DataKey = "SharpHDL.CilDiagnostic";
    private CilDiagnostic(MethodInfo method, int offset, string reason, CilSourceSpan? source,
        PortablePdbMapStatus? symbols, Exception? symbolFailure)
    { Method=method;IlOffset=offset;Reason=reason;Source=source;Symbols=symbols;SymbolFailure=symbolFailure; }

    /// <summary>Stable diagnostic family identifier for rejected CIL.</summary>
    public string Code => "SHDL-CIL-001";
    /// <summary>Original method whose translation was rejected.</summary>
    public MethodInfo Method { get; }
    /// <summary>Reported IL byte offset; malformed branch targets may lie outside the body.</summary>
    public int IlOffset { get; }
    /// <summary>Actionable compiler rejection reason, independent of display formatting.</summary>
    public string Reason { get; }
    /// <summary>Matched visible sequence-point span, or null for missing/hidden source.</summary>
    public CilSourceSpan? Source { get; }
    /// <summary>Symbol availability, or null when symbol reading itself failed.</summary>
    public PortablePdbMapStatus? Symbols { get; }
    /// <summary>Retained symbol failure; never substitutes for the primary CIL rejection.</summary>
    public Exception? SymbolFailure { get; }

    /// <summary>Retrieve compiler details; unrelated exceptions have no diagnostic.</summary>
    public static CilDiagnostic? FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Data[DataKey] as CilDiagnostic;
    }

    internal static NotSupportedException CreateException(MethodInfo method, int offset, string reason)
    {
        CilSourceSpan? source=null; PortablePdbMapStatus? status=null; Exception? failure=null;
        try
        {
            var symbols=PortablePdbSourceMap.Read(method);
            status=symbols.Status;
            // A malformed branch can report a negative target. Preserve that
            // rejection without sending an invalid lookup offset into the PDB API.
            source=offset<0?null:symbols.FindSource(offset);
        }
        catch(Exception error) when(error is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            // Bad/unreadable optional symbols must not hide the actual compiler
            // rejection. Keep the exception, expose the failure, and state it in
            // the message rather than silently swallowing it or inventing a span.
            failure=error;
        }
        var diagnostic=new CilDiagnostic(method,offset,reason,source,status,failure);
        string owner=method.DeclaringType==null?method.Name:method.DeclaringType.FullName+"."+method.Name;
        string message=owner+" IL_"+offset.ToString("x4",CultureInfo.InvariantCulture)+": "+reason;
        if(source!=null)message+=" ("+source.File+":"+source.StartLine.ToString(CultureInfo.InvariantCulture)+":"+source.StartColumn.ToString(CultureInfo.InvariantCulture)+")";
        if(failure!=null)message+=" [symbol lookup failed: "+failure.GetType().Name+"]";
        message+=" ["+diagnostic.Code+"]";
        var exception=new NotSupportedException(message,failure);
        exception.Data[DataKey]=diagnostic;
        return exception;
    }
}
