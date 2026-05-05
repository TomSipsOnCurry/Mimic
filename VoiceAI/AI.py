#!/usr/bin/env python3
"""
╔══════════════════════════════════════════════════════════╗
║   M I M I C  —  SLM Neural Imitation System              ║
║   In-Game Voice Chat Radio Edition (Llama.cpp + TTS)     ║
╚══════════════════════════════════════════════════════════╝
"""

import sys
import os
import time
import random
import re
import threading

# ─── Library Checks ────────────────────────────────────────────────────────────
try:
    from llama_cpp import Llama
except ImportError:
    print("Please install the SLM engine: pip install llama-cpp-python")
    sys.exit(1)

try:
    from voicebox import SimpleVoicebox
    from voicebox.tts import gTTS
    from voicebox.effects import Vocoder, Normalize
except ImportError:
    print("Please install voice dependencies: pip install voicebox gTTS numpy scipy")
    sys.exit(1)

# ─── ANSI colours ──────────────────────────────────────────────────────────────
R   = "\033[0m"
RED = "\033[31m"; GRN = "\033[32m"; YEL = "\033[33m"
CYN = "\033[36m"; WHT = "\033[37m"; BLD = "\033[1m"; DIM = "\033[2m"

# ══════════════════════════════════════════════════════════════════════════════
#  1. SLM ENGINE SETUP + VOICEBOX
# ══════════════════════════════════════════════════════════════════════════════
class SLMMimic:
    def __init__(self, model_path="model.gguf"):
        if not os.path.exists(model_path):
            print(f"{RED}Error: Could not find '{model_path}'{R}")
            print(f"Please place your GGUF model here and name it '{model_path}'")
            sys.exit(1)

        print(f"  {DIM}Loading Neural Core ({model_path})...{R}")
        self.llm = Llama(
            model_path=model_path,
            n_gpu_layers=-1, 
            n_ctx=2048,      
            verbose=False    
        )
        
        print(f"  {DIM}Initializing Walkie-Talkie Audio (Voicebox)...{R}")
        # Setup voicebox with the robotic/walkie-talkie effect
        self.voicebox = SimpleVoicebox(
            tts=gTTS(), 
            effects=[Vocoder.build(), Normalize()]
        )
        
        self.chat_history =[]
        
        self.system_prompt = (
            "You are a casual gamer playing a scary multiplayer co-op horror game. "
            "You are texting your friend (the user) in the in-game chat. "
            "RULES:\n"
            "1. NEVER act like an AI or an assistant.\n"
            "2. Keep it extremely short (1 to 8 words). You are busy surviving.\n"
            "3. Use casual gamer slang (bro, idk, wtf, nah, yeah, dude, wait, rip, lag).\n"
            "4. NEVER use capital letters or periods. Talk lazily.\n"
            "5. React scared to the game: panic if there's a monster, banter if it's safe, or say where you are.\n\n"
            "EXAMPLES OF YOUR BEHAVIOR:\n"
            "User: where are you\n"
            "You: idk im lost in the basement\n"
            "User: i died\n"
            "You: bro rip lol im running away\n"
            "User: open the door\n"
            "You: nah dude im too scared\n"
            "User: it is looking at me\n"
            "You: wtf run away"
        )

    def generate_response(self, player_message: str) -> str:
        self.chat_history.append({"role": "user", "content": player_message})
        
        if len(self.chat_history) > 6:
            self.chat_history.pop(0)

        messages = [{"role": "system", "content": self.system_prompt}]
        messages.extend(self.chat_history)

        output = self.llm.create_chat_completion(
            messages=messages,
            max_tokens=25,
            temperature=0.8,        
            top_p=0.9,
            repeat_penalty=1.15,    
            stop=["\n", "User:", "<|im_end|>"] 
        )
        
        raw_response = output['choices'][0]['message']['content'].strip()
        
        # Strip rogue characters but allow typical gamer punctuation like ! ? and '
        clean_response = re.sub(r'[^a-zA-Z0-9\s\?\'\!]', '', raw_response).lower().strip()
        
        if clean_response.startswith("you:"):
            clean_response = clean_response[4:].strip()
            
        clean_response = " ".join(clean_response.split())
        self.chat_history.append({"role": "assistant", "content": clean_response})
        
        return clean_response

    def speak(self, text: str):
        """Runs the Voicebox TTS engine."""
        if not text: return
        try:
            self.voicebox.say(text)
        except Exception as e:
            # Silently fail if internet drops so the game doesn't crash
            pass

# ══════════════════════════════════════════════════════════════════════════════
#  2. TERMINAL UI & THREADING
# ══════════════════════════════════════════════════════════════════════════════
def stream_text(text: str):
    """Prints text slowly for realism."""
    for char in text:
        sys.stdout.write(char)
        sys.stdout.flush()
        time.sleep(random.uniform(0.02, 0.08))
    print()

def print_header():
    os.system("cls" if os.name == "nt" else "clear")
    print(f"\n{RED}{BLD}")
    print("  ███╗   ███╗██╗███╗   ███╗██╗ ██████╗ ")
    print("  ████╗ ████║██║████╗ ████║██║██╔════╝ ")
    print("  ██╔████╔██║██║██╔████╔██║██║██║      ")
    print("  ██║╚██╔╝██║██║██║╚██╔╝██║██║██║      ")
    print("  ██║ ╚═╝ ██║██║██║ ╚═╝ ██║██║╚██████╗ ")
    print("  ╚═╝     ╚═╝╚═╝╚═╝     ╚═╝╚═╝ ╚═════╝ ")
    print(f"{R}{DIM}  SLM Neural Engine  ·  In-Game Voice Radio{R}")
    print(f"  {DIM}{'─'*54}{R}\n")

def run():
    print_header()
    mimic = SLMMimic("model.gguf")
    
    print_header()
    print(f"  {DIM}Radio initialized. Your friend is listening.{R}\n")

    while True:
        try:
            raw = input(f"  {GRN}>{R} ").strip()
        except (EOFError, KeyboardInterrupt):
            print(f"\n\n  {DIM}Connection lost.{R}\n"); break

        if not raw: continue
        if raw.lower() in ("quit", "exit"): break

        print(f"\n  {GRN}{BLD}[ YOU  ]{R}  {WHT}{raw}{R}")
        
        # Simulate SLM thinking time
        sys.stdout.write(f"  {DIM}{YEL}[ ALLY ]  . . .{R}   ")
        sys.stdout.flush()
        
        response = mimic.generate_response(raw)
        
        # Clear the "thinking" line
        sys.stdout.write("\r" + " " * 30 + "\r")
        sys.stdout.flush()
        
        # Print the speaker tag
        sys.stdout.write(f"  {YEL}{BLD}[ ALLY ]{R}  {YEL}")
        
        # Start Voicebox in a background thread so the UI doesn't freeze
        # This makes the audio play out loud simultaneously as the text types out!
        audio_thread = threading.Thread(target=mimic.speak, args=(response,), daemon=True)
        audio_thread.start()
        
        # Type the text to the screen
        stream_text(response)
        sys.stdout.write(R + "\n")
        
        # Wait for the audio to finish speaking before allowing the player to type again
        audio_thread.join()

if __name__ == "__main__":
    run()