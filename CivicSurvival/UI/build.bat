@echo off
set "CSII_USERDATAPATH=%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II"
cd /d "%~dp0"
npm run build
