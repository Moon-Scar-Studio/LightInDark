#!/usr/bin/env python3
"""Convert a NOS MoreCosmic ZIP to a deterministic native LID CosmicAddons ZIP."""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
from zipfile import ZipFile, ZipInfo, ZIP_DEFLATED


def read_nos_json(data):
    # NOS serializes capitalized booleans and Regex.Escape strings (e.g. Glitch\.Y).
    # Normalize tokens without changing boolean words inside quoted names.
    def normalize(match):
        token = match.group()
        if token.startswith('"'):
            return re.sub(r'\\.', lambda escape: escape.group() if escape.group()[1] in '"\\/bfnrtu'
                          else '\\' + escape.group(), token)
        return token.lower()
    return json.loads(re.sub(r'"(?:\\.|[^"\\])*"|\b(?:True|False)\b',
                             normalize, data.decode('utf-8-sig')))


def display_name(text):
    return re.sub(r'\\([^a-zA-Z0-9])', r'\1', text).replace('_', ' ')


def migrate(source, destination):
    with ZipFile(source) as archive:
        metadata = read_nos_json(archive.read('addon.meta'))
        contents = read_nos_json(archive.read('MoreCosmic/Contents.json'))
        addon_id = metadata['Id']
        if not re.fullmatch('[a-z0-9_]+', addon_id):
            raise ValueError('Addon ID must contain only lowercase letters, digits and underscores')
        pngs = {n: archive.read(n) for n in archive.namelist() if n.lower().endswith('.png')}
        by_hash = {}
        for name, data in pngs.items():
            digest = hashlib.md5(data).digest()
            # Older NOS writers omitted zero padding for individual digest bytes.
            for value in (digest.hex(), ''.join(format(byte, 'x') for byte in digest)):
                by_hash.setdefault(value, name)

        resources, manifest, mapping = {}, {}, []
        for category in ('Hats', 'Visors'):
            converted = manifest[category] = {}
            for item in contents.get(category, []):
                identity = item['Author'] + '\0' + item['Name']
                local_id = category.lower() + '_' + hashlib.sha256(identity.encode()).hexdigest()[:20]
                if local_id in converted:
                    raise ValueError('Duplicate cosmetic identity: ' + identity)
                images = {}
                for state, image in item.items():
                    if not isinstance(image, dict) or 'Address' not in image:
                        continue
                    if any(k not in ('Address', 'Hash', 'ExIsFront', 'AnimSync') for k in image):
                        raise ValueError('Unsupported image metadata: ' + str(image))
                    if image.get('AnimSync', 0) or image.get('ExIsFront', False):
                        raise ValueError('Unsupported animated/extra image: ' + str(image))
                    name = 'MoreCosmic/' + category.lower() + '/' + image['Address']
                    if name not in pngs:
                        name = by_hash.get(image.get('Hash'))
                    if name is None:
                        raise ValueError(f"Missing image for {item['Name']}/{state}")
                    data = pngs[name]
                    digest = hashlib.md5(data).digest()
                    if image.get('Hash') not in (digest.hex(), ''.join(format(b, 'x') for b in digest)):
                        raise ValueError('Image hash mismatch: ' + name)
                    if not data.startswith(b'\x89PNG\r\n\x1a\n'):
                        raise ValueError('Invalid PNG: ' + name)
                    path = category.lower() + '/' + PurePosixPath(name).name
                    if path in resources and resources[path] != data:
                        raise ValueError('Conflicting image path: ' + path)
                    resources[path] = data
                    images[state] = path
                preview = images.get('Preview') or images.get('Main') or images.get('Back')
                if preview is None:
                    raise ValueError('No preview image: ' + item['Name'])
                converted[local_id] = {
                    'path': preview, 'displayName': display_name(item['Name']),
                    'author': display_name(item['Author']), 'package': item.get('Package', metadata['Name']),
                    'colorMode': 'adaptive' if item.get('Adaptive') else 'fixed',
                    'noBounce': not item.get('Bounce', False),
                    'behindHat': item.get('BehindHat', False),
                    'backmostBack': item.get('BackmostBack', False),
                    'nosImages': images,
                }
                mapping.append({'category': category, 'name': item['Name'], 'author': item['Author'],
                                'id': addon_id + '/' + local_id, 'images': images})
        manifest['Skins'], manifest['NamePlates'] = {}, {}
        info = {k: metadata[k] for k in ('Id', 'Name', 'Version', 'Author')}
        info['Describe'] = metadata.get('Description', '')
        report = {'sourceSha256': hashlib.sha256(Path(source).read_bytes()).hexdigest(),
                  'sourceFile': Path(source).name, 'items': mapping}
        entries = {f'Resources/{name}': data for name, data in resources.items()}
        for name, data in (('AddonInfo.json', info), ('CosmicInfo.json', manifest),
                           ('Migration.json', report), ('Source/addon.meta', metadata),
                           ('Source/Contents.json', contents)):
            entries[name] = (json.dumps(data, ensure_ascii=False, indent=2) + '\n').encode()
        Path(destination).parent.mkdir(parents=True, exist_ok=True)
        with ZipFile(destination, 'w', compression=ZIP_DEFLATED, compresslevel=9) as output:
            for name, data in sorted(entries.items()):
                entry = ZipInfo(name, (2026, 1, 1, 0, 0, 0))
                entry.compress_type = ZIP_DEFLATED
                entry.external_attr = 0o100644 << 16
                output.writestr(entry, data)
        return {category: len(manifest[category]) for category in ('Hats', 'Visors')}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('destination', type=Path)
    args = parser.parse_args()
    print(json.dumps(migrate(args.source, args.destination), ensure_ascii=False))
