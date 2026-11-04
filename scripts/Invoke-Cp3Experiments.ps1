<#
.SYNOPSIS
    Executa os experimentos E01 a E05 do CP3 contra o ambiente Docker Compose e grava evidencias sanitizadas.

.DESCRIPTION
    Requer o ambiente em execucao (docker compose up -d) e o arquivo .env preenchido.
    Cria usuarios descartaveis por execucao, com senha aleatoria mantida somente em memoria.
    Tokens, senhas e cabecalhos de autenticacao nunca sao gravados nas evidencias.
    Nenhum comando remove volumes; E05 usa --force-recreate apenas em api e worker.

.EXAMPLE
    pwsh scripts/Invoke-Cp3Experiments.ps1 -Experiment All
#>
[CmdletBinding()]
param(
    [ValidateSet("All", "E01", "E02", "E03", "E04", "E05")]
    [string]$Experiment = "All",
    [string]$BaseUrl = "http://localhost:8080",
    [string]$ManagementUrl = "http://localhost:15672",
    [string]$OutputDirectory = "docs/evidencias/cp3-i06",
    [string]$QueueName = "expensehub.reports",
    [int]$TimeoutSeconds = 90
)

Set-StrictMode -Version 1
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$script:Failures = 0
$script:RunId = (Get-Date).ToUniversalTime().ToString("yyyyMMddHHmmss")
$script:Context = @{}

function Import-DotEnv {
    $values = @{}
    if (Test-Path ".env") {
        foreach ($line in Get-Content ".env") {
            if ($line -match "^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$") { $values[$Matches[1]] = $Matches[2] }
        }
    }
    foreach ($key in "IMAGE_TAG", "RABBITMQ_DEFAULT_USER", "RABBITMQ_DEFAULT_PASS", "SEED_ADMIN_EMAIL", "SEED_ADMIN_PASSWORD") {
        if (-not $values[$key] -and [Environment]::GetEnvironmentVariable($key)) { $values[$key] = [Environment]::GetEnvironmentVariable($key) }
    }
    return $values
}

$envValues = Import-DotEnv
foreach ($required in "RABBITMQ_DEFAULT_USER", "RABBITMQ_DEFAULT_PASS", "SEED_ADMIN_EMAIL", "SEED_ADMIN_PASSWORD") {
    if (-not $envValues[$required]) { throw "Defina $required no arquivo .env (modelo: .env.example)." }
}

function New-RandomPassword {
    $chars = (48..57) + (65..90) + (97..122)
    $body = -join (1..20 | ForEach-Object { [char]($chars | Get-Random) })
    return "Aa1!$body"
}

function Protect-Text([string]$Text) {
    $Text = $Text -replace "(?i)(accessToken|refreshToken)\W+[A-Za-z0-9_\-\.]{12,}", '$1=<redacted>'
    $Text = $Text -replace "(?i)Bearer\s+[A-Za-z0-9_\-\.]+", "Bearer <redacted>"
    $Text = $Text -replace "(?i)(password|senha)\W+\S+", '$1=<redacted>'
    return $Text
}

function Write-Evidence([string]$Name, [string]$Text) {
    Add-Content -Path (Join-Path $OutputDirectory "$Name.md") -Value (Protect-Text $Text) -Encoding utf8
}

function Test-Check([string]$Name, [string]$Check, [bool]$Condition, [string]$Detail = "") {
    $mark = if ($Condition) { "PASS" } else { "FAIL" }
    if (-not $Condition) { $script:Failures++ }
    Write-Evidence $Name "- [$mark] $Check $Detail"
    Write-Host "[$mark] $Name - $Check $Detail"
}

function Invoke-Api([string]$Method, [string]$Path, [string]$Token, $Body) {
    $headers = @{}
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = "$BaseUrl$Path"; Headers = $headers; SkipHttpErrorCheck = $true }
    if ($null -ne $Body) { $params["Body"] = ($Body | ConvertTo-Json -Depth 5); $params["ContentType"] = "application/json" }
    $response = Invoke-WebRequest @params
    $json = $null
    if ($response.Content -is [string] -and $response.Content.Length -gt 0) {
        try { $json = $response.Content | ConvertFrom-Json } catch { $json = $null }
    }
    return [pscustomobject]@{ Status = [int]$response.StatusCode; Json = $json; Raw = $response }
}

