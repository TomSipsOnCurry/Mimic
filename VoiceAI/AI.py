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
import time
import random
import re
import threading
from collections import deque

from llama_cpp import Llama

# ─── REGEX CLEANING OF SHORT FORMS (manual removal) ───────────────────────────
def remove_short_forms(text: str) -> str:
    """Remove or replace common short forms using regex."""
    patterns = {
        r'\bngl\b': '',
        r'\bidk\b': 'i don\'t know',
        r'\bwtf\b': 'what the',
        r'\blmao\b': 'laughing',
        r'\bikr\b': 'i know right',
        r'\btbh\b': 'honestly',
        r'\bomg\b': 'oh my god',
        r'\bty\b': 'thank you',
        r'\bpls\b': 'please',
        r'\bthx\b': 'thanks',
        r'\bu\b': 'you',
        r'\br\b': 'are',
        r'\by\b': 'why',
    }
    text = text.lower()
    for pattern, replacement in patterns.items():
        text = re.sub(pattern, replacement, text)
    text = re.sub(r'\s+', ' ', text).strip()
    text = re.sub(r'(\w+)ngl\b', r'\1', text)
    return text

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

        self.llm = Llama(
            model_path=model_path,
            n_gpu_layers=-1,
            n_ctx=2048,
            verbose=False
        )
        self.max_history = max_history
        self.chat_history = deque(maxlen=max_history * 2)  # stores both user and ally messages

        self.system_prompt = (
            "You are a casual gamer playing a scary multiplayer co-op horror game with the user. \n"
            "There is a monster trying to catch you and the player.\n"
            "You must pretend to evade the monster, hide from it, and panic.\n"
            "You are texting your friend (the user) in the in-game chat.\n"
            "RULES:\n"
            "1. NEVER act like an AI or assistant. Never explain yourself.\n"
            "2. Keep replies SHORT but natural – between 2 and 12 words.\n"
            "3. Use scared, casual language, but NEVER use abbreviations like 'ngl', 'idk', 'wtf', 'lmao', 'ikr'. \n"
            "4. Never use capitals or periods (lazy typing).\n"
            "5. Express fear when there's danger, and worry.\n"
            "6. Mirror the user's tone a little.\n\n"
            "7. Always defend that you are not the monster, you are a player."
            "EXAMPLES:\n"
            "User: where are you\n"
            "You: lost in the basement man\n"
            "User: i died\n"
            "You: saw that, running\n"
            "User: open the door\n"
            "You: too scared dude\n"
            "User: it's looking at me\n"
            "You: what the run\n"
            "User: what's the safe word?\n"
            "You: we don't have one\n"
            "User: bro i don't trust you\n"
            "You: same here honestly\n"
        )

    def predict_user_state(self, user_message: str) -> str:
        """Predict user state based on recent history + current message."""
        # Build recent conversation string from in‑memory history
        history_text = ""
        for msg in list(self.chat_history)[-6:]:
            history_text += f"{msg['role'].capitalize()}: {msg['content']}\n"

        predictor_prompt = (
            "Based on the recent conversation and the user's latest message, "
            "describe the user's likely intent and emotional state in one short sentence (max 12 words). "
            "If the user is accusing you of anything, say 'accusing me of ...'"
            "Be specific and natural. Examples:\n"
            "- 'asking for my location, sounds curious'\n"
            "- 'panicking because they saw the monster'\n"
            "- 'joking around, feeling amused'\n"
            "- 'just saying hi, neutral mood'\n"
            "- 'demanding i open a door, a bit annoyed'\n\n"
            f"Recent conversation:\n{history_text}\n"
            f"User's latest message: {user_message}\n"
            "User state:"
        )

        output = self.llm.create_chat_completion(
            messages=[{"role": "user", "content": predictor_prompt}],
            max_tokens=25,
            temperature=0.4,
            stop=["\n"]
        )
        prediction = output['choices'][0]['message']['content'].strip()
        if not prediction:
            prediction = "just talking, neutral"
        return prediction

    def generate_response(self, player_message: str) -> str:
        # Append current user message to history
        self.chat_history.append({"role": "user", "content": player_message})

        # Stage 1: predict user state
        user_state = self.predict_user_state(player_message)
        print(f"[ PREDICT ] {user_state}")

        # Stage 2: generate reply conditioned on prediction AND full recent history
        messages = [{"role": "system", "content": self.system_prompt}]
        messages.append({"role": "system", "content": f"The user's current state is: {user_state}. Respond accordingly."})
        # Add recent history (excluding the system prompts)
        #messages.extend(list(self.chat_history))

        output = self.llm.create_chat_completion(
            messages=messages,
            max_tokens=30,
            temperature=0.85,
            top_p=0.9,
            repeat_penalty=1.20,
            stop=["\n", "User:", "Ally:", "</s>"]
        )

        raw_response = output['choices'][0]['message']['content'].strip()
        clean_response = re.sub(r'[^a-zA-Z0-9\s\?\'\!]', '', raw_response).lower().strip()
        if clean_response.startswith("you:"):
            clean_response = clean_response[4:].strip()
        if clean_response.startswith("ally:"):
            clean_response = clean_response[5:].strip()
        clean_response = " ".join(clean_response.split())
        clean_response = remove_short_forms(clean_response)

        # Save ally response to history
        self.chat_history.append({"role": "assistant", "content": clean_response})

        return clean_response

# ─── TERMINAL UI ──────────────────────────────────────────────────────────────
def stream_text(text: str):
    for char in text:
        sys.stdout.write(char)
        sys.stdout.flush()
        time.sleep(random.uniform(0.02, 0.08))
    print()

def run():
    mimic = SLMMimic("model.gguf")

    while True:
        try:
            raw = input("> ").strip()
        except (EOFError, KeyboardInterrupt):
            break

        if not raw:
            continue
        if raw.lower() in ("quit", "exit"):
            break

        print(f"[ YOU ]  {raw}")

        response = mimic.generate_response(raw)

        print("[ ALLY ] ", end='')
        audio_thread = threading.Thread(target=speak, args=(response,), daemon=True)
        audio_thread.start()
        stream_text(response)
        audio_thread.join()

if __name__ == "__main__":
    run()