#!/usr/bin/env python3
"""
AI.py - dependency-free Mimic chat generator.

This file intentionally uses only the Python standard library. It reads the
Unity chat log at VoiceAI/chat_log.json and writes the latest generated line to
VoiceAI/mimic_response.json. Unity has its own runtime Mimic component, so this
script is useful for testing or external tooling without adding build-time
Python packages.
"""

from __future__ import annotations

import argparse
import datetime as _datetime
import json
import random
import re
import time
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional


BASE_DIR = Path(__file__).resolve().parent
DEFAULT_CHAT_LOG = BASE_DIR / "chat_log.json"
DEFAULT_OUTPUT = BASE_DIR / "mimic_response.json"
MAX_HISTORY = 400
MAX_WORDS = 12

_WORD_RE = re.compile(r"[a-z0-9']+")


def _utc_now() -> str:
    return _datetime.datetime.utcnow().replace(microsecond=0).isoformat() + "Z"


def clean(text: str) -> str:
    text = " ".join(_WORD_RE.findall((text or "").lower()))
    return re.sub(r"\s+", " ", text).strip()


def _words(text: str) -> List[str]:
    return _WORD_RE.findall((text or "").lower())


def _coerce_message(entry: Any) -> Optional[Dict[str, str]]:
    if isinstance(entry, str):
        message = clean(entry)
        return {"sender": "Player", "message": message} if message else None

    if not isinstance(entry, dict):
        return None

    sender = str(entry.get("sender") or entry.get("name") or "Player").strip()
    message = str(entry.get("message") or entry.get("text") or entry.get("content") or "").strip()
    message = clean(message)

    if not message or sender.lower() == "mimic":
        return None

    return {"sender": sender or "Player", "message": message}


def _extract_entries(payload: Any) -> Iterable[Any]:
    if isinstance(payload, list):
        return payload

    if isinstance(payload, dict):
        for key in ("messages", "entries", "chat", "history"):
            value = payload.get(key)
            if isinstance(value, list):
                return value

    return []


def load_chat_messages(path: Path = DEFAULT_CHAT_LOG) -> List[Dict[str, str]]:
    if not path.exists():
        return []

    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return []

    messages: List[Dict[str, str]] = []
    for entry in _extract_entries(payload):
        coerced = _coerce_message(entry)
        if coerced is not None:
            messages.append(coerced)

    return messages[-MAX_HISTORY:]


def _pick_fragment(words: List[str], max_words: int) -> List[str]:
    if not words:
        return []

    max_words = max(3, min(max_words, 20))
    minimum = min(len(words), max(2, min(4, max_words)))
    maximum = min(len(words), max_words)
    length = random.randint(minimum, maximum)
    start = random.randint(0, max(0, len(words) - length))
    return words[start : start + length]


def _mutate_pronouns(words: List[str]) -> None:
    swaps = {
        "i": "you",
        "me": "you",
        "my": "your",
        "mine": "yours",
        "we": "you",
        "our": "your",
    }

    for index, word in enumerate(words):
        if word in swaps and random.random() < 0.45:
            words[index] = swaps[word]


def _maybe_blend(words: List[str], messages: List[Dict[str, str]], max_words: int) -> None:
    if len(messages) < 2 or len(words) >= max_words or random.random() > 0.45:
        return

    other = random.choice(messages[-20:])["message"]
    other_words = _words(other)
    if not other_words:
        return

    remaining = max_words - len(words)
    take = min(remaining, random.randint(1, min(4, len(other_words))))
    start = random.randint(0, max(0, len(other_words) - take))

    if random.random() < 0.5 and len(words) < max_words:
        words.append("and")

    words.extend(other_words[start : start + max(0, max_words - len(words))])


def _maybe_whisper(words: List[str], max_words: int) -> None:
    if not words:
        return

    roll = random.random()
    if roll < 0.18 and len(words) < max_words:
        words.insert(0, "wait")
    elif roll < 0.28 and len(words) < max_words:
        words.insert(0, "no")
    elif roll < 0.36 and len(words) < max_words:
        words.append("again")


def _fallback(messages: List[Dict[str, str]], max_words: int) -> str:
    if not messages:
        return ""

    seed_words = _words(random.choice(messages[-20:])["message"])
    if not seed_words:
        return "i heard you"

    words = ["i", "heard"] + seed_words[: max(1, max_words - 2)]
    return " ".join(words[:max_words])


class MimicAI:
    def __init__(self, history_path: Path = DEFAULT_CHAT_LOG, max_words: int = MAX_WORDS, seed: Optional[int] = None):
        self.history_path = Path(history_path)
        self.max_words = max(3, min(int(max_words), 20))
        self.recent_lines: List[str] = []
        if seed is not None:
            random.seed(seed)

    def messages(self) -> List[Dict[str, str]]:
        return load_chat_messages(self.history_path)

    def generate(self, messages: Optional[List[Dict[str, str]]] = None) -> str:
        messages = messages if messages is not None else self.messages()
        if not messages:
            return ""

        seed = random.choice(messages[-20:])["message"]
        words = _pick_fragment(_words(seed), self.max_words)
        if len(words) < 2 and len(messages) > 1:
            words.extend(_words(random.choice(messages[-20:])["message"]))

        if not words:
            return ""

        _maybe_blend(words, messages, self.max_words)
        _mutate_pronouns(words)
        _maybe_whisper(words, self.max_words)
        words = words[: self.max_words]

        line = clean(" ".join(words))
        if line in self.recent_lines:
            line = _fallback(messages, self.max_words)

        if line:
            self.recent_lines.append(line)
            self.recent_lines = self.recent_lines[-8:]

        return line

    def respond(self, user_text: str) -> str:
        user_text = clean(user_text)
        if not user_text:
            return ""

        messages = self.messages()
        messages.append({"sender": "Player", "message": user_text})
        return self.generate(messages)


_AI = MimicAI()


def send_message(msg: str) -> str:
    return _AI.respond(msg)


def write_response(path: Path, message: str, source_count: int) -> None:
    payload = {
        "speaker": "MIMIC",
        "message": message,
        "timestampUtc": _utc_now(),
        "sourceCount": source_count,
    }
    path.write_text(json.dumps(payload, indent=2), encoding="utf-8")


def run_once(chat_log: Path, output: Path, max_words: int) -> str:
    ai = MimicAI(chat_log, max_words=max_words)
    messages = ai.messages()
    line = ai.generate(messages)
    if line:
        write_response(output, line, len(messages))
    return line


def watch(chat_log: Path, output: Path, interval: float, max_words: int) -> None:
    ai = MimicAI(chat_log, max_words=max_words)
    last_count = -1

    while True:
        messages = ai.messages()
        if messages and len(messages) != last_count:
            line = ai.generate(messages)
            if line:
                write_response(output, line, len(messages))
                print(line, flush=True)
            last_count = len(messages)

        time.sleep(max(0.5, interval))


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate dependency-free MIMIC chat lines.")
    parser.add_argument("--chat-log", type=Path, default=DEFAULT_CHAT_LOG)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--interval", type=float, default=4.0)
    parser.add_argument("--max-words", type=int, default=MAX_WORDS)
    parser.add_argument("--once", action="store_true", help="Generate one line and exit.")
    parser.add_argument("--message", help="Generate a reply from one message and exit.")
    args = parser.parse_args()

    if args.message:
        print(MimicAI(args.chat_log, max_words=args.max_words).respond(args.message))
        return 0

    if args.once:
        print(run_once(args.chat_log, args.output, args.max_words))
        return 0

    watch(args.chat_log, args.output, args.interval, args.max_words)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
