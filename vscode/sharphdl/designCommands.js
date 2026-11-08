'use strict';

/** @typedef {{workspaceUri: string, projectId: string}} DesignSelector */
/** @typedef {'Success'|'NeedsInput'|'InvalidInput'|'Busy'|'Failed'|'Cancelled'|'UnexpectedFailure'} DesignStatus */
/** @typedef {{status: DesignStatus, stage: string, diagnostic?: string, exitCode?: number}} DesignOutcome */

/**
 * Validate command inputs before invoking the shared design operation.
 * @param {(operation: string, selector: DesignSelector, token?: import('vscode').CancellationToken) => Promise<DesignOutcome>} execute
 */
function createDesignCommands(execute) {
  return async function dispatch(operation,input,token) {
    const result=(status,diagnostic)=>({operation,status,stage:'validate',diagnostic});
    if(operation!=='sharphdl.buildDesign'&&operation!=='sharphdl.runDesign')
      return result('InvalidInput','Unknown design command.');
    if(token!==undefined&&(!token||typeof token.isCancellationRequested!=='boolean'||
       typeof token.onCancellationRequested!=='function'))
      return result('InvalidInput','Expected a VS Code cancellation token.');
    if(token?.isCancellationRequested)return result('Cancelled','Cancelled before execution.');
    if(input===undefined)return result('NeedsInput','Select a workspace and design.');
    if(!input||typeof input!=='object'||Array.isArray(input))
      return result('InvalidInput','Expected a design selector.');
    // Older Explorer callers supplied an entry. Only its identity is trusted.
    const legacyUri=input.folder?.uri;
    const workspaceUri=input.workspaceUri??(legacyUri?.scheme==='file'&&typeof legacyUri.toString==='function'
      ?legacyUri.toString():undefined);
    const projectId=input.projectId??input.project?.id;
    if(typeof workspaceUri!=='string'||!workspaceUri.startsWith('file:')||
       typeof projectId!=='string'||!/^[A-Za-z_][A-Za-z0-9_$]*$/.test(projectId))
      return result('InvalidInput','Expected a file workspace URI and a valid project ID.');
    try {
      return {operation,...await execute(operation,{workspaceUri,projectId},token)};
    } catch(error) {
      return {operation,status:'UnexpectedFailure',stage:'execute',diagnostic:String(error?.stack||error)};
    }
  };
}
module.exports={createDesignCommands};
