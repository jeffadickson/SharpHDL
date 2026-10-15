'use strict';
/** Post-edge debugger conditions for the localhost reference simulator.
 * This is NOT a Verilator DAP adapter or a source breakpoint engine.
 */
const FIELDS=['inputAccepted','outputAccepted','inputStalled','outputStalled','inputReady','outputValid'];
function validateCondition(c) {
 if(!c||typeof c!=='object')throw new Error('Invalid hardware breakpoint');
 if(c.kind==='cycle'&&Number.isSafeInteger(c.value)&&c.value>=0)return c;
 if(c.kind==='boolean'&&FIELDS.includes(c.field))return c;
 if(c.kind==='stage'&&Number.isInteger(c.index)&&c.index>=0&&c.index<256)return c;
 throw new Error('Unsupported hardware breakpoint condition');
}
function matches(state,condition) {
 const c=validateCondition(condition);
 if(!state)return false;
 if(c.kind==='cycle')return Number(state.cycle)>=c.value;
 if(c.kind==='boolean')return state[c.field]===true;
 return Array.isArray(state.stageOccupied)&&state.stageOccupied[c.index]===true;
}
module.exports={validateCondition,matches,FIELDS};