function Get-Token([string]$Email, [string]$Password) {
    $login = Invoke-Api "POST" "/login" $null @{ email = $Email; password = $Password }
    if ($login.Status -ne 200) { throw "Login falhou para um usuario do experimento (HTTP $($login.Status))." }
    return $login.Json.accessToken
}

function Get-RabbitQueue {
    $secure = ConvertTo-SecureString $envValues["RABBITMQ_DEFAULT_PASS"] -AsPlainText -Force
    $credential = [pscredential]::new($envValues["RABBITMQ_DEFAULT_USER"], $secure)
    $queue = Invoke-RestMethod -Uri "$ManagementUrl/api/queues/%2F/$QueueName" -Credential $credential `
        -AllowUnencryptedAuthentication -Authentication Basic
    return [pscustomobject]@{ Ready = [int]$queue.messages_ready; Unacked = [int]$queue.messages_unacknowledged }
}

function Publish-Duplicate([string]$ReportId) {
    $secure = ConvertTo-SecureString $envValues["RABBITMQ_DEFAULT_PASS"] -AsPlainText -Force
    $credential = [pscredential]::new($envValues["RABBITMQ_DEFAULT_USER"], $secure)
    $body = @{
        properties = @{ delivery_mode = 2; content_type = "application/json" }
        routing_key = $QueueName
        payload = "{`"reportId`":`"$ReportId`"}"
        payload_encoding = "string"
    } | ConvertTo-Json -Depth 4
    return Invoke-RestMethod -Method Post -Uri "$ManagementUrl/api/exchanges/%2F/amq.default/publish" `
        -Credential $credential -AllowUnencryptedAuthentication -Authentication Basic -Body $body -ContentType "application/json"
}

function Wait-Report([string]$Id, [string]$Token, [string]$Expected) {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $detail = Invoke-Api "GET" "/api/reports/$Id" $Token $null
        if ($detail.Status -eq 200 -and $detail.Json.status -eq $Expected) { return $detail }
        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)
    return $detail
}

function Get-ServiceLogs([string]$Service, [string]$ReportId) {
    $lines = docker compose logs --no-color --tail 500 $Service 2>&1 | Where-Object { $_ -match [regex]::Escape($ReportId) }
    return @($lines | ForEach-Object { Protect-Text $_ })
}

function Add-Logs([string]$Name, [string]$Service, [string]$ReportId) {
    $lines = Get-ServiceLogs $Service $ReportId
    Write-Evidence $Name "`n**Logs de $Service para o reportId $ReportId** (filtrados e sanitizados):`n"
    Write-Evidence $Name '```text'
    foreach ($line in $lines) { Write-Evidence $Name $line }
    Write-Evidence $Name '```'
    return $lines
}

function New-Expense([string]$Token, [decimal]$Amount, [string]$Date) {
    $created = Invoke-Api "POST" "/api/expenses" $Token @{
        description = "Despesa do experimento CP3 $script:RunId"; amount = $Amount; expenseDate = $Date; categoryId = 1
    }
    if ($created.Status -ne 201) { throw "Criacao de despesa falhou (HTTP $($created.Status))." }
    $submitted = Invoke-Api "POST" "/api/expenses/$($created.Json.id)/submit" $Token $null
    if ($submitted.Status -ne 200) { throw "Envio de despesa falhou (HTTP $($submitted.Status))." }
    return $created.Json.id
}

