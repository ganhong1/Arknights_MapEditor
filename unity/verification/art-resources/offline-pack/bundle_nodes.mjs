import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const [mode, planPath, stagingRoot, installationRoot, unityJsDirectory] = process.argv.slice(2);
if (!['prepare', 'verify'].includes(mode) || !unityJsDirectory)
    throw new Error('Usage: node bundle_nodes.mjs prepare|verify <sources.json> <work> <installation> <unity-js/src>');
const moduleUrl = name => pathToFileURL(path.join(unityJsDirectory, name)).href;
const { BundleFile } = await import(moduleUrl('bundle.ts'));
const { ArrayBufferReader } = await import(moduleUrl('utils/reader.ts'));
const { BundleEnv } = await import(moduleUrl('assetFile.ts'));
const plan = JSON.parse(fs.readFileSync(planPath, 'utf8'));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const relativeParts = row => {
    const items = row.path.split('/');
    if (items.some(item => !item || item === '.' || item === '..' || /[\\:]/.test(item)))
        throw new Error('Invalid bundle path: ' + row.path);
    return items;
};

function decode(raw, gameFormat) {
    const exact = raw.buffer.slice(raw.byteOffset, raw.byteOffset + raw.byteLength);
    const reader = new ArrayBufferReader(exact);
    const bundle = Object.create(BundleFile.prototype);
    const signature = reader.readStringUntilZero();
    if (signature !== 'UnityFS')
        throw new Error('Expected UnityFS, got ' + signature);
    bundle.header = {
        signature, version: reader.readUInt32BE(),
        unityVersion: reader.readStringUntilZero(), unityReversion: reader.readStringUntilZero(),
    };
    bundle.nodes = [];
    bundle.blockInfos = [];
    bundle.options = { env: gameFormat ? BundleEnv.ARKNIGHTS : BundleEnv.NONE };
    bundle.readHeader(reader);
    if (bundle.header.size !== raw.length)
        throw new Error('Bundle file size does not match its header');
    if (bundle.header.flags & 0x80) {
        reader.align(16);
        const dataOffset = reader.position;
        const infoSize = bundle.header.compressedBlocksInfoSize;
        reader.seek(raw.length - infoSize);
        const info = bundle.decompressBuffer(reader.readBuffer(infoSize), bundle.header.flags & 0x3f,
            bundle.header.uncompressedBlocksInfoSize);
        bundle.readBlocksInfo(info);
        reader.seek(dataOffset);
    } else {
        bundle.readBlocksInfoAndDirectory(reader);
    }
    bundle.files = bundle.readFiles(bundle.readBlocks(reader));
    if (bundle.nodes.length === 0)
        throw new Error('Empty bundle directory');
    return bundle;
}

function bigEndian(value, length) {
    const bytes = Buffer.alloc(length);
    if (length === 8)
        bytes.writeBigUInt64BE(BigInt(value));
    else
        bytes.writeUIntBE(value, 0, length);
    return bytes;
}

const zeroTerminated = value => Buffer.from(value + '\0', 'utf8');

function unityFs(bundle) {
    // This intermediate preserves the decoded nodes and their names for Unity's official recompressor.
    let dataOffset = 0;
    const files = bundle.files.map(file => Buffer.from(file));
    const entries = bundle.nodes.map((node, index) => {
        const fileSize = files[index].length;
        const row = Buffer.concat([bigEndian(dataOffset, 8), bigEndian(fileSize, 8),
            bigEndian(node.flags, 4), zeroTerminated(node.path)]);
        dataOffset += fileSize;
        return row;
    });
    const info = Buffer.concat([Buffer.alloc(16), bigEndian(1, 4), bigEndian(dataOffset, 4),
        bigEndian(dataOffset, 4), bigEndian(0, 2), bigEndian(entries.length, 4), ...entries]);
    const prefix = Buffer.concat([zeroTerminated('UnityFS'), bigEndian(bundle.header.version, 4),
        zeroTerminated(bundle.header.unityVersion), zeroTerminated(bundle.header.unityReversion)]);
    const [major, minor, patch] = bundle.header.unityReversion.split(/[.a-z]/i).map(Number);
    // Older UnityFS v6 wrappers start the block directory immediately after the header.
    const aligned = bundle.header.version >= 7 ||
        (major === 2019 && (minor > 4 || (minor === 4 && patch >= 15)));
    const pad = aligned ? (16 - (prefix.length + 20) % 16) % 16 : 0;
    const total = prefix.length + 20 + pad + info.length + dataOffset;
    return Buffer.concat([prefix, bigEndian(total, 8), bigEndian(info.length, 4),
        bigEndian(info.length, 4), bigEndian(0x40, 4), Buffer.alloc(pad), info, ...files], total);
}

