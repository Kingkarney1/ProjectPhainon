"""Downloads the photo-scanned surface textures the cockpit uses, from ambientCG (all CC0).

    python3 -I Tools/fetch_textures.py

Each set lands in Assets/Phainon/Art/Textures/Cockpit/<Finish>/ as Color.jpg, Normal.jpg (OpenGL
convention, which is Unity's), Roughness.jpg, and where the scan has them Metalness.jpg,
Occlusion.jpg and Height.jpg. A set already on disk is left alone. The editor's AssetFactory packs
them for the render pipeline; if a set is missing it falls back to a generated finish.
"""
import io, os, sys, time, urllib.request, zipfile

# finish, ambientCG asset id, resolution. Resolution follows how large and how close the surface is.
SETS = [
    ("PaintedPanel",   "Plastic010", "4K"),   # cabin shell: smooth satin coat with light scuffs
    ("PowderCoat",     "Metal046A",  "4K"),   # instrument panel faces: black textured coat
    ("BrushedAluminium", "Metal011", "2K"),   # rails, edge strips, fasteners, knob tops
    ("AnodisedAluminium", "Metal032", "2K"),  # display housings, control surrounds
    ("CarbonComposite", "Fabric004", "2K"),   # console bodies
    ("Rubber",         "Rubber004",  "1K"),   # knobs, throttle grip
    ("MouldedPlastic", "Plastic011", "1K"),   # button caps, switch guards
    ("SeatLeather",    "Leather026", "1K"),   # seat
]
# map suffix in the archive -> file name here
MAPS = {"Color": "Color", "NormalGL": "Normal", "Roughness": "Roughness", "Metalness": "Metalness",
        "AmbientOcclusion": "Occlusion", "Displacement": "Height"}
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Phainon", "Art", "Textures", "Cockpit")

def fetch(finish, asset, res):
    folder = os.path.join(ROOT, finish)
    if os.path.exists(os.path.join(folder, "Color.jpg")):
        print("%-18s already here" % finish); return
    url = "https://ambientcg.com/get?file=%s_%s-JPG.zip" % (asset, res)
    request = urllib.request.Request(url, headers={"User-Agent": "curl"})
    # The site drops connections now and then; a few patient retries get through.
    for attempt in range(6):
        try:
            data = urllib.request.urlopen(request, timeout=600).read(); break
        except Exception:
            if attempt == 5: raise
            time.sleep(10)
    archive = zipfile.ZipFile(io.BytesIO(data))
    os.makedirs(folder, exist_ok=True)
    got = []
    for member in archive.namelist():
        stem, ext = os.path.splitext(os.path.basename(member))
        suffix = stem.rsplit("_", 1)[-1]
        # Only the image maps listed above are written, under names chosen here, never a path from the archive.
        if ext.lower() != ".jpg" or suffix not in MAPS: continue
        with open(os.path.join(folder, MAPS[suffix] + ".jpg"), "wb") as out: out.write(archive.read(member))
        got.append(MAPS[suffix])
    assert "Color" in got and "Normal" in got and "Roughness" in got, "%s: archive has no colour, normal or roughness map (%s)" % (asset, archive.namelist())
    print("%-18s %s %s: %s (%.0f MB download)" % (finish, asset, res, ", ".join(sorted(got)), len(data) / 1e6))

if __name__ == "__main__":
    for item in SETS:
        try: fetch(*item); time.sleep(3)
        except Exception as error:
            print("%-18s FAILED: %s" % (item[0], error)); sys.exit(1)
