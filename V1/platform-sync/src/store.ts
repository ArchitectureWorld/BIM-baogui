import {DatabaseSync} from 'node:sqlite';
import {mkdirSync} from 'node:fs';
import {join,resolve} from 'node:path';
import {randomUUID} from 'node:crypto';
import {redact} from './security.js';
import {SyncError,type Binding,type Run} from './types.js';

export class Store {
 readonly directory:string; private db:DatabaseSync;private token=randomUUID();
 constructor(directory:string){this.directory=resolve(directory);mkdirSync(this.directory,{recursive:true});this.db=new DatabaseSync(join(this.directory,'sync.sqlite'));this.db.exec('PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=5000; CREATE TABLE IF NOT EXISTS bindings(id TEXT PRIMARY KEY,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS runs(id TEXT PRIMARY KEY,project_id TEXT NOT NULL,data TEXT NOT NULL); CREATE TABLE IF NOT EXISTS events(seq INTEGER PRIMARY KEY,run_id TEXT NOT NULL,kind TEXT NOT NULL,data TEXT NOT NULL,at TEXT NOT NULL); CREATE TABLE IF NOT EXISTS locks(project_id TEXT PRIMARY KEY,run_id TEXT NOT NULL,owner TEXT NOT NULL,pid INTEGER NOT NULL);');}
 binding(id:string):Binding {const r=this.db.prepare('SELECT data FROM bindings WHERE id=?').get(id);if(!r)throw new SyncError('CONFIG','Unknown model binding.');return JSON.parse(String(r.data));}
 bindings():Binding[]{return this.db.prepare('SELECT data FROM bindings ORDER BY id').all().map(r=>JSON.parse(String(r.data)));}
 putBinding(b:Binding){this.db.prepare('INSERT INTO bindings VALUES(?,?) ON CONFLICT(id) DO UPDATE SET data=excluded.data').run(b.id,JSON.stringify(b));}
 run(id:string):Run {const r=this.db.prepare('SELECT data FROM runs WHERE id=?').get(id);if(!r)throw new SyncError('CONFIG','Unknown sync run.');return JSON.parse(String(r.data));}
 runs():Run[]{return this.db.prepare('SELECT data FROM runs ORDER BY rowid DESC').all().map(r=>JSON.parse(String(r.data)));}
 save(r:Run,kind='state'){r.updated_utc=new Date().toISOString();this.transaction(()=>{this.db.prepare('INSERT INTO runs VALUES(?,?,?) ON CONFLICT(id) DO UPDATE SET data=excluded.data').run(r.id,r.project_id,JSON.stringify(r));this.db.prepare('INSERT INTO events(run_id,kind,data,at) VALUES(?,?,?,?)').run(r.id,kind,JSON.stringify({state:r.state,reason:redact(r.reason),intent:r.intent}),r.updated_utc);});}
 succeed(r:Run,b:Binding){this.transaction(()=>{this.putBinding(b);this.db.prepare('UPDATE runs SET data=? WHERE id=?').run(JSON.stringify(r),r.id);this.db.prepare('INSERT INTO events(run_id,kind,data,at) VALUES(?,?,?,?)').run(r.id,'verified',JSON.stringify({state:r.state,result_id:r.result_id}),new Date().toISOString());});}
 events(runId:string):{kind:string;data:string}[]{return this.db.prepare('SELECT kind,data FROM events WHERE run_id=? ORDER BY seq').all(runId) as unknown as {kind:string;data:string}[];}
 acquire(project:string,run:string,reconcile=false){this.transaction(()=>{
  const row=this.db.prepare('SELECT * FROM locks WHERE project_id=?').get(project);
  if(row){let dead=false;try{process.kill(Number(row.pid),0);}catch{dead=true;}
   if(row.run_id!==run||!reconcile||row.owner!==''&&!dead)throw new SyncError('BUSY','Project lock is busy or has an unresolved run.');
   this.db.prepare('UPDATE locks SET owner=?,pid=? WHERE project_id=?').run(this.token,process.pid,project);
  }else this.db.prepare('INSERT INTO locks VALUES(?,?,?,?)').run(project,run,this.token,process.pid);
 });}
 retain(project:string,run:string){this.db.prepare('UPDATE locks SET owner=?,pid=0 WHERE project_id=? AND run_id=? AND owner=?').run('',project,run,this.token);}
 release(project:string,run:string){this.db.prepare('DELETE FROM locks WHERE project_id=? AND run_id=? AND owner=?').run(project,run,this.token);}
 private transaction(action:()=>void){this.db.exec('BEGIN IMMEDIATE');try{action();this.db.exec('COMMIT');}catch(e){this.db.exec('ROLLBACK');throw e;}}
 close(){this.db.close();}
}
