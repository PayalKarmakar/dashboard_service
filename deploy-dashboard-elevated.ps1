$ErrorActionPreference = 'Stop'
try {
  Copy-Item -Path 'D:\freelan\dashboard_service\DashboardService\bin\Release\net8.0-windows\*' -Destination 'C:\Program Files\SRP Innovations\DashboardService' -Recurse -Force
  'DEPLOY_SUCCESS' | Out-File -FilePath 'D:\freelan\dashboard_service\deploy-elevated.log' -Encoding utf8
} catch {
  "DEPLOY_FAILED: $($_.Exception.Message)" | Out-File -FilePath 'D:\freelan\dashboard_service\deploy-elevated.log' -Encoding utf8
  exit 1
}
