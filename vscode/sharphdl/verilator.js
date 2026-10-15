'use strict';
/** Parse Verilator GCC-style messages into independent diagnostic records.
 * No dependency on VS Code, so this parser is covered by plain Node tests.
 */
const diagnosticPattern = /^%(Warning(?:-[\w-]+)?|Error(?:-[\w-]+)?):\s+(.+?):(\d+):(\d+):\s*(.*)$/;
function parseDiagnostics(output) {
  const results=[];
  for (const line of String(output).split(/\r?\n/)) {
    const m=diagnosticPattern.exec(line.trim());
    if (!m) continue;
    results.push({severity:m[1].startsWith('Error')?'error':'warning',
      code:m[1],file:m[2],line:Number(m[3]),column:Number(m[4]),message:m[5]});
  }
  return results;
}
module.exports={parseDiagnostics};
