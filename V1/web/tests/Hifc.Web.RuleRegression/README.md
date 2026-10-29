# Frozen Local regression runtime scope

This project links the original Local tests and Core sources without changing
their behavior. The default runner executes every test and retains failures and
its nonzero exit code. There are no OS-based skips or accepted-failure exceptions.

Two original tests specifically exercise the Windows Local application's file
path and filename-cleanup contract:

| Original test | Windows-specific operation |
| --- | --- |
| `saved_rvt_filename_overrides_document_title` | Passes the literal `D:\模型\学校_综合楼.rvt` to `ExportFileNaming.Stem`, whose `RawModelName` calls `Path.GetFileNameWithoutExtension`. Linux treats backslashes as ordinary filename characters. |
| `filename_cleanup_is_scoped_and_keeps_chinese` | Passes `学校_楼/栋?` to `ExportFileNaming.Stem`; its cleanup uses `Path.GetInvalidFileNameChars`. Linux's invalid set does not include `?`, while Windows's does. |

These failures on Linux are still failures of the unmodified complete suite;
they must not be reported as all 267 original tests passing on Linux. Keep the
full failing Linux log and the full passing Windows log as separate evidence.

The Web server does not receive an RVT path to infer names or choose folders. Its
production entry point is `RuleAdapter.BuildExportPlan`: the client sends a
confirmed IFC basename. The server rejects every Windows filename-forbidden
character (all U+0000–U+001F controls plus `<>:"/\|?*`) independently of its host
OS, then checks the confirmed basename through Core `StemForNames`. Thus no
host-specific invalid character reaches Core cleanup. The server does not call
Core `Stem`, `SuggestNames`, `ModelName`, `FileKind`, `InitialDirectory`, `NextPath`
or `ResolveSelection` to interpret client filesystem paths.

Linux product acceptance uses the actual `Hifc.Web.RuleParityTests` entry-point
suite, including the exhaustive Windows-invalid-character rejection test and
Chinese basename/Windows drive/UNC/POSIX path cases. This is separate from the
Windows Local suite and does not substitute for native Revit/IFC acceptance.
