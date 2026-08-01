# H-IFC Revit 2020 Runtime Recovery Log

## Baseline

- Captured: 2026-08-01 Asia/Shanghai
- Active Revit PID: `60060`
- Revit start: `2026-08-01 21:46:23`
- Active document: `20260731test02.rvt`
- Current journal: `C:\Users\2899\AppData\Local\Autodesk\Revit\Autodesk Revit 2020\Journals\journal.0577.txt`
- Journal result: `API_ERROR` for `Hust.XAR.Shell.App` at line 643

## Vendor binaries

| File | Bytes | Last write | SHA-256 |
|---|---:|---|---|
| `Hust.XAR.Shell.dll` | 10,926,080 | 2026-03-20 16:19:56 | `9E00628A7B6F717773EFA3B68D41E5DAABD287FF0C0F89A9DD8624D28566E43E` |
| `Hust.IFC.RVT2HIFC.dll` | 36,873,728 | 2026-03-20 16:19:48 | `FE484FF8702EB6966497A40592F12646BA8D8CA48E74807E5FD1CCB4E9ABBF9B` |

Both binaries are under `C:\Program Files\HIFCTool\REVIT2020\net48` and predate both the last successful and first failed startup, so a binary replacement is not the observed transition.

## Object mapping configuration

| File | Bytes | SHA-256 |
|---|---:|---|
| Live `HIFCToolRvtToIfcConfig.txt` | 237 | `BDEA7F4D05537A9880CFDB896A0BEC4883B3038A57CDDE730B2520FBE7D43542` |
| Live `HIFCToolRvtToIfcConfig_Bak.txt` | 738 | `937B31E3E2F143A9990FF9BA60FA5772429C6FCF36BE90FEBBB5521967171CEC` |
| Installed baseline `HIFCToolRvtToIfcConfig.txt` | 738 | `937B31E3E2F143A9990FF9BA60FA5772429C6FCF36BE90FEBBB5521967171CEC` |

The two independent vendor baselines are byte-identical. The live file contains only its title/header and is independently unsuitable for object mapping.

## Startup-boundary evidence

`Hust.XAR.Shell.App.OnStartup` performs these operations in order:

1. obtain `UIApplication` through reflection;
2. construct five external-event handlers and call `ExternalEvent.Create`;
3. assign handlers into `MyGlobal`;
4. delete `HIFCToolExeclAttributeImport.json` when present;
5. call `MyRvtToIfcConfigContr.ReadAttrTxtFile`;
6. create ribbon panels and register application/document events.

The live generated JSON remains present after repeated failed Revit starts:

- path: `C:\Users\2899\Documents\HIFCTool\cnf\HIFCToolExeclAttributeImport.json`
- last write: `2026-07-31 17:23:44`
- bytes: 197,067
- SHA-256: `605BC02E8F7736072C8F0D3E675A714387D900B97CC61D85478220900F0AAD0B`

Therefore the current failure occurs before the delete/rebuild statement, not while parsing the current object map.

A Windows PowerShell 5.1 reflection probe redirected the vendor controller's input and output paths to a temporary directory, used the current 237-byte live object map plus current 11,554-byte attribute file, and invoked `MyRvtToIfcConfigContr.ReadAttrTxtFile("")`. Result:

```json
{"Success":true,"ResultCount":166,"OutputExists":true}
```

This independently confirms that the current `ReadAttrTxtFile` call is able to return all 166 attribute records. It does not make the 237-byte object map correct for export.

## Startup history

| Journal | Session time | H-IFC startup |
|---|---|---|
| `journal.0572.txt` | 2026-08-01 13:08 | `API_SUCCESS` |
| `journal.0573.txt` | 2026-08-01 15:15 | `API_ERROR` |
| `journal.0574.txt` | 2026-08-01 16:02 | `API_ERROR` |
| `journal.0575.txt` | 2026-08-01 20:53 | `API_ERROR` |
| `journal.0576.txt` | 2026-08-01 21:46 | `API_ERROR` |
| `journal.0577.txt` | 2026-08-01 21:47 | `API_ERROR` |

The 13:08 successful journal recorded H-IFC pushbuttons and event registrations before `API_SUCCESS`. Failed journals record none of those H-IFC side effects, which is consistent with the narrowed early-startup boundary.

## Planned changes and rollback

- The live 237-byte map will be copied to a timestamped `pre-bimbaogui` backup and hashed before replacement.
- Replacement content must have SHA-256 `937B31E3E2F143A9990FF9BA60FA5772429C6FCF36BE90FEBBB5521967171CEC`.
- A temporary user-level startup probe will capture the full exception chain around the vendor `OnStartup` call.
- The temporary deployed probe will be removed after diagnosis.
- No current RVT or IFC is overwritten by this recovery step.

## Result

### Object-map repair completed

- Completed: 2026-08-01 23:00 Asia/Shanghai
- Backup: `C:\Users\2899\Documents\HIFCTool\cnf\BaseFile\HIFCToolRvtToIfcConfig.pre-bimbaogui-20260801-230002.txt`
- Backup SHA-256: `BDEA7F4D05537A9880CFDB896A0BEC4883B3038A57CDDE730B2520FBE7D43542`
- External manifest: `C:\Users\2899\Documents\HIFCTool\cnf\BaseFile\20260801-230002-hifc-object-map-backup-manifest.json`
- Restored target bytes: 738
- Restored target SHA-256: `937B31E3E2F143A9990FF9BA60FA5772429C6FCF36BE90FEBBB5521967171CEC`
- Non-comment mapping rows: 4

The object map is now consistent with both vendor baselines. Official application recovery remains pending a safe Revit restart and fresh journal/probe output; this configuration correction alone is not recorded as the startup root cause.
