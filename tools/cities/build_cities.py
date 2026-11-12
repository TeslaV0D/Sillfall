#!/usr/bin/env python3
"""Builds the bundled city list for the weather sync's offline search (PLAN.md, section 5).

Source: GeoNames "cities15000" (all places with at least 15,000 inhabitants), CC BY 4.0, https://www.geonames.org/.
Output: src/Sillfall.Core/Weather/Cities/cities15000.tsv.gz, one city per line:

    name <TAB> country code <TAB> latitude <TAB> longitude <TAB> population <TAB> |key|key|...|

The keys are the name, the ASCII name and the alternate names in Latin script, normalised exactly like
CityIndex.Normalize in C# (decomposed, accents dropped, lower case, a few letters spelled out), plus the German
spelling with ae/oe/ue, so the app never has to normalise thousands of names while the user types.

Usage: python3 tools/cities/build_cities.py [path/to/cities15000.txt]  (downloads the file when no path is given)
"""

import gzip
import io
import re
import sys
import unicodedata
import urllib.request
import zipfile
from pathlib import Path

SOURCE = "https://download.geonames.org/export/dump/cities15000.zip"
OUTPUT = Path(__file__).resolve().parents[2] / "src/Sillfall.Core/Weather/Cities/cities15000.tsv.gz"
LATIN = re.compile(r"^[A-Za-z0-9\u00C0-\u024F' .,()/\-]+$")
SPELLED_OUT = {"ß": "ss", "æ": "ae", "œ": "oe", "ø": "o", "ł": "l", "đ": "d", "ð": "d", "þ": "th", "ı": "i"}
GERMAN = {"ä": "ae", "ö": "oe", "ü": "ue", "Ä": "Ae", "Ö": "Oe", "Ü": "Ue"}


def normalize(text: str) -> str:
    """Must match CityIndex.Normalize in src/Sillfall.Core/Weather/Cities/CityIndex.cs."""
    decomposed = unicodedata.normalize("NFKD", text)
    plain = "".join(c for c in decomposed if unicodedata.category(c) != "Mn").lower()
    plain = "".join(SPELLED_OUT.get(c, c) for c in plain)
    return " ".join(plain.replace("|", " ").split())


def german(text: str) -> str:
    return normalize("".join(GERMAN.get(c, c) for c in text))


def read_source(argv: list[str]) -> str:
    if len(argv) > 1:
        return Path(argv[1]).read_text(encoding="utf-8")
    request = urllib.request.Request(SOURCE, headers={"User-Agent": "Sillfall build script"})
    with urllib.request.urlopen(request, timeout=120) as response:
        archive = zipfile.ZipFile(io.BytesIO(response.read()))
    return archive.read("cities15000.txt").decode("utf-8")


def main(argv: list[str]) -> None:
    rows = []
    for line in read_source(argv).splitlines():
        fields = line.split("\t")
        if len(fields) < 15:
            continue
        name, ascii_name, alternates = fields[1], fields[2], fields[3]
        latitude, longitude, country = float(fields[4]), float(fields[5]), fields[8]
        population = int(fields[14] or 0)
        keys = []
        for candidate in [name, ascii_name, *alternates.split(",")]:
            candidate = candidate.strip()
            if not candidate or len(candidate) > 40 or not LATIN.match(candidate):
                continue
            for key in (normalize(candidate), german(candidate)):
                if key and key not in keys:
                    keys.append(key)
        if not keys:
            keys.append(normalize(name))
        rows.append((population, f"{name}\t{country}\t{latitude:.2f}\t{longitude:.2f}\t{population}\t|{'|'.join(keys)}|"))

    # Biggest first: the search lists them first when they match equally well.
    rows.sort(key=lambda row: -row[0])
    data = ("\n".join(row for _, row in rows) + "\n").encode("utf-8")
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    with gzip.GzipFile(OUTPUT, "wb", compresslevel=9, mtime=0) as file:
        file.write(data)
    print(f"{len(rows)} cities, {OUTPUT.stat().st_size} bytes -> {OUTPUT}")


if __name__ == "__main__":
    main(sys.argv)