function Initialize-Actors {
    if ($script:Context.ContainsKey("Tokens")) { return }
    $password = New-RandomPassword
    $adminToken = Get-Token $envValues["SEED_ADMIN_EMAIL"] $envValues["SEED_ADMIN_PASSWORD"]
    $actors = [ordered]@{ employeeA = "Employee"; employeeB = "Employee"; approver = "Approver"; finance = "Finance"; auditor = "Auditor" }
    $emails = @{}
    foreach ($name in $actors.Keys) {
        $emails[$name] = "cp3-$script:RunId-$name@example.com"
        $registered = Invoke-Api "POST" "/register" $null @{ email = $emails[$name]; password = $password }
        if ($registered.Status -notin 200, 201) { throw "Cadastro falhou para $name (HTTP $($registered.Status))." }
    }
    $users = (Invoke-Api "GET" "/api/admin/users" $adminToken $null).Json
    foreach ($name in $actors.Keys) {
        $id = ($users | Where-Object { $_.email -eq $emails[$name] }).id
        $updated = Invoke-Api "PUT" "/api/admin/users/$id/roles" $adminToken @{ roles = @($actors[$name]) }
        if ($updated.Status -ne 200) { throw "Atribuicao de role falhou para $name (HTTP $($updated.Status))." }
    }
    $tokens = @{}
    foreach ($name in $actors.Keys) { $tokens[$name] = Get-Token $emails[$name] $password }
    $script:Context["Tokens"] = $tokens
    $script:Context["Emails"] = $emails
    $script:Context["Password"] = $password
}

function Approve-And-Pay([string]$ExpenseId, [bool]$Pay) {
    $tokens = $script:Context["Tokens"]
    $approved = Invoke-Api "POST" "/api/expenses/$ExpenseId/approve" $tokens["approver"] $null
    if ($approved.Status -ne 200) { throw "Aprovacao falhou (HTTP $($approved.Status))." }
    if ($Pay) {
        $paid = Invoke-Api "POST" "/api/expenses/$ExpenseId/pay" $tokens["finance"] $null
        if ($paid.Status -ne 200) { throw "Pagamento falhou (HTTP $($paid.Status))." }
    }
}

function Test-Workbook([string]$Path, [decimal[]]$Numbers) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path))
    try {
        $sheet = $zip.Entries | Where-Object { $_.FullName -like "xl/worksheets/*.xml" } | Select-Object -First 1
        $reader = [System.IO.StreamReader]::new($sheet.Open())
        $xml = $reader.ReadToEnd(); $reader.Dispose()
    } finally { $zip.Dispose() }
    $values = [regex]::Matches($xml, "<v>([^<]+)</v>") | ForEach-Object { [decimal]::Parse($_.Groups[1].Value, [cultureinfo]::InvariantCulture) }
    foreach ($number in $Numbers) { if ($values -notcontains $number) { return $false } }
    return $true
}

function Invoke-E01 {
    $name = "E01-fluxo-normal"; Remove-Item (Join-Path $OutputDirectory "$name.md") -ErrorAction SilentlyContinue
    Write-Evidence $name "# E01 - Fluxo normal`n`nExecucao $script:RunId (UTC).`n"
    Initialize-Actors; $t = $script:Context["Tokens"]
    # Dados conhecidos (exemplo oficial): Mine = 3 reembolsos e 175; fora do periodo e de outro usuario ficam de fora.
    Approve-And-Pay (New-Expense $t["employeeA"] 100 "2026-09-01") $false
    Approve-And-Pay (New-Expense $t["employeeA"] 50 "2026-09-30") $false
    Approve-And-Pay (New-Expense $t["employeeA"] 25 "2026-09-15") $true
    Approve-And-Pay (New-Expense $t["employeeB"] 200 "2026-09-10") $false
    Approve-And-Pay (New-Expense $t["employeeA"] 500 "2026-10-01") $true
    Write-Evidence $name "Dados: A=100 (Approved), 50 (Approved), 25 (Paid) no periodo; B=200 (Approved); A=500 (Paid) fora do periodo.`n"
    $created = Invoke-Api "POST" "/api/reports" $t["employeeA"] @{ fromDate = "2026-09-01"; toDate = "2026-09-30"; scope = "Mine" }
    $id = $created.Json.id; $script:Context["E01ReportId"] = $id
    Write-Evidence $name "reportId: ``$id``"
    Test-Check $name "POST /api/reports retorna 202" ($created.Status -eq 202) "(HTTP $($created.Status))"
    Test-Check $name "Location aponta para o detalhe" ($created.Raw.Headers["Location"] -match "/api/reports/$id") ""
    $done = Wait-Report $id $t["employeeA"] "Completed"
    $result = $done.Json.result
    Test-Check $name "Relatorio chega a Completed" ($done.Json.status -eq "Completed") ""
    if ($done.Json.status -ne "Completed") { return }
    Test-Check $name "totalCount = 3 e totalAmount = 175" ($result.totalCount -eq 3 -and [decimal]$result.totalAmount -eq 175) ""
    $approved = $result.byStatus | Where-Object { $_.status -eq "Approved" }; $paid = $result.byStatus | Where-Object { $_.status -eq "Paid" }
    Test-Check $name "Approved 2 / 150 e Paid 1 / 25" ($approved.count -eq 2 -and [decimal]$approved.amount -eq 150 -and $paid.count -eq 1 -and [decimal]$paid.amount -eq 25) ""
    Test-Check $name "Cinco estados presentes" (@($result.byStatus).Count -eq 5) ""
    $file = Join-Path $OutputDirectory "report-$id.xlsx"
    $excel = Invoke-WebRequest -Uri "$BaseUrl/api/reports/$id/excel" -Headers @{ Authorization = "Bearer $($t['employeeA'])" } -OutFile $file -PassThru -SkipHttpErrorCheck
    Test-Check $name "Excel retorna 200 e .xlsx" ($excel.StatusCode -eq 200 -and $excel.Headers["Content-Disposition"] -match "report-$id\.xlsx") ""
    Test-Check $name "Planilha e um arquivo zip OOXML" ((Get-Content $file -AsByteStream -TotalCount 2) -join "," -eq "80,75") ""
    Test-Check $name "Planilha contem 3, 175, 150 e 25" (Test-Workbook $file @(3, 175, 150, 25)) ""
    Add-Logs $name "api" $id | Out-Null
    Add-Logs $name "worker" $id | Out-Null
}

