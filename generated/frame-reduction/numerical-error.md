# Calibrated sample energy: exhaustive signed 16-bit domain

Metric | Value
--- | ---:
Total pairs | 65536
Finite pairs | 65536
Non-finite pairs (excluded) | 0
Zero references (relative error excluded) | 1
Finite pairs with unrepresentable difference | 0
Maximum absolute error | 0
Maximum relative error | 0
RMS absolute error | 0

Relative error uses abs(actual-reference)/abs(reference). Zero references are included in absolute/RMS error. Non-finite pairs are counted separately; a zero finite-sample error does not excuse them. This report characterizes tested samples, not a proof over an untested domain.

Reference: clamp(2*x-10, -32768, 32767)^2 for every signed 16-bit input. All results fit exactly in double. This is native-model evidence; RTL evidence is the separate cycle replay and mixed AXI regression.
