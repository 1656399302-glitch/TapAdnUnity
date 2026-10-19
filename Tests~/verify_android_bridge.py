#!/usr/bin/env python3
"""Compile locally generated bridge sources against the real AAR, android.jar and Unity classes.
This checks Java API compatibility only; it does not build/export a player.
"""
import argparse
import pathlib
import subprocess
import tempfile
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--vendor', type=pathlib.Path, required=True)
parser.add_argument('--android-player', type=pathlib.Path, required=True)
args = parser.parse_args()
player = args.android_player.resolve()
platforms = sorted((player / 'SDK/platforms').glob('android-*'), key=lambda p: int(p.name.split('-')[1]))
android = platforms[-1] / 'android.jar'
unity = player / 'Variations/il2cpp/Release/Classes/classes.jar'
javac = player / 'OpenJDK/bin/javac'
if not javac.exists():
    javac = javac.with_suffix('.exe')
vendor = args.vendor.resolve()
with tempfile.TemporaryDirectory(prefix='tapadn-java-check-') as tmp:
    temporary = pathlib.Path(tmp)
    with zipfile.ZipFile(vendor / 'Runtime/Plugins/Android/dirichlet_ad_5.3.0.4.aar') as archive:
        (temporary / 'classes.jar').write_bytes(archive.read('classes.jar'))
    import os
    classpath = os.pathsep.join(str(p) for p in [temporary / 'classes.jar', android, unity])
    subprocess.run([str(javac), '-classpath', classpath, '-d', str(temporary / 'output'),
                    str(vendor / 'Native~/TapAdUnityBridge.java'),
                    str(vendor / 'Native~/TapAdnPrivacy.java')], check=True)
print('PASS: generated Java bridges compile against the installed TapADN SDK and Unity Android API.')
