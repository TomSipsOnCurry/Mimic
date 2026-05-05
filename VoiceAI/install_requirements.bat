@echo off
REM Install AI.py prerequisites for Windows

echo Installing AI.py prerequisites...
pip install -r requirements.txt
echo.
echo Installation complete!
echo.
echo Next steps:
echo 1. Place your GGUF model file in this directory as 'model.gguf'
echo 2. Run: python AI.py
pause
