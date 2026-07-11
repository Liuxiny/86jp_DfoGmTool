using System;
using System.Collections.Generic;

namespace GmPvfLib
{
    
    
    
    
    public sealed class PvfHashTable
    {
        
        public int EntryCount { get; }

        
        public IReadOnlyList<HashEntry> Entries => _entries;

        
        public IReadOnlyList<int> SortedOffsets => _sortedOffsets;

        private readonly HashEntry[] _entries;
        private readonly int[] _sortedOffsets;

        private PvfHashTable(int entryCount, HashEntry[] entries, int[] sortedOffsets)
        {
            EntryCount = entryCount;
            _entries = entries;
            _sortedOffsets = sortedOffsets;
        }

        
        
        
        public static PvfHashTable Parse(byte[] decryptedBytes)
        {
            if (decryptedBytes == null || decryptedBytes.Length < 4)
                throw new ArgumentException("HashTable 数据不足");

            int pos = 0;
            int entryCount = BitConverter.ToInt32(decryptedBytes, pos); pos += 4;

            int requiredForEntries = 4 + entryCount * 8;
            if (decryptedBytes.Length < requiredForEntries)
                throw new ArgumentException("HashTable 数据不足以包含全部条目");

            
            var entries = new HashEntry[entryCount];
            for (int i = 0; i < entryCount; i++)
            {
                int nameOff = BitConverter.ToInt32(decryptedBytes, pos); pos += 4;
                int pathOff = BitConverter.ToInt32(decryptedBytes, pos); pos += 4;
                entries[i] = new HashEntry(nameOff, pathOff);
            }

            
            int lookupCount = 0;
            int[] sortedOffsets = Array.Empty<int>();
            if (pos + 4 <= decryptedBytes.Length)
            {
                lookupCount = BitConverter.ToInt32(decryptedBytes, pos); pos += 4;
                if (lookupCount > 0 && pos + lookupCount * 4 <= decryptedBytes.Length)
                {
                    sortedOffsets = new int[lookupCount];
                    for (int i = 0; i < lookupCount; i++)
                    {
                        sortedOffsets[i] = BitConverter.ToInt32(decryptedBytes, pos); pos += 4;
                    }
                }
            }

            return new PvfHashTable(entryCount, entries, sortedOffsets);
        }

        
        
        
        public static PvfHashTable Build(IReadOnlyList<PvfFileItem> fileItems, Func<int, string> resolveString)
        {
            int count = fileItems.Count;
            var entries = new HashEntry[count];
            var uniqueOffsets = new HashSet<int>();

            for (int i = 0; i < count; i++)
            {
                entries[i] = new HashEntry(fileItems[i].NameOffset, fileItems[i].PathOffset);
                uniqueOffsets.Add(fileItems[i].NameOffset);
                if (fileItems[i].PathOffset >= 0) 
                    uniqueOffsets.Add(fileItems[i].PathOffset);
            }

            
            var sorted = new List<int>(uniqueOffsets);
            sorted.Sort((a, b) =>
            {
                string sa = resolveString(a);
                string sb = resolveString(b);
                return string.Compare(sa, sb, StringComparison.Ordinal);
            });

            return new PvfHashTable(count, entries, sorted.ToArray());
        }

        
        
        
        public bool ContainsOffset(int offset, Func<int, string> resolveString)
        {
            if (_sortedOffsets.Length == 0) return false;

            string target = resolveString(offset);
            int lo = 0, hi = _sortedOffsets.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                int cmp = string.Compare(resolveString(_sortedOffsets[mid]), target, StringComparison.Ordinal);
                if (cmp == 0) return true;
                if (cmp < 0) lo = mid + 1;
                else hi = mid - 1;
            }
            return false;
        }

        
        
        
        public byte[] ToBytes()
        {
            int size = 4 + EntryCount * 8 + 4 + _sortedOffsets.Length * 4;
            byte[] result = new byte[size];
            int pos = 0;

            WriteInt32(result, ref pos, EntryCount);
            for (int i = 0; i < EntryCount; i++)
            {
                WriteInt32(result, ref pos, _entries[i].NameOffset);
                WriteInt32(result, ref pos, _entries[i].PathOffset);
            }

            WriteInt32(result, ref pos, _sortedOffsets.Length);
            for (int i = 0; i < _sortedOffsets.Length; i++)
                WriteInt32(result, ref pos, _sortedOffsets[i]);

            return result;
        }

        private static void WriteInt32(byte[] buf, ref int pos, int value)
        {
            buf[pos] = (byte)value;
            buf[pos + 1] = (byte)(value >> 8);
            buf[pos + 2] = (byte)(value >> 16);
            buf[pos + 3] = (byte)(value >> 24);
            pos += 4;
        }
    }

    
    
    
    public struct HashEntry
    {
        public readonly int NameOffset;
        public readonly int PathOffset;

        public HashEntry(int nameOffset, int pathOffset)
        {
            NameOffset = nameOffset;
            PathOffset = pathOffset;
        }
    }
}
