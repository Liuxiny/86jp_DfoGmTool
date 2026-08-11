using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;

namespace GmPvfLib
{
    
    
    
    
    public sealed class PvfArchive : IDisposable
    {
        private const uint MagicSignature = 0x69706B6E; 

        private byte[] _strABuffer;
        private byte[] _strWBuffer;
        private byte[] _bodyBuffer;     
        private int _bodyOffset;        
        private int _bodyLength;        
        private readonly List<PvfFileData> _files = new List<PvfFileData>();
        private readonly List<GrpiItem> _groups = new List<GrpiItem>();
        private readonly Dictionary<string, int> _pathIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // Cache string offsets so new paths/tokens can reuse or extend NameTable.
        private Dictionary<string, int> _strAOffsetCache;
        private Dictionary<string, int> _strWOffsetCache;
        private PvfHashTable _hashTable;
        private bool _disposed;

        
        private PvfHeader _header;
        private bool _headerUsesGuard;
        private byte[] _rawTableBytes;   
        private int _rawTableOffset;     
        private int _rawTableSize;       
        private byte[] _rawHashBytes;    
        private byte[] _rawNameBytes;    
        private byte[] _rawGrpiBytes;    

        
        // Only changed/new file payloads live here; unchanged chunks are copied raw.
        private readonly Dictionary<int, byte[]> _overlay = new Dictionary<int, byte[]>();

        
        private readonly ConcurrentDictionary<int, byte[]> _chunkCache = new ConcurrentDictionary<int, byte[]>();

        public IReadOnlyList<PvfFileData> Files => _files;
        public int FileCount => _files.Count;
        public PvfHashTable HashTable => _hashTable;
        internal IReadOnlyList<GrpiItem> Groups => _groups;

        
        public bool HasModifications => _overlay.Count > 0;

        
        public int ModifiedCount => _overlay.Count;

        
        public PvfHeader GetHeader() => _header;

        
        public byte[] GetRawHashBytes() => (byte[])_rawHashBytes.Clone();

        
        public byte[] GetRawNameBytes() => (byte[])_rawNameBytes.Clone();

        
        private byte[] GetRawTableBytes()
        {
            if (_rawTableBytes == null)
                _rawTableBytes = _bodyBuffer.Slice(_rawTableOffset, _rawTableSize);
            return (byte[])_rawTableBytes.Clone();
        }

        private PvfArchive() { }

        
        
        
        public byte[] ToBytes()
        {
            
            byte[] tableBytes = GetRawTableBytes();
            byte[] nameBytes = (byte[])_rawNameBytes.Clone();

            byte[] hashBytes = (byte[])_rawHashBytes.Clone();
            PvfDecryptor.Decrypt("HASH", hashBytes); 

            byte[] grpiBytes = (byte[])_rawGrpiBytes.Clone();
            PvfDecryptor.Decrypt("GRPI", grpiBytes);

            
            var header = _header;
            byte[] headerBytes = StructToBytes(header);
            PvfDecryptor.Decrypt("HeaD", headerBytes);
            if (_headerUsesGuard)
                PvfDecryptor.DecryptGuard(headerBytes);

            
            int totalSize = 0x30 + tableBytes.Length + hashBytes.Length +
                            nameBytes.Length + grpiBytes.Length + _bodyLength;
            byte[] result = new byte[totalSize];
            int pos = 0;

            Array.Copy(headerBytes, 0, result, pos, 0x30); pos += 0x30;
            Array.Copy(tableBytes, 0, result, pos, tableBytes.Length); pos += tableBytes.Length;
            Array.Copy(hashBytes, 0, result, pos, hashBytes.Length); pos += hashBytes.Length;
            Array.Copy(nameBytes, 0, result, pos, nameBytes.Length); pos += nameBytes.Length;
            Array.Copy(grpiBytes, 0, result, pos, grpiBytes.Length); pos += grpiBytes.Length;
            Buffer.BlockCopy(_bodyBuffer, _bodyOffset, result, pos, _bodyLength);

            return result;
        }

