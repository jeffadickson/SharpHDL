#!/usr/bin/env python3
"""Black-box localhost API regression for the real .NET live simulator.

Launch server separately with:
    dotnet run --project tools/SharpHDL.Live/SharpHDL.Live.csproj
Then execute this test. No mocks, external Python packages, or fake simulator.
"""
import json
import os
from urllib.request import Request, urlopen
from urllib.error import HTTPError

BASE = os.environ.get("SHARPDHL_LIVE_URL", "http://127.0.0.1:8093").rstrip("/")


def request(path, payload=None):
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    req = Request(BASE + path, data=data, headers={"Content-Type": "application/json"})
    with urlopen(req, timeout=10) as response:
        return json.load(response)


def state_signature(state):
    """Fields whose rollback must be byte-for-byte equivalent."""
    return {key: state[key] for key in (
        "cycle", "inputReady", "outputValid", "inputAccepted",
        "outputAccepted", "inputStalled", "outputStalled",
        "stageOccupied", "outputData", "pendingTokens", "stageTokenIds"
    )}


def main():
    initial = request("/api/reset", {})
    assert initial["cycle"] == 0
    assert initial["canStepBack"] is False
    assert initial["rewindDepth"] == 0

    steps = [
        {"valid": True, "data": 11, "ready": False},
        {"valid": True, "data": 24, "ready": False},
        {"valid": False, "data": 0, "ready": True},
        {"valid": True, "data": 65535, "ready": True},
    ]
    states = [state_signature(initial)]
    for i, cmd in enumerate(steps, 1):
        result = request("/api/step", cmd)
        state = result["state"]
        assert state["cycle"] == i
        assert state["rewindDepth"] == i
        assert state["canStepBack"] is True
        states.append(state_signature(state))

    # An invalid request must not advance the hardware clock or damage checkpoints.
    try:
        request("/api/step", {"valid": True, "data": 65536, "ready": True})
        raise AssertionError("out-of-range data accepted")
    except HTTPError as error:
        assert error.code == 400, error

    unchanged = request("/api/state")
    assert state_signature(unchanged) == states[-1]
    assert unchanged["rewindDepth"] == len(steps)

    for index in range(len(steps) - 1, -1, -1):
        restored = request("/api/stepBack", {})["state"]
        assert state_signature(restored) == states[index], (index, restored)
        assert restored["rewindDepth"] == index

    try:
        request("/api/stepBack", {})
        raise AssertionError("empty history accepted")
    except HTTPError as error:
        assert error.code == 409, error

    # Replaying a committed series must yield exactly the same outputs.
    for i, cmd in enumerate(steps, 1):
        replay = request("/api/step", cmd)["state"]
        assert state_signature(replay) == states[i], i

    request("/api/reset", {})
    last = request("/api/state")
    assert last["cycle"] == 0 and last["rewindDepth"] == 0
    print("PASS: live step/back/replay, transaction tags, bounded state, reset and API errors")


if __name__ == "__main__":
    main()
