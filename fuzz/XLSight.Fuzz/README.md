# XLSight.Fuzz

Coverage-guided fuzz harness for worksheet parsing parity between:

- `WorksheetScanner.ScanRows` (XmlReader-based baseline)
- `XlsxSheetScanner.ScanRows` (byte-engine)

## Local usage (AFL++ + SharpFuzz)

1. Install tooling:

```bash
dotnet tool install --global SharpFuzz.CommandLine --version 2.2.0
```

2. Download the helper script from SharpFuzz:

```bash
curl -fsSL https://raw.githubusercontent.com/Metalnem/sharpfuzz/master/scripts/fuzz.ps1 -o fuzz.ps1
```

3. Start fuzzing from repository root:

```bash
pwsh ./fuzz.ps1 fuzz/XLSight.Fuzz/XLSight.Fuzz.csproj -i fuzz/XLSight.Fuzz/Testcases
```

Crashes are written to `findings/crashes`.

## Notes

- Inputs larger than 2 MB are ignored by the harness to keep cycles tight.
- The harness treats known parse failures as expected and escalates only unexpected exceptions or parity mismatches.