        private static byte[] StructToBytes<T>(T value) where T : struct
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
            byte[] bytes = new byte[size];
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                System.Runtime.InteropServices.Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false);
            }
            finally
            {
                handle.Free();
            }
            return bytes;
        }

        
        
        
        public static PvfArchive Open(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("PVF 文件不存在", filePath);
            return Open(File.ReadAllBytes(filePath));
        }

        
        
        
        public static PvfArchive Open(byte[] data)
        {
            if (data == null || data.Length < 0x30)
                throw new InvalidDataException("数据不足以包含 PVF 头部");

            var archive = new PvfArchive();
            archive.Parse(data);
            return archive;
        }

        
        
        
        public string GetFileContent(PvfFileData file)
        {
            if (file == null) return string.Empty;
            int idx = file.Index >= 0 ? file.Index : _files.IndexOf(file);
            byte[] overlayData;
            if (idx >= 0 && _overlay.TryGetValue(idx, out overlayData))
            {
                
                return DecodeRawData(file.Entry.DataType, overlayData);
            }
            return DecodeFileData(file.Entry);
        }

        
        
        
        public string GetFileContent(int fileIndex)
        {
            if (fileIndex < 0 || fileIndex >= _files.Count) return string.Empty;
            byte[] overlayData;
            if (_overlay.TryGetValue(fileIndex, out overlayData))
                return DecodeRawData(_files[fileIndex].Entry.DataType, overlayData);
            return DecodeFileData(_files[fileIndex].Entry);
        }

        
        
        
        public int FindFileIndex(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return -1;

            var normalizedPath = NormalizeArchivePath(relativePath);
            return _pathIndex.TryGetValue(normalizedPath, out var index) ? index : -1;
        }

        
        
        
        public string GetFileContent(string relativePath)
        {
            var fileIndex = FindFileIndex(relativePath);
            return fileIndex >= 0 ? GetFileContent(fileIndex) : string.Empty;
        }

        // Raw bytes are used for byte-level comparison and same-size edit detection.
        public byte[] GetFileRawData(int fileIndex)
        {
            if (fileIndex < 0 || fileIndex >= _files.Count) return null;
            return GetFileRawData(_files[fileIndex]);
        }

        // Existing paths are replaced; missing paths are appended as new PVF files.
        public void SetFileRawData(string relativePath, byte[] newData, int dataType = 1)
        {
            int fileIndex = FindFileIndex(relativePath);
            if (fileIndex >= 0)
            {
                SetFileRawData(fileIndex, newData);
                return;
            }

            AddFileRawData(relativePath, newData, dataType);
        }

        public void SetFileContent(string relativePath, string text, int dataType = 1)
        {
            int fileIndex = FindFileIndex(relativePath);
            if (fileIndex >= 0)
            {
                SetFileContent(fileIndex, text);
                return;
            }

            AddFileContent(relativePath, text, dataType);
        }

        public int AddFileRawData(string relativePath, byte[] data, int dataType = 1)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("PVF relative path cannot be empty.", nameof(relativePath));

            string normalized = NormalizeArchivePath(relativePath);
            int existingIndex = FindFileIndex(normalized);
            if (existingIndex >= 0)
            {
                SetFileRawData(existingIndex, data);
                return existingIndex;
            }

            SplitArchivePath(normalized, out string path, out string name);
            int nameOffset = GetOrAddStringOffset(name);
            int pathOffset = GetOrAddStringOffset(path ?? string.Empty);

            var item = new PvfFileItem
            {
                NameOffset = nameOffset,
                PathOffset = pathOffset,
                // New files get their real chunk/index offsets during SaveAs().
                ChunkIndex = -1,
                DataOffset = 0,
                DataSize = data != null ? data.Length : 0,
                DataType = dataType
            };

            int index = _files.Count;
            var file = new PvfFileData
            {
                Name = name,
                Path = path ?? string.Empty,
                Entry = item,
                Index = index
            };

            _files.Add(file);
            _pathIndex[normalized] = index;
            _overlay[index] = data != null ? (byte[])data.Clone() : Array.Empty<byte>();
            _header.FileCount = _files.Count;
            return index;
        }

        public int AddFileContent(string relativePath, string text, int dataType = 1)
        {
            byte[] raw = EncodeTextToRaw(dataType, text);
            return AddFileRawData(relativePath, raw, dataType);
        }

        
        
        
        public byte[] GetFileRawData(PvfFileData file)
        {
            if (file == null) return null;
            int idx = file.Index >= 0 ? file.Index : _files.IndexOf(file);
            byte[] overlayData;
            if (idx >= 0 && _overlay.TryGetValue(idx, out overlayData))
                return (byte[])overlayData.Clone();

            var item = file.Entry;
            byte[] chunk = GetChunkData(item.ChunkIndex);
            if (chunk == null || item.DataOffset < 0 || item.DataSize <= 0 ||
                item.DataOffset + item.DataSize > chunk.Length)
                return null;
            return chunk.Slice(item.DataOffset, item.DataSize);
        }

        
        
        
        public void SetFileRawData(int fileIndex, byte[] newData)
        {
            if (fileIndex < 0 || fileIndex >= _files.Count)
                throw new ArgumentOutOfRangeException(nameof(fileIndex));
            if (newData == null) newData = Array.Empty<byte>();
            _overlay[fileIndex] = newData;
        }

        
        
        
        public void SetFileContent(int fileIndex, string text)
        {
            if (fileIndex < 0 || fileIndex >= _files.Count)
                throw new ArgumentOutOfRangeException(nameof(fileIndex));
            var item = _files[fileIndex].Entry;
            byte[] encoded = EncodeTextToRaw(item.DataType, text);
            _overlay[fileIndex] = encoded;
        }

        
        
        
        public bool IsFileModified(int fileIndex)
        {
            return _overlay.ContainsKey(fileIndex);
        }

        
        
        
        public void RevertFile(int fileIndex)
        {
            _overlay.Remove(fileIndex);
        }

        
        
        
        public void RevertAll()
        {
            _overlay.Clear();
        }

        
        
        
        public int IndexOf(PvfFileData file)
        {
            if (file == null) return -1;
            return file.Index >= 0 ? file.Index : _files.IndexOf(file);
        }

        
        
        
        public byte[] GetChunkData(int chunkIndex)
        {
            if (chunkIndex < 0 || chunkIndex >= _groups.Count)
                return null;

            return _chunkCache.GetOrAdd(chunkIndex, ci =>
            {
                var prev = ci > 0 ? _groups[ci - 1] : default;
                var curr = _groups[ci];

                int start = _bodyOffset + prev.CompressedSize;
                int size = curr.CompressedSize - prev.CompressedSize;
                if (size <= 0 || start + size > _bodyOffset + _bodyLength)
                    return null;

                byte[] encrypted = new byte[size];
                Buffer.BlockCopy(_bodyBuffer, start, encrypted, 0, size);
                PvfDecryptor.Decrypt("BodY", encrypted);
                return PvfDecryptor.ZlibDecompress(encrypted);
            });
        }

        
        
        
        public byte[] GetChunkRawEncrypted(int chunkIndex)
        {
            if (chunkIndex < 0 || chunkIndex >= _groups.Count)
                return null;

            var prev = chunkIndex > 0 ? _groups[chunkIndex - 1] : default;
            var curr = _groups[chunkIndex];

            int start = _bodyOffset + prev.CompressedSize;
            int size = curr.CompressedSize - prev.CompressedSize;
            if (size <= 0 || start + size > _bodyOffset + _bodyLength)
                return null;

            var result = new byte[size];
            Buffer.BlockCopy(_bodyBuffer, start, result, 0, size);
            return result;
        }

        #region 解析流程

        private void Parse(byte[] allBytes)
        {
            PvfHeader header = default;
            bool decoded = false;
            Exception lastHeaderError = null;
            foreach (var usesGuard in new[] { true, false })
            {
                try
                {
                    header = DecodeHeaderCandidate(allBytes, usesGuard);
                    _headerUsesGuard = usesGuard;
                    decoded = true;
                    break;
                }
                catch (InvalidDataException ex)
                {
                    lastHeaderError = ex;
                }
            }

            if (!decoded)
                throw new InvalidDataException("PVF header did not match a supported format.", lastHeaderError);
            _header = header;

            
            int pos = 0x30;
            int tableOffset = pos;
            int tableSize = header.FileCount * 0x18;
            pos += tableSize;

            int hashOffset = pos;
            pos += header.HashTableSize;

            int nameOffset = pos;
            pos += header.NameTableSize;

            int grpiOffset = pos;
            int grpiSize = header.GroupCount * 8;
            pos += grpiSize;

            
            _bodyBuffer = allBytes;
            _bodyOffset = pos;
            _bodyLength = header.BodySize;

            
            
            _rawTableOffset = tableOffset;
            _rawTableSize = tableSize;

            
            byte[] hashBytes = allBytes.Slice(hashOffset, header.HashTableSize);
            byte[] nameBytes = allBytes.Slice(nameOffset, header.NameTableSize);
            byte[] grpiBytes = allBytes.Slice(grpiOffset, grpiSize);

            _rawNameBytes = (byte[])nameBytes.Clone(); 

            
            PvfDecryptor.Decrypt("GRPI", grpiBytes);
            PvfDecryptor.Decrypt("HASH", hashBytes);
            _rawGrpiBytes = grpiBytes;
            _rawHashBytes = hashBytes;

            BuildStringBuffers(nameBytes);

            
            ParseFileItemsFast(header.FileCount, allBytes, tableOffset);
            ParseGroupItemsFast(header.GroupCount, grpiBytes);
            _hashTable = PvfHashTable.Parse(hashBytes);
        }

        private static PvfHeader DecodeHeaderCandidate(byte[] allBytes, bool usesGuard)
        {
            byte[] headerBytes = allBytes.Slice(0, 0x30);
            if (usesGuard)
                PvfDecryptor.DecryptGuard(headerBytes);
            if (PvfDecryptor.Decrypt("HeaD", headerBytes) != 0)
                throw new InvalidDataException("PVF header decryption failed.");

            var header = headerBytes.ToStruct<PvfHeader>();
            if (header.Signature != MagicSignature)
                throw new InvalidDataException("PVF signature is invalid.");
            ValidateHeaderLayout(header, allBytes.Length);
            return header;
        }

        private static void ValidateHeaderLayout(PvfHeader header, int dataLength)
        {
            if (header.FileCount < 0 || header.GroupCount < 0 ||
                header.HashTableSize < 0 || header.NameTableSize < 0 || header.BodySize < 0)
            {
                throw new InvalidDataException("PVF header contains a negative section size.");
            }

            try
            {
                long declaredLength = checked(
                    0x30L + (long)header.FileCount * 0x18 + header.HashTableSize +
                    header.NameTableSize + (long)header.GroupCount * 8 + header.BodySize);
                if (declaredLength > dataLength)
                    throw new InvalidDataException("PVF header sections exceed the file boundary.");
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("PVF header section sizes overflowed.", ex);
            }
        }

        private void BuildStringBuffers(byte[] nameBytes)
        {
            if (nameBytes == null || nameBytes.Length < 16) return;

            int idx = 8; 
            _strABuffer = DecryptStringBuffer(nameBytes, ref idx, "sTrA", 0xAA74472E);
            _strWBuffer = DecryptStringBuffer(nameBytes, ref idx, "sTrW", 0x9A82F037);
        }

        private static byte[] DecryptStringBuffer(byte[] bytes, ref int index, string key, uint xorConst)
        {
            if (index + 8 > bytes.Length)
                return Array.Empty<byte>();

            int cnt1 = BitConverter.ToInt32(bytes, index); index += 4;
            int cnt2 = BitConverter.ToInt32(bytes, index); index += 4;

            int encSize = (int)(cnt1 ^ xorConst);
            if (encSize <= 0 || index + encSize > bytes.Length)
                return Array.Empty<byte>();

            byte[] encrypted = bytes.Slice(index, encSize);
            index += encSize;

            PvfDecryptor.Decrypt2(key, encrypted);
            return PvfDecryptor.ZlibDecompress(encrypted);
        }

        private void ParseFileItemsFast(int count, byte[] buffer, int offset)
        {
            _files.Capacity = count;
            var stringCache = new Dictionary<int, string>(count / 4);
            unsafe
            {
                fixed (byte* pBase = buffer)
                {
                    byte* pTable = pBase + offset;
                    for (int i = 0; i < count; i++)
                    {
                        PvfFileItem* pItem = (PvfFileItem*)(pTable + i * 0x18);
                        var item = *pItem;

                        if (!stringCache.TryGetValue(item.NameOffset, out string name))
                        {
                            name = ResolveString(item.NameOffset);
                            stringCache[item.NameOffset] = name;
                        }
                        if (!stringCache.TryGetValue(item.PathOffset, out string path))
                        {
                            path = ResolveString(item.PathOffset);
                            stringCache[item.PathOffset] = path;
                        }

                        _files.Add(new PvfFileData
                        {
                            Name = name,
                            Path = path,
                            Entry = item,
                            Index = i
                        });

                        var archivePath = NormalizeArchivePath(path, name);
                        if (!_pathIndex.ContainsKey(archivePath))
                            _pathIndex.Add(archivePath, i);
                    }
                }
            }
        }

        private static string NormalizeArchivePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return string.Empty;

            var normalized = relativePath.Replace('\\', '/').Trim();
            while (normalized.StartsWith("./", StringComparison.Ordinal) || normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.StartsWith("./", StringComparison.Ordinal)
                    ? normalized.Substring(2)
                    : normalized.Substring(1);
            }

            return normalized.TrimEnd('/');
        }

        private static string NormalizeArchivePath(string path, string name)
        {
            if (string.IsNullOrEmpty(path))
                return NormalizeArchivePath(name);

            if (string.IsNullOrEmpty(name))
                return NormalizeArchivePath(path);

            return NormalizeArchivePath(path + "/" + name);
        }

        private unsafe void ParseGroupItemsFast(int count, byte[] grpiBytes)
        {
            _groups.Capacity = count;
            fixed (byte* pBase = grpiBytes)
            {
                for (int i = 0; i < count; i++)
                {
                    GrpiItem* pItem = (GrpiItem*)(pBase + i * 8);
                    _groups.Add(*pItem);
                }
            }
        }

        #endregion

        #region 文件内容解码

        private string DecodeFileData(PvfFileItem item)
        {
            switch (item.DataType)
            {
                case 1: return DecodeType1(item);  
                case 3: return DecodeType3(item);  
                default: return string.Empty;
            }
        }

        
        
        
        private string DecodeRawData(int dataType, byte[] data)
        {
            if (data == null || data.Length == 0) return string.Empty;
            switch (dataType)
            {
                case 1: return DecodeType1Raw(data);
                case 3: return Encoding.Unicode.GetString(data);
                default: return string.Empty;
            }
        }

        
        
        
        // Re-encode decompiled text back to the raw PVF payload format.
        internal byte[] EncodeTextToRaw(int dataType, string text)
        {
            if (text == null) text = string.Empty;
            switch (dataType)
            {
                case 1: return EncodeType1Text(text);
                case 3: return Encoding.Unicode.GetBytes(text);
                default: return Encoding.UTF8.GetBytes(text);
            }
        }

        
        
        
        private string DecodeType1(PvfFileItem item)
        {
            byte[] chunk = GetChunkData(item.ChunkIndex);
            if (chunk == null || item.DataOffset < 0 || item.DataSize <= 0 ||
                item.DataOffset + item.DataSize > chunk.Length)
                return string.Empty;

            byte[] data = chunk.Slice(item.DataOffset, item.DataSize);
            return DecodeType1Raw(data);
        }

        
        
        
        private string DecodeType1Raw(byte[] data)
        {
            int lineCount = data.Length / 5;
            if (lineCount == 0) return string.Empty;

            var sb = new StringBuilder(data.Length * 2);
            for (int i = 0; i < lineCount; i++)
            {
                int off = i * 5;
                byte type = data[off];
                int value = BitConverter.ToInt32(data, off + 1);
                AppendScriptToken(sb, type, value);
            }
            return sb.ToString();
        }

        private void AppendScriptToken(StringBuilder sb, byte type, int value)
        {
            switch (type)
            {
                case 0: 
                    sb.Append(value).Append(' ');
                    break;
                case 2: 
                    sb.Append(BitConverter.ToSingle(BitConverter.GetBytes(value), 0).ToString("R", CultureInfo.InvariantCulture)).Append(' ');
                    break;
                case 3: 
                    sb.AppendLine().Append(ResolveString(value)).AppendLine();
                    break;
                case 5: 
                    sb.AppendLine().Append("{5=`").Append(EscapeBacktickString(ResolveString(value))).Append("`}");
                    break;
                case 6: 
                    sb.Append('`').Append(EscapeBacktickString(ResolveString(value))).Append("` ");
                    break;
                case 7: 
                    sb.AppendLine().Append("{7=`").Append(EscapeBacktickString(ResolveString(value))).Append("`}");
                    break;
            }
        }

        private static string EscapeBacktickString(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("`", "``");
        }

        private byte[] EncodeType1Text(string text)
        {
            var tokens = new List<Type1Token>();
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch))
                {
                    i++;
                    continue;
                }

                if (ch == '#')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }

                if (ch == '`')
                {
                    string value;
                    int nextIndex;
                    if (TryReadBacktickString(text, i, out value, out nextIndex))
                    {
                        tokens.Add(new Type1Token(6, GetOrAddStringOffset(value)));
                        i = nextIndex;
                        continue;
                    }
                }

                if (ch == '{')
                {
                    int end = FindMarkerEnd(text, i + 1);
                    if (end > i)
                    {
                        string marker = text.Substring(i, end - i + 1).Trim();
                        Type1Token markerToken;
                        if (TryParseSpecialMarker(marker, out markerToken))
                        {
                            tokens.Add(markerToken);
                            i = end + 1;
                            continue;
                        }
                    }
                }

                if (ch == '[')
                {
                    int end = text.IndexOf(']', i + 1);
                    if (end > i)
                    {
                        string tag = text.Substring(i, end - i + 1);
                        tokens.Add(new Type1Token(3, GetOrAddStringOffset(tag)));
                        i = end + 1;
                        continue;
                    }
                }

                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    if (text[i] == '`' || text[i] == '{' || text[i] == '[')
                        break;
                    i++;
                }

                if (i == start)
                {
                    i++;
                    continue;
                }

                string token = text.Substring(start, i - start);
                int intValue;
                float floatValue;
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                {
                    tokens.Add(new Type1Token(0, intValue));
                }
                else if (float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                {
                    tokens.Add(new Type1Token(2, BitConverter.ToInt32(BitConverter.GetBytes(floatValue), 0)));
                }
                else
                {
                    tokens.Add(new Type1Token(3, GetOrAddStringOffset(token)));
                }
            }

            byte[] raw = new byte[tokens.Count * 5];
            for (int n = 0; n < tokens.Count; n++)
            {
                int off = n * 5;
                raw[off] = tokens[n].Type;
                byte[] valueBytes = BitConverter.GetBytes(tokens[n].Value);
                Buffer.BlockCopy(valueBytes, 0, raw, off + 1, 4);
            }
            return raw;
        }

        private static bool TryReadBacktickString(string text, int start, out string value, out int nextIndex)
        {
            value = string.Empty;
            nextIndex = start + 1;
            if (start < 0 || start >= text.Length || text[start] != '`')
                return false;

            var sb = new StringBuilder();
            int i = start + 1;
            while (i < text.Length)
            {
                if (text[i] == '`')
                {
                    if (i + 1 < text.Length && text[i + 1] == '`')
                    {
                        sb.Append('`');
                        i += 2;
                        continue;
                    }

                    value = sb.ToString();
                    nextIndex = i + 1;
                    return true;
                }

                sb.Append(text[i]);
                i++;
            }

            value = sb.ToString();
            nextIndex = i;
            return true;
        }

        private static int FindMarkerEnd(string text, int start)
        {
            bool inBacktick = false;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '`')
                {
                    if (inBacktick && i + 1 < text.Length && text[i + 1] == '`')
                    {
                        i++;
                        continue;
                    }
                    inBacktick = !inBacktick;
                    continue;
                }

                if (!inBacktick && text[i] == '}')
                    return i;
            }
            return -1;
        }

        private bool TryParseSpecialMarker(string marker, out Type1Token token)
        {
            token = default(Type1Token);
            if (string.IsNullOrEmpty(marker) || marker.Length < 4 || marker[0] != '{' || marker[marker.Length - 1] != '}')
                return false;

            byte type;
            if (marker.StartsWith("{5=", StringComparison.OrdinalIgnoreCase))
                type = 5;
            else if (marker.StartsWith("{7=", StringComparison.OrdinalIgnoreCase))
                type = 7;
            else
                return false;

            string inner = marker.Substring(3, marker.Length - 4).Trim();
            if (inner.Length >= 2 && inner[0] == '`' && inner[inner.Length - 1] == '`')
                inner = inner.Substring(1, inner.Length - 2);

            int numericValue;
            int value = int.TryParse(inner, NumberStyles.Integer, CultureInfo.InvariantCulture, out numericValue)
                ? numericValue
                : GetOrAddStringOffset(inner);
            token = new Type1Token(type, value);
            return true;
        }

        private struct Type1Token
        {
            public readonly byte Type;
            public readonly int Value;

            public Type1Token(byte type, int value)
            {
                Type = type;
                Value = value;
            }
        }

        
        
        
        private string DecodeType3(PvfFileItem item)
        {
            byte[] chunk = GetChunkData(item.ChunkIndex);
            if (chunk == null || item.DataOffset < 0 || item.DataSize <= 0 ||
                item.DataOffset + item.DataSize > chunk.Length)
                return string.Empty;

            return Encoding.Unicode.GetString(chunk, item.DataOffset, item.DataSize);
        }

        #endregion

        #region 字符串表

        
        
        
        
        public string ResolveString(int magicOffset)
        {
            if (magicOffset < 0) return string.Empty;

            if ((magicOffset & 1) != 0) 
            {
                int offset = (magicOffset >> 1) * 2;
                return ReadUnicodeString(_strWBuffer, offset);
            }
            else 
            {
                int offset = magicOffset >> 1;
                return ReadUtf8String(_strABuffer, offset);
            }
        }

        private static string ReadUtf8String(byte[] buffer, int start)
        {
            if (buffer == null || start < 0 || start >= buffer.Length)
                return string.Empty;

            int end = Array.IndexOf(buffer, (byte)0, start);
            if (end < start) return string.Empty;
            return Encoding.UTF8.GetString(buffer, start, end - start);
        }

        private static string ReadUnicodeString(byte[] buffer, int start)
        {
            if (buffer == null || start < 0 || start >= buffer.Length)
                return string.Empty;

            for (int i = start; i < buffer.Length - 1; i += 2)
            {
                if (buffer[i] == 0 && buffer[i + 1] == 0)
                {
                    int len = i - start;
                    if (len <= 0) return string.Empty;
                    len = (len / 2) * 2; 
                    return Encoding.Unicode.GetString(buffer, start, len);
                }
            }

            
            int remaining = ((buffer.Length - start) / 2) * 2;
            return remaining > 0
                ? Encoding.Unicode.GetString(buffer, start, remaining)
                : string.Empty;
        }

        internal int GetOrAddStringOffset(string value, bool preferUnicode = false)
        {
            if (value == null) value = string.Empty;
            EnsureStringOffsetCache();

            int offset;
            if (!preferUnicode && _strAOffsetCache.TryGetValue(value, out offset))
                return offset;
            if (_strWOffsetCache.TryGetValue(value, out offset))
                return offset;
            if (preferUnicode && _strAOffsetCache.TryGetValue(value, out offset))
                return offset;

            if (preferUnicode)
                return AppendUnicodeString(value);
            return AppendUtf8String(value);
        }

        // Repack sTrA/sTrW so added names and script strings resolve in the new PVF.
        internal byte[] BuildNameTableBytes()
        {
            byte[] strA = _strABuffer ?? new byte[] { 0 };
            byte[] strW = _strWBuffer ?? new byte[] { 0, 0 };

            using (var ms = new MemoryStream())
            {
                if (_rawNameBytes != null && _rawNameBytes.Length >= 8)
                    ms.Write(_rawNameBytes, 0, 8);
                else
                    ms.Write(new byte[8], 0, 8);

                WriteNameTableSection(ms, "sTrA", strA, 0xAA74472E);
                WriteNameTableSection(ms, "sTrW", strW, 0x9A82F037);
                return ms.ToArray();
            }
        }

        private void EnsureStringOffsetCache()
        {
            if (_strAOffsetCache != null && _strWOffsetCache != null)
                return;

            _strAOffsetCache = new Dictionary<string, int>(StringComparer.Ordinal);
            _strWOffsetCache = new Dictionary<string, int>(StringComparer.Ordinal);

            if (_strABuffer == null) _strABuffer = new byte[] { 0 };
            if (_strWBuffer == null) _strWBuffer = new byte[] { 0, 0 };

            int pos = 0;
            while (pos < _strABuffer.Length)
            {
                int end = Array.IndexOf(_strABuffer, (byte)0, pos);
                if (end < 0) end = _strABuffer.Length;
                string value = end > pos ? Encoding.UTF8.GetString(_strABuffer, pos, end - pos) : string.Empty;
                if (!_strAOffsetCache.ContainsKey(value))
                    _strAOffsetCache[value] = pos << 1;
                pos = end + 1;
            }

            pos = 0;
            while (pos + 1 < _strWBuffer.Length)
            {
                int end = pos;
                while (end + 1 < _strWBuffer.Length && !(_strWBuffer[end] == 0 && _strWBuffer[end + 1] == 0))
                    end += 2;
                string value = end > pos ? Encoding.Unicode.GetString(_strWBuffer, pos, end - pos) : string.Empty;
                if (!_strWOffsetCache.ContainsKey(value))
                    _strWOffsetCache[value] = ((pos / 2) << 1) | 1;
                pos = end + 2;
            }
        }

        private int AppendUtf8String(string value)
        {
            byte[] textBytes = Encoding.UTF8.GetBytes(value);
            int oldLength = _strABuffer != null ? _strABuffer.Length : 0;
            byte[] next = new byte[oldLength + textBytes.Length + 1];
            if (_strABuffer != null) Buffer.BlockCopy(_strABuffer, 0, next, 0, _strABuffer.Length);
            Buffer.BlockCopy(textBytes, 0, next, oldLength, textBytes.Length);
            next[next.Length - 1] = 0;
            _strABuffer = next;

            int magicOffset = oldLength << 1;
            _strAOffsetCache[value] = magicOffset;
            return magicOffset;
        }

        private int AppendUnicodeString(string value)
        {
            byte[] textBytes = Encoding.Unicode.GetBytes(value);
            int oldLength = _strWBuffer != null ? _strWBuffer.Length : 0;
            if ((oldLength & 1) != 0) oldLength++;

            byte[] next = new byte[oldLength + textBytes.Length + 2];
            if (_strWBuffer != null) Buffer.BlockCopy(_strWBuffer, 0, next, 0, _strWBuffer.Length);
            Buffer.BlockCopy(textBytes, 0, next, oldLength, textBytes.Length);
            _strWBuffer = next;

            int magicOffset = ((oldLength / 2) << 1) | 1;
            _strWOffsetCache[value] = magicOffset;
            return magicOffset;
        }

        private static void WriteNameTableSection(Stream output, string key, byte[] rawBuffer, uint xorConst)
        {
            if (rawBuffer == null || rawBuffer.Length == 0)
                rawBuffer = key == "sTrW" ? new byte[] { 0, 0 } : new byte[] { 0 };

            byte[] compressed = PvfDecryptor.ZlibCompress(rawBuffer);
            byte[] encrypted = (byte[])compressed.Clone();
            PvfDecryptor.Decrypt2(key, encrypted);

            WriteInt32(output, (int)(encrypted.Length ^ xorConst));
            WriteInt32(output, rawBuffer.Length ^ encrypted.Length);
            output.Write(encrypted, 0, encrypted.Length);
        }

        private static void WriteInt32(Stream output, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            output.Write(bytes, 0, bytes.Length);
        }

        private static void SplitArchivePath(string relativePath, out string path, out string name)
        {
            string normalized = NormalizeArchivePath(relativePath);
            int slash = normalized.LastIndexOf('/');
            if (slash < 0)
            {
                path = string.Empty;
                name = normalized;
                return;
            }

            path = normalized.Substring(0, slash);
            name = normalized.Substring(slash + 1);
        }

        #endregion

        
        
        
        public PvfHashTable RebuildHashTable()
        {
            var items = new PvfFileItem[_files.Count];
            for (int i = 0; i < _files.Count; i++)
                items[i] = _files[i].Entry;
            return PvfHashTable.Build(items, ResolveString);
        }

        
        
        
        
        // Save writes a new PVF container, but only rebuilds chunks touched by overlay.
        public void SaveAs(string outputPath, Action<int, int> onProgress = null)
        {
            if (_overlay.Count == 0 && _files.Count == _header.FileCount)
            {
                File.WriteAllBytes(outputPath, ToBytes());
                return;
            }

            var modifiedChunks = new HashSet<int>();
            var newFileIndices = new List<int>();
            for (int i = 0; i < _files.Count; i++)
            {
                var item = _files[i].Entry;
                if (item.ChunkIndex < 0 || item.ChunkIndex >= _groups.Count)
                {
                    newFileIndices.Add(i);
                    continue;
                }

                if (_overlay.ContainsKey(i))
                    modifiedChunks.Add(item.ChunkIndex);
            }

            int originalChunkCount = _groups.Count;

            string outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            string tempBodyPath = outputPath + ".body.tmp";
            var newGroups = new List<GrpiItem>(originalChunkCount + (newFileIndices.Count > 0 ? 1 : 0));
            var newItems = new PvfFileItem[_files.Count];
            for (int i = 0; i < _files.Count; i++)
                newItems[i] = _files[i].Entry;

            int cumulativeCompressed = 0;

            try
            {
                using (var bodyStream = new FileStream(tempBodyPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024))
                {
                    for (int ci = 0; ci < originalChunkCount; ci++)
                    {
                        if (!modifiedChunks.Contains(ci))
                        {
                            byte[] rawEncrypted = GetChunkRawEncrypted(ci);
                            if (rawEncrypted != null)
                            {
                                bodyStream.Write(rawEncrypted, 0, rawEncrypted.Length);
                                cumulativeCompressed += rawEncrypted.Length;
                                newGroups.Add(new GrpiItem
                                {
                                    CompressedSize = cumulativeCompressed,
                                    OriginalSize = _groups[ci].OriginalSize
                                });
                            }
                        }
                        else
                        {
                            byte[] originalChunk = GetChunkData(ci);
                            byte[] newChunk = RebuildChunkWithOverlay(ci, originalChunk, newItems);

                            byte[] compressed = PvfDecryptor.ZlibCompress(newChunk);
                            byte[] encrypted = (byte[])compressed.Clone();
                            PvfDecryptor.Decrypt("BodY", encrypted);

                            bodyStream.Write(encrypted, 0, encrypted.Length);
                            cumulativeCompressed += encrypted.Length;
                            newGroups.Add(new GrpiItem
                            {
                                CompressedSize = cumulativeCompressed,
                                OriginalSize = newChunk.Length
                            });
                        }

                        if (onProgress != null && (ci % 100 == 0 || ci == originalChunkCount - 1))
                            onProgress(ci + 1, originalChunkCount + (newFileIndices.Count > 0 ? 1 : 0));
                    }

                    if (newFileIndices.Count > 0)
                    {
                        int newChunkIndex = newGroups.Count;
                        using (var chunkStream = new MemoryStream())
                        {
                            foreach (int fileIndex in newFileIndices)
                            {
                                byte[] data;
                                if (!_overlay.TryGetValue(fileIndex, out data) || data == null)
                                    data = Array.Empty<byte>();

                                var item = newItems[fileIndex];
                                item.ChunkIndex = newChunkIndex;
                                item.DataOffset = (int)chunkStream.Position;
                                item.DataSize = data.Length;
                                if (data.Length > 0)
                                    chunkStream.Write(data, 0, data.Length);
                                newItems[fileIndex] = item;
                            }

                            byte[] newChunk = chunkStream.ToArray();
                            if (newChunk.Length > 0)
                            {
                                byte[] compressed = PvfDecryptor.ZlibCompress(newChunk);
                                byte[] encrypted = (byte[])compressed.Clone();
                                PvfDecryptor.Decrypt("BodY", encrypted);

                                bodyStream.Write(encrypted, 0, encrypted.Length);
                                cumulativeCompressed += encrypted.Length;
                                newGroups.Add(new GrpiItem
                                {
                                    CompressedSize = cumulativeCompressed,
                                    OriginalSize = newChunk.Length
                                });
                            }
                            else if (newChunkIndex > 0)
                            {
                                foreach (int fileIndex in newFileIndices)
                                {
                                    var item = newItems[fileIndex];
                                    item.ChunkIndex = newChunkIndex - 1;
                                    item.DataOffset = 0;
                                    item.DataSize = 0;
                                    newItems[fileIndex] = item;
                                }
                            }
                        }

                        if (onProgress != null)
                            onProgress(originalChunkCount + 1, originalChunkCount + 1);
                    }
                }

                byte[] tableBytes = new byte[newItems.Length * 0x18];
                for (int i = 0; i < newItems.Length; i++)
                {
                    byte[] itemBytes = StructToBytes(newItems[i]);
                    Array.Copy(itemBytes, 0, tableBytes, i * 0x18, 0x18);
                }

                // HASH must be rebuilt when file paths/counts or offsets change.
                byte[] hashBytes = PvfHashTable.Build(newItems, ResolveString).ToBytes();
                PvfDecryptor.Decrypt("HASH", hashBytes);
                byte[] nameBytes = BuildNameTableBytes();

                byte[] grpiBytes = new byte[newGroups.Count * 8];
                for (int i = 0; i < newGroups.Count; i++)
                {
                    byte[] g = StructToBytes(newGroups[i]);
                    Array.Copy(g, 0, grpiBytes, i * 8, 8);
                }
                PvfDecryptor.Decrypt("GRPI", grpiBytes);

                var header = _header;
                // Header sizes must match the rebuilt table/name/grpi/body sections.
                header.FileCount = newItems.Length;
                header.BodySize = cumulativeCompressed;
                header.GroupCount = newGroups.Count;
                header.HashTableSize = hashBytes.Length;
                header.NameTableSize = nameBytes.Length;

                byte[] headerBytes = StructToBytes(header);
                PvfDecryptor.Decrypt("HeaD", headerBytes);
                if (_headerUsesGuard)
                    PvfDecryptor.DecryptGuard(headerBytes);

                using (var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
                {
                    outFs.Write(headerBytes, 0, 0x30);
                    outFs.Write(tableBytes, 0, tableBytes.Length);
                    outFs.Write(hashBytes, 0, hashBytes.Length);
                    outFs.Write(nameBytes, 0, nameBytes.Length);
                    outFs.Write(grpiBytes, 0, grpiBytes.Length);

                    using (var bodyIn = new FileStream(tempBodyPath, FileMode.Open, FileAccess.Read, FileShare.None, 256 * 1024))
                    {
                        byte[] copyBuf = new byte[256 * 1024];
                        int read;
                        while ((read = bodyIn.Read(copyBuf, 0, copyBuf.Length)) > 0)
                            outFs.Write(copyBuf, 0, read);
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tempBodyPath)) File.Delete(tempBodyPath); } catch { }
            }
        }

        
        
        
        private byte[] RebuildChunkWithOverlay(int chunkIndex, byte[] originalChunk, PvfFileItem[] newItems)
        {
            
            var segments = new List<(int origOffset, int origSize, int fileIndex, byte[] newData)>();
            for (int i = 0; i < _files.Count; i++)
            {
                var item = _files[i].Entry;
                byte[] overlayData;
                bool hasOverlay = _overlay.TryGetValue(i, out overlayData);
                if (item.ChunkIndex != chunkIndex || (item.DataSize <= 0 && !hasOverlay)) continue;

                segments.Add((item.DataOffset, item.DataSize, i, hasOverlay ? overlayData : null));
            }
            segments.Sort((a, b) => a.origOffset.CompareTo(b.origOffset));

            var ms = new MemoryStream();
            int srcPos = 0;
            foreach (var seg in segments)
            {
                
                if (seg.origOffset > srcPos && originalChunk != null)
                    ms.Write(originalChunk, srcPos, seg.origOffset - srcPos);

                var item = newItems[seg.fileIndex];
                item.DataOffset = (int)ms.Position;

                if (seg.newData != null)
                {
                    ms.Write(seg.newData, 0, seg.newData.Length);
                    item.DataSize = seg.newData.Length;
                }
                else if (originalChunk != null && seg.origOffset >= 0 &&
                         seg.origOffset + seg.origSize <= originalChunk.Length)
                {
                    ms.Write(originalChunk, seg.origOffset, seg.origSize);
                }

                newItems[seg.fileIndex] = item;
                srcPos = seg.origOffset + seg.origSize;
            }

            
            if (originalChunk != null && srcPos < originalChunk.Length)
                ms.Write(originalChunk, srcPos, originalChunk.Length - srcPos);

            return ms.ToArray();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _strABuffer = null;
            _strWBuffer = null;
            _bodyBuffer = null;
            _rawTableBytes = null;
            _rawHashBytes = null;
            _rawNameBytes = null;
            _rawGrpiBytes = null;
            _strAOffsetCache = null;
            _strWOffsetCache = null;
            _files.Clear();
            _groups.Clear();
            _overlay.Clear();
            _chunkCache.Clear();
        }
    }
}