function Invoke-E02 {
    $name = "E02-worker-parado"; Remove-Item (Join-Path $OutputDirectory "$name.md") -ErrorAction SilentlyContinue
    Write-Evidence $name "# E02 - Worker parado`n`nExecucao $script:RunId (UTC).`n"
    Initialize-Actors; $t = $script:Context["Tokens"]
    docker compose stop worker | Out-Null
    $created = Invoke-Api "POST" "/api/reports" $t["employeeA"] @{ fromDate = "2026-09-01"; toDate = "2026-09-30"; scope = "Mine" }
    $id = $created.Json.id; $script:Context["E02ReportId"] = $id
    Write-Evidence $name "reportId: ``$id``"
    Test-Check $name "POST retorna 202 com o worker parado" ($created.Status -eq 202) "(HTTP $($created.Status))"
    Start-Sleep -Seconds 3
    $queue = Get-RabbitQueue
    Test-Check $name "RabbitMQ: mensagem Ready na fila" ($queue.Ready -ge 1) "(Ready=$($queue.Ready), Unacked=$($queue.Unacked))"
    $pending = Invoke-Api "GET" "/api/reports/$id" $t["employeeA"] $null
    Test-Check $name "Relatorio continua Pending" ($pending.Json.status -eq "Pending") ""
    $excel = Invoke-Api "GET" "/api/reports/$id/excel" $t["employeeA"] $null
    Test-Check $name "Excel do relatorio nao concluido retorna 409" ($excel.Status -eq 409) ""
    docker compose up -d worker | Out-Null
    $done = Wait-Report $id $t["employeeA"] "Completed"
    Test-Check $name "Mesmo reportId chega a Completed" ($done.Json.status -eq "Completed" -and $done.Json.id -eq $id) ""
    Start-Sleep -Seconds 2
    $after = Get-RabbitQueue
    Test-Check $name "Fila esvaziada (Ready=0, Unacked=0)" ($after.Ready -eq 0 -and $after.Unacked -eq 0) "(Ready=$($after.Ready), Unacked=$($after.Unacked))"
    Add-Logs $name "worker" $id | Out-Null
}

