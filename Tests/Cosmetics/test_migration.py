import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest
from zipfile import ZipFile
import zlib

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('migration', ROOT / 'Tools/migrate-nos-cosmetics.py')
migration = importlib.util.module_from_spec(spec)
spec.loader.exec_module(migration)
PACK = ROOT / 'Light/Resources/CosmicAddons/halfsugarscloset.zip'


class MigrationTests(unittest.TestCase):
    def test_nos_tokens_do_not_corrupt_strings(self):
        result = migration.read_nos_json(b'{"Name":"True False Glitch\\.Y","Flag":True,"Other":False}')
        self.assertEqual(result, {'Name': r'True False Glitch\.Y', 'Flag': True, 'Other': False})
        self.assertEqual(migration.display_name(result['Name']), 'True False Glitch.Y')

    def test_real_pack_has_all_items_and_verified_pngs(self):
        with ZipFile(PACK) as archive:
            manifest = json.loads(archive.read('CosmicInfo.json'))
            source = json.loads(archive.read('Source/Contents.json'))
            mapping = json.loads(archive.read('Migration.json'))['items']
            self.assertEqual((len(manifest['Hats']), len(manifest['Visors'])), (59, 34))
            self.assertEqual(len(mapping), 93)
            for record in mapping:
                category = record['category']
                original = next(i for i in source[category] if i['Name'] == record['name'] and i['Author'] == record['author'])
                converted = manifest[category][record['id'].split('/')[1]]
                self.assertEqual(converted['displayName'], migration.display_name(original['Name']))
                self.assertEqual(converted['author'], migration.display_name(original['Author']))
                self.assertEqual(converted['colorMode'] == 'adaptive', original['Adaptive'])
                for state, path in converted['nosImages'].items():
                    png = archive.read('Resources/' + path)
                    digest = hashlib.md5(png).digest()
                    self.assertIn(original[state]['Hash'], (digest.hex(), ''.join(format(b, 'x') for b in digest)))
            images = [n for n in archive.namelist() if n.endswith('.png')]
            self.assertEqual(len(images), 194)
            for name in images:
                png = archive.read(name)
                offset, compressed = 8, bytearray()
                self.assertEqual(png[:8], b'\x89PNG\r\n\x1a\n')
                while offset < len(png):
                    length = struct.unpack('>I', png[offset:offset + 4])[0]
                    kind = png[offset + 4:offset + 8]
                    data = png[offset + 8:offset + 8 + length]
                    crc = struct.unpack('>I', png[offset + 8 + length:offset + 12 + length])[0]
                    self.assertEqual(zlib.crc32(kind + data), crc, name)
                    if kind == b'IDAT': compressed.extend(data)
                    offset += 12 + length
                self.assertTrue(zlib.decompress(compressed), name)

    def test_conversion_is_reproducible_and_resolves_hash_aliases(self):
        with ZipFile(PACK) as pack, tempfile.TemporaryDirectory() as temporary:
            temporary = Path(temporary)
            source = temporary / "half sugar's closet(9.12).zip"
            with ZipFile(source, 'w') as archive:
                archive.writestr('addon.meta', pack.read('Source/addon.meta'))
                archive.writestr('MoreCosmic/Contents.json', pack.read('Source/Contents.json'))
                for name in pack.namelist():
                    if name.startswith('Resources/'):
                        archive.writestr('MoreCosmic/' + name.removeprefix('Resources/'), pack.read(name))
            first, second = temporary / 'first.zip', temporary / 'second.zip'
            self.assertEqual(migration.migrate(source, first), {'Hats': 59, 'Visors': 34})
            migration.migrate(source, second)
            self.assertEqual(first.read_bytes(), second.read_bytes())
            with ZipFile(first) as converted:
                self.assertEqual(converted.read('CosmicInfo.json'), pack.read('CosmicInfo.json'))

    def test_missing_image_fails_instead_of_silently_dropping_item(self):
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary) / 'broken.zip'
            with ZipFile(source, 'w') as archive:
                archive.writestr('addon.meta', '{"Id":"broken","Name":"Broken","Author":"A","Version":"1"}')
                archive.writestr('MoreCosmic/Contents.json', '{"Hats":[{"Name":"Hat","Author":"A","Main":{"Address":"missing.png","Hash":"bad"}}]}')
            with self.assertRaisesRegex(ValueError, 'Missing image'):
                migration.migrate(source, Path(temporary) / 'out.zip')


if __name__ == '__main__':
    unittest.main()
