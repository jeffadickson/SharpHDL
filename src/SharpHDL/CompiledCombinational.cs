using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;

namespace SharpHDL
{
    /// <summary>Compiles a pure two-state combinational cone into a .NET delegate.
    /// Inputs are supplied in the order of InputNames; unsupported operations fail explicitly.</summary>
    public sealed class CompiledCombinational
    {
        readonly Func<BigInteger[], BigInteger>? evaluator;
        readonly Action<BigInteger[], BigInteger[]>? evaluatorVector;
        readonly Signal[] inputs;
        public IReadOnlyList<string> InputNames => Array.AsReadOnly(inputs.Select(x => x.Name).ToArray());

        public CompiledCombinational(Module module, Expr output) : this(module, output, false) { }

        // Sequential engines compile next-state cones with registers as explicit read-only inputs.
        internal CompiledCombinational(Module module, Expr output, bool includeRegisters)
            : this(module, output, includeRegisters, null) { }

        internal CompiledCombinational(Module module, Expr output, bool includeRegisters,
            IReadOnlyDictionary<SynchronousRam, BigInteger[]>? memoryImages)
            : this(module, new[] { output }, includeRegisters, memoryImages, false) { }

        // One expression tree + one .NET JIT delegate for the complete next-state vector.
        internal CompiledCombinational(Module module, Expr[] outputs, bool includeRegisters,
            IReadOnlyDictionary<SynchronousRam, BigInteger[]>? memoryImages, bool vector)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (outputs == null || (!vector && outputs.Length == 0) || outputs.Any(x => x == null))
                throw new ArgumentException("At least one non-null expression is required.", nameof(outputs));
            module.Validate();
            inputs = module.Signals.Where(x => x.IsInput || (includeRegisters && x.Kind == "reg")).ToArray();
            var values = Expression.Parameter(typeof(BigInteger[]), "values");
            var indexes = inputs.Select((s, i) => new { s, i }).ToDictionary(x => x.s, x => x.i);
            var stack = new HashSet<Expr>();
            Expression Build(Expr e)
            {
                if (!stack.Add(e)) throw new InvalidOperationException("Combinational cycle");
                try
                {
                    Expression a, b, result;
                    var signal = e as Signal;
                    if (signal != null)
                    {
                        if (signal.Owner != module) throw new ArgumentException("Foreign module signal");
                        if (signal.IsInput)
                            result = Expression.ArrayIndex(values, Expression.Constant(indexes[signal]));
                        else if (signal.Kind == "reg")
                        {
                            if (!includeRegisters) throw new NotSupportedException("Sequential state is not part of a pure combinational cone");
                            result = Expression.ArrayIndex(values, Expression.Constant(indexes[signal]));
                        }
                        else result = Build(signal.Driver);
                    }
                    else if (e.Op == "const") result = Expression.Constant(e.Literal);
                    else
                    {
                        a = Build(e.Args[0]);
                        if (e.Op == "mux")
                            result = Expression.Condition(Expression.Equal(a, Expression.Constant(BigInteger.Zero)),
                                Build(e.Args[2]), Build(e.Args[1]));
                        else if (e.Op == "~") result = Expression.OnesComplement(a);
                        else if (e.Op == "resize") result = a;
                        else if (e.Op == "slice" || e.Op == ">>")
                            result = Expression.RightShift(a, Expression.Constant(e.Offset));
                        else if (e.Op == "<<")
                            result = Expression.LeftShift(a, Expression.Constant(e.Offset));
                        else if (e.Op == "signextend")
                            result = Expression.Call(typeof(Expr).GetMethod(nameof(Expr.SignedValue))!, a,
                                Expression.Constant(e.Args[0].Width));
                        else if (e.Op == "asr")
                            result = Expression.RightShift(Expression.Call(typeof(Expr).GetMethod(nameof(Expr.SignedValue))!, a,
                                Expression.Constant(e.Args[0].Width)), Expression.Constant(e.Offset));
                        else if (e.Op == "memory")
                        {
                            if (memoryImages == null || e.Memory == null || !memoryImages.TryGetValue(e.Memory, out var image))
                                throw new NotSupportedException("Compiled memory read requires an owned RAM image.");
                            result = Expression.Condition(Expression.LessThan(a, Expression.Constant(new BigInteger(image.Length))),
                                Expression.ArrayIndex(Expression.Constant(image), Expression.Convert(a, typeof(int))),
                                Expression.Constant(BigInteger.Zero));
                        }
                        else if (e.Op == "lookup")
                        {
                            var table = Expression.Constant(e.Table);
                            result = Expression.Condition(Expression.LessThan(a, Expression.Constant(new BigInteger(e.Table.Length))),
                                Expression.ArrayIndex(table, Expression.Convert(a, typeof(int))),
                                Expression.Constant(BigInteger.Zero));
                        }
                        else
                        {
                            b = Build(e.Args[1]);
                            if (e.Width <= 64 && (e.Op == "+" || e.Op == "-" || e.Op == "*" ||
                                e.Op == "&" || e.Op == "|" || e.Op == "^"))
                            {
                                var left = Expression.Convert(a, typeof(ulong));
                                var right = Expression.Convert(b, typeof(ulong));
                                Expression word = e.Op switch {
                                    "+" => Expression.Add(left, right),
                                    "-" => Expression.Subtract(left, right),
                                    "*" => Expression.Multiply(left, right),
                                    "&" => Expression.And(left, right),
                                    "|" => Expression.Or(left, right),
                                    _ => Expression.ExclusiveOr(left, right)
                                };
                                result = Expression.Convert(word, typeof(BigInteger));
                            }
                            else switch (e.Op)
                            {
                                case "+": result = Expression.Add(a, b); break;
                                case "-": result = Expression.Subtract(a, b); break;
                                case "*": result = Expression.Multiply(a, b); break;
                                case "&": result = Expression.And(a, b); break;
                                case "|": result = Expression.Or(a, b); break;
                                case "^": result = Expression.ExclusiveOr(a, b); break;
                                case "==": result = Bool(Expression.Equal(a, b)); break;
                                case "<": result = Bool(Expression.LessThan(a, b)); break;
                                case "slt":
                                    result = Bool(Expression.LessThan(Signed(a, e.Args[0].Width), Signed(b, e.Args[1].Width)));
                                    break;
                                case "concat":
                                    result = Expression.Or(Expression.LeftShift(a, Expression.Constant(e.Args[1].Width)), b); break;
                                default: throw new NotSupportedException("Unsupported compiled operation: " + e.Op);
                            }
                        }
                    }
                    return Expression.And(result, Expression.Constant(Expr.Mask(e.Width)));
                }
                finally { stack.Remove(e); }
            }
            static Expression Bool(Expression predicate) => Expression.Condition(predicate,
                Expression.Constant(BigInteger.One), Expression.Constant(BigInteger.Zero));
            static Expression Signed(Expression value, int width) => Expression.Call(
                typeof(Expr).GetMethod(nameof(Expr.SignedValue))!, value, Expression.Constant(width));
            if (vector)
            {
                var target = Expression.Parameter(typeof(BigInteger[]), "next");
                var assigns = outputs.Select((node, i) => (Expression)Expression.Assign(
                    Expression.ArrayAccess(target, Expression.Constant(i)), Build(node))).ToArray();
                evaluatorVector = Expression.Lambda<Action<BigInteger[], BigInteger[]>>(
                    Expression.Block(assigns.Concat(new Expression[] { Expression.Empty() })), values, target).Compile();
            }
            else
                evaluator = Expression.Lambda<Func<BigInteger[], BigInteger>>(Build(outputs[0]), values).Compile();
        }
        // The sequential backend supplies a trusted width-checked state vector.
        // Do not repeat caller-boundary validation inside every clock-edge cone.
        internal BigInteger EvaluateUnchecked(BigInteger[] values) => evaluator!(values);
        internal void EvaluateVectorUnchecked(BigInteger[] values, BigInteger[] target) => evaluatorVector!(values, target);

        public BigInteger Evaluate(params BigInteger[] values)
        {
            if (values == null || values.Length != inputs.Length) throw new ArgumentException("Incorrect input count");
            for (int i = 0; i < values.Length; i++)
                if (values[i] < 0 || values[i] > Expr.Mask(inputs[i].Width)) throw new ArgumentOutOfRangeException(nameof(values));
            return evaluator!(values);
        }
    }
}
