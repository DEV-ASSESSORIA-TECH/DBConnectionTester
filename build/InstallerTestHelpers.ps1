# Read-only helpers shared by package inspection and disposable-environment lifecycle tests.
function Read-MsiTable {
    param([Parameter(Mandatory)] $Database, [Parameter(Mandatory)][string] $Sql,
          [Parameter(Mandatory)][string[]] $Columns)
    $view = $Database.OpenView($Sql)
    try {
        [void]$view.Execute()
        while ($record = $view.Fetch()) {
            try {
                $row = [ordered]@{}
                for ($index = 0; $index -lt $Columns.Count; $index++) { $row[$Columns[$index]] = $record.StringData($index + 1) }
                [pscustomobject]$row
            }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    }
    finally { [void]$view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
}

function Assert-InstallerCondition {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Get-MsiPropertyMap {
    param($Database)
    $values = @{}
    foreach ($row in Read-MsiTable $Database 'SELECT `Property`, `Value` FROM `Property`' @('Name', 'Value')) {
        $values[$row.Name] = $row.Value
    }
    return $values
}
