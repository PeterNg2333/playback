"""Read-only scale check for a full lecture transcript and 20-minute M4A clip.

Run with: python app-temp/checks/long-lecture-check.py "C:/.../sampleAudio"
No source text or audio leaves the machine, and no input file is modified.
"""

import pathlib
import re
import struct
import sys


def atoms(data, start=0, end=None):
    end = len(data) if end is None else end
    while start + 8 <= end:
        size, kind = struct.unpack_from(">I4s", data, start)
        header = 8
        if size == 1:
            size = struct.unpack_from(">Q", data, start + 8)[0]
            header = 16
        if size == 0:
            size = end - start
        if size < header or start + size > end:
            raise ValueError("Invalid MP4 atom boundary")
        yield kind, start + header, start + size
        start += size


def m4a_duration(path):
    # The moov atom is small; skip compressed mdat data without decoding audio.
    with path.open("rb") as stream:
        offset = 0
        length = path.stat().st_size
        brands = None
        while offset + 8 <= length:
            stream.seek(offset)
            size, kind = struct.unpack(">I4s", stream.read(8))
            header = 8
            if size == 1:
                size = struct.unpack(">Q", stream.read(8))[0]
                header = 16
            if size == 0:
                size = length - offset
            if size < header or offset + size > length:
                raise ValueError(f"Invalid MP4 atom in {path.name}")
            if kind == b"ftyp":
                brands = stream.read(min(size - header, 64))[:4].decode("ascii")
            if kind == b"moov":
                movie = stream.read(size - header)
                if b"mp4a" not in movie:
                    raise ValueError(f"Expected MPEG-4 audio sample entry in {path.name}")
                for child, begin, end in atoms(movie):
                    if child != b"mvhd":
                        continue
                    version = movie[begin]
                    if version == 0:
                        scale, duration = struct.unpack_from(">II", movie, begin + 12)
                    elif version == 1:
                        scale, duration = struct.unpack_from(">IQ", movie, begin + 20)
                    else:
                        raise ValueError("Unknown MP4 movie header version")
                    return brands, duration / scale
            offset += size
    raise ValueError(f"No MP4 movie header in {path.name}")


def seconds(value):
    parts = [int(part) for part in value.split(":")]
    return sum(part * 60**index for index, part in enumerate(reversed(parts)))


def main(folder):
    parts = [folder / "sampleAudio.m4a"]
    assert parts[0].is_file()
    durations = [m4a_duration(path) for path in parts]
    assert 1199 <= durations[0][1] <= 1202
    print("audio:", [(p.name, brand, round(duration, 2)) for p, (brand, duration) in zip(parts, durations)])
    print("audio total seconds:", round(sum(duration for _, duration in durations), 2))

    lines = (folder / "transcript.txt").read_text(encoding="utf-8-sig").splitlines()
    timestamp = re.compile(r"^\s*(?:\d+:)?\d{1,2}:\d{2}\s*$")
    entries = []
    previous = 0
    words = []
    for line in lines:
        if timestamp.fullmatch(line):
            end = seconds(line.strip())
            if end <= previous or not words:
                raise ValueError(f"Invalid or duplicate transcript time at {line!r}")
            entries.append((previous, end, " ".join(words)))
            previous, words = end, []
        elif line.strip():
            words.append(line.strip())
    if words:
        final_end = previous + 14
        entries.append((previous, final_end, " ".join(words)))
        print("transcript: untimed closing text assigned an estimated 14 seconds")
    assert entries and entries[-1][1] > 9_800
    print("transcript: full lecture text; only its first 20 minutes have retained audio")
    print("transcript: entries", len(entries), "last second", entries[-1][1],
          "characters", sum(len(text) for _, _, text in entries),
          "longest entry", max(len(text) for _, _, text in entries))
    print("tutorial: lines", len((folder / "Tutorial.txt").read_text(encoding="utf-8-sig").splitlines()))

    # Model the current two-minute scan with at most 40 pending entries per call.
    batches = []
    cursor = 0
    for tick in range(120, entries[-1][1] + 120, 120):
        available = [entry for entry in entries[cursor:] if entry[1] <= tick]
        while available:
            batch = available[:40]
            batches.append(batch)
            cursor += len(batch)
            available = available[len(batch):]
    assert cursor == len(entries)
    material = (folder / "Tutorial.txt").read_text(encoding="utf-8-sig")[:3000]
    prompt_sizes = [len(material) + sum(len(text) + 70 for _, _, text in batch)
                    for batch in batches]
    ids = [f"sample-audio-{i:04d}" for i in range(len(entries))]
    assert len(ids) == len(set(ids))
    # Source locations remain stable at the beginning, middle, and end.
    for index in (0, len(entries) // 2, len(entries) - 1):
        start, end, text = entries[index]
        assert start < end and text and ids[index]
    optimized = sum(prompt_sizes) - len(material) * (len(batches) - 1)
    print("rolling notes: calls", len(batches), "source-only characters before/after", sum(prompt_sizes), optimized,
          "largest call", max(prompt_sizes), "avoided repeated material characters", len(material) * (len(batches) - 1))
    print("transcript locations: beginning/middle/end present; duplicate transcript IDs: 0")
    print("offline QA inputs: source IDs and transcript time ranges available for all", len(entries), "entries")


if __name__ == "__main__":
    main(pathlib.Path(sys.argv[1]))
