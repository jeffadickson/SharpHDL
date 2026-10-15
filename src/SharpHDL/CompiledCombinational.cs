#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;

namespace SharpHDL
{
    /// <summary>
    /// Compiles a two-state hardware DAG into one straight-line .NET delegate.
    /// Shared nodes have one local assignment, not a duplicated expression tree.
    /// Nodes up to 32/64 bits use uint/ulong locals; wider nodes use BigInteger.
    /// </summary>
    public sealed class CompiledCombinational
    {
        readonly Func<BigInteger[], BigInteger>? evaluator;
        readonly Action<BigInteger[], BigInteger[]>? evaluatorVector;
        readonly Signal[] inputs;
        public IReadOnlyList<string> InputNames => Array.AsReadOnly(inputs.Select(x => x.Name).ToArray());

        /// <summary>Captured signal leaves plus computed nodes; aliases and constants need no local.</summary>
        public int ScheduledNodeCount { get; }

        public CompiledCombinational(Module module, Expr output) : this(module, output, false) { }

        internal CompiledCombinational(Module module, Expr output, bool includeRegisters)
            : this(module, output, includeRegisters, null) { }

        internal CompiledCombinational(Module module, Expr output, bool includeRegisters,
            IReadOnlyDictionary<SynchronousRam, BigInteger[]>? memoryImages)
            : this(module, new[] { output }, includeRegisters, memoryImages, false) { }

        /// <summary>Build one schedule for all roots so register D cones share intermediate values.</summary>
        internal CompiledCombinational(Module module, Expr[] outputs, bool includeRegisters,
            IReadOnlyDictionary<SynchronousRam, BigInteger[]>? memoryImages, bool vector)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (outputs == null || (!vector && outputs.Length == 0) || outputs.Any(x => x == null))
                throw new ArgumentException("Non-null output expressions are required.", nameof(outputs));
            module.Validate();
            inputs = module.Signals.Where(x => x.IsInput || (includeRegisters && x.Kind == "reg")).ToArray();
            var values = Expression.Parameter(typeof(BigInteger[]), "values");
            var indexes = inputs.Select((s, i) => new { s, i }).ToDictionary(x => x.s, x => x.i);
            var built = new Dictionary<Expr, Expression>();
            var active = new HashSet<Expr>();
            var locals = new List<ParameterExpression>();
            var schedule = new List<Expression>();

            Expression Build(Expr node)
            {
                if (built.TryGetValue(node, out var cached)) return cached;
                if (!active.Add(node)) throw new InvalidOperationException("Combinational cycle.");
                try
                {
                    Expression result;
                    if (node is Signal signal)
                    {
                        if (signal.Owner != module) throw new ArgumentException("Foreign module signal.");
                        if (signal.IsInput || signal.Kind == "reg")
                        {
                            if (!indexes.TryGetValue(signal, out var index))
                                throw new NotSupportedException("Registers are not pure combinational inputs.");
                            result = Expression.ArrayIndex(values, Expression.Constant(index));
                        }
                        else
                        {
                            // A wire/output is an alias, not a second evaluation of its driver.
                            result = Build(signal.Driver);
                            built.Add(node, result);
                            return result;
                        }
                    }
                    else if (node.Op == "const")
                    {
                        result = Number(node.Literal, Storage(node.Width));
                        built.Add(node, result);
                        return result;
                    }
                    else
                    {
                        var a = Build(node.Args[0]);
                        switch (node.Op)
                        {
                            case "mux":
                                result = Expression.Condition(Expression.NotEqual(a, Number(0, a.Type)),
                                    Build(node.Args[1]), Build(node.Args[2]));
                                break;
                            case "~": result = Expression.OnesComplement(a); break;
                            case "resize": result = a; break;
                            case "signextend": result = Signed(a, node.Args[0].Width); break;
                            case "slice":
                            case ">>":
                                result = node.Offset >= node.Args[0].Width ? Number(0, a.Type)
                                    : Expression.RightShift(a, Expression.Constant(node.Offset));
                                break;
                            case "<<":
                                result = node.Offset >= node.Width ? Number(0, a.Type)
                                    : Expression.LeftShift(a, Expression.Constant(node.Offset));
                                break;
                            case "asr":
                                var signed = Signed(a, node.Args[0].Width);
                                // CLR word shifts mask the count; RTL shifts must saturate instead.
                                int shift = signed.Type == typeof(int) ? Math.Min(node.Offset, 31)
                                    : signed.Type == typeof(long) ? Math.Min(node.Offset, 63) : node.Offset;
                                result = Expression.RightShift(signed, Expression.Constant(shift));
                                break;
                            case "lookup": result = ReadImage(node.Table, a); break;
                            case "memory":
                                if (memoryImages == null || node.Memory == null ||
                                    !memoryImages.TryGetValue(node.Memory, out var image))
                                    throw new NotSupportedException("Compiled memory read requires an owned RAM image.");
                                result = ReadImage(image, a);
                                break;
                            default:
                                var b = Build(node.Args[1]);
                                switch (node.Op)
                                {
                                    case "+": result = Expression.Add(a, b); break;
                                    case "-": result = Expression.Subtract(a, b); break;
                                    case "*": case "dsp48e2": result = Expression.Multiply(a, b); break;
                                    case "&": result = Expression.And(a, b); break;
                                    case "|": result = Expression.Or(a, b); break;
                                    case "^": result = Expression.ExclusiveOr(a, b); break;
                                    case "==": result = Bit(Expression.Equal(a, b)); break;
                                    case "<": result = Bit(Expression.LessThan(a, b)); break;
                                    case "slt": result = Bit(Expression.LessThan(
                                        Signed(a, node.Args[0].Width), Signed(b, node.Args[1].Width))); break;
                                    case "concat":
                                        result = Expression.Or(Expression.LeftShift(Bits(a, node.Width),
                                            Expression.Constant(node.Args[1].Width)), Bits(b, node.Width));
                                        break;
                                    default: throw new NotSupportedException("Unsupported compiled operation: " + node.Op);
                                }
                                break;
                        }
                    }
                    var local = Expression.Variable(Storage(node.Width), "n" + locals.Count);
                    locals.Add(local);
                    schedule.Add(Expression.Assign(local, Bits(result, node.Width)));
                    built.Add(node, local);
                    return local;
                }
                finally { active.Remove(node); }
            }

