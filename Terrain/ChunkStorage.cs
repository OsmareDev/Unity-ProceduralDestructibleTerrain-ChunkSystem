using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.IO;
using System;

public class ChunkStorage {
    // ─── Formato del archivo .region ──────────────────────────────────────
    // [Magic uint32][Version uint16][RegionSize uint16]    ← 8 bytes header
    // [offset uint32 | length uint32] × 256                ← 2048 bytes tabla
    // [datos de chunks...]                                 ← posición variable
    //
    // offset == 0 significa que ese slot está vacío (chunk no guardado).
    // Los chunks nuevos siempre se añaden al final (append-only).
    // ──────────────────────────────────────────────────────────────────────

    private const uint   MAGIC       = 0x00112233u;                 // Identifier to check for data corruption
    private const ushort VERSION     = 1;                           // Version of the data, to check for compatibility
    private const int    REGION_SIZE = 16;                          // Number of chunks in one axis of the table
    private const int    SLOTS       = REGION_SIZE * REGION_SIZE;   // All slots available to save data from the chunks the file
    private const int    HEADER_SIZE = 8;                           // Bits of the header check, MAGIC (4) + VERSION (2) + REGION_SIZE (2)
    private const int    TABLE_SIZE  = SLOTS * 8;                   // Space required in bits for the offset table
    private const int    DATA_START  = HEADER_SIZE + TABLE_SIZE;    // Starting point in the file of the Chunk Data

    private readonly static string m_saveDir = Application.persistentDataPath + "/WorldChunks/";

    #region SaveLoad
    public static async Task SaveManyAsync(IEnumerable<ChunkLiteBurst> chunks) {
        Directory.CreateDirectory(m_saveDir);
        Dictionary<Vector2Int, List<(int, byte[])>> byRegion = new();

        foreach (ChunkLiteBurst chunk in chunks) {
            
            Vector2Int region       = ToRegion(chunk.Coords);
            int        localIndex   = ToLocalIndex(chunk.Coords);
            byte[]     data         = Serialize(chunk);

            if (!byRegion.TryGetValue(region, out List<(int, byte[])> list))
                byRegion[region] = list = new List<(int, byte[])>();

            list.Add((localIndex, data));
        }

        foreach (var (region, entries) in byRegion) {
            string path = RegionPath(region);
            var (offsets, lengths) = await ReadTableAsync(path);

            await using var stream = new FileStream(path, 
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                4096, useAsync: true);

            if (stream.Length < DATA_START) stream.SetLength(DATA_START);

            foreach (var (localIndex, data) in entries) {
                long writeOffset;

                if (offsets[localIndex] != 0)
                    writeOffset = offsets[localIndex];
                else
                    writeOffset = stream.Length;

                stream.Position = writeOffset;
                await stream.WriteAsync(data);

                offsets[localIndex] = (uint)writeOffset;
                lengths[localIndex] = (uint)data.Length;
            }

            await WriteTableAsync(stream, offsets, lengths);
        }
    }

    public static async Task LoadManyAsync(IEnumerable<ChunkLiteBurst> chunks) {
        Dictionary<Vector2Int, List<(ChunkLiteBurst, int)>> byRegion = new();

        foreach (ChunkLiteBurst chunk in chunks) {
            Vector2Int coord = chunk.Coords;
            Vector2Int region = ToRegion(coord);
            if (!byRegion.TryGetValue(region, out var list))
                byRegion[region] = list = new List<(ChunkLiteBurst, int)>();

            list.Add((chunk, ToLocalIndex(coord)));
        }

        foreach (var (region, entries) in byRegion) {
            string path = RegionPath(region);
            if (!File.Exists(path)) continue;

            var (offsets, lengths) = await ReadTableAsync(path);

            await using var stream = new FileStream(path, 
                FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, useAsync: true);

            foreach (var (chunk, localIndex) in entries) {
                uint offset = offsets[localIndex];
                uint length = lengths[localIndex];

                if (offset == 0) continue;

                byte[] data = new byte[length];
                stream.Position = offset;
                await stream.ReadAsync(data, 0, data.Length);
                Deserialize(data, chunk);
                chunk.IsLoaded = true;
            }
        }
    }

