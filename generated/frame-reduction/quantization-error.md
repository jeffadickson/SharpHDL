# Fixed-point conversion: 12 to 8 fractional bits

Metric | Value
--- | ---:
Total pairs | 65536
Finite pairs | 65536
Non-finite pairs (excluded) | 0
Zero references (relative error excluded) | 1
Finite pairs with unrepresentable difference | 0
Maximum absolute error | 0.001953125
Maximum relative error | 1
RMS absolute error | 0.0011320335077509404

Relative error uses abs(actual-reference)/abs(reference). Zero references are included in absolute/RMS error. Non-finite pairs are counted separately; a zero finite-sample error does not excuse them. This report characterizes tested samples, not a proof over an untested domain.

Exhaustive graph evaluation of all signed 16-bit encodings with 12 fractional bits, converted to signed 16-bit with 8 fractional bits, nearest-even rounding and saturation. This domain does not reach saturation. Maximum absolute error is exactly half an output LSB (1/512). RTL numerical operators have separate regression tests; this report describes graph evaluation.
