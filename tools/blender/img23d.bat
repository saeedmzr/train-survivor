@echo off
REM Image -> 3D with TripoSR (local, RTX 5070 / 8 GB).
REM   img23d.bat <image> [out-name]
REM Output lands in C:\Users\saeed\Apps\img3d\out\<out-name>\0
setlocal
set VENV=C:\Users\saeed\Apps\img3d-venv\Scripts\python.exe
set SRC=%~1
if "%SRC%"=="" (
  echo usage: img23d.bat ^<image^> [out-name]
  exit /b 1
)
if "%~2"=="" (set NAME=%~n1) else (set NAME=%~2)
mkdir "%TEMP%\img23d_in" 2>nul
mkdir "C:\Users\saeed\Apps\img3d\out\%NAME%\0" 2>nul

REM TripoSR wants a grey-background square image; composite RGBA cutouts onto mid grey first.
"%VENV%" "C:\Users\saeed\Apps\img3d\prep_input.py" "%SRC%" "%TEMP%\img23d_in\%NAME%.png" || exit /b 1

cd /d "C:\Users\saeed\Apps\img3d\TripoSR"
"%VENV%" run.py "%TEMP%\img23d_in\%NAME%.png" --device cuda:0 --no-remove-bg ^
  --output-dir "C:\Users\saeed\Apps\img3d\out\%NAME%" ^
  --model-save-format glb --bake-texture --texture-resolution 1024
echo.
echo mesh: C:\Users\saeed\Apps\img3d\out\%NAME%\0\mesh.glb   (note: xatlas writes OBJ text, rename to .obj for Blender)
