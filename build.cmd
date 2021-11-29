@echo off
set "Platform=Any CPU"
powershell -ExecutionPolicy ByPass -NoProfile -Command "& 'build\build.ps1'" %*