function Invoke-E03 {
    $name = "E03-duplicata"; Remove-Item (Join-Path $OutputDirectory "$name.md") -ErrorAction SilentlyContinue
    Write-Evidence $name "# E03 - Duplicata apos reinicio do worker`n`nExecucao $script:RunId (UTC).`n"
    Initialize-Actors; $t = $script:Context["Tokens"]
    if (-not $script:Context.ContainsKey("E01ReportId")) { Invoke-E01 }
    $id = $script:Context["E01ReportId"]
    Write-Evidence $name "reportId: ``$id`` (concluido no E01)"
    $before = Invoke-Api "GET" "/api/reports/$id" $t["employeeA"] $null
    $countBefore = @((Invoke-Api "GET" "/api/reports" $t["employeeA"] $null).Json).Count
    docker compose restart worker | Out-Null
    Start-Sleep -Seconds 5
    $publish = Publish-Duplicate $id
    Test-Check $name "Mensagem republicada na fila (routed)" ($publish.routed -eq $true) ""
    Start-Sleep -Seconds 8
    $after = Invoke-Api "GET" "/api/reports/$id" $t["employeeA"] $null
    $countAfter = @((Invoke-Api "GET" "/api/reports" $t["employeeA"] $null).Json).Count
    Test-Check $name "Resultado identico" (($before.Json.result | ConvertTo-Json -Depth 6 -Compress) -eq ($after.Json.result | ConvertTo-Json -Depth 6 -Compress)) ""
    Test-Check $name "generatedAt e completedAt inalterados" ($before.Json.result.generatedAt -eq $after.Json.result.generatedAt -and $before.Json.completedAt -eq $after.Json.completedAt) ""
    Test-Check $name "Quantidade de solicitacoes inalterada" ($countBefore -eq $countAfter) "($countBefore -> $countAfter)"
    $queue = Get-RabbitQueue
    Test-Check $name "Fila vazia apos consumo (ACK)" ($queue.Ready -eq 0 -and $queue.Unacked -eq 0) ""
    $lines = Add-Logs $name "worker" $id
    Test-Check $name "Log do worker registra duplicata ignorada" (@($lines | Where-Object { $_ -match "duplicate delivery ignored" }).Count -ge 1) ""
}

function Invoke-E04 {
    $name = "E04-isolamento"; Remove-Item (Join-Path $OutputDirectory "$name.md") -ErrorAction SilentlyContinue
    Write-Evidence $name "# E04 - Isolamento`n`nExecucao $script:RunId (UTC).`n"
    Initialize-Actors; $t = $script:Context["Tokens"]
    $period = @{ fromDate = "2026-09-01"; toDate = "2026-09-30" }
    $own = Invoke-Api "POST" "/api/reports" $t["employeeA"] ($period + @{ scope = "Mine" })
    $ownId = $own.Json.id; if (-not $ownId) { $ownId = $own.Json.Id }
    Write-Evidence $name "Relatorio do Employee A: ``$ownId``"
    Test-Check $name "Employee solicita Mine" ($own.Status -in 202, 503 -and $ownId) "(HTTP $($own.Status))"
    Test-Check $name "Employee nao solicita Global (403)" ((Invoke-Api "POST" "/api/reports" $t["employeeA"] ($period + @{ scope = "Global" })).Status -eq 403) ""
    Test-Check $name "Auditor nao solicita Mine (403)" ((Invoke-Api "POST" "/api/reports" $t["auditor"] ($period + @{ scope = "Mine" })).Status -eq 403) ""
    $global = Invoke-Api "POST" "/api/reports" $t["auditor"] ($period + @{ scope = "Global" })
    Test-Check $name "Auditor solicita Global" ($global.Status -in 202, 503) "(HTTP $($global.Status))"
    Test-Check $name "Anonimo recebe 401" ((Invoke-Api "GET" "/api/reports" $null $null).Status -eq 401) ""
    Test-Check $name "Employee B: detalhe alheio 404" ((Invoke-Api "GET" "/api/reports/$ownId" $t["employeeB"] $null).Status -eq 404) ""
    Test-Check $name "Employee B: Excel alheio 404" ((Invoke-Api "GET" "/api/reports/$ownId/excel" $t["employeeB"] $null).Status -eq 404) ""
    Test-Check $name "Auditor: detalhe alheio 404" ((Invoke-Api "GET" "/api/reports/$ownId" $t["auditor"] $null).Status -eq 404) ""
    Test-Check $name "Auditor: Excel alheio 404" ((Invoke-Api "GET" "/api/reports/$ownId/excel" $t["auditor"] $null).Status -eq 404) ""
    $auditorList = @((Invoke-Api "GET" "/api/reports" $t["auditor"] $null).Json)
    Test-Check $name "Listagem do Auditor nao contem o relatorio alheio" (@($auditorList | Where-Object { $_.id -eq $ownId }).Count -eq 0) ""
    $auditorId = $global.Json.id; if (-not $auditorId) { $auditorId = $global.Json.Id }
    Test-Check $name "Employee A: detalhe do relatorio do Auditor 404" ((Invoke-Api "GET" "/api/reports/$auditorId" $t["employeeA"] $null).Status -eq 404) ""
    Test-Check $name "Controle: Employee A le o proprio relatorio (200)" ((Invoke-Api "GET" "/api/reports/$ownId" $t["employeeA"] $null).Status -eq 200) ""
}

