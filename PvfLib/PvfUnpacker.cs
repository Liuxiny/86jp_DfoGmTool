using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GmPvfLib
{
    
    
    
    public static class PvfUnpacker
    {
        
        
        
        public class Progress
        {
            public int Current { get; set; }
            public int Total { get; set; }
            public string CurrentFile { get; set; }
            public int Errors { get; set; }
        }

        
        
        
        
        
        
        
        public static UnpackResult Unpack(PvfArchive archive, string outputDir, Action<Progress> onProgress = null)
        {
            return UnpackCore(archive, outputDir, false, onProgress);
        }

        // Export Type=1/3 as editable text for PackText() round-trip.
        public static UnpackResult UnpackText(PvfArchive archive, string outputDir, Action<Progress> onProgress = null)
        {
            return UnpackCore(archive, outputDir, true, onProgress);
        }

        private static UnpackResult UnpackCore(PvfArchive archive, string outputDir, bool decompileText, Action<Progress> onProgress)
        {
            if (archive == null) throw new ArgumentNullException(nameof(archive));
            if (string.IsNullOrEmpty(outputDir)) throw new ArgumentException("输出目录不能为空", nameof(outputDir));

            var result = new UnpackResult { Total = archive.FileCount };
            var progress = new Progress { Total = archive.FileCount };

            
            var conflictPaths = BuildConflictSet(archive);

            
            int lastChunkIndex = -1;
            byte[] currentChunk = null;

            for (int i = 0; i < archive.FileCount; i++)
            {
                var file = archive.Files[i];
                progress.Current = i + 1;
                progress.CurrentFile = file.Path + "/" + file.Name;

                try
                {
                    
                    if (file.Name.EndsWith("/") || file.Name.EndsWith("\\"))
                    {
                        string dirMark = NormalizePath(file.Path, file.Name);
                        string dirFull = Path.Combine(outputDir, dirMark);
                        if (!Directory.Exists(dirFull))
                            Directory.CreateDirectory(dirFull);
                        result.Extracted++;
                        continue;
                    }

                    
                    string relativePath = NormalizePath(file.Path, file.Name);
                    
                    if (conflictPaths.Contains(relativePath.ToLowerInvariant()))
                        relativePath += "._file";
                    string fullPath = Path.Combine(outputDir, relativePath);
                    string dir = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    
                    if (file.Entry.ChunkIndex != lastChunkIndex)
                    {
                        currentChunk = archive.GetChunkData(file.Entry.ChunkIndex);
                        lastChunkIndex = file.Entry.ChunkIndex;
                    }

                    
                    if (file.Entry.DataSize <= 0)
                    {
                        if (decompileText && (file.Entry.DataType == 1 || file.Entry.DataType == 3))
                            File.WriteAllText(fullPath, string.Empty, new UTF8Encoding(false));
                        else
                            File.WriteAllBytes(fullPath, Array.Empty<byte>());
                        result.Extracted++;
                        continue;
                    }

                    if (currentChunk == null || file.Entry.DataOffset < 0 ||
                        file.Entry.DataOffset + file.Entry.DataSize > currentChunk.Length)
                    {
                        result.Skipped++;
                        continue;
                    }

                    
                    // Text mode writes decoded script/string content, not raw bytes.
                    if (decompileText && (file.Entry.DataType == 1 || file.Entry.DataType == 3))
                    {
                        File.WriteAllText(fullPath, archive.GetFileContent(i), new UTF8Encoding(false));
                    }
                    else
                    {
                        switch (file.Entry.DataType)
                        {
                            case 1:
                                WriteType1(fullPath, currentChunk, file.Entry);
                                break;
                            case 3:
                                WriteType3(fullPath, currentChunk, file.Entry);
                                break;
                            default:
                                WriteBinary(fullPath, currentChunk, file.Entry);
                                break;
                        }
                    }
                    result.Extracted++;
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    progress.Errors = result.Errors;
                    result.LastError = ex.Message;
                }

                if (onProgress != null && (i % 1000 == 0 || i == archive.FileCount - 1))
                    onProgress(progress);
            }

            return result;
        }

        private static void WriteType1(string path, byte[] chunk, PvfFileItem item)
        {
            
            
            byte[] data = new byte[item.DataSize];
            Array.Copy(chunk, item.DataOffset, data, 0, item.DataSize);
            File.WriteAllBytes(path, data);
        }

        private static void WriteType3(string path, byte[] chunk, PvfFileItem item)
        {
            
            byte[] data = new byte[item.DataSize];
            Array.Copy(chunk, item.DataOffset, data, 0, item.DataSize);
            File.WriteAllBytes(path, data);
        }

        private static void WriteBinary(string path, byte[] chunk, PvfFileItem item)
        {
            byte[] data = new byte[item.DataSize];
            Array.Copy(chunk, item.DataOffset, data, 0, item.DataSize);
            File.WriteAllBytes(path, data);
        }

        private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

        private static string NormalizePath(string dir, string name)
        {
            
            string combined = (dir + "/" + name).Replace('/', Path.DirectorySeparatorChar);
            combined = combined.TrimEnd(Path.DirectorySeparatorChar);
            while (combined.Length > 0 && (combined[0] == Path.DirectorySeparatorChar || combined[0] == '.'))
            {
                if (combined.Length > 1 && combined[0] == '.' && combined[1] == Path.DirectorySeparatorChar)
                {
                    combined = combined.Substring(2);
                    continue;
                }
                if (combined[0] == Path.DirectorySeparatorChar)
                {
                    combined = combined.Substring(1);
                    continue;
                }
                break;
            }

            
            var parts = combined.Split(Path.DirectorySeparatorChar);
            for (int i = 0; i < parts.Length; i++)
            {
                var sb = new StringBuilder(parts[i]);
                for (int j = 0; j < sb.Length; j++)
                {
                    if (Array.IndexOf(InvalidChars, sb[j]) >= 0)
                        sb[j] = '_';
                }
                parts[i] = sb.ToString();
            }
            return string.Join(Path.DirectorySeparatorChar.ToString(), parts);
        }

        
        
        
        private static HashSet<string> BuildConflictSet(PvfArchive archive)
        {
            var filePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < archive.FileCount; i++)
            {
                string rel = NormalizePath(archive.Files[i].Path, archive.Files[i].Name);
                filePaths.Add(rel.ToLowerInvariant());

                
                string dir = Path.GetDirectoryName(rel);
                while (!string.IsNullOrEmpty(dir))
                {
                    dirPaths.Add(dir.ToLowerInvariant());
                    dir = Path.GetDirectoryName(dir);
                }
            }

            
            var conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in dirPaths)
                if (filePaths.Contains(d))
                    conflicts.Add(d);
            return conflicts;
        }
    }

    
    
    
    public class UnpackResult
    {
        public int Total { get; set; }
        public int Extracted { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
        public string LastError { get; set; }
    }
}
