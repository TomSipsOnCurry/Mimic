#!/usr/bin/env python3
import sys
import pyttsx3

if len(sys.argv) < 2:
    sys.exit(1)

text = sys.argv[1]
engine = pyttsx3.init()
engine.setProperty('rate', 150)
engine.say(text)
engine.runAndWait()
