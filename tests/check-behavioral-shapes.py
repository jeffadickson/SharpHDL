"""Check implementation properties that numeric equivalence alone cannot establish."""
from pathlib import Path
import re
p = Path(__file__).resolve().parents[1] / 'generated' / 'behavioral'
checks = 0
for flavor in ('Cs', 'Debug'):
    loop = (p / f'Cil{flavor}Loop12.graph.txt').read_text()
    assert not re.search(r'^reg ', loop, re.M)
    assert len(re.findall(r'bits\[32\] \+ ', loop)) == 12
    for name in ('Double', 'CheckedDouble'):
        graph = (p / f'Cil{flavor}{name}.graph.txt').read_text()
        assert ' * ' not in graph and ' << ' in graph
    half = (p / f'Cil{flavor}Half.graph.txt').read_text()
    assert ' asr ' in half and ' mux ' in half and ' + ' in half
    unsigned = (p / f'Cil{flavor}UnsignedHalf.graph.txt').read_text()
    assert ' >> ' in unsigned
    checks += 5
print(f'PASS {checks} structural checks: unrolled loop has no counter, multiply/divide use shifts')
