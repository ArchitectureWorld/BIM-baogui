# Stage01 JSON Failure Report Design

## Goal

When a Stage01 Revit commit fails, write a complete machine-readable failure report next to the active GHA. Keep the Grasshopper UI concise and point the user to the report path.

## Output Contract

- Directory: `Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)`.
- File name: `BIMBaoGui.Stage01.failure-yyyyMMdd-HHmmss-fff.json`.
- Encoding: UTF-8 without BOM.
- Format: indented JSON.
- The `.json` extension must not be treated as a Grasshopper assembly.
- Each failure creates a unique report. No plugin backup is created.

## JSON Schema

The root object contains:

- `schemaVersion`: fixed at `1.0`.
- `reportId`: a new GUID.
- `occurredUtc` and `occurredLocal`.
- `diagnosticCode`: `DIAG_STAGE01_COMMIT_FAILED`.
- `operationStage`: the last named Stage01 commit stage reached before failure.
- `transactionRolledBack`: whether the transaction group rollback was requested.
- `plugin`: name, assembly version, assembly path, and SHA-256 when readable.
- `host`: Revit version number, version name, build, and process architecture.
- `document`: title, path, read-only state, family-document state, and worksharing state.
- `exceptionChain`: ordered outer-to-inner exception entries with depth, CLR type, message, source, target site, HResult, and complete stack trace.

The report must not contain Stage01 payload JSON, form values, organization values, or other business-field content.

## Runtime Flow

1. Track a short `operationStage` value before each commit boundary: validation, units, project position, project information, internal storage, official projection, transaction commit, and readback verification.
2. On an exception, request transaction-group rollback.
3. Build the failure report from host, document, plugin, stage, and exception context.
4. Write to a unique temporary file in the GHA directory, then atomically rename it to the final `.json` path.
5. Return a concise UI message containing the diagnostic code, exception type, and full report path.
6. If report writing fails, never replace the original failure. Return `REPORT_WRITE_FAILED` with both the original exception summary and the report-write exception summary.

## Code Boundaries

- `Stage01FailureReportWriter`: JSON DTO construction, redacted content, atomic file write, and fallback result.
- `Stage01RevitService`: operation-stage tracking and invocation of the writer from the outer commit catch.
- The report writer accepts explicit context values so file generation can be unit-tested without running Revit.

## Tests

- Writes valid JSON beside a supplied GHA path.
- Uses the required timestamped file name and UTF-8 encoding.
- Includes plugin, host, document, stage, rollback, and complete exception-chain fields.
- Excludes payload and business-field values.
- Preserves the original exception when report writing fails.
- Stage01 UI contract contains only a concise report reference, not the complete stack trace.

## Acceptance

After deployment, reproducing the current Revit failure creates a JSON report in:

`C:\Users\2899\AppData\Roaming\Grasshopper\Libraries\BIMbaogui`

The Grasshopper component displays the generated report path. The report can be read directly to identify the failing Revit API boundary.
