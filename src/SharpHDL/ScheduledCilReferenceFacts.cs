#nullable enable
using System;

namespace SharpHDL;

public static partial class CilCompiler
{
    // A predicate refers to a particular local's current value. Every write to
    // that local invalidates all dependent facts, even if its abstract type stays
    // unchanged. This prevents stale guards from authorizing a later dereference.
    private readonly record struct ScheduledNullTest(int Local,bool TrueIsNonNull,ScheduledStackKind Candidates);
    private readonly record struct ScheduledStackValue(ScheduledStackKind Kind,int Local=-1,ScheduledNullTest? Test=null,bool? Boolean=null);

    private static ScheduledStackValue ForgetScheduledLocal(ScheduledStackValue value,int local)
        =>value with {Local=value.Local==local?-1:value.Local,Test=value.Test?.Local==local?null:value.Test};

    private static ScheduledNullTest? ForgetScheduledLocal(ScheduledNullTest? test,int local)
        =>test?.Local==local?null:test;

    private static ScheduledNullTest? JoinScheduledTests(ScheduledNullTest? left,ScheduledNullTest? right)
        =>left.HasValue&&right.HasValue&&left.Value.Local==right.Value.Local&&left.Value.TrueIsNonNull==right.Value.TrueIsNonNull
            ?left.Value with{Candidates=left.Value.Candidates|right.Value.Candidates}:null;
}
