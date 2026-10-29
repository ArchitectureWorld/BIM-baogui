import {DatabaseSync} from 'node:sqlite';
import {lstatSync,mkdirSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {spawnSync} from 'node:child_process';
import {accountRoot,noLinks,runtimeIdentity} from './web-runtime.js';
import {SyncError} from './types.js';

const fail=():never=>{throw new SyncError('BLOCKED','Web SQLite files and sidecars must be ordinary, singly linked files in the isolated Web state directory.');};
function checkLeaf(path:string){const stat=lstatSync(path,{throwIfNoEntry:false});if(stat&&(stat.isSymbolicLink()||!stat.isFile()||stat.nlink!==1))fail();}
function checkWindowsReparse(paths:string[]){
 if(process.platform!=='win32')return;
 // Node's lstat covers symbolic links/junctions; inspect the native attribute as
 // well so other Windows reparse tags cannot be opened by SQLite.
 const script='$ErrorActionPreference="Stop"; [Console]::InputEncoding=[Text.UTF8Encoding]::new($false); $paths=ConvertFrom-Json ([Console]::In.ReadToEnd()); foreach($path in $paths){try{$a=[IO.File]::GetAttributes($path)}catch{$e=$_.Exception;while($e.InnerException){$e=$e.InnerException};if($e -is [IO.FileNotFoundException] -or $e -is [IO.DirectoryNotFoundException]){continue};throw};if(($a -band [IO.FileAttributes]::ReparsePoint) -ne 0){exit 3}}';
 const executable=join(process.env.SystemRoot??'C:\\Windows','System32','WindowsPowerShell','v1.0','powershell.exe');
 const result=spawnSync(executable,['-NoLogo','-NoProfile','-NonInteractive','-EncodedCommand',Buffer.from(script,'utf16le').toString('base64')],{input:JSON.stringify(paths),encoding:'utf8',windowsHide:true,timeout:10000});
 if(result.error||result.status!==0)fail();
}
export function openWebDatabase(directory:string,name:'sync.sqlite'|'real-evidence.sqlite'):DatabaseSync{
 if(name!=='sync.sqlite'&&name!=='real-evidence.sqlite')fail();
 const expected=join(accountRoot(runtimeIdentity().accountId),'state');if(resolve(directory)!==expected)fail();
 noLinks(directory);mkdirSync(directory,{recursive:true});
 const file=join(directory,name),leaves=['','-wal','-shm','-journal'].map(s=>file+s);
 for(const leaf of leaves)checkLeaf(leaf);checkWindowsReparse([directory,...leaves]);for(const leaf of leaves)checkLeaf(leaf);
 return new DatabaseSync(file);
}
