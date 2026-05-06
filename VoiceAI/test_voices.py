#!/usr/bin/env python3
"""
test_voices.py - Interactive voice testing for TTS
Allows testing different voices and settings to find the best fit for the game.
"""

import pyttsx3
import time
import re


def list_voices():
    """List all available voices on the system."""
    engine = pyttsx3.init()
    voices = engine.getProperty('voices')
    
    print("\n" + "=" * 70)
    print("AVAILABLE VOICES FOR TEXT-TO-SPEECH")
    print("=" * 70)
    
    # Filter for English voices
    english_voices = [v for v in voices if 'en' in str(v.languages).lower() or 'en' in str(v.id).lower()]
    
    print(f"\nFound {len(english_voices)} English voices:\n")
    for i, voice in enumerate(english_voices):
        print(f"[{i:2}] {voice.name:30} | {voice.id[:50]}")
    
    return english_voices


def test_voice(voice_id, text, rate=150, volume=1.0):
    """Test a specific voice with given text and settings."""
    engine = pyttsx3.init()
    engine.setProperty('rate', rate)
    engine.setProperty('volume', volume)
    
    # Set the voice
    voices = engine.getProperty('voices')
    for voice in voices:
        if voice.id == voice_id:
            engine.setProperty('voice', voice_id)
            break
    
    print(f"\n[Speaking] Rate: {rate}, Volume: {volume * 100:.0f}%")
    engine.say(text)
    engine.runAndWait()
    engine.stop()


def get_voice_by_index(voices, index):
    """Get voice by index."""
    try:
        return voices[int(index)]
    except (ValueError, IndexError):
        return None


def interactive_test():
    """Interactive voice testing menu."""
    voices = list_voices()
    
    test_phrases = {
        '1': "where are you",
        '2': "lost in the basement man",
        '3': "too scared dude",
        '4': "i see it, run",
        '5': "help me",
        '6': "what the, run",
        '7': "open the door",
        '8': "same here honestly",
        '9': "Custom message"
    }
    
    selected_voice = None
    
    while True:
        print("\n" + "=" * 70)
        print("VOICE TESTING MENU")
        print("=" * 70)
        
        if selected_voice:
            voice_info = selected_voice
            print(f"\nSelected Voice: {voice_info.name}")
            print(f"ID: {voice_info.id}")
        else:
            print("\nNo voice selected")
        
        print("\nOptions:")
        print("  [s] Select a voice")
        print("  [t] Test with phrase")
        print("  [c] Custom text")
        print("  [a] Adjust rate and volume")
        print("  [l] List all voices")
        print("  [q] Quit")
        
        choice = input("\nEnter choice: ").strip().lower()
        
        if choice == 'q':
            print("\nGoodbye!")
            break
        
        elif choice == 's':
            try:
                idx = int(input(f"Enter voice number (0-{len(voices)-1}): ").strip())
                selected_voice = get_voice_by_index(voices, idx)
                if selected_voice:
                    print(f"✓ Selected: {selected_voice.name}")
                else:
                    print("✗ Invalid voice number")
            except ValueError:
                print("✗ Please enter a valid number")
        
        elif choice == 't':
            if not selected_voice:
                print("✗ Please select a voice first")
                continue
            
            print("\nTest phrases:")
            for key, phrase in test_phrases.items():
                if key != '9':
                    print(f"  [{key}] {phrase}")
            print(f"  [c] Custom message")
            
            phrase_choice = input("Enter choice: ").strip()
            
            if phrase_choice in test_phrases and phrase_choice != '9':
                test_voice(selected_voice.id, test_phrases[phrase_choice])
            elif phrase_choice == 'c':
                custom_text = input("Enter custom text: ").strip()
                if custom_text:
                    test_voice(selected_voice.id, custom_text)
        
        elif choice == 'c':
            if not selected_voice:
                print("✗ Please select a voice first")
                continue
            
            custom_text = input("Enter text to speak: ").strip()
            if custom_text:
                test_voice(selected_voice.id, custom_text)
        
        elif choice == 'a':
            if not selected_voice:
                print("✗ Please select a voice first")
                continue
            
            try:
                rate = int(input("Enter speech rate (100-300, default 150): ").strip() or "150")
                volume = float(input("Enter volume (0.0-1.0, default 1.0): ").strip() or "1.0")
                
                # Test with adjustment
                test_phrase = input("Enter test phrase: ").strip() or "where are you"
                test_voice(selected_voice.id, test_phrase, rate=rate, volume=volume)
                
                print(f"\n✓ Tested with rate={rate}, volume={volume}")
                print(f"  Recommendation: engine.setProperty('rate', {rate})")
                print(f"  Recommendation: engine.setProperty('volume', {volume})")
                
            except ValueError:
                print("✗ Invalid input")
        
        elif choice == 'l':
            list_voices()
        
        else:
            print("✗ Invalid choice")


if __name__ == "__main__":
    interactive_test()