    #endregion
    #region Serialization
    private static byte[] Serialize(ChunkLiteBurst chunk) {
        using MemoryStream ms = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(ms);

        float[] tmp = chunk.HeightMap.ToArray();
        writer.Write(tmp.Length);
        writer.Write(MemoryMarshal.AsBytes(tmp.AsSpan()));

        return ms.ToArray();
    }

    private static void Deserialize(byte[] data, ChunkLiteBurst chunk) {
        using MemoryStream ms = new MemoryStream(data);
        using BinaryReader reader = new BinaryReader(ms);

        int length = reader.ReadInt32();
        int byteCount = length * sizeof(float);
        byte[] buffer = reader.ReadBytes(byteCount);
        float[] tmp = new float[length];
        Buffer.BlockCopy(buffer, 0, tmp, 0, byteCount);
        tmp.CopyTo(chunk.HeightMap);
    }

    #endregion
    #region OffsetTable
    private static async Task<(uint[] offsets, uint[] lengths)> ReadTableAsync(string path) {
        uint[] offsets = new uint[SLOTS];
        uint[] lengths = new uint[SLOTS];

        if (!File.Exists(path)) return (offsets, lengths);

        await using var stream = new FileStream(path, 
            FileMode.Open, FileAccess.Read, FileShare.Read, 
            4096, useAsync: true);

        if (stream.Length < DATA_START) return (offsets, lengths);

        // Validate the file
        byte[] header = new byte[HEADER_SIZE];
        await stream.ReadAsync(header, 0, HEADER_SIZE);
        if (BitConverter.ToUInt32(header, 0) != MAGIC) {
            throw new InvalidDataException($"Region File corrupted in: {path}");
        }

        // Read all the table
        byte[] table = new byte[TABLE_SIZE];
        await stream.ReadAsync(table, 0, TABLE_SIZE);

        for (int i = 0; i < SLOTS; i++) {
            offsets[i] = BitConverter.ToUInt32(table, i * 8);
            lengths[i] = BitConverter.ToUInt32(table, i * 8 + 4);
        }

        return (offsets, lengths);
    }

    private static async Task WriteTableAsync(FileStream stream, uint[] offsets, uint[] lengths) {
        // Write header
        // if (stream.Length < HEADER_SIZE) {
            stream.Position = 0;
            byte[] header = new byte[HEADER_SIZE];
            BitConverter.TryWriteBytes(header.AsSpan(0), MAGIC);
            BitConverter.TryWriteBytes(header.AsSpan(4), VERSION);
            BitConverter.TryWriteBytes(header.AsSpan(6), (ushort)REGION_SIZE);
            await stream.WriteAsync(header);
        // }

        // Write table
        stream.Position = HEADER_SIZE;
        byte[] table = new byte[TABLE_SIZE];
        for (int i = 0; i < SLOTS; i++) {
            BitConverter.TryWriteBytes(table.AsSpan(i * 8),     offsets[i]);
            BitConverter.TryWriteBytes(table.AsSpan(i * 8 + 4), lengths[i]);
        }

        await stream.WriteAsync(table);
        await stream.FlushAsync();
    }

    #endregion
    #region Coords&Extras
    private static Vector2Int ToRegion(Vector2Int c) => new(FloorDiv(c.x, REGION_SIZE), FloorDiv(c.y, REGION_SIZE));
    private static int ToLocalIndex(Vector2Int c) => Mod(c.y, REGION_SIZE) * REGION_SIZE + Mod(c.x, REGION_SIZE);

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;
    private static int Mod(int a, int b) { int r = a % b; return r < 0 ? r + b : r; }

    private static string RegionPath(Vector2Int r) => Path.Combine(m_saveDir, $"r.{r.x}.{r.y}.region");

    #endregion
}