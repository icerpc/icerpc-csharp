@echo off
powershell -ExecutionPolicy ByPass -NoProfile -Command "& 'build\build.ps1'" %*
