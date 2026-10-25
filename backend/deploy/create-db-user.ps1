# Creates the API's own PostgreSQL user, makes it the owner of the baytak database, and stores the connection string in the
# API's app settings. Run once. psql asks for the postgres admin password; the new user's password is generated here and is
# never displayed. Needs psql (PostgreSQL client) and a signed-in Azure CLI.
param(
    [string]$Server = "studio.postgres.database.azure.com",
    [string]$Database = "baytak",
    [string]$User = "baytak_app",
    [string]$ResourceGroup = "rg-northeurope",
    [string]$FunctionApp = "baytak-api-8803",
    [string]$Psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
)

$bytes = New-Object byte[] 24
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
$password = [Convert]::ToBase64String($bytes) -replace '[+/=]', ''

# The user can connect only to its own database; other roles lose the default CONNECT on it.
& $Psql "host=$Server port=5432 dbname=postgres user=postgres sslmode=require" -v ON_ERROR_STOP=1 `
    -c "CREATE ROLE $User LOGIN PASSWORD '$password';" `
    -c "GRANT $User TO postgres;" `
    -c "ALTER DATABASE $Database OWNER TO $User;" `
    -c "REVOKE CONNECT ON DATABASE $Database FROM PUBLIC;"
if ($LASTEXITCODE -ne 0) { throw "psql failed, so nothing was stored." }

# On Azure flexible servers the public schema of a new database belongs to azure_pg_admin, not to the database owner,
# so the user could not create its tables. Give it the schema (this one runs inside the new database).
& $Psql "host=$Server port=5432 dbname=$Database user=postgres sslmode=require" -v ON_ERROR_STOP=1 `
    -c "ALTER SCHEMA public OWNER TO $User;"
if ($LASTEXITCODE -ne 0) { throw "Could not give $User the public schema, so nothing was stored." }

az functionapp config appsettings set -g $ResourceGroup -n $FunctionApp -o none --settings `
    "ConnectionStrings__Baytak=Host=$Server;Database=$Database;Username=$User;Password=$password;Ssl Mode=Require"
if ($LASTEXITCODE -ne 0) { throw "The user was created but the connection string could not be saved." }

Remove-Variable password, bytes
Write-Host "Done: $User owns $Database, and $FunctionApp has its connection string."
