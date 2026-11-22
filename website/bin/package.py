"""Package the room-code website for deployment."""
import hashlib
from pathlib import Path
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[1]
FILES = [
    'README.md', 'config.example.php', 'schema.sql',
    'app/bootstrap.php', 'app/HttpError.php', 'app/RoomCode.php', 'app/Security.php', 'app/Store.php',
    'app/I18n.php', 'app/translations.json', 'public_html/i18n.php',
    'public_html/assets/i18n.js',
    'bin/check.php', 'bin/cleanup.php',
    'public_html/index.php', 'public_html/api.php', 'public_html/.htaccess', 'public_html/robots.txt',
    'public_html/assets/site.css', 'public_html/assets/site.js', 'public_html/assets/favicon.svg',
    'public_html/assets/RussoOne-Regular.ttf', 'public_html/assets/GolosText.ttf',
    'public_html/assets/RussoOne-LICENSE.txt', 'public_html/assets/GolosText-LICENSE.txt',
]
output = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else ROOT.parent / 'dist/repo-rooms.zip'
output.parent.mkdir(parents=True, exist_ok=True)
manifest = []
with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
    for name in FILES:
        data = (ROOT / name).read_bytes()
        archive.writestr(name, data)
        manifest.append(hashlib.sha256(data).hexdigest() + '  ' + name)
    archive.writestr('SHA256SUMS.txt', '\n'.join(manifest) + '\n')
print(f'Packaged {len(FILES)} files: {output}')