function Invoke-E05 {
    $name = "E05-persistencia-versao"; Remove-Item (Join-Path $OutputDirectory "$name.md") -ErrorAction SilentlyContinue
    Write-Evidence $name "# E05 - Persistencia e versao`n`nExecucao $script:RunId (UTC).`n"
    Initialize-Actors; $t = $script:Context["Tokens"]
    if (-not $script:Context.ContainsKey("E01ReportId")) { Invoke-E01 }
    $id = $script:Context["E01ReportId"]
    Write-Evidence $name "reportId: ``$id`` (concluido no E01)"
    $before = Invoke-Api "GET" "/api/reports/$id" $t["employeeA"] $null
    $revision = {
        param($service)
        $labels = docker inspect (docker compose ps -q $service) --format '{{json .Config.Labels}}' | ConvertFrom-Json
        return [string]$labels.'org.opencontainers.image.revision'
    }
    $images = docker compose images 2>&1 | Out-String
    Write-Evidence $name "`n**docker compose images (antes):**`n"; Write-Evidence $name '```text'; Write-Evidence $name $images; Write-Evidence $name '```'
    $apiBefore = & $revision "api"; $workerBefore = & $revision "worker"
    $expected = $envValues["IMAGE_TAG"]
    Test-Check $name "Revisao da imagem api = IMAGE_TAG" ($apiBefore -eq $expected) "($apiBefore)"
    Test-Check $name "Revisao da imagem worker = IMAGE_TAG" ($workerBefore -eq $expected) "($workerBefore)"
    docker compose up -d --force-recreate api worker | Out-Null
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds); $healthy = $false
    do { Start-Sleep -Seconds 3; try { $healthy = (Invoke-RestMethod "$BaseUrl/health").status -eq "ok" } catch { $healthy = $false } } while (-not $healthy -and (Get-Date) -lt $deadline)
    Test-Check $name "API saudavel apos recriar api e worker" $healthy ""
    $token = Get-Token $script:Context["Emails"]["employeeA"] $script:Context["Password"]
    $after = Invoke-Api "GET" "/api/reports/$id" $token $null
    Test-Check $name "Resultado anterior consultavel apos recriar" ($after.Status -eq 200 -and $after.Json.status -eq "Completed") ""
    Test-Check $name "Resultado identico ao anterior" (($before.Json.result | ConvertTo-Json -Depth 6 -Compress) -eq ($after.Json.result | ConvertTo-Json -Depth 6 -Compress)) ""
    Test-Check $name "Excel ainda baixavel" ((Invoke-Api "GET" "/api/reports/$id/excel" $token $null).Status -eq 200) ""
    Test-Check $name "Revisao inalterada apos recriar (api)" ((& $revision "api") -eq $expected) ""
    Test-Check $name "Revisao inalterada apos recriar (worker)" ((& $revision "worker") -eq $expected) ""
    Write-Evidence $name "`nComando usado: ``docker compose up -d --force-recreate api worker`` (sem remover volumes)."
}

$steps = if ($Experiment -eq "All") { "E01", "E02", "E03", "E04", "E05" } else { @($Experiment) }
foreach ($step in $steps) {
    Write-Host "== $step =="
    & "Invoke-$step"
}

$summary = Join-Path $OutputDirectory "resumo.md"
Set-Content -Path $summary -Encoding utf8 -Value "# Resumo da execucao $script:RunId`n`nExperimentos: $($steps -join ', ')`n`nFalhas: $script:Failures"
Write-Host "Falhas: $script:Failures. Evidencias em $OutputDirectory"
if ($script:Failures -gt 0) { exit 1 }
