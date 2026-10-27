#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SymbolStream=System.IO.Stream;

namespace SharpHDL;

/// <summary>Availability of symbols matched to the actual loaded module.</summary>
public enum PortablePdbMapStatus
{
    /// <summary>A matching Portable PDB was read, possibly with hidden/unmapped instructions.</summary>
    Available,
    /// <summary>No matching sidecar or embedded Portable PDB was available.</summary>
    UnavailableSymbols,
    /// <summary>The loaded module has no accessible on-disk PE image.</summary>
    UnavailableImage,
    /// <summary>The on-disk image has a different module version identity from the loaded module.</summary>
    ImageMismatch
}

/// <summary>Unmodified source coordinates and optional document checksum from matched symbols.</summary>
/// <param name="File">Document path recorded in the PDB; local file existence is not implied.</param>
/// <param name="StartLine">One-based starting line.</param>
/// <param name="StartColumn">PDB starting column.</param>
/// <param name="EndLine">One-based ending line.</param>
/// <param name="EndColumn">PDB ending column.</param>
/// <param name="DocumentHashAlgorithm">Document hash algorithm GUID, or null when absent.</param>
/// <param name="DocumentHash">Uppercase hexadecimal document hash, or null when absent.</param>
public sealed record CilSourceSpan(string File,int StartLine,int StartColumn,int EndLine,int EndColumn,
    Guid? DocumentHashAlgorithm,string? DocumentHash);

/// <summary>One sequence-point boundary, including hidden regions that must not inherit a visible location.</summary>
/// <param name="IlOffset">Original bytecode offset.</param>
/// <param name="IsHidden">Whether the PDB explicitly marks this boundary hidden.</param>
/// <param name="Source">Visible source span, or null for hidden/unmapped regions.</param>
public sealed record PortablePdbPoint(int IlOffset,bool IsHidden,CilSourceSpan? Source);

/// <summary>Immutable method-specific Portable PDB data with bounded IL lookup.</summary>
public sealed class PortablePdbMethodMap
{
    internal PortablePdbMethodMap(PortablePdbMapStatus status,int bodySize,IEnumerable<PortablePdbPoint> points)
        {Status=status;BodySize=bodySize;Points=Array.AsReadOnly(points.OrderBy(p=>p.IlOffset).ToArray());}
    /// <summary>Whether matching symbols were available; unavailable data is never replaced with a guessed source.</summary>
    public PortablePdbMapStatus Status { get; }
    /// <summary>Original CIL body size in bytes.</summary>
    public int BodySize { get; }
    /// <summary>All visible/hidden boundaries, sorted by IL offset.</summary>
    public IReadOnlyList<PortablePdbPoint> Points { get; }

    /// <summary>Find the active sequence-point span, or null before any point, in hidden regions, or outside the body.</summary>
    public CilSourceSpan? FindSource(int ilOffset)
    {
        if(ilOffset<0)throw new ArgumentOutOfRangeException(nameof(ilOffset));
        if(ilOffset>=BodySize)return null;
        int low=0,high=Points.Count-1,index=-1;
        while(low<=high)
        {
            int middle=low+(high-low)/2;
            if(Points[middle].IlOffset<=ilOffset){index=middle;low=middle+1;}else high=middle-1;
        }
        return index<0?null:Points[index].Source;
    }
}

public static partial class PortablePdbSourceMap
{
    /// <summary>
    /// Read matched sidecar/embedded Portable PDB symbols using the BCL PE reader.
    /// Missing/mismatched symbols return an explicit unavailable status. Malformed
    /// PE/PDB data and unexpected IO errors propagate. A supplied stream provider
    /// follows PEReader's ownership/read/seek contract; null means no sidecar.
    /// </summary>
    public static PortablePdbMethodMap Read(MethodInfo method,Func<string,SymbolStream?>? pdbStreamProvider=null)
    {
        ArgumentNullException.ThrowIfNull(method);
        int bodySize=method.GetMethodBody()?.GetILAsByteArray()?.Length??0;
        var path=method.Module.FullyQualifiedName;
        if(!File.Exists(path))return new(PortablePdbMapStatus.UnavailableImage,bodySize,Array.Empty<PortablePdbPoint>());
        using var image=File.OpenRead(path);
        using var pe=new PEReader(image);
        var imageMetadata=pe.GetMetadataReader();
        if(imageMetadata.GetGuid(imageMetadata.GetModuleDefinition().Mvid)!=method.Module.ModuleVersionId)
            return new(PortablePdbMapStatus.ImageMismatch,bodySize,Array.Empty<PortablePdbPoint>());
        static SymbolStream? OpenSidecar(string candidate)=>File.Exists(candidate)?File.OpenRead(candidate):null;
        if(!pe.TryOpenAssociatedPortablePdb(path,pdbStreamProvider??OpenSidecar,out var provider,out _))
            return new(PortablePdbMapStatus.UnavailableSymbols,bodySize,Array.Empty<PortablePdbPoint>());
        using(provider)
        {
            var reader=provider.GetMetadataReader();
            var entity=MetadataTokens.EntityHandle(method.MetadataToken);
            if(entity.Kind!=HandleKind.MethodDefinition)
                return new(PortablePdbMapStatus.Available,bodySize,Array.Empty<PortablePdbPoint>());
            var info=reader.GetMethodDebugInformation(MetadataTokens.MethodDebugInformationHandle(MetadataTokens.GetRowNumber(entity)));
            var points=new List<PortablePdbPoint>();
            foreach(var point in info.GetSequencePoints())
            {
                if(point.Offset<0||point.Offset>=bodySize)throw new BadImageFormatException("Sequence point lies outside its method body.");
                CilSourceSpan? source=null;
                var documentHandle=point.Document.IsNil?info.Document:point.Document;
                if(!point.IsHidden&&point.StartLine>0&&!documentHandle.IsNil)
                {
                    var document=reader.GetDocument(documentHandle);
                    var file=reader.GetString(document.Name);
                    if(!string.IsNullOrWhiteSpace(file))
                        source=new CilSourceSpan(file,point.StartLine,point.StartColumn,point.EndLine,point.EndColumn,
                            document.HashAlgorithm.IsNil?null:reader.GetGuid(document.HashAlgorithm),
                            document.Hash.IsNil?null:Convert.ToHexString(reader.GetBlobBytes(document.Hash)));
                }
                points.Add(new(point.Offset,point.IsHidden,source));
            }
            return new(PortablePdbMapStatus.Available,bodySize,points);
        }
    }
}
