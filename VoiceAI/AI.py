<<<<<<< Updated upstream:VoiceAI/AI.py
"""
AI.py - Casual multiplayer co-op horror game companion AI

SETUP:
------
Before running this script, install prerequisites:
  macOS/Linux:  bash install_requirements.sh
  Windows:      install_requirements.bat

Or manually install with: pip install -r requirements.txt

Then place your GGUF model file in this directory as 'model.gguf'
"""

import sys
import os
=======
>>>>>>> Stashed changes:Assets/Scripts/VoiceAI/AI.py
import time
import random
import re
from llama_cpp import Llama


# ─── CLEAN TEXT ─────────────────────────────────────────────
def clean(text: str) -> str:
    text = text.lower()
    text = re.sub(r'[^a-z0-9\s\.\'\!\?]', '', text)
    return " ".join(text.split())

<<<<<<< Updated upstream:VoiceAI/AI.py
# ─── TTS ENGINE (removed) ───────────────────────────────────────────────────
# Text-to-speech has been removed in favor of a lightweight SAM-based player
# in VoiceAI/sam_player.html. The speak function now only prints the text so
# callers can still use it without a runtime TTS dependency.
def speak(text: str):
    if not text:
        return
    text = remove_short_forms(text)
    print(f"[TTS disabled] {text}")

# ─── SLM ENGINE WITH IN‑MEMORY HISTORY ONLY ──────────────────────────────────
class SLMMimic:
    def __init__(self, model_path="model.gguf", max_history=8):
        if not os.path.exists(model_path):
            print(f"Error: Could not find '{model_path}'")
            print(f"Please place your GGUF model here and name it '{model_path}'")
            sys.exit(1)
=======
>>>>>>> Stashed changes:Assets/Scripts/VoiceAI/AI.py

# ─── CORE AI ────────────────────────────────────────────────
class MimicAI:
    def __init__(self, model_path="model.gguf"):
        self.llm = Llama(
            model_path=model_path,
            n_gpu_layers=-1,
            n_ctx=2048,
            verbose=False
        )

        # user corpus (builds over time)
        self.user_corpus = []

        # fear corpus (static)
        self.fear_corpus = [
            "i see you",
            "you keep coming back",
            "you paused again",
            "you are still there",
            "i remember that",
            "you didnt mean to type that",
            "you hesitated",
        ]

        self.start_time = time.time()

    # ─── CHECK TIME GATE (10 min) ────────────────────────────
    def ready(self):
        return (time.time() - self.start_time) > 600  # 10 minutes

    # ─── GENERATE FROM CORPUS USING LLM ──────────────────────
    def generate_from_seed(self, seed: str):
        prompt = f"""
continue this in a short casual way (max 12 words, lowercase):

{seed}
"""

        output = self.llm(
            prompt,
            max_tokens=20,
            temperature=0.9,
            top_p=0.9,
            stop=["\n"]
        )

        text = output["choices"][0]["text"].strip()
        return clean(text)

    # ─── MAIN FUNCTION ───────────────────────────────────────
    def respond(self, user_text: str) -> str:
        user_text = clean(user_text)
        self.user_corpus.append(user_text)

        # defaults
        use_generated = False
        use_fear = False

        # probability logic
        if self.ready():
            if random.random() < 0.20:   # 20%: generate from convo corpus
                use_generated = True
            if random.random() < 0.05:   # 5%: fear injection
                use_fear = True

        # ─── FEAR MODE ───────────────────────────────────────
        if use_fear:
            seed = random.choice(self.fear_corpus)
            return self.generate_from_seed(seed)

        # ─── GENERATED FROM USER CORPUS ──────────────────────
        if use_generated and len(self.user_corpus) > 0:
            seed = random.choice(self.user_corpus)
            return self.generate_from_seed(seed)

        # ─── NORMAL RESPONSE ─────────────────────────────────
        output = self.llm(
            f"reply casually in 2-10 words, lowercase:\n{user_text}",
            max_tokens=20,
            temperature=0.8,
            stop=["\n"]
        )

        return clean(output["choices"][0]["text"])


# ─── SIMPLE FUNCTION LOOP ───────────────────────────────────
ai = MimicAI("model.gguf")

def send_message(msg: str) -> str:
    return ai.respond(msg)


# ─── OPTIONAL CLI ──────────────────────────────────────────
if __name__ == "__main__":
    while True:
        try:
            msg = input("> ")
        except:
            break

        if msg.lower() in ("exit", "quit"):
            break

        print(send_message(msg))