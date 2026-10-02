@echo off
setlocal
cd /d "%~dp0"

where python >nul 2>&1
if errorlevel 1 (
  exit /b 0
)

netstat -ano | findstr /R /C:":64316 .*LISTENING" >nul 2>&1
if not errorlevel 1 (
  echo Camera service is already running on port 64316. Not starting a second copy.
  exit /b 0
)

if exist ".venv\Scripts\python.exe" (
  ".venv\Scripts\python.exe" -m uvicorn main:app --host 0.0.0.0 --port 64316
) else (
  python -m uvicorn main:app --host 0.0.0.0 --port 64316
)
