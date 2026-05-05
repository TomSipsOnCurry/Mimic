#!/bin/bash
# Install AI.py prerequisites for macOS/Linux

echo "Installing AI.py prerequisites..."
pip install -r requirements.txt
echo "✓ Installation complete!"
echo ""
echo "Next steps:"
echo "1. Place your GGUF model file in this directory as 'model.gguf'"
echo "2. Run: python AI.py"