function nodeDigest(bundle) {
    const hash = createHash('sha256');
    for (const [index, node] of bundle.nodes.entries()) {
        const bytes = Buffer.from(bundle.files[index]);
        hash.update(JSON.stringify([node.path, node.flags, bytes.length, sha(bytes)]) + '\n');
    }
    return hash.digest('hex');
}

function atomicWrite(target, content) {
    fs.mkdirSync(path.dirname(target), { recursive: true });
    if (fs.existsSync(target))
        throw new Error('Refusing to overwrite ' + target);
    const partial = target + '.tmp';
    fs.writeFileSync(partial, content, { flag: 'wx' });
    fs.renameSync(partial, target);
}

const sourceEvidencePath = path.join(stagingRoot, 'source-nodes.json');
const stagedFiles = mode === 'verify'
    ? new Map(JSON.parse(fs.readFileSync(sourceEvidencePath, 'utf8')).map(row => [row.path, row]))
    : null;
const records = [];
for (const row of plan.files) {
    const pieces = relativeParts(row);
    if (mode === 'prepare') {
        const raw = fs.readFileSync(row.source);
        if (raw.length !== row.sourceBytes)
            throw new Error('Source size changed for ' + row.path);
        const sourceSha256 = sha(raw);
        if (row.sourceSha256 && row.sourceSha256 !== sourceSha256)
            throw new Error('Source hash changed for ' + row.path);
        const bundle = decode(raw, true);
        const intermediate = unityFs(bundle);
        atomicWrite(path.join(stagingRoot, 'Staging', ...pieces), intermediate);
        records.push({ path: row.path, sourceType: row.sourceType, sourceSha256,
            sourceBytes: raw.length, intermediateBytes: intermediate.length, nodeSha256: nodeDigest(bundle) });
    } else {
        const file = fs.readFileSync(path.join(installationRoot, 'Bundles', ...pieces));
        const standard = decode(file, false);
        const types = [...new Set(standard.blockInfos.map(block => block.flags & 0x3f))].sort();
        if (types.some(type => type !== 0 && type !== 2 && type !== 3))
            throw new Error('Unexpected output compression for ' + row.path + ': ' + types);
        const original = stagedFiles.get(row.path);
        const nodeSha256 = nodeDigest(standard);
        if (!original || nodeSha256 !== original.nodeSha256)
            throw new Error('Decoded nodes differ from input: ' + row.path);
        records.push({ ...original, bytes: file.length, sha256: sha(file), blockTypes: types });
    }
    if (records.length % 500 === 0) console.log(mode, records.length, '/', plan.files.length);
}

if (mode === 'prepare') {
    atomicWrite(sourceEvidencePath, JSON.stringify(records, null, 2) + '\n');
    atomicWrite(path.join(stagingRoot, 'paths.txt'), records.map(row => row.path).join('\n') + '\n');
} else {
    const bytes = records.reduce((sum, row) => sum + row.bytes, 0);
    const catalogSha256 = sha(fs.readFileSync(path.join(installationRoot, 'catalog.json')));
    atomicWrite(path.join(installationRoot, 'bundle-integrity.json'), JSON.stringify({
        sourceManifestSha256: plan.sourceManifestSha256, catalogSha256,
        totalBundles: records.length, bytes, algorithm: 'Unity BuildCompression.LZ4Runtime',
        decodedNodesVerified: true, bundles: records,
    }, null, 2) + '\n');
    console.log('Verified compressed byte total', bytes);
}
console.log(mode, records.length, 'bundle files complete');
