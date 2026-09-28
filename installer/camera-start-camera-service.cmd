@echo off
setlocal
cd /d "%~dp0"

if exist ".venv\Scripts\python.exe" (
  ".venv\Scripts\python.exe" -m uvicorn main:app --host 0.0.0.0 --port 64316
  exit /b %errorlevel%
)

where python >nul 2>&1
if errorlevel 1 (
  exit /b 0
)

python -m uvicorn main:app --host 0.0.0.0 --port 64316
