# DB Connection Tester portable

Windows x64. No installation of .NET is required. Complete includes the GUI
(`DBConnectionTester.exe`) and CLI (`dbct.exe`); CLI includes only `dbct.exe`.

Run `dbct.exe --help` for commands and `dbct.exe run --help` for execution options.
Use `--data-dir` to choose the application's storage explicitly. For a USB drive:

```powershell
Set-Location $PSScriptRoot
.\dbct.exe run --type tcp --host example.org --port 443 --count 1 --data-dir .\Data
```

Relative paths refer to the terminal's current directory. In scripts, prefer
`--data-dir (Join-Path $PSScriptRoot 'Data')` so changing the working directory
does not change storage. The GUI accepts the same `--data-dir` argument.

Without explicit storage, existing storage and the user's preference are
discovered. A new run with no existing storage creates it in LocalAppData.
The CLI refuses ambiguous storage and does not change the GUI's preference.
Queries require existing storage and write access; they are not strictly read-only.

Passwords: `--password-env VARIABLE_NAME` or `--password-stdin`. Never pass the
password itself as an argument. Results go to stdout; progress and errors to stderr.
Use `--format json` for automation. Ctrl+C cancels and finalizes an active run.

Do not remove the drive during execution. Native libraries may be extracted to a
writable temporary directory. SQL Anywhere requires an external x64 ODBC driver;
Windows authentication uses the identity running the process. SQLite WAL storage
is intended for local disks, including locally connected removable media, not SMB.

These release ZIPs contain application binaries, not profiles or history.
The GUI's portable data package is a separate v1 format with a database and at
most one executable. Transfer application data using its backup/clone feature;
do not copy an open data.db without its consistent backup.