            // All supported two-state operators are pure and total on initialized,
            // masked inputs. Both mux cones may be scheduled; array accesses retain
            // their own bounds guards. Do not add partial/side-effecting operations
            // to this schedule without explicit control-dependent lowering.
            var roots = outputs.Select(Build).ToArray();
            ScheduledNodeCount = schedule.Count;
            if (vector)
            {
                var target = Expression.Parameter(typeof(BigInteger[]), "next");
                for (int i = 0; i < roots.Length; i++)
                    schedule.Add(Expression.Assign(Expression.ArrayAccess(target, Expression.Constant(i)),
                        AsBig(roots[i])));
                schedule.Add(Expression.Empty());
                evaluatorVector = Expression.Lambda<Action<BigInteger[], BigInteger[]>>(
                    Expression.Block(locals, schedule), values, target).Compile();
            }
            else
            {
                schedule.Add(AsBig(roots[0]));
                evaluator = Expression.Lambda<Func<BigInteger[], BigInteger>>(
                    Expression.Block(locals, schedule), values).Compile();
            }
        }

        /// <summary>Choose unsigned native storage without losing the declared bit width.</summary>
        static Type Storage(int width) => width <= 32 ? typeof(uint) : width <= 64 ? typeof(ulong) : typeof(BigInteger);

        static Expression Number(BigInteger value, Type type) => type == typeof(uint)
            ? Expression.Constant((uint)value) : type == typeof(ulong)
            ? Expression.Constant((ulong)value) : Expression.Constant(value);

        static Expression AsBig(Expression value) => value.Type == typeof(BigInteger)
            ? value : Expression.Convert(value, typeof(BigInteger));

        /// <summary>Truncate before BigInteger-to-word conversion, which is checked by the CLR.</summary>
        static Expression Bits(Expression value, int width)
        {
            var target = Storage(width);
            if (value.Type == typeof(BigInteger))
                value = Expression.And(value, Expression.Constant(Expr.Mask(width)));
            if (value.Type != target) value = Expression.Convert(value, target);
            if (target == typeof(BigInteger))
                return Expression.And(value, Expression.Constant(Expr.Mask(width)));
            if ((target == typeof(uint) && width == 32) || (target == typeof(ulong) && width == 64))
                return value;
            return Expression.And(value, Number(Expr.Mask(width), target));
        }

        /// <summary>Interpret a bit vector as signed without introducing signed overflow checks.</summary>
        static Expression Signed(Expression value, int width)
        {
            if (width > 64)
                return Expression.Call(typeof(Expr).GetMethod(nameof(Expr.SignedValue))!, value, Expression.Constant(width));
            int padding = (width <= 32 ? 32 : 64) - width;
            var signed = Expression.Convert(value, width <= 32 ? typeof(int) : typeof(long));
            return Expression.RightShift(Expression.LeftShift(signed, Expression.Constant(padding)),
                Expression.Constant(padding));
        }

        static Expression Bit(Expression predicate) => Expression.Condition(predicate,
            Expression.Constant(1u), Expression.Constant(0u));

        /// <summary>Read-before-write images are stable throughout one evaluation; invalid addresses yield zero.</summary>
        static Expression ReadImage(BigInteger[] image, Expression address) => Expression.Condition(
            Expression.LessThan(address, Number(image.Length, address.Type)),
            Expression.ArrayIndex(Expression.Constant(image), Expression.Convert(address, typeof(int))),
            Expression.Constant(BigInteger.Zero));

        // Only the state-owning sequential engine may bypass public input checks.
        internal BigInteger EvaluateUnchecked(BigInteger[] values) => evaluator!(values);
        internal void EvaluateVectorUnchecked(BigInteger[] values, BigInteger[] target) => evaluatorVector!(values, target);

        /// <summary>Evaluate a scalar cone using input values in InputNames order.</summary>
        public BigInteger Evaluate(params BigInteger[] values)
        {
            if (values == null || values.Length != inputs.Length) throw new ArgumentException("Incorrect input count.");
            for (int i = 0; i < values.Length; i++)
                if (values[i] < 0 || values[i] > Expr.Mask(inputs[i].Width)) throw new ArgumentOutOfRangeException(nameof(values));
            return evaluator!(values);
        }
    }
}
