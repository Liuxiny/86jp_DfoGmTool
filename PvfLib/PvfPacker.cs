using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GmPvfLib
{
    
    
    
    
    public static class PvfPacker
    {
        
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
        
        private static readonly HashSet<char> InvalidCharSet = new HashSet<char>(InvalidFileNameChars);

        public class Progress
        {
            public int Current { get; set; }
            public int Total { get; set; }
            public string Phase { get; set; }
        }

        public class PackResult
        {
            public int TotalFiles { get; set; }
            public int Replaced { get; set; }
            // New PVF paths appended during full/text packing.
            public int Added { get; set; }
            public int Unchanged { get; set; }
            public int SkippedChunks { get; set; }
            public int RebuiltChunks { get; set; }
            public int OutputSize { get; set; }
        }

        public static PackResult Pack(string templatePvfPath, string inputDir, string outputPvfPath, Action<Progress> onProgress = null)
        {
            if (!File.Exists(templatePvfPath))
                throw new FileNotFoundException("模板 PVF 文件不存在", templatePvfPath);
            if (!Directory.Exists(inputDir))
                throw new DirectoryNotFoundException("输入目录不存在: " + inputDir);

            using (var archive = PvfArchive.Open(templatePvfPath))
            {
                // Use content comparison so equal-size edits are detected.
                return PackFullCore(archive, inputDir, outputPvfPath, false, onProgress);
            }
        }

        public static PackResult Pack(PvfArchive archive, string inputDir, string outputPvfPath, Action<Progress> onProgress = null)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            if (!Directory.Exists(inputDir))
                throw new DirectoryNotFoundException("输入目录不存在: " + inputDir);
            // Use content comparison so equal-size edits are detected.
            return PackFullCore(archive, inputDir, outputPvfPath, false, onProgress);
        }

        public static PackResult PackFull(string templatePvfPath, string inputDir, string outputPvfPath, Action<Progress> onProgress = null)
        {
            if (!File.Exists(templatePvfPath))
                throw new FileNotFoundException("Template PVF file not found.", templatePvfPath);
            if (!Directory.Exists(inputDir))
                throw new DirectoryNotFoundException("Input directory not found: " + inputDir);

            using (var archive = PvfArchive.Open(templatePvfPath))
            {
                return PackFull(archive, inputDir, outputPvfPath, onProgress);
            }
        }

        public static PackResult PackFull(PvfArchive archive, string inputDir, string outputPvfPath, Action<Progress> onProgress = null)
        {
            return PackFullCore(archive, inputDir, outputPvfPath, false, onProgress);
        }

        public static PackResult PackText(string templatePvfPath, string inputDir, string outputPvfPath, Action<Progress> onProgress = null)
        {
            if (!File.Exists(templatePvfPath))
                throw new FileNotFoundException("Template PVF file not found.", templatePvfPath);
            if (!Directory.Exists(inputDir))
                throw new DirectoryNotFoundException("Input directory not found: " + inputDir);

            using (var archive = PvfArchive.Open(templatePvfPath))
            {
                return PackText(archive, inputDir, outputPvfPath, onProgress);
            }
        }

        public static PackResult PackText(PvfArchive archive, string inputDir, string outputPvfPath, Action<Progress> onProgress = null)
        {
            return PackFullCore(archive, inputDir, outputPvfPath, true, onProgress);
        }

        private static PackResult PackFullCore(PvfArchive archive, string inputDir, string outputPvfPath, bool inputIsDecompiledText, Action<Progress> onProgress)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            if (!Directory.Exists(inputDir))
                throw new DirectoryNotFoundException("Input directory not found: " + inputDir);

            var result = new PackResult { TotalFiles = archive.FileCount };
            var progress = new Progress { Phase = "Indexing disk files" };
            var diskIndex = BuildDiskIndex(inputDir);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < archive.FileCount; i++)
            {
                var file = archive.Files[i];
                if (file.Name.EndsWith("/") || file.Name.EndsWith("\\"))
                    continue;

                string relPath = BuildRelativePath(file.Path, file.Name);
                string diskPath;
                if (!diskIndex.TryGetValue(relPath, out diskPath))
                    continue;

                seen.Add(relPath);
                if (inputIsDecompiledText && (file.Entry.DataType == 1 || file.Entry.DataType == 3))
                {
                    // Text round-trip: compare decoded text before re-encoding.
                    string diskText = File.ReadAllText(diskPath, Encoding.UTF8);
                    string oldText = archive.GetFileContent(i);
                    if (!string.Equals(diskText, oldText, StringComparison.Ordinal))
                    {
                        archive.SetFileRawData(i, archive.EncodeTextToRaw(file.Entry.DataType, diskText));
                        result.Replaced++;
                    }
                    else
                    {
                        result.Unchanged++;
                    }
                }
                else
                {
                    byte[] diskData = File.ReadAllBytes(diskPath);
                    byte[] oldData = archive.GetFileRawData(i) ?? Array.Empty<byte>();
                    // Raw mode compares bytes, not file size, so same-size edits are caught.
                    if (!ByteArrayEquals(diskData, oldData))
                    {
                        archive.SetFileRawData(i, diskData);
                        result.Replaced++;
                    }
                    else
                    {
                        result.Unchanged++;
                    }
                }

                if (onProgress != null && (i % 1000 == 0 || i == archive.FileCount - 1))
                {
                    progress.Phase = "Comparing files";
                    progress.Current = i + 1;
                    progress.Total = archive.FileCount;
                    onProgress(progress);
                }
            }

            foreach (var kvp in diskIndex)
            {
                string relPath = NormalizeDiskRelativePath(kvp.Key);
                if (seen.Contains(relPath) || archive.FindFileIndex(relPath) >= 0)
                    continue;

                // Disk-only paths are appended and later included in rebuilt HASH/NameTable.
                int dataType = GuessDataType(relPath);
                byte[] diskData = ReadDiskDataForArchive(archive, dataType, kvp.Value, inputIsDecompiledText);
                archive.AddFileRawData(relPath, diskData, dataType);
                result.Added++;
            }

            result.TotalFiles = archive.FileCount;
            archive.SaveAs(outputPvfPath, (current, total) =>
            {
                if (onProgress != null)
                    onProgress(new Progress { Phase = "Writing PVF", Current = current, Total = total });
            });
            result.OutputSize = File.Exists(outputPvfPath) ? (int)Math.Min(int.MaxValue, new FileInfo(outputPvfPath).Length) : 0;
            return result;
        }

        public static PackResult PackAndSync(string templatePvfPath, string inputDir, string outputPvfPath, string clientDir, IEnumerable<string> serverPvfPaths, Action<Progress> onProgress = null)
        {
            var result = PackFull(templatePvfPath, inputDir, outputPvfPath, onProgress);
            SyncScriptPvf(outputPvfPath, clientDir, serverPvfPaths, true);
            return result;
        }

        public static PackResult PackTextAndSync(string templatePvfPath, string inputDir, string outputPvfPath, string clientDir, IEnumerable<string> serverPvfPaths, Action<Progress> onProgress = null)
        {
            var result = PackText(templatePvfPath, inputDir, outputPvfPath, onProgress);
            SyncScriptPvf(outputPvfPath, clientDir, serverPvfPaths, true);
            return result;
        }

        public static void SyncScriptPvf(string pvfPath, string clientDir, IEnumerable<string> serverPvfPaths = null, bool updateFileVerJson = true)
        {
            if (!File.Exists(pvfPath))
                throw new FileNotFoundException("PVF file not found.", pvfPath);

            if (!string.IsNullOrWhiteSpace(clientDir))
            {
                if (!Directory.Exists(clientDir))
                    Directory.CreateDirectory(clientDir);
                string clientPvf = Path.Combine(clientDir, "Script.pvf");
                // Avoid touching the client PVF when bytes are already identical.
                CopyIfDifferent(pvfPath, clientPvf);

                if (updateFileVerJson)
                    UpdateFileVerJson(clientDir, "Script.pvf");
            }

            if (serverPvfPaths != null)
            {
                foreach (string target in serverPvfPaths)
                {
                    if (string.IsNullOrWhiteSpace(target)) continue;
                    string dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    CopyIfDifferent(pvfPath, target);
                }
            }
        }

        public static void UpdateFileVerJson(string clientDir, string filename = "Script.pvf")
        {
            if (string.IsNullOrWhiteSpace(clientDir))
                throw new ArgumentException("Client directory cannot be empty.", nameof(clientDir));

            string filePath = Path.Combine(clientDir, filename);
            string jsonPath = Path.Combine(clientDir, "file_ver.json");
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Client file not found.", filePath);
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("file_ver.json not found.", jsonPath);

            string sha1 = ComputeSha1Lower(filePath);
            long size = new FileInfo(filePath).Length;
            string json = File.ReadAllText(jsonPath, Encoding.UTF8);
            string updated = UpdateJsonStringProperty(json, filename, "hash", sha1);
            updated = UpdateJsonNumberProperty(updated, filename, "file_size", size);
            // Launcher checks SHA1/file_size; write only when those values changed.
            if (!string.Equals(updated, json, StringComparison.Ordinal))
                File.WriteAllText(jsonPath, updated, new UTF8Encoding(false));
        }

        private static byte[] ReadDiskDataForArchive(PvfArchive archive, int dataType, string diskPath, bool inputIsDecompiledText)
        {
            if (inputIsDecompiledText && (dataType == 1 || dataType == 3))
                return archive.EncodeTextToRaw(dataType, File.ReadAllText(diskPath, Encoding.UTF8));
            return File.ReadAllBytes(diskPath);
        }

        private static bool ByteArrayEquals(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i]) return false;
            return true;
        }

        private static int GuessDataType(string relativePath)
        {
            string ext = Path.GetExtension(relativePath ?? string.Empty).ToLowerInvariant();
            switch (ext)
            {
                case ".txt":
                case ".tbl":
                case ".inc":
                    return 3;
                default:
                    return 1;
            }
        }

        private static string NormalizeDiskRelativePath(string relative)
        {
            return (relative ?? string.Empty).Replace('\\', '/').TrimStart('/').TrimEnd('/');
        }

        private static void CopyIfDifferent(string source, string target)
        {
            if (PathsEqual(source, target)) return;
            if (File.Exists(target) && FilesAreSame(source, target)) return;
            File.Copy(source, target, true);
        }

        private static bool FilesAreSame(string left, string right)
        {
            var li = new FileInfo(left);
            var ri = new FileInfo(right);
            if (!li.Exists || !ri.Exists || li.Length != ri.Length) return false;
            return string.Equals(ComputeSha1Lower(left), ComputeSha1Lower(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool PathsEqual(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                     Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string ComputeSha1Lower(string path)
        {
            using (var sha1 = SHA1.Create())
            using (var fs = File.OpenRead(path))
            {
                byte[] hash = sha1.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string UpdateJsonStringProperty(string json, string filename, string property, string value)
        {
            int objStart, objEnd;
            if (!FindJsonObjectByFilename(json, filename, out objStart, out objEnd))
                throw new InvalidDataException("Cannot find entry in file_ver.json: " + filename);

            string obj = json.Substring(objStart, objEnd - objStart + 1);
            string replacement = "\"" + property + "\": \"" + JsonEscape(value) + "\"";
            int prop = IndexOfJsonProperty(obj, property);
            if (prop >= 0)
            {
                int colon = obj.IndexOf(':', prop);
                int valueStart = colon + 1;
                while (valueStart < obj.Length && char.IsWhiteSpace(obj[valueStart])) valueStart++;
                int valueEnd = FindJsonValueEnd(obj, valueStart);
                obj = obj.Substring(0, prop) + replacement + obj.Substring(valueEnd);
            }
            else
            {
                int insertAt = obj.LastIndexOf('}');
                string prefix = obj.IndexOf(':') >= 0 ? ",\n    " : "\n    ";
                obj = obj.Substring(0, insertAt) + prefix + replacement + "\n  " + obj.Substring(insertAt);
            }
            return json.Substring(0, objStart) + obj + json.Substring(objEnd + 1);
        }

        private static string UpdateJsonNumberProperty(string json, string filename, string property, long value)
        {
            int objStart, objEnd;
            if (!FindJsonObjectByFilename(json, filename, out objStart, out objEnd))
                throw new InvalidDataException("Cannot find entry in file_ver.json: " + filename);

            string obj = json.Substring(objStart, objEnd - objStart + 1);
            string replacement = "\"" + property + "\": " + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int prop = IndexOfJsonProperty(obj, property);
            if (prop >= 0)
            {
                int colon = obj.IndexOf(':', prop);
                int valueStart = colon + 1;
                while (valueStart < obj.Length && char.IsWhiteSpace(obj[valueStart])) valueStart++;
                int valueEnd = FindJsonValueEnd(obj, valueStart);
                obj = obj.Substring(0, prop) + replacement + obj.Substring(valueEnd);
            }
            else
            {
                int insertAt = obj.LastIndexOf('}');
                string prefix = obj.IndexOf(':') >= 0 ? ",\n    " : "\n    ";
                obj = obj.Substring(0, insertAt) + prefix + replacement + "\n  " + obj.Substring(insertAt);
            }
            return json.Substring(0, objStart) + obj + json.Substring(objEnd + 1);
        }

        private static bool FindJsonObjectByFilename(string json, string filename, out int objStart, out int objEnd)
        {
            objStart = -1;
            objEnd = -1;
            string needle = "\"filename\"";
            int pos = 0;
            while ((pos = json.IndexOf(needle, pos, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int colon = json.IndexOf(':', pos + needle.Length);
                if (colon < 0) break;
                int valueStart = colon + 1;
                while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;
                if (valueStart >= json.Length || json[valueStart] != '\"') { pos += needle.Length; continue; }
                int valueEnd = FindStringEnd(json, valueStart + 1);
                if (valueEnd < 0) break;
                string found = UnescapeSimpleJsonString(json.Substring(valueStart + 1, valueEnd - valueStart - 1));
                if (string.Equals(found, filename, StringComparison.OrdinalIgnoreCase))
                {
                    objStart = json.LastIndexOf('{', pos);
                    objEnd = FindMatchingBrace(json, objStart);
                    return objStart >= 0 && objEnd >= objStart;
                }
                pos = valueEnd + 1;
            }
            return false;
        }

        private static int IndexOfJsonProperty(string obj, string property)
        {
            return obj.IndexOf("\"" + property + "\"", StringComparison.OrdinalIgnoreCase);
        }

        private static int FindJsonValueEnd(string text, int start)
        {
            if (start >= text.Length) return start;
            if (text[start] == '\"')
            {
                int end = FindStringEnd(text, start + 1);
                return end >= 0 ? end + 1 : text.Length;
            }
            int i = start;
            while (i < text.Length && text[i] != ',' && text[i] != '}' && text[i] != '\r' && text[i] != '\n') i++;
            return i;
        }

        private static int FindStringEnd(string text, int start)
        {
            bool esc = false;
            for (int i = start; i < text.Length; i++)
            {
                if (esc) { esc = false; continue; }
                if (text[i] == '\\') { esc = true; continue; }
                if (text[i] == '\"') return i;
            }
            return -1;
        }

        private static int FindMatchingBrace(string text, int start)
        {
            if (start < 0) return -1;
            bool inString = false, esc = false;
            int depth = 0;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (esc) { esc = false; continue; }
                    if (c == '\\') { esc = true; continue; }
                    if (c == '\"') inString = false;
                    continue;
                }
                if (c == '\"') { inString = true; continue; }
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static string JsonEscape(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string UnescapeSimpleJsonString(string value)
        {
            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
        private static PackResult PackCore(PvfArchive archive, string inputDir, string outputPvfPath, Action<Progress> onProgress)
        {
            var result = new PackResult { TotalFiles = archive.FileCount };
            var progress = new Progress { Total = archive.FileCount, Phase = "Building index" };

            
            var diskIndex = BuildDiskIndex(inputDir);

            
            int chunkCount = 0;
            var chunkGroups = new SortedDictionary<int, List<int>>();
            var fileDiskPaths = new string[archive.FileCount]; 

            progress.Phase = "Matching files";
            for (int i = 0; i < archive.FileCount; i++)
            {
                var file = archive.Files[i];
                int ci = file.Entry.ChunkIndex;
                if (ci >= chunkCount) chunkCount = ci + 1;

                List<int> list;
                if (!chunkGroups.TryGetValue(ci, out list))
                {
                    list = new List<int>();
                    chunkGroups[ci] = list;
                }
                list.Add(i);

                
                if (file.Entry.DataSize > 0 && !file.Name.EndsWith("/") && !file.Name.EndsWith("\\"))
                {
                    string relPath = BuildRelativePath(file.Path, file.Name);
                    string diskPath;
                    if (diskIndex.TryGetValue(relPath.ToLowerInvariant(), out diskPath))
                        fileDiskPaths[i] = diskPath;
                }
            }

            if (onProgress != null)
            {
                progress.Phase = "Matching files";
                progress.Current = archive.FileCount;
                onProgress(progress);
            }

            
            var diskFileSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fileDiskPaths.Length; i++)
            {
                string dp = fileDiskPaths[i];
                if (dp != null && !diskFileSizes.ContainsKey(dp))
                {
                    var fi = new FileInfo(dp);
                    diskFileSizes[dp] = fi.Exists ? fi.Length : -1;
                }
            }

            
            var newItems = new PvfFileItem[archive.FileCount];
            for (int i = 0; i < archive.FileCount; i++)
                newItems[i] = archive.Files[i].Entry;

            
            
            
            var chunkNeedRebuild = new bool[chunkCount];
            for (int i = 0; i < archive.FileCount; i++)
            {
                string dp = fileDiskPaths[i];
                if (dp == null) continue;
                var item = archive.Files[i].Entry;
                long diskSize;
                if (diskFileSizes.TryGetValue(dp, out diskSize) && diskSize != item.DataSize)
                {
                    chunkNeedRebuild[item.ChunkIndex] = true;
                }
            }

            
            string outDir = Path.GetDirectoryName(outputPvfPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            
            string tempBodyPath = outputPvfPath + ".body.tmp";
            var newGroups = new List<GrpiItem>(chunkCount);
            int cumulativeCompressed = 0;

            try
            {
                using (var bodyStream = new FileStream(tempBodyPath, FileMode.Create, FileAccess.Write, FileShare.None, 256 * 1024))
                {
                    for (int ci = 0; ci < chunkCount; ci++)
                    {
                        List<int> fileIndices;
                        if (!chunkGroups.TryGetValue(ci, out fileIndices))
                            fileIndices = null;

                        bool needDecompress = chunkNeedRebuild[ci] && fileIndices != null && fileIndices.Count > 0;

                        if (!needDecompress)
                        {
                            
                            
                            byte[] rawEncrypted = archive.GetChunkRawEncrypted(ci);
                            if (rawEncrypted != null)
                            {
                                bodyStream.Write(rawEncrypted, 0, rawEncrypted.Length);
                                cumulativeCompressed += rawEncrypted.Length;
                                
                                var origGroups = archive.Groups;
                                newGroups.Add(new GrpiItem
                                {
                                    CompressedSize = cumulativeCompressed,
                                    OriginalSize = origGroups[ci].OriginalSize
                                });
                                result.SkippedChunks++;
                            }
                        }
                        else
                        {
                            
                            byte[] originalChunk = archive.GetChunkData(ci);
                            var fileUpdates = new List<FileUpdate>(fileIndices.Count);

                            foreach (int fi in fileIndices)
                            {
                                string dp = fileDiskPaths[fi];
                                var item = newItems[fi];

                                if (dp == null || item.DataSize <= 0)
                                {
                                    fileUpdates.Add(new FileUpdate { FileIndex = fi, NewData = null, Changed = false });
                                    continue;
                                }

                                long diskSize;
                                diskFileSizes.TryGetValue(dp, out diskSize);

                                if (diskSize != item.DataSize)
                                {
                                    
                                    byte[] diskData = File.ReadAllBytes(dp);
                                    result.Replaced++;
                                    fileUpdates.Add(new FileUpdate { FileIndex = fi, NewData = diskData, Changed = true });
                                }
                                else
                                {
                                    
                                    result.Unchanged++;
                                    fileUpdates.Add(new FileUpdate { FileIndex = fi, NewData = null, Changed = false });
                                }
                            }

                            
                            byte[] newChunk = RebuildChunk(originalChunk, fileUpdates, newItems);
                            result.RebuiltChunks++;

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

                        if (onProgress != null && (ci % 50 == 0 || ci == chunkCount - 1))
                        {
                            progress.Phase = $"Packing chunk {ci + 1}/{chunkCount}";
                            progress.Current = ci + 1;
                            progress.Total = chunkCount;
                            onProgress(progress);
                        }
                    }
                }

                
                byte[] tableBytes = new byte[archive.FileCount * 0x18];
                for (int i = 0; i < archive.FileCount; i++)
                {
                    byte[] itemBytes = StructToBytes(newItems[i]);
                    Array.Copy(itemBytes, 0, tableBytes, i * 0x18, 0x18);
                }

                
                byte[] hashBytes = archive.GetRawHashBytes();
                PvfDecryptor.Decrypt("HASH", hashBytes);
                byte[] nameBytes = archive.GetRawNameBytes();

                
                byte[] grpiBytes = new byte[newGroups.Count * 8];
                for (int i = 0; i < newGroups.Count; i++)
                {
                    byte[] g = StructToBytes(newGroups[i]);
                    Array.Copy(g, 0, grpiBytes, i * 8, 8);
                }
                PvfDecryptor.Decrypt("GRPI", grpiBytes);

                
                var header = archive.GetHeader();
                header.BodySize = cumulativeCompressed;
                header.GroupCount = newGroups.Count;
                header.HashTableSize = hashBytes.Length;
                header.NameTableSize = nameBytes.Length;

                byte[] headerBytes = StructToBytes(header);
                PvfDecryptor.Decrypt("HeaD", headerBytes);

                
                int totalPvfSize = 0x30 + tableBytes.Length + hashBytes.Length +
                                   nameBytes.Length + grpiBytes.Length + cumulativeCompressed;
                result.OutputSize = totalPvfSize;

                using (var outFs = new FileStream(outputPvfPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024))
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

                return result;
            }
            finally
            {
                
                try { if (File.Exists(tempBodyPath)) File.Delete(tempBodyPath); } catch { }
            }
        }

        
        
        
        private static byte[] RebuildChunk(byte[] originalChunk, List<FileUpdate> fileUpdates, PvfFileItem[] newItems)
        {
            var segments = new List<(int origOffset, int origSize, int fileIndex, byte[] newData)>();
            foreach (var upd in fileUpdates)
            {
                var item = newItems[upd.FileIndex];
                if (item.DataSize <= 0) continue;
                segments.Add((item.DataOffset, item.DataSize, upd.FileIndex, upd.Changed ? upd.NewData : null));
            }
            segments.Sort((a, b) => a.origOffset.CompareTo(b.origOffset));

            
            var ms = new MemoryStream();
            int srcPos = 0;
            foreach (var seg in segments)
            {
                
                if (seg.origOffset > srcPos && originalChunk != null)
                {
                    ms.Write(originalChunk, srcPos, seg.origOffset - srcPos);
                }

                
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

        private static Dictionary<string, string> BuildDiskIndex(string rootDir)
        {
            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string root = rootDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string filePath in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
            {
                string relative = filePath.Substring(root.Length);
                // Keep path casing for newly appended PVF entries; dictionary lookup is case-insensitive.
                string key = NormalizeDiskRelativePath(relative);
                if (!index.ContainsKey(key))
                    index[key] = filePath;
            }
            return index;
        }

        private static string BuildRelativePath(string dir, string name)
        {
            string combined = (dir + "/" + name).Replace('\\', '/');
            combined = combined.TrimEnd('/');
            while (combined.Length > 0)
            {
                if (combined.Length > 1 && combined[0] == '.' && combined[1] == '/')
                {
                    combined = combined.Substring(2);
                    continue;
                }
                if (combined[0] == '/')
                {
                    combined = combined.Substring(1);
                    continue;
                }
                break;
            }

            var parts = combined.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                bool hasInvalid = false;
                for (int j = 0; j < parts[i].Length; j++)
                {
                    if (InvalidCharSet.Contains(parts[i][j]))
                    { hasInvalid = true; break; }
                }
                if (hasInvalid)
                {
                    var sb = new StringBuilder(parts[i]);
                    for (int j = 0; j < sb.Length; j++)
                    {
                        if (InvalidCharSet.Contains(sb[j]))
                            sb[j] = '_';
                    }
                    parts[i] = sb.ToString();
                }
            }
            return string.Join("/", parts);
        }

        private static byte[] StructToBytes<T>(T value) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] bytes = new byte[size];
            var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { Marshal.StructureToPtr(value, handle.AddrOfPinnedObject(), false); }
            finally { handle.Free(); }
            return bytes;
        }

        private struct FileUpdate
        {
            public int FileIndex;
            public byte[] NewData;
            public bool Changed;
        }
    }
}
